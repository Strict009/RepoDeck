namespace RepoDeck.Models;

/// <summary>What is wrong with an installation, if anything.</summary>
public enum HealthProblem
{
    /// <summary>The folder RepoDeck recorded is gone.</summary>
    MissingDirectory,

    /// <summary>The folder is there, but the program RepoDeck recorded is not.</summary>
    MissingExecutable,

    /// <summary>The folder is there and empty.</summary>
    EmptyDirectory,

    /// <summary>
    /// The record itself does not describe a valid installation - no path, or a path
    /// outside RepoDeck's own folder, which means the file was edited by hand.
    /// </summary>
    InconsistentRecord,

    /// <summary>Files RepoDeck recorded as owning are no longer there.</summary>
    MissingOwnedEntries
}

/// <summary>The state of one installed application's files.</summary>
/// <remarks>
/// A deliberately shallow check: RepoDeck confirms the things it recorded are still where
/// it left them. It does not hash every file or attempt to detect tampering, because it
/// cannot tell an application updating its own data from something going wrong, and a
/// health check that cried wolf would be worse than none.
/// </remarks>
public sealed record InstallationHealth
{
    public required string ApplicationId { get; init; }

    public IReadOnlyList<HealthProblem> Problems { get; init; } = [];

    /// <summary>Plain English, one line per problem. Safe to show verbatim.</summary>
    public IReadOnlyList<string> Explanations { get; init; } = [];

    public bool IsHealthy => Problems.Count == 0;

    /// <summary>
    /// True when reinstalling the same release would plausibly fix this. A record that is
    /// itself inconsistent is not repaired by fetching files - it is repaired by removing
    /// the record, which is the user's decision rather than RepoDeck's.
    /// </summary>
    public bool IsRepairable =>
        Problems.Count > 0
        && !Problems.Contains(HealthProblem.InconsistentRecord);

    public string Summary => Problems.Count switch
    {
        0 => "This looks intact.",
        1 => Explanations.Count > 0 ? Explanations[0] : "Something is wrong with this installation.",
        _ => $"{Problems.Count} things are wrong with this installation."
    };

    public static InstallationHealth Healthy(string id) => new() { ApplicationId = id };
}

/// <summary>What RepoDeck would do to put a damaged installation back.</summary>
/// <remarks>
/// Repair is deliberately not surgery. RepoDeck does not try to work out which file is
/// missing and fetch that one; it reinstalls the release the manifest records, through
/// exactly the same staged path a first install uses. Anything cleverer would mean
/// reasoning about a half-broken directory, which is precisely the situation in which
/// reasoning is least reliable.
/// </remarks>
public sealed record RepairPlan
{
    public required ApplicationManifest Current { get; init; }
    public required InstallationHealth Health { get; init; }

    /// <summary>The release that will be fetched again: the one already recorded.</summary>
    public string? ReleaseTag { get; init; }
    public string? AssetName { get; init; }
    public string? AssetUrl { get; init; }
    public long AssetSize { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];
    public IReadOnlyList<string> BlockingIssues { get; init; } = [];

    public bool CanProceed => BlockingIssues.Count == 0 && AssetUrl is { Length: > 0 };

    /// <summary>What RepoDeck will do, for the confirmation.</summary>
    public IReadOnlyList<string> WillDo =>
    [
        $"Download {AssetName} again from the release you already have.",
        "Check the downloaded file is complete.",
        "Put the working copy aside and replace it with a fresh one.",
        "Put the old one back if anything goes wrong."
    ];

    public static RepairPlan NotPossible(
        ApplicationManifest current, InstallationHealth health, string reason) => new()
    {
        Current = current,
        Health = health,
        BlockingIssues = [reason]
    };
}
