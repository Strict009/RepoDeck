using RepoDeck.Models;

namespace RepoDeck.Services.GitHub;

/// <summary>
/// RepoDeck's entire view of GitHub. Everything above this interface works with
/// RepoDeck models and never sees HTTP, JSON or rate-limit headers.
/// Implementations throw <see cref="GitHubApiException"/> for every failure worth showing.
/// </summary>
public interface IGitHubClient
{
    Task<RepositorySearchResult> SearchRepositoriesAsync(
        RepositorySearchQuery query, CancellationToken cancellationToken = default);

    Task<GitHubRepository> GetRepositoryAsync(
        string owner, string name, CancellationToken cancellationToken = default);

    /// <summary>Raw README markdown, or null when the repository has no README.</summary>
    Task<string?> GetReadmeAsync(
        string owner, string name, CancellationToken cancellationToken = default);

    /// <summary>Language name to bytes. Empty when GitHub reports none.</summary>
    Task<IReadOnlyDictionary<string, long>> GetLanguagesAsync(
        string owner, string name, CancellationToken cancellationToken = default);

    /// <summary>Most recent releases, newest first. Empty when the repository has none.</summary>
    Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(
        string owner, string name, int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>Rate limit as of the most recent response. <see cref="RateLimitStatus.Unknown"/> before the first call.</summary>
    RateLimitStatus RateLimit { get; }

    /// <summary>Raised whenever a response updates the known rate limit.</summary>
    event Action<RateLimitStatus>? RateLimitChanged;

    /// <summary>Whether requests are being sent with a personal access token.</summary>
    bool IsAuthenticated { get; }
}
