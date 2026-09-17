namespace RepoDeck.Models;

/// <summary>
/// What a repository's file listing says about how it is built.
/// </summary>
/// <remarks>
/// Produced in two passes. The first reads the file listing alone, which is one GitHub
/// request for the whole repository. The second reads a small number of manifest files
/// named in <see cref="FilesWorthReading"/> - only where their contents would actually
/// change a conclusion, because each one costs another request.
/// </remarks>
public sealed record ProjectStructure
{
    public IReadOnlyList<ProjectType> ProjectTypes { get; init; } = [];
    public BuildSystem BuildSystem { get; init; } = BuildSystem.Unknown;

    /// <summary>Frameworks and runtimes recognised, e.g. "Avalonia", "Electron", "WPF".</summary>
    public IReadOnlyList<string> Frameworks { get; init; } = [];

    public IReadOnlyList<Evidence> Evidence { get; init; } = [];

    /// <summary>Manifest files whose contents would sharpen the classification.</summary>
    public IReadOnlyList<string> FilesWorthReading { get; init; } = [];

    /// <summary>Set when the file listing itself was incomplete, so absence proves nothing.</summary>
    public bool ListingWasTruncated { get; init; }

    /// <summary>Hints about what kind of thing this is, gathered while reading structure.</summary>
    public IReadOnlyList<ApplicationTypeHint> ApplicationHints { get; init; } = [];

    public ProjectType PrimaryProjectType => ProjectTypes.Count > 0 ? ProjectTypes[0] : ProjectType.Unknown;

    public bool IsEmpty => ProjectTypes.Count == 0 && Frameworks.Count == 0;

    public static ProjectStructure Unknown { get; } = new();
}

/// <summary>
/// A weighted vote for what kind of project this is, with the observation behind it.
/// Votes are combined rather than letting the first rule that fires decide.
/// </summary>
public sealed record ApplicationTypeHint(ApplicationType Type, int Weight, Evidence Evidence);
