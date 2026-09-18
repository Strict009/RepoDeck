using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.Services.Update;

public sealed partial class UpdateService
{
    private const int MaxScannedFiles = 20_000;

    /// <summary>
    /// A directory RepoDeck is building in. Disposing it removes whatever is left, so an
    /// abandoned attempt cleans up after itself whatever went wrong - including a thrown
    /// exception on a path nobody thought about.
    /// </summary>
    private sealed class ManagedDirectory : IDisposable
    {
        private readonly IAppLog _log;
        private readonly string _root;
        private bool _kept;

        public ManagedDirectory(string path, IAppLog log, string root)
        {
            Path = path;
            _log = log;
            _root = root;

            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        /// <summary>Called once the contents have been moved somewhere permanent.</summary>
        public void Keep() => _kept = true;

        public void Dispose()
        {
            if (_kept) return;

            try
            {
                if (!Directory.Exists(Path)) return;

                if (!ArchivePathGuard.IsInside(_root, System.IO.Path.GetFullPath(Path))) return;

                Directory.Delete(Path, recursive: true);
            }
            catch (Exception ex)
            {
                _log.Warn("Update", $"Could not clean up {Path}: {ex.Message}");
            }
        }
    }

    private string NewStagingPath()
    {
        var root = Path.Combine(_paths.Apps, InstallationService.StagingFolderName);
        Directory.CreateDirectory(root);

        return Path.Combine(root, "update-" + Guid.NewGuid().ToString("N"));
    }

    private string NewRollbackPath(UpdatePlan plan)
    {
        var root = Path.Combine(_paths.Apps, RollbackFolderName);
        Directory.CreateDirectory(root);

        var name = Path.GetFileName(plan.ProposedInstallDirectory.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name)) name = "installation";

        return Path.Combine(root, $"{name}-{Guid.NewGuid().ToString("N")[..8]}");
    }

