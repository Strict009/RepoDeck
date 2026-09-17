using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Decides what kind of thing a repository is, by combining weighted evidence.
/// </summary>
/// <remarks>
/// Votes are summed rather than letting the first matching rule win, so a Node package
/// that both declares a "bin" entry and depends on Electron is judged on the balance of
/// evidence instead of on rule ordering. Repository names are never consulted: naming is
/// marketing, not structure.
/// </remarks>
public static class ApplicationClassifier
{
    public static ClassificationResult Classify(
        ProjectStructure structure,
        ReleaseAnalysis releases,
        GitHubRepository repository)
    {
        var votes = new Dictionary<ApplicationType, int>();
        var evidence = new List<Evidence>();

        foreach (var hint in structure.ApplicationHints)
        {
            Add(votes, hint.Type, hint.Weight);
            evidence.Add(hint.Evidence);
        }

        AddReleaseEvidence(releases, votes, evidence);
        AddTopicEvidence(repository, votes, evidence);

        if (votes.Count == 0)
        {
            return new ClassificationResult(
                ApplicationType.Unknown, Confidence.Unknown, evidence,
                ["RepoDeck found nothing in the repository that says what kind of project this is."]);
        }

        var ranked = votes.OrderByDescending(v => v.Value).ToList();
        var winner = ranked[0];
        var runnerUpScore = ranked.Count > 1 ? ranked[1].Value : 0;
        var margin = winner.Value - runnerUpScore;

        var confidence = ResolveConfidence(winner.Value, margin);

        var unknowns = new List<string>();
        if (confidence is Confidence.Unknown or Confidence.Possible)
        {
            unknowns.Add("The evidence points in more than one direction, so this classification is tentative.");
        }

        return new ClassificationResult(winner.Key, confidence, evidence, unknowns);
    }

    private static void AddReleaseEvidence(
        ReleaseAnalysis releases, Dictionary<ApplicationType, int> votes, List<Evidence> evidence)
    {
        if (!releases.HasRelease) return;

        var binaries = releases.SoftwareAssets;
        if (binaries.Count == 0)
        {
            // Releases that carry no built software suggest something consumed as source.
            Add(votes, ApplicationType.Library, 20);
            evidence.Add(Evidence.Against(
                "Publishes releases, but none of them contain a ready-to-run program.",
                EvidenceSource.Release));
            return;
        }

        // A platform-specific build is evidence of a program meant to be run. An
        // unlabelled archive is not: a library can perfectly well ship its headers in a
        // ZIP, and treating that as an application is how a C++ library gets called a
        // desktop app.
        var platformBuilds = binaries
            .Where(a => a.Platform != OsPlatform.Unknown && a.PackageType.IsRunnableSoftware())
            .ToList();

        if (platformBuilds.Count > 0)
        {
            Add(votes, ApplicationType.DesktopApplication, 35);
            Add(votes, ApplicationType.CliTool, 25);
            var plural = platformBuilds.Count == 1 ? "" : "s";
            evidence.Add(new Evidence(
                $"Publishes {platformBuilds.Count} platform-specific build{plural} in its latest release.",
                EvidenceSource.Release));
        }
        else
        {
            evidence.Add(Evidence.Against(
                "Its release files are not labelled for any particular system, so they may be "
                + "source or library files rather than a program.",
                EvidenceSource.Release));
        }

        if (binaries.Any(a => a.PackageType is PackageType.WindowsInstaller
                              or PackageType.AppImage or PackageType.MacDiskImage))
        {
            Add(votes, ApplicationType.DesktopApplication, 30);
            evidence.Add(new Evidence(
                "Ships an installer or application bundle, which is how desktop programs are distributed.",
                EvidenceSource.Release));
        }
    }

    private static void AddTopicEvidence(
        GitHubRepository repository, Dictionary<ApplicationType, int> votes, List<Evidence> evidence)
    {
        // Topics are self-declared, so they count for less than what is actually in the files.
        var topics = repository.Topics.Select(t => t.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        (string Topic, ApplicationType Type, int Weight)[] mapping =
        [
            ("library", ApplicationType.Library, 25),
            ("sdk", ApplicationType.Library, 25),
            ("framework", ApplicationType.Framework, 25),
            ("cli", ApplicationType.CliTool, 25),
            ("command-line", ApplicationType.CliTool, 25),
            ("game", ApplicationType.Game, 25),
            ("desktop", ApplicationType.DesktopApplication, 25),
            ("gui", ApplicationType.DesktopApplication, 25),
            ("plugin", ApplicationType.PluginOrExtension, 30),
            ("extension", ApplicationType.PluginOrExtension, 25),
            ("server", ApplicationType.Server, 25),
            ("self-hosted", ApplicationType.Server, 20),
            ("webapp", ApplicationType.WebApplication, 25),
            ("android", ApplicationType.MobileApplication, 25),
            ("ios", ApplicationType.MobileApplication, 25),
            ("devtools", ApplicationType.DeveloperTool, 20)
        ];

        foreach (var (topic, type, weight) in mapping)
        {
            if (!topics.Contains(topic)) continue;

            Add(votes, type, weight);
            evidence.Add(new Evidence(
                $"The authors tagged it \"{topic}\" on GitHub.", EvidenceSource.Metadata));
        }
    }

    /// <summary>
    /// Confidence never reaches Confirmed: what a project <em>is</em> is always an inference
    /// from how it is built, never a fact GitHub states outright.
    /// </summary>
    private static Confidence ResolveConfidence(int topScore, int margin) => topScore switch
    {
        >= 100 when margin >= 30 => Confidence.Likely,
        >= 70 when margin >= 20 => Confidence.Likely,
        >= 50 => Confidence.Possible,
        >= 25 => Confidence.Possible,
        _ => Confidence.Unknown
    };

    private static void Add(Dictionary<ApplicationType, int> votes, ApplicationType type, int weight) =>
        votes[type] = votes.TryGetValue(type, out var existing) ? existing + weight : weight;
}

public sealed record ClassificationResult(
    ApplicationType Type,
    Confidence Confidence,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<string> Unknowns);
