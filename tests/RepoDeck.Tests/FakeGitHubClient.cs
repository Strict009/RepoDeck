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

    /// <summary>When set, a search waits on this before returning.</summary>
    public TaskCompletionSource? SearchGate { get; set; }

    /// <summary>Results returned for any search, keyed by the query text.</summary>
    public Dictionary<string, List<GitHubRepository>> ResultsByText { get; } = new();

    public List<GitHubRepository> DefaultResults { get; set; } = [];
    public GitHubRepository? Repository { get; set; }
    public string? Readme { get; set; }
    public Dictionary<string, long> Languages { get; set; } = new();
    public List<GitHubRelease> Releases { get; set; } = [];

    public Exception? SearchThrows { get; set; }
    public Exception? ReleasesThrows { get; set; }
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
            TotalCount = items.Count,
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

    public Task<string?> GetReadmeAsync(
        string owner, string name, CancellationToken cancellationToken = default)
    {
        ReadmeCallCount++;
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadmeThrows is not null) throw ReadmeThrows;
        return Task.FromResult(Readme);
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
        cancellationToken.ThrowIfCancellationRequested();
        if (ReleasesThrows is not null) throw ReleasesThrows;
        return Task.FromResult<IReadOnlyList<GitHubRelease>>(Releases);
    }

    public void RaiseRateLimit(RateLimitStatus status)
    {
        RateLimit = status;
        RateLimitChanged?.Invoke(status);
    }
}
