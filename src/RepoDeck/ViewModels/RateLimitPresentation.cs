using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>
/// How a GitHub rate-limit snapshot is worded in the shell.
/// </summary>
/// <remarks>
/// Pure, so the wording can be checked without standing up a window. The strip gets a
/// word and a meter; the exact arithmetic goes in a tooltip, where anyone who wants the
/// numbers can find them and nobody else has to read them.
/// </remarks>
public sealed record RateLimitPresentation(
    bool IsKnown,
    string Label,
    double Percent,
    string Detail,
    string Warning)
{
    /// <summary>Before the first response there is nothing honest to show.</summary>
    public static RateLimitPresentation Unknown { get; } = new(
        IsKnown: false,
        Label: "",
        Percent: 0,
        Detail: "RepoDeck has not contacted GitHub yet.",
        Warning: "");

    public static RateLimitPresentation From(RateLimitStatus status, DateTimeOffset? now = null)
    {
        if (!status.IsKnown) return Unknown;

        var fraction = status.Limit <= 0 ? 0 : (double)status.Remaining / status.Limit;

        var label = status.IsExhausted
            ? "ALLOWANCE USED UP"
            : fraction < 0.25 ? "ALLOWANCE LOW" : "ALLOWANCE OK";

        var authentication = status.IsAuthenticated
            ? "Signed in with a token."
            : "Not signed in, so the allowance is small. See Settings.";

        var resets = status.ResetsAt is null
            ? ""
            : $" Resets {Humanize.TimeUntil(status.ResetsAt, now)}.";

        return new RateLimitPresentation(
            IsKnown: true,
            Label: label,
            Percent: Math.Clamp(fraction * 100, 0, 100),
            Detail: $"{status.Remaining} of {status.Limit} GitHub requests left. {authentication}{resets}",
            Warning: status.IsExhausted ? "GitHub limit reached" : "");
    }
}
