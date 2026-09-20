using RepoDeck.Models;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Tests;

/// <summary>
/// A controllable stand-in for GitHub. Tests can make a call block until released,
/// which is what makes ordering and cancellation behaviour testable.
/// </summary>
internal sealed class FakeGitHubClient : IGitHubClient
{
    private readonly Dictionary<string, RepositorySearchResult> _searchResults = new();

    public RateLimitStatus RateLimit { get; private set; } = RateLimitStatus.Unknown;
    public event Action<RateLimitStatus>? RateLimitChanged;
    public bool IsAuthenticated { get; set; }

    public int SearchCallCount { get; private set; }
    public int RepositoryCallCount { get; private set; }
    public int ReadmeCallCount { get; private set; }
    public int ReleaseCallCount { get; private set; }

    /// <summary>Which project the last release query actually asked about.</summary>
    public string? LastReleasesOwner { get; private set; }
    public string? LastReleasesName { get; private set; }
    public int TreeCallCount { get; private set; }
    public int TextFileCallCount { get; private set; }

    /// <summary>When set, a search waits on this before returning.</summary>
    public TaskCompletionSource? SearchGate { get; set; }

    /// <summary>Holds README requests open until released.</summary>
    public TaskCompletionSource? ReadmeGate { get; set; }

    /// <summary>Holds file-listing requests open, ignoring cancellation.</summary>
    public TaskCompletionSource? TreeGate { get; set; }

    /// <summary>Results returned for any search, keyed by the query text.</summary>
    public Dictionary<string, List<GitHubRepository>> ResultsByText { get; } = new();

    public List<GitHubRepository> DefaultResults { get; set; } = [];

    /// <summary>Overrides the reported total, for testing how counts are worded.</summary>
    public int? TotalCount { get; set; }
    public GitHubRepository? Repository { get; set; }
    public string? Readme { get; set; }
    public Dictionary<string, long> Languages { get; set; } = new();
    public List<GitHubRelease> Releases { get; set; } = [];
    public RepositoryTree Tree { get; set; } = RepositoryTree.Empty;
    public Dictionary<string, string> TextFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? SearchThrows { get; set; }
    public Exception? ReleasesThrows { get; set; }
    public Exception? TreeThrows { get; set; }
    public Exception? ReadmeThrows { get; set; }

    public async Task<RepositorySearchResult> SearchRepositoriesAsync(
        RepositorySearchQuery query, CancellationToken cancellationToken = default)
    {
        SearchCallCount++;

        var gate = SearchGate;
        if (gate is not null)
        {
            await using (cancellationToken.Register(() => gate.TrySetCanceled()))
            {
                await gate.Task;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (SearchThrows is not null) throw SearchThrows;

        var items = ResultsByText.TryGetValue(query.Text, out var byText) ? byText : DefaultResults;

        return new RepositorySearchResult
        {
            Items = items,
            TotalCount = TotalCount ?? items.Count,
            Page = query.Page,
            PerPage = query.PerPage
        };
    }

    public Task<GitHubRepository> GetRepositoryAsync(
        string owner, string name, CancellationToken cancellationToken = default)
    {
        RepositoryCallCount++;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Repository ?? TestRepositories.Create(name, owner));
    }

    public async Task<string?> GetReadmeAsync(
        string owner, string name, CancellationToken cancellationToken = default)
    {
        ReadmeCallCount++;

        // Lets a test hold one request open while it starts another, which is the only
        // way to reproduce a stale result arriving after a newer one.
        if (ReadmeGate is not null)
        {
            await ReadmeGate.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (ReadmeThrows is not null) throw ReadmeThrows;
        return Readme;
    }

    public Task<IReadOnlyDictionary<string, long>> GetLanguagesAsync(
        string owner, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyDictionary<string, long>>(Languages);
    }

    public Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(
        string owner, string name, int limit = 10, CancellationToken cancellationToken = default)
    {
        ReleaseCallCount++;
        LastReleasesOwner = owner;
        LastReleasesName = name;
        cancellationToken.ThrowIfCancellationRequested();
        if (ReleasesThrows is not null) throw ReleasesThrows;
        return Task.FromResult<IReadOnlyList<GitHubRelease>>(Releases);
    }

    public async Task<RepositoryTree> GetTreeAsync(
        string owner, string name, string? reference = null, CancellationToken cancellationToken = default)
    {
        TreeCallCount++;
        cancellationToken.ThrowIfCancellationRequested();

        // Deliberately does NOT observe the token, modelling work already handed off that
        // finishes after the caller has given up on it. That is the case a cancellation
        // token cannot cover and a generation guard has to.
        if (TreeGate is not null) await TreeGate.Task;

        if (TreeThrows is not null) throw TreeThrows;
        return Tree;
    }

    public Task<string?> GetTextFileAsync(
        string owner, string name, string path, CancellationToken cancellationToken = default)
    {
        TextFileCallCount++;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TextFiles.TryGetValue(path, out var content) ? content : null);
    }

    public void RaiseRateLimit(RateLimitStatus status)
    {
        RateLimit = status;
        RateLimitChanged?.Invoke(status);
    }
}
