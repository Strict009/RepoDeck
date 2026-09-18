using System.Text.Json.Serialization;

namespace RepoDeck.Models;

/// <summary>How an update would be carried out.</summary>
public enum UpdateStrategy
{
    Unknown,

    /// <summary>
    /// Stage the new version, back up the old one, swap them, check the result, then
    /// remove the backup. The only strategy RepoDeck performs.
    /// </summary>
    ReplaceManagedInstallation,

    /// <summary>
    /// RepoDeck would fetch the file and stop, exactly as it does for a first install of
    /// a system installer. Not an update it performs.
    /// </summary>
    DownloadOnly,

    /// <summary>Nothing RepoDeck can do. The user is pointed at the project.</summary>
    NotSupported
}

/// <summary>
/// Exactly what RepoDeck would do to move an installation from one release to another -
/// written down, inspectable, and not executed.
/// </summary>
/// <remarks>
/// The same decide/do boundary as <see cref="InstallPlan"/>, for the same reason. The
/// update service carries this out and re-decides nothing: in particular it does not pick
/// an asset, because asset selection already exists in one place and having two would
/// mean two places for it to be got wrong.
///
/// It also carries the manifest it is replacing. A rollback needs the old record as much
/// as the old files, and reconstructing it afterwards from what is left on disk would be
/// guessing at the moment guessing is least affordable.
/// </remarks>
public sealed record UpdatePlan
{
    /// <summary>The installation being replaced, exactly as recorded.</summary>
    public required ApplicationManifest Current { get; init; }

    public required string Owner { get; init; }
    public required string Name { get; init; }

    [JsonIgnore]
    public string FullName => Owner + "/" + Name;

    // ---- From ------------------------------------------------------------
    public string InstalledVersionText { get; init; } = "";

    // ---- To --------------------------------------------------------------
    public string? TargetReleaseTag { get; init; }
    public string? TargetReleaseName { get; init; }
    public long? TargetReleaseId { get; init; }
    public DateTimeOffset? TargetPublishedAt { get; init; }
    public bool TargetIsPrerelease { get; init; }
    public string TargetVersionText { get; init; } = "";

    /// <summary>Release notes as GitHub returned them. Untrusted text, never rendered as markup.</summary>
    public string? TargetReleaseNotes { get; init; }

    public string? TargetReleaseUrl { get; init; }

    // ---- What would be fetched -------------------------------------------
    public string? AssetName { get; init; }
    public string? AssetUrl { get; init; }
    public long AssetSize { get; init; }

    public OsPlatform Platform { get; init; } = OsPlatform.Unknown;
    public CpuArchitecture Architecture { get; init; } = CpuArchitecture.Unknown;
    public PackageType PackageType { get; init; } = PackageType.Unknown;

    // ---- How -------------------------------------------------------------
    public UpdateStrategy Strategy { get; init; } = UpdateStrategy.Unknown;

    /// <summary>The install strategy the new files would be handled with.</summary>
    public InstallStrategy InstallStrategy { get; init; } = InstallStrategy.Unknown;

    /// <summary>
    /// True when the files being replaced include the running program. RepoDeck asks the
    /// user to close it; it never closes anything itself.
    /// </summary>
    public bool RequiresApplicationClosed { get; init; }

    /// <summary>
    /// True when this plan is a repair rather than an update: the same release, fetched
    /// again to put a damaged installation back.
    /// </summary>
    /// <remarks>
    /// Repair runs the update transaction deliberately, so it inherits every protection
    /// including rollback. What it must not inherit is the update transactions account of
    /// itself - a history reading "Updated to v0.0.3" after somebody pressed REPAIR
    /// describes something that did not happen. The repair records its own events instead.
    /// </remarks>
    public bool IsRepair { get; init; }

    public string ProposedInstallDirectory { get; init; } = "";

    /// <summary>Executable names expected afterwards, carried from the analysis.</summary>
    public IReadOnlyList<string> ExecutableCandidates { get; init; } = [];

    // ---- How sure --------------------------------------------------------
    public Confidence Confidence { get; init; } = Confidence.Unknown;
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Reasons this cannot be carried out. Non-empty means no update.</summary>
    public IReadOnlyList<string> BlockingIssues { get; init; } = [];

    [JsonIgnore]
    public bool CanProceed =>
        BlockingIssues.Count == 0
        && AssetUrl is { Length: > 0 }
        && Strategy == UpdateStrategy.ReplaceManagedInstallation;

    [JsonIgnore]
    public string StepSummary => "Download -> Verify -> Prepare -> Replace";

    /// <summary>"0.0.3 -> 0.0.4", for the confirmation heading.</summary>
    [JsonIgnore]
    public string TransitionText => InstalledVersionText + "  →  " + TargetVersionText;

    public static UpdatePlan NotPossible(
        ApplicationManifest current, UpdateStrategy strategy, string reason) => new()
    {
        Current = current,
        Owner = current.Owner,
        Name = current.Name,
        InstalledVersionText = current.DisplayVersion,
        Strategy = strategy,
        BlockingIssues = [reason],
        Confidence = Confidence.Unsupported
    };
}
