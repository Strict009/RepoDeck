using System.Diagnostics;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

/// <summary>Whether a managed application appears to be running right now.</summary>
public interface IRunningApplicationDetector
{
    /// <summary>
    /// True when a process appears to be running from inside this installation's folder.
    /// </summary>
    bool IsRunning(ApplicationManifest manifest);
}

/// <summary>
/// Looks for a running process whose executable lives inside a managed installation.
/// </summary>
/// <remarks>
/// RepoDeck never closes anything. This exists so it can say "close Sharpemu before
/// updating it" instead of replacing files underneath a running program and leaving the
/// user with a half-updated application and an error they cannot act on.
///
/// The check is best-effort by nature. Reading another process's executable path needs
/// permissions that are not always granted, processes start and stop between the check and
/// the swap, and on Linux a program may be running from a path that no longer matches. A
/// false negative therefore has to be survivable, and it is: the file move fails, and the
/// rollback puts the previous version back. This turns the common case into a clear
/// sentence rather than being the thing that makes updates safe.
///
/// A failure to determine anything is treated as "not running". Refusing to update because
/// RepoDeck could not read the process table would block updates on locked-down machines
/// for no benefit, since the swap itself is already protected.
/// </remarks>
public sealed class RunningApplicationDetector : IRunningApplicationDetector
{
    private readonly IAppLog _log;

    public RunningApplicationDetector(IAppLog log)
    {
        _log = log;
    }

    public bool IsRunning(ApplicationManifest manifest)
    {
        if (!manifest.IsRunnableInstallation) return false;

        string root;

        try
        {
            root = Path.GetFullPath(manifest.InstalledPath);
        }
        catch
        {
            return false;
        }

        if (!Directory.Exists(root)) return false;

        // Matching on the executable's file name first keeps this cheap: reading every
        // process's module path is expensive and often refused, so it is only done for
        // processes whose name already looks like the right one.
        var executable = manifest.ExecutableRelativePath is { Length: > 0 } relative
            ? Path.GetFileNameWithoutExtension(relative)
            : null;

        if (string.IsNullOrWhiteSpace(executable)) return false;

        Process[] processes;

        try
        {
            processes = Process.GetProcessesByName(executable);
        }
        catch (Exception ex)
        {
            _log.Warn("Update", $"Could not look for running copies of {manifest.Id}: {ex.Message}");
            return false;
        }

        try
        {
            foreach (var process in processes)
            {
                if (RunsFrom(process, root)) return true;
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        return false;
    }

    private bool RunsFrom(Process process, string root)
    {
        try
        {
            var path = process.MainModule?.FileName;

            if (string.IsNullOrWhiteSpace(path)) return false;

            return ArchivePathGuard.IsInside(root, Path.GetFullPath(path));
        }
        catch (Exception ex)
        {
            // Reading another process's module path is commonly refused. A process RepoDeck
            // cannot see into is not evidence of anything either way.
            _log.Info("Update", $"Could not read the path of process {process.Id}: {ex.Message}");
            return false;
        }
    }
}
