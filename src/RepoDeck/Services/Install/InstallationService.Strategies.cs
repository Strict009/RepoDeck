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

        // An archive with nothing runnable in it is not an installation. The download is
        // kept - it succeeded, and a later version may do better with it - but the
        // Installed library only ever means "RepoDeck established something you can run".
        if (!selection.Found)
        {
            _log.Info("Install", $"{plan.FullName}: extracted, but nothing runnable was found. "
                                 + "Recorded as downloaded rather than installed.");

            return RecordDownloadOnly(plan, download,
                "Download completed, but RepoDeck could not identify a runnable application "
                + "in this release.");
        }

        // Promotion is the moment the files become an installation.
        progress?.Report(new InstallationProgress { Stage = InstallationStage.Registering });

        var final = PromoteStaging(staging, plan);

        // Material ambiguity is not resolved by picking the winner and hoping. The files
        // are installed; which of them to run is a question for the user.
        var state = selection.IsAmbiguous
            ? InstallationState.AwaitingExecutableChoice
            : InstallationState.Installed;

        var manifest = BuildManifest(plan, download, final) with
        {
            State = state,
            ExecutableRelativePath = selection.IsAmbiguous ? null : selection.Chosen,
            AlternativeExecutables = CandidatesFor(selection),
            ExecutableIsAmbiguous = selection.IsAmbiguous,
            OwnedEntries = TopLevelEntries(final),
            NotInstalledReason = selection.IsAmbiguous
                ? "RepoDeck found multiple possible application executables."
                : null
        };

        _store.Save(manifest);

        _log.Info("Install", $"Installed {plan.FullName} into {final}. "
                             + (selection.IsAmbiguous
                                 ? $"{manifest.AlternativeExecutables.Count} possible executables; awaiting a choice."
                                 : $"Executable: {selection.Chosen}"));

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Finished });

        return new InstallationResult
        {
            Succeeded = true,
            Manifest = manifest,
            ExecutableIsAmbiguous = selection.IsAmbiguous,
            ExecutableNote = selection.IsAmbiguous
                ? "RepoDeck found multiple possible application executables."
                : selection.Reason
        };
    }

    /// <summary>
    /// When the choice is unresolved, the best guess belongs in the candidate list rather
    /// than being quietly promoted to "the executable".
    /// </summary>
    private static IReadOnlyList<string> CandidatesFor(ExecutableSelection selection)
    {
        if (!selection.IsAmbiguous) return selection.Alternatives;

        return selection.Chosen is null
            ? selection.Alternatives
            : new[] { selection.Chosen }.Concat(selection.Alternatives).ToList();
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
    private InstallationResult RecordDownloadOnly(
        InstallPlan plan, DownloadedFile download, string? reason = null)
    {
        var explanation = reason ?? DefaultDownloadOnlyReason(plan);

        var manifest = BuildManifest(plan, download, Path.GetDirectoryName(download.Path)!) with
        {
            State = InstallationState.Downloaded,
            DownloadedFilePath = download.Path,
            ExecutableRelativePath = null,
            NotInstalledReason = explanation,
            LaunchStrategy = LaunchStrategy.SystemInstalled
        };

        _store.Save(manifest);

        _log.Info("Install", $"Downloaded {plan.FullName} to {download.Path} and stopped: "
                             + "RepoDeck does not run installers.");

        return new InstallationResult
        {
            Succeeded = true,
            Manifest = manifest,
            DownloadedOnly = true,
            ExecutableNote = explanation
        };
    }

    private static string DefaultDownloadOnlyReason(InstallPlan plan) => plan.Strategy switch
    {
        InstallStrategy.WindowsInstaller =>
            "This is a Windows installer. RepoDeck downloaded it but will not run installers "
            + "on your behalf.",
        InstallStrategy.LinuxPackage =>
            "This is a system package. RepoDeck downloaded it but will not hand it to the "
            + "package manager on your behalf.",
        _ => "RepoDeck downloaded this but could not establish a runnable application from it."
    };

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
