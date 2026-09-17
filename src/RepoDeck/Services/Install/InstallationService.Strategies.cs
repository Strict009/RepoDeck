using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed partial class InstallationService
{
    private async Task<InstallationResult> CompleteAsync(
        InstallPlan plan,
        DownloadedFile download,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return plan.Strategy switch
        {
            InstallStrategy.PortableArchive =>
                await InstallArchiveAsync(plan, download, progress, cancellationToken).ConfigureAwait(false),

            InstallStrategy.StandaloneExecutable or InstallStrategy.LinuxAppImage =>
                InstallSingleFile(plan, download, progress),

            // RepoDeck fetches these and stops. Running a system installer, or handing a
            // package to a package manager, needs elevation and is the user's decision.
            InstallStrategy.WindowsInstaller or InstallStrategy.LinuxPackage =>
                RecordDownloadOnly(plan, download),

            _ => InstallationResult.Failed(
                $"RepoDeck does not know how to carry out a {plan.Strategy.ToDisplayString()} installation.")
        };
    }

    private async Task<InstallationResult> InstallArchiveAsync(
        InstallPlan plan,
        DownloadedFile download,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var directory = EnsureManagedDirectory(plan);

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Extracting });

        var extractionProgress = new Progress<string>(detail =>
            progress?.Report(new InstallationProgress
            {
                Stage = InstallationStage.Extracting,
                Detail = detail
            }));

        var extraction = await _extraction
            .ExtractAsync(download.Path, directory, plan.PackageType, extractionProgress, cancellationToken)
            .ConfigureAwait(false);

        if (extraction.RefusedEntries.Count > 0)
        {
            _log.Warn("Install",
                $"{extraction.RefusedEntries.Count} entries in {plan.AssetName} were refused "
                + "because they tried to write outside the installation folder.");
        }

        progress?.Report(new InstallationProgress { Stage = InstallationStage.LocatingExecutable });

        var selection = LocateExecutable(plan, directory);

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Registering });

        var manifest = BuildManifest(plan, download, directory) with
        {
            ExecutablePath = selection.Chosen is null ? null : Path.Combine(directory, selection.Chosen),
            AlternativeExecutables = selection.Alternatives
                .Select(a => Path.Combine(directory, a))
                .ToList()
        };

        _store.Save(manifest);

        _log.Info("Install", $"Installed {plan.FullName} into {directory}. "
                             + (selection.Found ? $"Executable: {selection.Chosen}" : "No executable identified."));

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Finished });

        return new InstallationResult { Succeeded = true, Manifest = manifest };
    }

    /// <summary>
    /// A single downloaded file that is itself the program: a standalone executable or
    /// an AppImage. It is moved into the managed folder and, on Unix, made executable.
    /// It is not run.
    /// </summary>
    private InstallationResult InstallSingleFile(
        InstallPlan plan, DownloadedFile download, IProgress<InstallationProgress>? progress)
    {
        var directory = EnsureManagedDirectory(plan);
        var target = Path.Combine(directory, Path.GetFileName(download.Path));

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Registering });

        File.Move(download.Path, target, overwrite: true);
        TryMakeExecutable(target);

        var manifest = BuildManifest(plan, download, directory) with { ExecutablePath = target };
        _store.Save(manifest);

        _log.Info("Install", $"Installed {plan.FullName} as a single file at {target}");

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Finished });

        return new InstallationResult { Succeeded = true, Manifest = manifest };
    }

    /// <summary>
    /// Records that the file was fetched and left alone. RepoDeck will not run an
    /// installer, so the user is told where it is and decides for themselves.
    /// </summary>
    private InstallationResult RecordDownloadOnly(InstallPlan plan, DownloadedFile download)
    {
        var manifest = BuildManifest(plan, download, Path.GetDirectoryName(download.Path)!) with
        {
            IsDownloadOnly = true,
            DownloadedFilePath = download.Path,
            ExecutablePath = null,
            LaunchStrategy = LaunchStrategy.SystemInstalled
        };

        _store.Save(manifest);

        _log.Info("Install", $"Downloaded {plan.FullName} to {download.Path} and stopped: "
                             + "RepoDeck does not run installers.");

        return new InstallationResult
        {
            Succeeded = true,
            Manifest = manifest,
            DownloadedOnly = true
        };
    }

    private ExecutableSelection LocateExecutable(InstallPlan plan, string directory)
    {
        var files = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Take(20_000)
            .Select(path => new CandidateFile(
                Path.GetRelativePath(directory, path),
                HasExecutableBit(path),
                SafeLength(path)))
            .ToList();

        return ExecutableLocator.Select(files, plan.ExecutableCandidates, plan.Name, plan.Platform);
    }

    private ApplicationManifest BuildManifest(InstallPlan plan, DownloadedFile download, string directory) => new()
    {
        Owner = plan.Owner,
        Name = plan.Name,
        RepositoryUrl = plan.RepositoryUrl,
        ReleaseTag = plan.ReleaseTag,
        ReleaseName = plan.ReleaseName,
        ReleasePublishedAt = plan.ReleasePublishedAt,
        IsPrerelease = plan.IsPrerelease,
        AssetName = plan.AssetName,
        AssetSize = download.Size,
        AssetSha256 = download.Sha256,
        InstalledPath = directory,
        Platform = plan.Platform,
        Architecture = plan.Architecture,
        PackageType = plan.PackageType,
        Strategy = plan.Strategy,
        LaunchStrategy = plan.LaunchStrategy,
        InstalledAt = _time.GetUtcNow()
    };
}
