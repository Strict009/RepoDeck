using System.Diagnostics;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed record LaunchResult
{
    public required bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }

    public static LaunchResult Ok() => new() { Succeeded = true };
    public static LaunchResult Failed(string message) => new() { Succeeded = false, ErrorMessage = message };
}

/// <summary>
/// Starts an installed application.
/// </summary>
/// <remarks>
/// The only place in RepoDeck that runs downloaded software, and it does so only when
/// the user presses Run. Before starting anything it re-confirms that the executable is
/// the one recorded in the manifest, that it still exists, and that it sits inside
/// RepoDeck's own Apps folder. Elevation is never requested: if a program needs
/// administrator rights it will ask for them itself, which is the operating system's
/// decision to present, not RepoDeck's to make.
/// </remarks>
public sealed class LaunchService
{
    private readonly AppPaths _paths;
    private readonly IInstalledAppStore _store;
    private readonly IAppLog _log;
    private readonly TimeProvider _time;

    public LaunchService(
        AppPaths paths, IInstalledAppStore store, IAppLog log, TimeProvider? timeProvider = null)
    {
        _paths = paths;
        _store = store;
        _log = log;
        _time = timeProvider ?? TimeProvider.System;
    }

    public LaunchResult Launch(ApplicationManifest manifest)
    {
        if (manifest.IsDownloadOnly)
        {
            return LaunchResult.Failed(
                "RepoDeck downloaded this but did not install it, because it is a system installer. "
                + "Open the containing folder and run it yourself if you want to.");
        }

        var executable = manifest.ExecutablePath;

        if (string.IsNullOrWhiteSpace(executable))
        {
            return LaunchResult.Failed(
                "RepoDeck did not identify a program to run inside this download.");
        }

        var full = Path.GetFullPath(executable);

        // Re-checked at launch, not just at install: the manifest is a file on disk and
        // could have been edited between the two.
        if (!ArchivePathGuard.IsInside(_paths.Apps, full))
        {
            _log.Error("Launch",
                $"Refused to run {full} for {manifest.Id}: outside RepoDeck's managed folder.");

            return LaunchResult.Failed(
                "RepoDeck will only run programs inside its own folder, and this one is not.");
        }

        if (!File.Exists(full))
        {
            _log.Warn("Launch", $"{manifest.Id} is registered but {full} is missing.");
            return LaunchResult.Failed(
                "The program is missing. It may have been moved or deleted since it was installed.");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = full,
                WorkingDirectory = Path.GetDirectoryName(full)!,

                // The shell handles file associations and per-application manifests.
                // It does not elevate: RepoDeck never sets Verb to "runas".
                UseShellExecute = true
            });

            RecordRun(manifest);
            _log.Info("Launch", $"Started {manifest.Id} from {full}");
            return LaunchResult.Ok();
        }
        catch (Exception ex)
        {
            _log.Error("Launch", $"Could not start {manifest.Id} from {full}", ex);
            return LaunchResult.Failed(
                "The program would not start. The log file has the details.");
        }
    }

    /// <summary>True when the recorded executable is still where the manifest says.</summary>
    public bool IsIntact(ApplicationManifest manifest)
    {
        if (manifest.IsDownloadOnly)
        {
            return manifest.DownloadedFilePath is { Length: > 0 } path && File.Exists(path);
        }

        return manifest.ExecutablePath is { Length: > 0 } executable && File.Exists(executable);
    }

    private void RecordRun(ApplicationManifest manifest)
    {
        try
        {
            _store.Save(manifest with
            {
                LastRunAt = _time.GetUtcNow(),
                RunCount = manifest.RunCount + 1
            });
        }
        catch (Exception ex)
        {
            // Failing to record a run must never stop the program from starting.
            _log.Warn("Launch", $"Could not record the run of {manifest.Id}: {ex.Message}");
        }
    }
}