    /// <summary>
    /// Unpacks or copies the new version into the staging directory. Identical in shape to
    /// the install service's handling, deliberately: an update that assembled files
    /// differently from an install would be a second way for the same thing to be wrong.
    /// </summary>
    private async Task AssembleAsync(
        UpdatePlan plan,
        DownloadedFile download,
        string staging,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (plan.InstallStrategy == InstallStrategy.PortableArchive)
        {
            var detail = new Progress<string>(d =>
                progress?.Report(new UpdateProgress { Stage = UpdateStage.Preparing, Detail = d }));

            var extraction = await _extraction
                .ExtractAsync(download.Path, staging, plan.PackageType, detail, cancellationToken)
                .ConfigureAwait(false);

            if (extraction.RefusedEntries.Count > 0)
            {
                _log.Warn("Update",
                    $"{extraction.RefusedEntries.Count} entries in {plan.AssetName} were refused "
                    + "because they tried to write outside the installation folder.");
            }

            return;
        }

        // A single file that is itself the program. Copied rather than moved, so a failure
        // before promotion leaves the download intact.
        var fileName = Path.GetFileName(download.Path);
        var target = Path.Combine(staging, fileName);

        await using (var input = new FileStream(
            download.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        await using (var output = new FileStream(
            target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        TryMakeExecutable(target);
    }

    /// <summary>
    /// Finds the program in the staged copy.
    /// </summary>
    /// <remarks>
    /// Falls back to the machine RepoDeck is running on when the plan does not record a
    /// platform. An older manifest, or an asset whose name never said which platform it
    /// was for, leaves it Unknown - and with Unknown the locator rejects every Windows
    /// executable, so a repair of such an installation could never find anything. The
    /// machine doing the repairing is the right answer to "which platform".
    /// </remarks>
    private ExecutableSelection Locate(UpdatePlan plan, string directory)
    {
        var platform = plan.Platform == OsPlatform.Unknown ? _machine.OperatingSystem : plan.Platform;

        var files = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Take(MaxScannedFiles)
            .Select(path => new CandidateFile(
                Path.GetRelativePath(directory, path),
                HasExecutableBit(path),
                SafeLength(path)))
            .ToList();

        return ExecutableLocator.Select(files, plan.ExecutableCandidates, plan.Name, platform);
    }

    /// <summary>
    /// Checks the promoted installation is really there and really contains what was
    /// staged. The move reported success; this confirms the file system agrees.
    /// </summary>
    private static bool ValidateLive(string live, ExecutableSelection selection, out string failure)
    {
        if (!Directory.Exists(live))
        {
            failure = "The updated files are not where RepoDeck put them.";
            return false;
        }

        // An ambiguous selection has no single expected file, so the directory existing
        // and not being empty is as much as can honestly be checked.
        if (selection.IsAmbiguous || selection.Chosen is null)
        {
            if (!Directory.EnumerateFileSystemEntries(live).Any())
            {
                failure = "The updated installation folder is empty.";
                return false;
            }

            failure = "";
            return true;
        }

        var executable = Path.Combine(live, selection.Chosen);

        if (!File.Exists(executable))
        {
            failure = $"The updated installation does not contain {selection.Chosen}.";
            return false;
        }

        failure = "";
        return true;
    }

    private ApplicationManifest BuildManifest(
        UpdatePlan plan,
        DownloadedFile download,
        string directory,
        ExecutableSelection selection,
        ApplicationManifest previous) =>
        previous with
        {
            // Provenance moves to the new release; everything about where it lives and how
            // long the user has had it is carried forward.
            ReleaseTag = plan.TargetReleaseTag,
            ReleaseName = plan.TargetReleaseName,
            ReleaseId = plan.TargetReleaseId,
            ReleasePublishedAt = plan.TargetPublishedAt,
            IsPrerelease = plan.TargetIsPrerelease,

            AssetName = plan.AssetName,
            AssetDownloadUrl = plan.AssetUrl,
            AssetSize = download.Size,
            AssetSha256 = download.Sha256,

            State = selection.IsAmbiguous
                ? InstallationState.AwaitingExecutableChoice
                : InstallationState.Installed,

            InstalledPath = directory,
            ExecutableRelativePath = selection.IsAmbiguous ? null : selection.Chosen,
            AlternativeExecutables = selection.IsAmbiguous && selection.Chosen is not null
                ? new[] { selection.Chosen }.Concat(selection.Alternatives).ToList()
                : selection.Alternatives,
            ExecutableIsAmbiguous = selection.IsAmbiguous,
            OwnedEntries = TopLevelEntries(directory),

            Platform = plan.Platform,
            Architecture = plan.Architecture,
            PackageType = plan.PackageType,
            Strategy = plan.InstallStrategy,

            NotInstalledReason = selection.IsAmbiguous
                ? "RepoDeck found multiple possible application executables."
                : null,

            // The install date is when this application arrived, not when it last changed.
            UpdatedAt = _time.GetUtcNow()
        };

    /// <summary>
    /// A plan shaped for the download service. The update planner already chose the asset;
    /// this only carries it across, and re-decides nothing.
    /// </summary>
    private static InstallPlan AsInstallPlan(UpdatePlan plan) => new()
    {
        Owner = plan.Owner,
        Name = plan.Name,
        RepositoryUrl = plan.Current.RepositoryUrl,
        RepositoryId = plan.Current.RepositoryId,
        ReleaseTag = plan.TargetReleaseTag,
        ReleaseName = plan.TargetReleaseName,
        ReleaseId = plan.TargetReleaseId,
        ReleasePublishedAt = plan.TargetPublishedAt,
        IsPrerelease = plan.TargetIsPrerelease,
        AssetName = plan.AssetName,
        AssetUrl = plan.AssetUrl,
        AssetSize = plan.AssetSize,
        Platform = plan.Platform,
        Architecture = plan.Architecture,
        PackageType = plan.PackageType,
        Strategy = plan.InstallStrategy,
        ProposedInstallDirectory = plan.ProposedInstallDirectory,
        RequiresExtraction = plan.PackageType.RequiresExtraction(),
        ExecutableCandidates = plan.ExecutableCandidates,
        Confidence = plan.Confidence
    };

    /// <summary>
    /// Confirms a path is somewhere RepoDeck is allowed to act, whatever a manifest says.
    /// </summary>
    private void Guard(string path, string what)
    {
        if (!ArchivePathGuard.IsInside(_paths.Apps, Path.GetFullPath(path)))
        {
            throw new InvalidOperationException(
                $"Refused to {what} outside RepoDeck's managed folder: {path}");
        }
    }

    private void DeleteManaged(string directory)
    {
        var full = Path.GetFullPath(directory);
        Guard(full, "delete");

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
            _log.Warn("Update", $"Could not remove {path}: {ex.Message}");
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
            _log.Warn("Update", $"Could not mark {path} as executable: {ex.Message}");
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

    private static IReadOnlyList<string> TopLevelEntries(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory)
                .Select(Path.GetFileName)
                .Where(n => n is { Length: > 0 })
                .Select(n => n!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
