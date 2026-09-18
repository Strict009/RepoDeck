namespace RepoDeck.Models;

/// <summary>
/// What RepoDeck knows about whether an installed application is current.
/// </summary>
/// <remarks>
/// Five answers, and the two honest ones matter most. A project that tags releases
/// "final-build-2" and "final-build-really" has no ordering RepoDeck can defend, and
/// saying so is better than guessing - offering somebody an "update" that is older than
/// what they have would be worse than offering nothing.
/// </remarks>
public enum UpdateState
{
    /// <summary>RepoDeck has not looked yet.</summary>
    NotChecked,

    /// <summary>The installed release is the newest stable one.</summary>
    UpToDate,

    /// <summary>A newer stable release exists and RepoDeck can install it.</summary>
    UpdateAvailable,

    /// <summary>
    /// A newer release exists, but RepoDeck cannot install it - no asset for this machine,
    /// or a kind of package it will not handle. The user is told where to get it.
    /// </summary>
    ManualUpdateRequired,

    /// <summary>
    /// The release tags cannot be compared, or the check failed. RepoDeck says so rather
    /// than guessing in either direction.
    /// </summary>
    Unknown
}

/// <summary>The result of one update check, and why it says what it says.</summary>
/// <remarks>
/// Never modifies the installation. This is a question answered, nothing more, and it is
/// held in memory rather than written into the manifest: an answer about a remote release
/// goes stale, and a stale answer stored as fact is worse than no answer.
/// </remarks>
public sealed record UpdateCheck
{
    public UpdateState State { get; init; } = UpdateState.NotChecked;

    /// <summary>The version RepoDeck read from the installed release tag, when it could.</summary>
    public ReleaseVersion? InstalledVersion { get; init; }

    /// <summary>The newest suitable release found, when there was one.</summary>
    public GitHubRelease? LatestRelease { get; init; }

    public ReleaseVersion? LatestVersion { get; init; }

    /// <summary>
    /// The file an update would fetch, when the check found one. Recorded here so the
    /// user can see what they would be downloading before agreeing to anything - the
    /// plan that actually does it is not built until they ask.
    /// </summary>
    public string? AssetName { get; init; }

    /// <summary>Its size in bytes, when GitHub reported one. Zero means unstated.</summary>
    public long AssetSize { get; init; }

    /// <summary>When the check ran, so the UI can say how fresh the answer is.</summary>
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>In plain English. Always populated for anything other than NotChecked.</summary>
    public string Explanation { get; init; } = "";

    /// <summary>The evidence, for the Why? disclosure.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>How big a jump this would be, when both versions were readable.</summary>
    public VersionStep Step { get; init; } = VersionStep.NotNewer;

    public bool HasUpdate => State is UpdateState.UpdateAvailable or UpdateState.ManualUpdateRequired;

    /// <summary>The tag shown as "available", or empty when there is nothing to offer.</summary>
    public string AvailableVersionText => LatestRelease?.TagName ?? "";

    /// <summary>Short label. Never the only way a state is communicated.</summary>
    public string Label => State switch
    {
        UpdateState.UpToDate => "Up to date",
        UpdateState.UpdateAvailable => "Update available",
        UpdateState.ManualUpdateRequired => "Manual update required",
        UpdateState.Unknown => "Can't determine",
        _ => "Not checked"
    };

    public static UpdateCheck NotChecked { get; } = new()
    {
        State = UpdateState.NotChecked,
        Explanation = "RepoDeck has not checked this for updates yet."
    };

    public static UpdateCheck Undetermined(string explanation, params string[] reasons) => new()
    {
        State = UpdateState.Unknown,
        Explanation = explanation,
        Reasons = reasons,
        CheckedAt = DateTimeOffset.UtcNow
    };
}
