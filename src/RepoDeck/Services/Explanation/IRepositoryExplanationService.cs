using RepoDeck.Models;

namespace RepoDeck.Services.Explanation;

/// <summary>
/// Turns GitHub data into something a non-developer can act on.
/// </summary>
/// <remarks>
/// This is an interface specifically so an AI-backed implementation can be dropped in
/// later without the rest of RepoDeck depending on AI being available. The heuristic
/// implementation must always remain usable on its own.
/// </remarks>
public interface IRepositoryExplanationService
{
    /// <summary>
    /// A short explanation from search-result data alone - no extra network calls.
    /// Used for repository cards on the Discover page.
    /// </summary>
    RepositoryExplanation ExplainFromMetadata(GitHubRepository repository);

    /// <summary>
    /// A fuller explanation using the README, languages and releases.
    /// Async because an AI-backed implementation will need to be.
    /// </summary>
    Task<RepositoryExplanation> ExplainAsync(
        RepositoryDetails details, CancellationToken cancellationToken = default);
}
