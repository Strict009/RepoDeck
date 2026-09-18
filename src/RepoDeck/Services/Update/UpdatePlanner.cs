using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Install;

namespace RepoDeck.Services.Update;

/// <summary>
/// Turns "there is a newer release" into exactly what RepoDeck would do about it.
/// </summary>
/// <remarks>
/// Pure. The decide/do boundary again: this produces a plan and the update service carries
/// it out without re-deciding anything, in particular without picking an asset. Asset
/// selection lives in <see cref="ReleaseAnalyzer"/> and nowhere else, because two places
/// that choose which file to download are two places that can disagree about it.
///
/// It refuses more readily than the install planner does, because the stakes differ: a
/// first install that goes wrong leaves nothing behind, whereas a bad update replaces
/// something that was working.
/// </remarks>
public static class UpdatePlanner
{
    public static UpdatePlan Create(
        ApplicationManifest current,
        UpdateCheck check,
        MachineProfile machine,
        AppPaths paths)
    {
        if (check.State == UpdateState.ManualUpdateRequired)
        {
            return UpdatePlan.NotPossible(current, UpdateStrategy.NotSupported,
                check.Reasons.FirstOrDefault()
                ?? "RepoDeck cannot install the newer release for you.")
                with
            {
                TargetReleaseTag = check.LatestRelease?.TagName,
                TargetVersionText = check.LatestVersion?.ToString() ?? "",
                TargetReleaseUrl = check.LatestRelease?.HtmlUrl,
                Reasons = check.Reasons
            };
        }

        if (check.State != UpdateState.UpdateAvailable || check.LatestRelease is null)
        {
            return UpdatePlan.NotPossible(current, UpdateStrategy.Unknown,
                "There is no newer release RepoDeck can offer.");
        }

        // A download-only installation was never really installed - there is nothing on
        // disk to replace. Updating one means fetching the new file, which is the ordinary
        // install path rather than an update.
        if (current.State == InstallationState.Downloaded)
        {
            return UpdatePlan.NotPossible(current, UpdateStrategy.DownloadOnly,
                "RepoDeck only downloaded this rather than installing it, so there is nothing "
                + "for it to replace. Fetch the new version from the project's page.")
                with
            {
                TargetReleaseTag = check.LatestRelease.TagName,
                TargetVersionText = check.LatestVersion?.ToString() ?? check.LatestRelease.TagName,
                TargetReleaseUrl = check.LatestRelease.HtmlUrl
            };
        }

        var release = check.LatestRelease;

        // The same analysis the first install used. Nothing is re-decided here.
        var analysis = ReleaseAnalyzer.Analyze([release], machine);
        var asset = analysis.Recommended;

        if (asset is null)
        {
            return UpdatePlan.NotPossible(current, UpdateStrategy.NotSupported,
                analysis.NoRecommendationReason
                ?? $"Nothing in release {release.TagName} suits {machine.Description}.");
        }

        var installStrategy = ResolveInstallStrategy(asset);

        var warnings = new List<string>(asset.Warnings);
        var blockers = new List<string>();

        if (release.Prerelease)
        {
            warnings.Add($"Release {release.TagName} is marked as a pre-release and may be unfinished.");
        }

        // A system installer cannot replace a managed installation: RepoDeck will not run
        // it, so swapping files would leave the user with a downloaded file where a working
        // program used to be.
        if (installStrategy is InstallStrategy.WindowsInstaller or InstallStrategy.LinuxPackage)
        {
            return UpdatePlan.NotPossible(current, UpdateStrategy.DownloadOnly,
                "The new release is a system installer. RepoDeck will not run installers, and "
                + "replacing your working copy with one would leave you worse off.")
                with
            {
                TargetReleaseTag = release.TagName,
                TargetVersionText = check.LatestVersion?.ToString() ?? release.TagName,
                TargetReleaseUrl = release.HtmlUrl,
                AssetName = asset.Name,
                AssetSize = asset.Size,
                Reasons = asset.Reasons
            };
        }

        if (installStrategy is InstallStrategy.SourceBuild or InstallStrategy.Unsupported)
        {
            return UpdatePlan.NotPossible(current, UpdateStrategy.NotSupported,
                $"RepoDeck does not know how to install a {asset.PackageType.ToDisplayString()}.");
        }

        // Changing what kind of thing the installation is halfway through its life is not
        // an update RepoDeck is prepared to perform unsupervised.
        if (current.Strategy != InstallStrategy.Unknown && current.Strategy != installStrategy)
        {
            warnings.Add($"This release is published as a "
                         + $"{installStrategy.ToDisplayString().ToLowerInvariant()}, where the version "
                         + $"you have was a {current.Strategy.ToDisplayString().ToLowerInvariant()}.");
        }

        if (current.NeedsExecutableChoice)
        {
            warnings.Add("You have not yet chosen which file to run for this application. "
                         + "Updating will ask again.");
        }

        var directory = current.InstalledPath;

        if (!ArchivePathGuard.IsInside(paths.Apps, Path.GetFullPath(directory)))
        {
            // A manifest that points outside the managed root is not one RepoDeck acts on.
            blockers.Add("This application's recorded location is outside RepoDeck's own folder, "
                         + "so RepoDeck will not replace it.");
        }

        return new UpdatePlan
        {
            Current = current,
            Owner = current.Owner,
            Name = current.Name,
            InstalledVersionText = current.DisplayVersion,

            TargetReleaseTag = release.TagName,
            TargetReleaseName = release.DisplayName,
            TargetReleaseId = release.Id,
            TargetPublishedAt = release.PublishedAt ?? release.CreatedAt,
            TargetIsPrerelease = release.Prerelease,
            TargetVersionText = check.LatestVersion?.ToString() ?? release.TagName,
            TargetReleaseNotes = release.Body,
            TargetReleaseUrl = release.HtmlUrl,

            AssetName = asset.Name,
            AssetUrl = asset.DownloadUrl,
            AssetSize = asset.Size,
            Platform = asset.Platform,
            Architecture = asset.Architecture,
            PackageType = asset.PackageType,

            Strategy = blockers.Count == 0
                ? UpdateStrategy.ReplaceManagedInstallation
                : UpdateStrategy.NotSupported,
            InstallStrategy = installStrategy,

            // The files about to be replaced include the program itself, so it cannot be
            // running while they are swapped. RepoDeck asks; it never closes anything.
            RequiresApplicationClosed = current.IsRunnableInstallation,

            ProposedInstallDirectory = directory,
            ExecutableCandidates = PredictExecutables(current, asset),

            Confidence = ResolveConfidence(asset),
            Reasons = [.. check.Reasons, .. asset.Reasons],
            Warnings = warnings,
            BlockingIssues = blockers
        };
    }

