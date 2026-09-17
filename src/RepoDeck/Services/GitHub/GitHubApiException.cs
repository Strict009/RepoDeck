using RepoDeck.Models;

namespace RepoDeck.Services.GitHub;

public enum GitHubErrorKind
{
    /// <summary>Could not reach GitHub at all.</summary>
    Network,

    /// <summary>API rate limit exhausted.</summary>
    RateLimited,

    /// <summary>Repository or resource does not exist (or is private).</summary>
    NotFound,

    /// <summary>Authenticated but not permitted, or blocked.</summary>
    Forbidden,

    /// <summary>GitHub rejected the search query itself.</summary>
    InvalidQuery,

    /// <summary>GitHub had a problem, or returned something unexpected.</summary>
    ServerError,

    Unexpected
}

/// <summary>
/// A GitHub failure translated into something worth showing a person.
/// <see cref="UserMessage"/> is always safe to put straight on screen.
/// </summary>
public sealed class GitHubApiException : Exception
{
    public GitHubApiException(
        GitHubErrorKind kind,
        string userMessage,
        string? technicalDetail = null,
        RateLimitStatus? rateLimit = null,
        Exception? inner = null)
        : base(technicalDetail ?? userMessage, inner)
    {
        Kind = kind;
        UserMessage = userMessage;
        RateLimit = rateLimit;
    }

    public GitHubErrorKind Kind { get; }

    /// <summary>Plain-English, no stack traces, no jargon.</summary>
    public string UserMessage { get; }

    public RateLimitStatus? RateLimit { get; }

    public bool IsRetryable => Kind is GitHubErrorKind.Network or GitHubErrorKind.ServerError;
}
