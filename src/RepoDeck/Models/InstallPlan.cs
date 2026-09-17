using System.Text.Json.Serialization;

namespace RepoDeck.Models;

/// <summary>
/// Exactly what RepoDeck would do to install something - written down, inspectable,
/// and not executed.
/// </summary>
/// <remarks>
/// This is the boundary between deciding and doing. Milestone 2 produces plans;
/// Milestone 3's installer consumes one without re-analysing anything. Everything the
/// installer needs is here, and nothing here is a secret, so a plan can be logged,
/// serialised and shown to the user in full.
/// </remarks>
public sealed record InstallPlan
{
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string RepositoryUrl { get; init; }

    public string FullName => $"{Owner}/{Name}";

    // ---- What would be downloaded ----------------------------------------
    public string? ReleaseTag { get; init; }
    public string? ReleaseName { get; init; }
    public DateTimeOffset? ReleasePublishedAt { get; init; }
    public bool IsPrerelease { get; init; }

    public string? AssetName { get; init; }
    public string? AssetUrl { get; init; }
    public long AssetSize { get; init; }

    public OsPlatform Platform { get; init; } = OsPlatform.Unknown;
    public CpuArchitecture Architecture { get; init; } = CpuArchitecture.Unknown;
    public PackageType PackageType { get; init; } = PackageType.Unknown;

    // ---- What would be done ----------------------------------------------
    public InstallStrategy Strategy { get; init; } = InstallStrategy.Unknown;
    public LaunchStrategy LaunchStrategy { get; init; } = LaunchStrategy.Unknown;

    /// <summary>Where RepoDeck would put it. Always inside RepoDeck's own managed folder.</summary>
    public string ProposedInstallDirectory { get; init; } = "";

    public bool RequiresExtraction { get; init; }

    /// <summary>
    /// Executable names RepoDeck expects to find after extracting. These are predictions
    /// from the repository and asset names, not observations - nothing has been downloaded.
    /// </summary>
    public IReadOnlyList<string> ExecutableCandidates { get; init; } = [];

    /// <summary>True when carrying out the plan would need administrator or root rights.</summary>
    public bool RequiresElevation { get; init; }

    // ---- How sure RepoDeck is --------------------------------------------
    public Confidence Confidence { get; init; } = Confidence.Unknown;
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Reasons this plan cannot be carried out at all. Non-empty means no install.</summary>
    public IReadOnlyList<string> BlockingIssues { get; init; } = [];

    [JsonIgnore]
    public bool CanProceed => BlockingIssues.Count == 0 && AssetUrl is not null;

    /// <summary>
    /// True for a plan that RepoDeck understands but deliberately will not carry out,
    /// such as one requiring a source build.
    /// </summary>
    [JsonIgnore]
    public bool IsDeliberatelyUnsupported =>
        Strategy is InstallStrategy.SourceBuild or InstallStrategy.Unsupported;

    /// <summary>A one-line summary of the sequence, for the preview header.</summary>
    [JsonIgnore]
    public string StepSummary => Strategy switch
    {
        InstallStrategy.PortableArchive =>
            "Download → Extract → Locate executable → Register with RepoDeck",
        InstallStrategy.StandaloneExecutable =>
            "Download → Verify → Register with RepoDeck",
        InstallStrategy.WindowsInstaller =>
            "Download → Show the installer to you → You choose whether to run it",
        InstallStrategy.LinuxAppImage =>
            "Download → Mark executable → Register with RepoDeck",
        InstallStrategy.LinuxPackage =>
            "Download → Hand to the system package manager (needs root)",
        InstallStrategy.SourceBuild =>
            "Would need compiling from source, which RepoDeck does not do",
        _ => "No installation route identified"
    };

    public static InstallPlan NotPossible(
        string owner, string name, string url, InstallStrategy strategy, params string[] blockers) => new()
    {
        Owner = owner,
        Name = name,
        RepositoryUrl = url,
        Strategy = strategy,
        LaunchStrategy = LaunchStrategy.NotLaunchable,
        Confidence = Confidence.Confirmed,
        BlockingIssues = blockers
    };
}
