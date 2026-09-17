using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed partial class InstallationService
{
    /// <summary>
    /// Creates the installation folder, having first confirmed it really is inside
    /// RepoDeck's Apps directory. The plan proposes a path; this refuses to trust it.
    /// </summary>
    private string EnsureManagedDirectory(InstallPlan plan)
    {
        var directory = Path.GetFullPath(plan.ProposedInstallDirectory);

        if (!ArchivePathGuard.IsInside(_paths.Apps, directory))
        {
            throw new InvalidOperationException(
                $"Refused to install outside RepoDeck's managed folder: {directory}");
        }

        // A previous attempt may have left something behind; start from a clean folder.
        if (Directory.Exists(directory)) DeleteManagedDirectory(directory);

        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Removes a half-finished installation so it cannot be mistaken for a working one.</summary>
    private void RollBack(InstallPlan plan)
    {
        try
        {
            var directory = Path.GetFullPath(plan.ProposedInstallDirectory);
            if (!ArchivePathGuard.IsInside(_paths.Apps, directory)) return;
            if (!Directory.Exists(directory)) return;

            DeleteManagedDirectory(directory);
            _log.Info("Install", $"Rolled back the failed installation at {directory}");
        }
        catch (Exception ex)
        {
            _log.Warn("Install", $"Could not roll back the failed installation: {ex.Message}");
        }
    }

    public Task<bool> UninstallAsync(
        ApplicationManifest manifest, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Download-only records own no folder, so there is nothing to delete.
            if (!manifest.IsDownloadOnly)
            {
                var directory = Path.GetFullPath(manifest.InstalledPath);

                if (!ArchivePathGuard.IsInside(_paths.Apps, directory))
                {
                    // This should be impossible, and is exactly why it is checked again.
                    _log.Error("Install",
                        $"Refused to uninstall {manifest.Id}: {directory} is outside RepoDeck's folder.");
                    return Task.FromResult(false);
                }

                if (Directory.Exists(directory)) DeleteManagedDirectory(directory);
            }

            _store.Remove(manifest.Owner, manifest.Name);
            _log.Info("Install", $"Uninstalled {manifest.Id}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _log.Error("Install", $"Could not uninstall {manifest.Id}", ex);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Deletes a directory RepoDeck owns. The caller has already established that the
    /// path is inside Apps; this re-checks anyway, because the cost of being wrong here
    /// is somebody's files.
    /// </summary>
    private void DeleteManagedDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);

        if (!ArchivePathGuard.IsInside(_paths.Apps, full))
        {
            throw new InvalidOperationException(
                $"Refused to delete a directory outside RepoDeck's managed folder: {full}");
        }

        Directory.Delete(full, recursive: true);
    }

    private void TryMakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            File.SetUnixFileMode(path,
                File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
        }
        catch (Exception ex)
        {
            _log.Warn("Install", $"Could not mark {path} as executable: {ex.Message}");
        }
    }

    private static bool HasExecutableBit(string path)
    {
        if (OperatingSystem.IsWindows()) return false;

        try
        {
            return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}
