using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Works out which operating systems a project supports, and how sure RepoDeck is.
/// </summary>
/// <remarks>
/// A published build for a platform is the only thing treated as Confirmed. Everything
/// else - what the framework is capable of, what the language usually implies - is an
/// inference and is labelled as such. A Windows-only toolkit produces an explicit
/// Unsupported for the other platforms, which is a stronger and more useful statement
/// than Unknown.
/// </remarks>
public static class PlatformSupportAnalyzer
{
    private static readonly string[] CrossPlatformFrameworks = ["Avalonia", "Electron", "Tauri", ".NET MAUI"];
    private static readonly string[] WindowsOnlyFrameworks = ["WPF", "Windows Forms"];

    public static (IReadOnlyDictionary<OsPlatform, Confidence> Platforms, IReadOnlyList<Evidence> Evidence)
        Analyze(ProjectStructure structure, ReleaseAnalysis releases)
    {
        var platforms = new Dictionary<OsPlatform, Confidence>();
        var evidence = new List<Evidence>();

        // Strongest evidence first: an actual published build for that platform.
        foreach (var asset in releases.SoftwareAssets.Where(a => a.Platform != OsPlatform.Unknown))
        {
            platforms[asset.Platform] = Confidence.Confirmed;
        }

        foreach (var platform in platforms.Keys.ToList())
        {
            evidence.Add(new Evidence(
                $"A {platform.DisplayName()} build is published in the latest release.",
                EvidenceSource.Release));
        }

        // Windows-only toolkits rule other platforms out rather than leaving them open.
        var windowsOnly = structure.Frameworks.Intersect(WindowsOnlyFrameworks, StringComparer.OrdinalIgnoreCase).ToList();
        if (windowsOnly.Count > 0)
        {
            Upgrade(platforms, OsPlatform.Windows, Confidence.Likely);
            RuleOut(platforms, OsPlatform.Linux);
            RuleOut(platforms, OsPlatform.MacOS);
            evidence.Add(new Evidence(
                $"Uses {windowsOnly[0]}, which only runs on Windows.", EvidenceSource.FileContents));
        }

        var crossPlatform = structure.Frameworks.Intersect(CrossPlatformFrameworks, StringComparer.OrdinalIgnoreCase).ToList();
        if (crossPlatform.Count > 0)
        {
            foreach (var platform in new[] { OsPlatform.Windows, OsPlatform.Linux, OsPlatform.MacOS })
            {
                Upgrade(platforms, platform, Confidence.Likely);
            }

            evidence.Add(new Evidence(
                $"Built with {crossPlatform[0]}, which runs on Windows, Linux and macOS.",
                EvidenceSource.FileContents));
        }

        // Language-level portability is a weak signal, so it only fills gaps.
        if (platforms.Count == 0 && structure.PrimaryProjectType is
                ProjectType.DotNet or ProjectType.Rust or ProjectType.Go or
                ProjectType.Python or ProjectType.Node or ProjectType.Java)
        {
            foreach (var platform in new[] { OsPlatform.Windows, OsPlatform.Linux, OsPlatform.MacOS })
            {
                Upgrade(platforms, platform, Confidence.Possible);
            }

            evidence.Add(new Evidence(
                $"Written for {structure.PrimaryProjectType.ToDisplayString()}, which is normally portable, "
                + "though no builds are published to confirm it.",
                EvidenceSource.FileStructure));
        }

        return (platforms, evidence);
    }

    /// <summary>Raises a platform's confidence, never lowers it.</summary>
    private static void Upgrade(Dictionary<OsPlatform, Confidence> platforms, OsPlatform platform, Confidence level)
    {
        if (!platforms.TryGetValue(platform, out var existing) || Rank(level) > Rank(existing))
        {
            platforms[platform] = level;
        }
    }

    /// <summary>Marks a platform unsupported unless a published build proves otherwise.</summary>
    private static void RuleOut(Dictionary<OsPlatform, Confidence> platforms, OsPlatform platform)
    {
        if (platforms.TryGetValue(platform, out var existing) && existing == Confidence.Confirmed) return;
        platforms[platform] = Confidence.Unsupported;
    }

    private static int Rank(Confidence confidence) => confidence switch
    {
        Confidence.Confirmed => 4,
        Confidence.Likely => 3,
        Confidence.Possible => 2,
        Confidence.RequiresInspection => 1,
        _ => 0
    };
}
