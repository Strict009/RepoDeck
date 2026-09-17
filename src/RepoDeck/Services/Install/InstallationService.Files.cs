using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed partial class InstallationService
{
    /// <summary>A guard against a pathological archive turning executable discovery into a crawl.</summary>
    private const int MaxScannedFiles = 20_000;

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

    private bool RemoveInstallation(ApplicationManifest manifest)
    {
        var directory = Path.GetFullPath(manifest.InstalledPath);

        if (!ArchivePathGuard.IsInside(_paths.Apps, directory))
        {
            // Should be impossible, which is exactly why it is checked again here.
            _log.Error("Install",
                $"Refused to uninstall {manifest.Id}: {directory} is outside RepoDeck's folder.");
            return false;
        }

        if (Directory.Exists(directory)) DeleteManagedDirectory(directory);

        _store.Remove(manifest.Owner, manifest.Name);
        _log.Info("Install", $"Uninstalled {manifest.Id}");
        return true;
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
        _log.Info("Install", $"Forgot the downloaded file for {manifest.Id}");
        return true;
    }

    /// <summary>
    /// Deletes a directory RepoDeck owns. Callers have already established that the path
    /// is inside Apps; this re-checks anyway, because the cost of being wrong here is
    /// somebody's files.
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
