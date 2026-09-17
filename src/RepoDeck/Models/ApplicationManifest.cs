using System.Text.Json.Serialization;

namespace RepoDeck.Models;

/// <summary>
/// Whether RepoDeck installed something or merely fetched it.
/// </summary>
/// <remarks>
/// The distinction is deliberate and load-bearing. A downloaded installer is not an
/// installed application: RepoDeck has a file on disk and nothing more. Conflating the
/// two would let a successful download masquerade as working software.
/// </remarks>
public enum InstallationState
{
    /// <summary>Unpacked into RepoDeck's managed folder and ready to run.</summary>
    Installed,

    /// <summary>
    /// The asset was obtained and preserved, but RepoDeck could not establish a runnable
    /// application from it - a system installer it will not run, or an archive with
    /// nothing runnable inside.
    /// </summary>
    Downloaded,

    /// <summary>
    /// The files are installed, but several looked like the program and RepoDeck will not
    /// guess. Runnable once the user says which one it is.
    /// </summary>
    AwaitingExecutableChoice
}

/// <summary>
/// RepoDeck's record of one installed or downloaded application.
/// </summary>
/// <remarks>
/// This is RepoDeck's own bookkeeping, not the application's. It is written only after an
/// installation has been promoted out of staging, and is the only thing consulted
/// afterwards, so the Installed page never has to contact GitHub to know what is on the
/// machine. Provenance is recorded in full - repository id, release id, asset url - so a
/// later milestone can check for updates without re-deriving any of it. No token or
/// secret is ever stored here.
/// </remarks>
public sealed record ApplicationManifest
{
    // ---- Identity ---------------------------------------------------------
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string RepositoryUrl { get; init; }

    /// <summary>GitHub's numeric repository id, stable across renames.</summary>
    public long RepositoryId { get; init; }

    [JsonIgnore]
    public string Id => $"{Owner}/{Name}";

    // ---- Provenance -------------------------------------------------------
    public string? ReleaseTag { get; init; }
    public string? ReleaseName { get; init; }

    /// <summary>GitHub's numeric release id, when it was known.</summary>
    public long? ReleaseId { get; init; }

    public DateTimeOffset? ReleasePublishedAt { get; init; }
    public bool IsPrerelease { get; init; }

    public string? AssetName { get; init; }

    /// <summary>The canonical GitHub download address the file came from.</summary>
    public string? AssetDownloadUrl { get; init; }

    public long AssetSize { get; init; }

    /// <summary>SHA-256 of the file as received, so later checks can detect a change.</summary>
    public string? AssetSha256 { get; init; }

    // ---- What is on disk --------------------------------------------------
    public InstallationState State { get; init; } = InstallationState.Installed;

    /// <summary>
    /// Absolute path of the folder RepoDeck owns for this application. Always inside the
    /// Apps directory, and re-checked against it at every use rather than trusted.
    /// </summary>
    public required string InstalledPath { get; init; }

    /// <summary>
    /// The executable, relative to <see cref="InstalledPath"/>. Stored relative so the
    /// record survives RepoDeck's folder moving between machines or users.
    /// </summary>
    public string? ExecutableRelativePath { get; init; }

    /// <summary>Other plausible executables, relative to <see cref="InstalledPath"/>.</summary>
    public IReadOnlyList<string> AlternativeExecutables { get; init; } = [];

    /// <summary>
    /// True when several candidates were plausible and RepoDeck is not confident it
    /// picked the right one. The UI says so rather than pretending otherwise.
    /// </summary>
    public bool ExecutableIsAmbiguous { get; init; }

    /// <summary>
    /// Top-level entries inside <see cref="InstalledPath"/> that RepoDeck created, so an
    /// uninstall knows exactly what it owns.
    /// </summary>
    public IReadOnlyList<string> OwnedEntries { get; init; } = [];

    /// <summary>The fetched file itself, for a <see cref="InstallationState.Downloaded"/> record.</summary>
    public string? DownloadedFilePath { get; init; }

    /// <summary>
    /// Why this was not established as a runnable application. Shown verbatim on the
    /// Downloads page, so it has to read as an explanation rather than an error code.
    /// </summary>
    public string? NotInstalledReason { get; init; }

    // ---- Classification ---------------------------------------------------
    public OsPlatform Platform { get; init; } = OsPlatform.Unknown;
    public CpuArchitecture Architecture { get; init; } = CpuArchitecture.Unknown;
    public PackageType PackageType { get; init; } = PackageType.Unknown;
    public InstallStrategy Strategy { get; init; } = InstallStrategy.Unknown;
    public LaunchStrategy LaunchStrategy { get; init; } = LaunchStrategy.Unknown;

    // ---- History ----------------------------------------------------------
    public DateTimeOffset InstalledAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public int RunCount { get; init; }

    // ---- Derived ----------------------------------------------------------

    /// <summary>True when the file was fetched but no runnable application was established.</summary>
    [JsonIgnore]
    public bool IsDownloadOnly => State == InstallationState.Downloaded;

    /// <summary>Installed files are present, but which one to run is still unresolved.</summary>
    [JsonIgnore]
    public bool NeedsExecutableChoice => State == InstallationState.AwaitingExecutableChoice;

    /// <summary>
    /// True only when RepoDeck established a runnable application. This is what the
    /// Installed library means, and what Run depends on.
    /// </summary>
    [JsonIgnore]
    public bool IsRunnableInstallation =>
        State == InstallationState.Installed && ExecutableRelativePath is { Length: > 0 };

    /// <summary>Absolute path of the executable, or null when there is none.</summary>
    [JsonIgnore]
    public string? ExecutablePath => ExecutableRelativePath is { Length: > 0 } relative
        ? Path.Combine(InstalledPath, relative)
        : null;

    [JsonIgnore]
    public bool HasExecutable => ExecutablePath is not null;

    [JsonIgnore]
    public string DisplayVersion => ReleaseTag ?? ReleaseName ?? "unknown version";
}
