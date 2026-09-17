using System.Text.Json.Serialization;

namespace RepoDeck.Models;

/// <summary>
/// RepoDeck's record of one installed application.
/// </summary>
/// <remarks>
/// This is RepoDeck's own bookkeeping, not the application's. It is written after an
/// install completes and is the only thing consulted afterwards, so the Installed page
/// never has to contact GitHub to know what is on the machine. Stored as JSON under
/// the Data folder.
/// </remarks>
public sealed record ApplicationManifest
{
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string RepositoryUrl { get; init; }

    /// <summary>Stable identity for this installation, independent of display names.</summary>
    [JsonIgnore]
    public string Id => $"{Owner}/{Name}";

    public string? ReleaseTag { get; init; }
    public string? ReleaseName { get; init; }
    public DateTimeOffset? ReleasePublishedAt { get; init; }
    public bool IsPrerelease { get; init; }

    public string? AssetName { get; init; }
    public long AssetSize { get; init; }

    /// <summary>SHA-256 of the downloaded file, recorded so a later check can detect tampering.</summary>
    public string? AssetSha256 { get; init; }

    /// <summary>Absolute path of the folder RepoDeck installed into. Always inside Apps.</summary>
    public required string InstalledPath { get; init; }

    /// <summary>Absolute path of the executable RepoDeck would run, when there is one.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Other executables found alongside it, for when the chosen one is wrong.</summary>
    public IReadOnlyList<string> AlternativeExecutables { get; init; } = [];

    public OsPlatform Platform { get; init; } = OsPlatform.Unknown;
    public CpuArchitecture Architecture { get; init; } = CpuArchitecture.Unknown;
    public PackageType PackageType { get; init; } = PackageType.Unknown;
    public InstallStrategy Strategy { get; init; } = InstallStrategy.Unknown;
    public LaunchStrategy LaunchStrategy { get; init; } = LaunchStrategy.Unknown;

    public DateTimeOffset InstalledAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public int RunCount { get; init; }

    /// <summary>
    /// True when RepoDeck downloaded the file but deliberately did not install it -
    /// a Windows installer or a Linux package, which the user runs themselves.
    /// </summary>
    public bool IsDownloadOnly { get; init; }

    /// <summary>The downloaded file itself, for download-only installations.</summary>
    public string? DownloadedFilePath { get; init; }

    [JsonIgnore]
    public bool HasExecutable => !string.IsNullOrWhiteSpace(ExecutablePath);

    [JsonIgnore]
    public string DisplayVersion => ReleaseTag ?? ReleaseName ?? "unknown version";
}
