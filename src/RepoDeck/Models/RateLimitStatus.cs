namespace RepoDeck.Models;

/// <summary>
/// Snapshot of GitHub's rate-limit headers from the most recent response.
/// Unauthenticated limits are low (60/hour core, 10/minute search), so this is
/// surfaced in the UI rather than hidden.
/// </summary>
public sealed record RateLimitStatus
{
    public int Limit { get; init; }
    public int Remaining { get; init; }
    public DateTimeOffset? ResetsAt { get; init; }
    public bool IsAuthenticated { get; init; }

    public bool IsExhausted => Remaining <= 0;

    public TimeSpan? TimeUntilReset =>
        ResetsAt is null ? null : ResetsAt.Value - DateTimeOffset.UtcNow;

    public static RateLimitStatus Unknown { get; } = new() { Limit = 0, Remaining = -1 };

    public bool IsKnown => Remaining >= 0;
}
