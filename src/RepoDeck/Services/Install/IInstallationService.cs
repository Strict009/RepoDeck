using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed record InstallationResult
{
    public required bool Succeeded { get; init; }
    public ApplicationManifest? Manifest { get; init; }

    /// <summary>Plain English, safe to show. Null on success.</summary>
    public string? ErrorMessage { get; init; }

    public bool WasCancelled { get; init; }

    /// <summary>
    /// True when several files looked like plausible programs. The UI says so rather than
    /// presenting a guess as a decision.
    /// </summary>
    public bool ExecutableIsAmbiguous { get; init; }

    /// <summary>How the executable was chosen, when that is worth explaining.</summary>
    public string? ExecutableNote { get; init; }

    /// <summary>
    /// True when RepoDeck downloaded the file but deliberately did not install it,
    /// because running a system installer is the user's decision, not RepoDeck's.
    /// </summary>
    public bool DownloadedOnly { get; init; }

    public static InstallationResult Failed(string message) =>
        new() { Succeeded = false, ErrorMessage = message };

    public static InstallationResult Cancelled() =>
        new() { Succeeded = false, WasCancelled = true };
}

/// <summary>
/// Carries out an <see cref="InstallPlan"/>.
/// </summary>
/// <remarks>
/// This is the only class in RepoDeck that acts on a plan, and it re-decides nothing:
/// every choice was made during analysis. It never runs what it downloads. A plan for a
/// Windows installer or a Linux package results in a download and a note telling the
/// user where the file is - RepoDeck does not run installers on anyone's behalf, and
/// never requests elevation.
/// </remarks>
public interface IInstallationService
{
    Task<InstallationResult> InstallAsync(
        InstallPlan plan,
        IProgress<InstallationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an installation. Only ever deletes inside RepoDeck's own Apps folder.
    /// </summary>
    Task<bool> UninstallAsync(ApplicationManifest manifest, CancellationToken cancellationToken = default);
}
