using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed partial class InstallationService
{
    /// <summary>A guard against a pathological archive turning executable discovery into a crawl.</summary>
    private const int MaxScannedFiles = 20_000;

    /// <summary>Enough to outlast a scanner holding a handle, not enough to hang the page.</summary>
    private const int DeleteAttempts = 3;
    private const int DeleteRetryDelayMs = 250;

    public Task<bool> UninstallAsync(
        ApplicationManifest manifest, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult(manifest.State == InstallationState.Downloaded
                ? RemoveDownloadedFile(manifest)
                : RemoveInstallation(manifest));
        }
        catch (Exception ex)
        {
            _log.Error("Install", $"Could not uninstall {manifest.Id}", ex);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Removes an installation, having first established that the path in the manifest is
    /// somewhere RepoDeck is actually allowed to delete.
    /// </summary>
    /// <remarks>
    /// The manifest is a JSON file on the user's disk. Something other than RepoDeck can
    /// edit it - a mistake, a broken sync, or somebody who thinks pointing it at a system folder
    /// would be funny - so its contents are treated as a claim to be checked rather than a
    /// fact to be acted on. Every path is resolved and re-tested against the managed root
    /// at the moment of use, and a record that fails is refused and left alone rather than
    /// quietly dropped: deleting the record of an installation RepoDeck would not touch
    /// would lose the evidence that something is wrong.
    /// </remarks>
    private bool RemoveInstallation(ApplicationManifest manifest)
    {
        string directory;

        try
        {
            directory = Path.GetFullPath(manifest.InstalledPath);
        }
        catch (Exception ex)
        {
            _log.Error("Install",
                $"Refused to uninstall {manifest.Id}: its recorded path is unusable ({ex.Message}).");
            return false;
        }

        if (!ArchivePathGuard.IsInside(_paths.Apps, directory))
        {
            // Should be impossible, which is exactly why it is checked again here.
            _log.Error("Install",
                $"Refused to uninstall {manifest.Id}: {directory} is outside RepoDeck's folder.");
            return false;
        }

        // The managed root itself is not an application, and neither are the folders
        // RepoDeck uses for its own working copies.
        if (IsManagedRootOrReserved(directory))
        {
            _log.Error("Install",
                $"Refused to uninstall {manifest.Id}: {directory} is one of RepoDeck's own folders.");
            return false;
        }

        if (Directory.Exists(directory) && !DeleteManagedDirectory(directory))
        {
            // Same rule as a refused path: the record stays. An application whose files
            // are still on disk is not uninstalled, and dropping the record here would
            // leave the user with files nothing knows about and no way to try again.
            _log.Error("Install",
                $"Could not uninstall {manifest.Id}: {directory} could not be removed. "
                + "The record has been kept.");

            return false;
        }

        _store.Remove(manifest.Owner, manifest.Name);
        _history.Record(LifecycleEvent.Uninstalled(manifest));
        _log.Info("Install", $"Uninstalled {manifest.Id}");
        return true;
    }

    /// <summary>
    /// True for the Apps folder itself and for RepoDeck's working directories inside it.
    /// An installation is always a folder beneath Apps, never Apps and never staging.
    /// </summary>
    private bool IsManagedRootOrReserved(string directory)
    {
        var apps = Path.GetFullPath(_paths.Apps).TrimEnd(Path.DirectorySeparatorChar);
        var candidate = directory.TrimEnd(Path.DirectorySeparatorChar);

        if (string.Equals(apps, candidate, StringComparison.OrdinalIgnoreCase)) return true;

        string[] reserved = [StagingFolderName, ".rollback"];

        foreach (var name in reserved)
        {
            var root = Path.GetFullPath(Path.Combine(apps, name)).TrimEnd(Path.DirectorySeparatorChar);

            if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            if (ArchivePathGuard.IsInside(root, candidate)) return true;
        }

        return false;
    }

    /// <summary>
    /// A downloaded installer owns no application folder - just the file RepoDeck fetched,
    /// which lives in Downloads and is removed with the record.
    /// </summary>
    private bool RemoveDownloadedFile(ApplicationManifest manifest)
    {
        if (manifest.DownloadedFilePath is { Length: > 0 } file)
        {
            var full = Path.GetFullPath(file);

            if (ArchivePathGuard.IsInside(_paths.Downloads, full))
            {
                TryDeleteFile(full);
            }
            else
            {
                _log.Warn("Install",
                    $"Left {full} alone while forgetting {manifest.Id}: it is outside the Downloads folder.");
            }
        }

        _store.Remove(manifest.Owner, manifest.Name);
        _history.Record(LifecycleEvent.Uninstalled(manifest));
        _log.Info("Install", $"Forgot the downloaded file for {manifest.Id}");
        return true;
    }

    /// <summary>
    /// Deletes a directory RepoDeck owns. Callers have already established that the path
    /// is inside Apps; this re-checks anyway, because the cost of being wrong here is
    /// somebody's files.
    /// </summary>
    /// <summary>
    /// Deletes a directory RepoDeck owns, and then checks that it is actually gone.
    /// </summary>
    /// <remarks>
    /// The check is not paranoia. On Windows a recursive delete can return successfully
    /// while the removal is still pending, because another process holds a handle to
    /// something inside - a virus scanner reading a freshly written executable, a second
    /// copy of RepoDeck, a file browser with the folder open. A live uninstall hit exactly
    /// that: RepoDeck reported success, dropped the record, and left 69 MB on disk that
    /// nothing was tracking any more.
    ///
    /// So the result is verified rather than assumed, with a short retry for the pending
    /// case, and the caller is told the truth either way.
    /// </remarks>
    private bool DeleteManagedDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);

        if (!ArchivePathGuard.IsInside(_paths.Apps, full))
        {
            throw new InvalidOperationException(
                $"Refused to delete a directory outside RepoDeck's managed folder: {full}");
        }

        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
            }
            catch (Exception ex) when (attempt < DeleteAttempts)
            {
                _log.Warn("Install",
                    $"Could not remove {full} (attempt {attempt}): {ex.Message}");
            }

            // The only answer that counts. A delete that returned without throwing has
            // still not removed anything if the directory is standing there afterwards.
            if (!Directory.Exists(full)) return true;

            if (attempt < DeleteAttempts) Thread.Sleep(DeleteRetryDelayMs);
        }

        return !Directory.Exists(full);
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _log.Warn("Install", $"Could not remove {path}: {ex.Message}");
        }
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
