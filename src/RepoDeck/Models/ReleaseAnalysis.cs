namespace RepoDeck.Models;

/// <summary>How well one release asset suits a particular machine.</summary>
public enum AssetCompatibility
{
    /// <summary>Platform and architecture both match explicitly.</summary>
    Compatible,

    /// <summary>Probably runs here, but something was inferred rather than stated.</summary>
    LikelyCompatible,

    /// <summary>Would run, but through emulation rather than natively.</summary>
    CompatibleThroughEmulation,

    /// <summary>Not enough in the name to tell. Must not be presented as compatible.</summary>
    Unknown,

    /// <summary>Positively for a different platform or architecture.</summary>
    Incompatible,

    /// <summary>Not software: source code, a checksum, a signature.</summary>
    NotApplicable
}

/// <summary>
/// One release asset, as RepoDeck reads it.
/// </summary>
/// <remarks>
/// The score exists so ranking is deterministic and testable. It is deliberately not
/// shown to the user: the reasons are what get displayed, because an unexplained number
/// is not an explanation.
/// </remarks>
public sealed record AssetAnalysis
{
    public required string Name { get; init; }
    public required string DownloadUrl { get; init; }
    public long Size { get; init; }

    public PackageType PackageType { get; init; } = PackageType.Unknown;
    public OsPlatform Platform { get; init; } = OsPlatform.Unknown;
    public CpuArchitecture Architecture { get; init; } = CpuArchitecture.Unknown;

    /// <summary>True when the platform came from the file extension or an explicit token.</summary>
    public bool PlatformIsExplicit { get; init; }
    public bool ArchitectureIsExplicit { get; init; }

    public bool IsSourceArchive => PackageType == PackageType.SourceArchive;
    public bool IsMetadataFile => PackageType == PackageType.Metadata;
    public bool IsPortable { get; init; }
    public bool RequiresElevation { get; init; }

    public AssetCompatibility Compatibility { get; init; } = AssetCompatibility.Unknown;

    /// <summary>Internal ranking value. Not displayed.</summary>
    public int Score { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool IsUsable => Compatibility is
        AssetCompatibility.Compatible or AssetCompatibility.LikelyCompatible or
        AssetCompatibility.CompatibleThroughEmulation;
}

/// <summary>The result of examining a release and ranking its assets for one machine.</summary>
public sealed record ReleaseAnalysis
{
    public GitHubRelease? Release { get; init; }

    /// <summary>Every asset, ranked best first.</summary>
    public IReadOnlyList<AssetAnalysis> Assets { get; init; } = [];

    /// <summary>The asset RepoDeck would download, or null when nothing here suits this machine.</summary>
    public AssetAnalysis? Recommended { get; init; }

    /// <summary>Why no asset could be recommended. Null when one was.</summary>
    public string? NoRecommendationReason { get; init; }

    public bool HasRelease => Release is not null;
    public bool HasRecommendation => Recommended is not null;

    /// <summary>Assets that are software rather than source archives, checksums or signatures.</summary>
    public IReadOnlyList<AssetAnalysis> SoftwareAssets =>
        Assets.Where(a => !a.IsSourceArchive && !a.IsMetadataFile).ToList();

    public bool HasAnyBinary => SoftwareAssets.Count > 0;

    public static ReleaseAnalysis None(string reason) => new() { NoRecommendationReason = reason };
}
