using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Services.Install;

/// <summary>
/// Turns an analysis into a concrete, inspectable plan - and nothing else.
/// </summary>
/// <remarks>
/// This class downloads nothing, extracts nothing and runs nothing. That separation is
/// the point: Milestone 3's installer consumes a finished plan without re-deciding
/// anything, and the user can read the whole plan before any of it happens.
/// </remarks>
public sealed partial class InstallPlanner
{
    private readonly AppPaths _paths;

    public InstallPlanner(AppPaths paths)
    {
        _paths = paths;
    }

    public InstallPlan Create(
        GitHubRepository repository,
        RepositoryAnalysis analysis,
        ReleaseAnalysis releases,
        MachineProfile machine)
    {
        var owner = repository.OwnerLogin;
        var name = repository.Name;
        var url = repository.HtmlUrl;

        if (!analysis.IsComplete)
        {
            return InstallPlan.NotPossible(owner, name, url, InstallStrategy.Unknown,
                "RepoDeck could not finish analysing this repository, so it will not propose a plan. "
                + (analysis.IncompleteReason ?? ""));
        }

        if (!releases.HasRelease)
        {
            return InstallPlan.NotPossible(owner, name, url, InstallStrategy.SourceBuild,
                "This project publishes no releases. Using it would mean building it from source, "
                + "which RepoDeck does not do.");
        }

        if (releases.Recommended is null)
        {
            var strategy = releases.HasAnyBinary ? InstallStrategy.Unsupported : InstallStrategy.SourceBuild;
            return InstallPlan.NotPossible(owner, name, url, strategy,
                releases.NoRecommendationReason ?? "No suitable download was found.");
        }

        var asset = releases.Recommended;
        var release = releases.Release!;
        var installStrategy = ResolveStrategy(asset);

        var warnings = new List<string>(asset.Warnings);
        var blockers = new List<string>();

        if (installStrategy is InstallStrategy.WindowsInstaller or InstallStrategy.LinuxPackage)
        {
            warnings.Add("This is a system installer. RepoDeck will not run installers on your behalf, "
                         + "so it would download the file and leave the decision to you.");
        }

        if (installStrategy == InstallStrategy.Unsupported)
        {
            blockers.Add($"RepoDeck does not know how to install a {asset.PackageType.ToDisplayString()}.");
        }

        if (release.Prerelease)
        {
            warnings.Add($"Release {release.TagName} is marked as a pre-release and may be unfinished.");
        }

        if (!analysis.IsRunnableApplication && analysis.ApplicationType != ApplicationType.Unknown)
        {
            warnings.Add($"RepoDeck thinks this is a "
                         + $"{analysis.ApplicationType.ToDisplayString().ToLowerInvariant()}, "
                         + "which may not be something you run directly.");
        }

        return new InstallPlan
        {
            Owner = owner,
            Name = name,
            RepositoryUrl = url,
            ReleaseTag = release.TagName,
            ReleaseName = release.DisplayName,
            ReleasePublishedAt = release.PublishedAt ?? release.CreatedAt,
            IsPrerelease = release.Prerelease,
            AssetName = asset.Name,
            AssetUrl = asset.DownloadUrl,
            AssetSize = asset.Size,
            Platform = asset.Platform,
            Architecture = asset.Architecture,
            PackageType = asset.PackageType,
            Strategy = installStrategy,
            LaunchStrategy = ResolveLaunchStrategy(installStrategy),
            ProposedInstallDirectory = Path.Combine(_paths.Apps, SafeFolderName(owner, name)),
            RequiresExtraction = asset.PackageType.RequiresExtraction(),
            ExecutableCandidates = PredictExecutables(repository, asset, machine),
            RequiresElevation = asset.RequiresElevation,
            Confidence = ResolveConfidence(asset),
            Reasons = asset.Reasons,
            Warnings = warnings,
            BlockingIssues = blockers
        };
    }
}
