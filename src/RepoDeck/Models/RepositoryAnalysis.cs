namespace RepoDeck.Models;

/// <summary>
/// What RepoDeck concluded about a repository, and why.
/// </summary>
/// <remarks>
/// A structured result rather than a bag of strings: the install planner consumes this
/// directly and must never have to re-parse prose to find out what it is dealing with.
/// Anything RepoDeck could not establish stays <see cref="Confidence.Unknown"/> and is
/// listed in <see cref="Unknowns"/> rather than being guessed at.
/// </remarks>
public sealed record RepositoryAnalysis
{
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string RepositoryUrl { get; init; }

    /// <summary>Build ecosystems detected, most significant first.</summary>
    public IReadOnlyList<ProjectType> ProjectTypes { get; init; } = [];

    public ProjectType PrimaryProjectType => ProjectTypes.Count > 0 ? ProjectTypes[0] : ProjectType.Unknown;

    public ApplicationType ApplicationType { get; init; } = ApplicationType.Unknown;
    public Confidence ApplicationTypeConfidence { get; init; } = Confidence.Unknown;

    public string? PrimaryLanguage { get; init; }

    /// <summary>Frameworks and runtimes detected, e.g. "Avalonia", "Electron", "WPF".</summary>
    public IReadOnlyList<string> Frameworks { get; init; } = [];

    public BuildSystem BuildSystem { get; init; } = BuildSystem.Unknown;

    /// <summary>Platforms the project appears to support, with how sure RepoDeck is of each.</summary>
    public IReadOnlyDictionary<OsPlatform, Confidence> SupportedPlatforms { get; init; } =
        new Dictionary<OsPlatform, Confidence>();

    public IReadOnlyList<CpuArchitecture> SupportedArchitectures { get; init; } = [];

    public bool HasReleases { get; init; }
    public bool HasDownloadableBinaries { get; init; }

    public InstallStrategy InstallStrategy { get; init; } = InstallStrategy.Unknown;
    public LaunchStrategy LaunchStrategy { get; init; } = LaunchStrategy.Unknown;

    /// <summary>Overall confidence in the classification as a whole.</summary>
    public Confidence Confidence { get; init; } = Confidence.Unknown;

    public IReadOnlyList<Evidence> Evidence { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Things RepoDeck specifically could not determine, stated plainly.</summary>
    public IReadOnlyList<string> Unknowns { get; init; } = [];

    /// <summary>
    /// False when analysis could not be finished - a rate limit, a truncated file listing
    /// or a network failure. An incomplete analysis must never be presented as confident.
    /// </summary>
    public bool IsComplete { get; init; } = true;

    public string? IncompleteReason { get; init; }

    /// <summary>Evidence that supports the conclusion.</summary>
    public IReadOnlyList<Evidence> SupportingEvidence =>
        Evidence.Where(e => e.Supports).ToList();

    /// <summary>Evidence that argues against it. Shown too - RepoDeck does not hide the awkward parts.</summary>
    public IReadOnlyList<Evidence> ContradictingEvidence =>
        Evidence.Where(e => !e.Supports).ToList();

    public bool IsRunnableApplication => ApplicationType is
        ApplicationType.DesktopApplication or ApplicationType.CliTool or
        ApplicationType.Game or ApplicationType.Server or ApplicationType.DeveloperTool;

    /// <summary>Platform support for one operating system, defaulting to unknown.</summary>
    public Confidence PlatformSupport(OsPlatform platform) =>
        SupportedPlatforms.TryGetValue(platform, out var confidence) ? confidence : Confidence.Unknown;
}
