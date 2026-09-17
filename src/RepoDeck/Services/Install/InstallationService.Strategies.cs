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
                await InstallSingleFileAsync(plan, download, progress, cancellationToken).ConfigureAwait(false),

            // RepoDeck fetches these and stops. Running a system installer, or handing a
            // package to a package manager, needs elevation and is the user's decision.
            InstallStrategy.WindowsInstaller or InstallStrategy.LinuxPackage =>
                RecordDownloadOnly(plan, download),

            _ => InstallationResult.Failed(
                $"RepoDeck does not know how to carry out a {plan.Strategy.ToDisplayString()} installation.")
        };
    }

    /// <summary>
    /// Extracts into a staging folder, checks the result, and only then promotes it to
    /// the application's real directory. Until the promotion succeeds and the manifest is
    /// written, nothing about this installation is visible to the rest of RepoDeck.
    /// </summary>
    private async Task<InstallationResult> InstallArchiveAsync(
        InstallPlan plan,
        DownloadedFile download,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var staging = CreateStagingArea();

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Extracting });

        var extractionProgress = new Progress<string>(detail =>
            progress?.Report(new InstallationProgress
            {
                Stage = InstallationStage.Extracting,
                Detail = detail
            }));

        var extraction = await _extraction
            .ExtractAsync(download.Path, staging.Path, plan.PackageType, extractionProgress, cancellationToken)
            .ConfigureAwait(false);

        if (extraction.RefusedEntries.Count > 0)
        {
            _log.Warn("Install",
                $"{extraction.RefusedEntries.Count} entries in {plan.AssetName} were refused "
                + "because they tried to write outside the installation folder.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new InstallationProgress { Stage = InstallationStage.LocatingExecutable });

        var selection = LocateExecutable(plan, staging.Path);

        // Promotion is the moment this becomes an installation.
        progress?.Report(new InstallationProgress { Stage = InstallationStage.Registering });

        var final = PromoteStaging(staging, plan);

        var manifest = BuildManifest(plan, download, final) with
        {
            State = InstallationState.Installed,
            ExecutableRelativePath = selection.Chosen,
            AlternativeExecutables = selection.Alternatives,
            ExecutableIsAmbiguous = selection.IsAmbiguous,
            OwnedEntries = TopLevelEntries(final)
        };

        _store.Save(manifest);

        _log.Info("Install", $"Installed {plan.FullName} into {final}. "
                             + (selection.Found
                                 ? $"Executable: {selection.Chosen}"
                                   + (selection.IsAmbiguous ? " (ambiguous)" : "")
                                 : "No executable identified."));

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Finished });

        return new InstallationResult
        {
            Succeeded = true,
            Manifest = manifest,
            ExecutableIsAmbiguous = selection.IsAmbiguous,
            ExecutableNote = selection.Reason
        };
    }

    /// <summary>
    /// A single downloaded file that is itself the program: a standalone executable the
    /// plan identified as such, or an AppImage. It is staged, promoted and registered by
    /// the same route as an archive, and is never run.
    /// </summary>
    private async Task<InstallationResult> InstallSingleFileAsync(
        InstallPlan plan,
        DownloadedFile download,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var staging = CreateStagingArea();

        var fileName = Path.GetFileName(download.Path);
        var staged = Path.Combine(staging.Path, fileName);

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Extracting });

        // Copied rather than moved, so a failure before promotion leaves the download intact.
        await CopyFileAsync(download.Path, staged, cancellationToken).ConfigureAwait(false);
        TryMakeExecutable(staged);

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Registering });

        var final = PromoteStaging(staging, plan);

        var manifest = BuildManifest(plan, download, final) with
        {
            State = InstallationState.Installed,
            ExecutableRelativePath = fileName,
            OwnedEntries = TopLevelEntries(final)
        };

        _store.Save(manifest);
        TryDeleteFile(download.Path);

        _log.Info("Install", $"Installed {plan.FullName} as a single file at {final}");

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Finished });

        return new InstallationResult { Succeeded = true, Manifest = manifest };
    }

    /// <summary>
    /// Records that the file was fetched and left alone. This is deliberately not an
    /// installation: the manifest says Downloaded, and nothing pretends otherwise.
    /// </summary>
    private InstallationResult RecordDownloadOnly(InstallPlan plan, DownloadedFile download)
    {
        var manifest = BuildManifest(plan, download, Path.GetDirectoryName(download.Path)!) with
        {
            State = InstallationState.Downloaded,
            DownloadedFilePath = download.Path,
            ExecutableRelativePath = null,
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
            .Take(MaxScannedFiles)
            .Select(path => new CandidateFile(
                Path.GetRelativePath(directory, path),
                HasExecutableBit(path),
                SafeLength(path)))
            .ToList();

        return ExecutableLocator.Select(files, plan.ExecutableCandidates, plan.Name, plan.Platform);
    }

    private ApplicationManifest BuildManifest(
        InstallPlan plan, DownloadedFile download, string directory) => new()
    {
        Owner = plan.Owner,
        Name = plan.Name,
        RepositoryUrl = plan.RepositoryUrl,
        RepositoryId = plan.RepositoryId,
        ReleaseTag = plan.ReleaseTag,
        ReleaseName = plan.ReleaseName,
        ReleaseId = plan.ReleaseId,
        ReleasePublishedAt = plan.ReleasePublishedAt,
        IsPrerelease = plan.IsPrerelease,
        AssetName = plan.AssetName,
        AssetDownloadUrl = plan.AssetUrl,
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

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var output = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }
}