    /// <summary>
    /// What to look for after extracting. The executable that is already working is the
    /// best prediction there is - far better than guessing from the asset name again.
    /// </summary>
    private static IReadOnlyList<string> PredictExecutables(
        ApplicationManifest current, AssetAnalysis asset)
    {
        var names = new List<string>();

        if (current.ExecutableRelativePath is { Length: > 0 } existing)
        {
            names.Add(Path.GetFileName(existing));
        }

        foreach (var alternative in current.AlternativeExecutables)
        {
            var name = Path.GetFileName(alternative);
            if (name.Length > 0) names.Add(name);
        }

        if (!asset.PackageType.RequiresExtraction()) names.Add(asset.Name);

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static InstallStrategy ResolveInstallStrategy(AssetAnalysis asset) => asset.PackageType switch
    {
        PackageType.Zip or PackageType.SevenZip or PackageType.TarGz or PackageType.TarXz =>
            InstallStrategy.PortableArchive,

        PackageType.WindowsExecutable =>
            AssetNameParser.LooksLikeInstaller(asset.Name)
                ? InstallStrategy.WindowsInstaller
                : InstallStrategy.StandaloneExecutable,

        PackageType.WindowsInstaller => InstallStrategy.WindowsInstaller,
        PackageType.AppImage => InstallStrategy.LinuxAppImage,
        PackageType.DebianPackage or PackageType.RpmPackage => InstallStrategy.LinuxPackage,
        PackageType.SourceArchive => InstallStrategy.SourceBuild,
        _ => InstallStrategy.Unsupported
    };

    private static Confidence ResolveConfidence(AssetAnalysis asset) => asset.Compatibility switch
    {
        AssetCompatibility.Compatible => Confidence.Likely,
        AssetCompatibility.CompatibleThroughEmulation => Confidence.Possible,
        AssetCompatibility.LikelyCompatible => Confidence.Possible,
        _ => Confidence.Unknown
    };
}
