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
                "RepoDeck downloaded this but did not install it. "
                + "Open the containing folder and run it yourself if you want to.");
        }

        if (manifest.NeedsExecutableChoice)
        {
            return LaunchResult.Failed(
                "RepoDeck found multiple possible application executables. "
                + "Choose which one to run before starting it.");
        }

        var relative = manifest.ExecutableRelativePath;

        if (string.IsNullOrWhiteSpace(relative))
        {
            return LaunchResult.Failed(
                "RepoDeck did not identify a program to run inside this download.");
        }

        // The stored path is relative, so it is resolved through the same guard that
        // protects extraction. A manifest edited to contain "..\..\something.exe" is
        // refused here rather than climbing out of the application's folder.
        var full = ArchivePathGuard.ResolveSafePath(manifest.InstalledPath, relative);

        if (full is null)
        {
            _log.Error("Launch",
                $"Refused to run {relative} for {manifest.Id}: it does not resolve inside the installation.");

            return LaunchResult.Failed(
                "RepoDeck will only run a program from inside the application's own folder.");
        }

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

                // The application's own folder, so it finds the files sitting beside it.
                WorkingDirectory = Path.GetDirectoryName(full)!,

                // Launched directly, never through the shell. ShellExecute would apply file
                // associations and verbs, which turns "run this file" into "do whatever the
                // system thinks this extension means" - and it is the route through which a
                // non-executable could end up being interpreted. Elevation is never
                // requested either: Verb is left unset.
                UseShellExecute = false

                // Arguments are deliberately never set. Nothing from a repository, a README
                // or a release description is ever passed to a process RepoDeck starts.
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

    /// <summary>True when what the manifest describes is still on disk.</summary>
    public bool IsIntact(ApplicationManifest manifest)
    {
        if (manifest.IsDownloadOnly)
        {
            return manifest.DownloadedFilePath is { Length: > 0 } path && File.Exists(path);
        }

        // An unresolved installation is intact when its files are there, even though
        // there is no executable chosen yet.
        if (manifest.NeedsExecutableChoice)
        {
            return Directory.Exists(manifest.InstalledPath);
        }

        return manifest.ExecutableRelativePath is { Length: > 0 } relative
               && ArchivePathGuard.ResolveSafePath(manifest.InstalledPath, relative) is { } full
               && File.Exists(full);
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
