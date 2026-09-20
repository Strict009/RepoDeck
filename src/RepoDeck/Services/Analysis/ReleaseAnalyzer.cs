using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Chooses which release, and which file within it, suits this machine.
/// </summary>
/// <remarks>
/// Pure and deterministic. The rule that matters most: a source archive can never be
/// recommended while any compatible binary exists, and when only source exists RepoDeck
/// says so rather than offering to download something it cannot install.
/// </remarks>
public static class ReleaseAnalyzer
{
    /// <summary>Picks the release to analyse: the newest stable one, falling back to a prerelease.</summary>
    public static GitHubRelease? SelectRelease(IReadOnlyList<GitHubRelease> releases)
    {
        // Drafts are invisible to everyone but the maintainers and must be ignored.
        var published = releases.Where(r => !r.Draft).ToList();
        if (published.Count == 0) return null;

        var stable = published
            .Where(r => !r.Prerelease)
            .OrderByDescending(r => r.PublishedAt ?? r.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        if (stable is not null) return stable;

        return published
            .OrderByDescending(r => r.PublishedAt ?? r.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    public static ReleaseAnalysis Analyze(IReadOnlyList<GitHubRelease> releases, MachineProfile machine)
    {
        var release = SelectRelease(releases);

        if (release is null)
        {
            return ReleaseAnalysis.None(
                "This project has published no releases, so there is nothing ready-made to download.");
        }

        var analyzed = release.Assets
            // An asset RepoDeck cannot fetch is not a candidate, whatever else it looks
            // like. GitHub normally supplies a download URL for everything it lists, but
            // "normally" is not a guarantee about a remote API, and offering to install
            // something with nowhere to get it from produces a button that cannot work.
            .Where(asset => !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            .Select(AssetNameParser.Parse)
            .Select(asset => CompatibilityAnalyzer.Evaluate(asset, machine))
            .OrderByDescending(asset => asset.Score)
            .ThenBy(asset => asset.Size)
            .ToList();

        var (recommended, reason) = ChooseRecommendation(analyzed, machine, release);

        return new ReleaseAnalysis
        {
            Release = release,
            Assets = analyzed,
            Recommended = recommended,
            NoRecommendationReason = reason
        };
    }

    private static (AssetAnalysis? Asset, string? Reason) ChooseRecommendation(
        IReadOnlyList<AssetAnalysis> assets, MachineProfile machine, GitHubRelease release)
    {
        if (assets.Count == 0)
        {
            return (null, $"Release {release.TagName} has no attached files, only source code.");
        }

        // Source archives and checksums are excluded outright: they are not installable,
        // so they must never win by default just because nothing else scored.
        var installable = assets
            .Where(a => !a.IsSourceArchive && !a.IsMetadataFile)
            .ToList();

        if (installable.Count == 0)
        {
            return (null, $"Release {release.TagName} contains only source code, "
                          + "which RepoDeck cannot install because it would have to be compiled first.");
        }

        var usable = installable.Where(a => a.IsUsable).ToList();

        if (usable.Count == 0)
        {
            var platforms = installable
                .Where(a => a.Platform != OsPlatform.Unknown)
                .Select(a => a.Platform.DisplayName())
                .Distinct()
                .ToList();

            var built = platforms.Count > 0
                ? $" The available builds are for {string.Join(" and ", platforms)}."
                : "";

            return (null, $"None of the files in release {release.TagName} suit "
                          + $"{machine.Description}.{built}");
        }

        return (usable[0], null);
    }
}
