using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

public sealed partial class RepositoryDetailsViewModel
{
    private void ApplyProject(RepositoryAnalysis analysis)
    {
        ProjectTypeText = analysis.ApplicationType.ToDisplayString();

        ClassificationConfidenceText = analysis.ApplicationType == ApplicationType.Unknown
            ? "RepoDeck could not determine what kind of project this is."
            : analysis.ApplicationTypeConfidence.ToDisplayString();

        var technologies = new List<string>();
        if (analysis.PrimaryProjectType != ProjectType.Unknown)
        {
            technologies.Add(analysis.PrimaryProjectType.ToDisplayString());
        }

        technologies.AddRange(analysis.Frameworks);

        if (technologies.Count == 0 && analysis.PrimaryLanguage is { Length: > 0 })
        {
            technologies.Add(analysis.PrimaryLanguage);
        }

        TechnologyText = technologies.Count > 0 ? string.Join(" / ", technologies) : "Not determined";

        var supported = analysis.SupportedPlatforms
            .Where(p => p.Value is Confidence.Confirmed or Confidence.Likely or Confidence.Possible)
            .OrderBy(p => p.Key.ToString(), StringComparer.Ordinal)
            .Select(p => p.Key.DisplayName())
            .ToList();

        PlatformsText = supported.Count > 0 ? string.Join(" / ", supported) : "Not determined";
    }

    private void ApplyCompatibility(RepositoryAnalysis analysis, ReleaseAnalysis releases)
    {
        CompatibilityEvidence.Clear();

        var support = analysis.PlatformSupport(_machine.OperatingSystem);
        var recommended = releases.Recommended;

        // The headline names the machine, so a compatibility claim is never abstract.
        CompatibilityHeadline = recommended is not null
            ? $"{_machine.Description}: {DescribeCompatibility(recommended.Compatibility)}"
            : $"{_machine.Description}: {DescribeSupport(support)}";

        CompatibilityIsFavourable = recommended?.IsUsable == true;

        foreach (var evidence in BuildCompatibilityEvidence(analysis, releases))
        {
            CompatibilityEvidence.Add(evidence);
        }
    }

    private static string DescribeCompatibility(AssetCompatibility compatibility) => compatibility switch
    {
        AssetCompatibility.Compatible => "Likely compatible",
        AssetCompatibility.CompatibleThroughEmulation => "Compatible, but not natively",
        AssetCompatibility.LikelyCompatible => "Possibly compatible",
        AssetCompatibility.Incompatible => "Not compatible",
        _ => "Cannot be determined"
    };

    private static string DescribeSupport(Confidence support) => support switch
    {
        Confidence.Confirmed => "Supported",
        Confidence.Likely => "Likely supported, but nothing is published to download",
        Confidence.Possible => "Possibly supported, but nothing is published to download",
        Confidence.Unsupported => "Not supported",
        _ => "Cannot be determined"
    };

    private List<Evidence> BuildCompatibilityEvidence(RepositoryAnalysis analysis, ReleaseAnalysis releases)
    {
        var evidence = new List<Evidence>();
        var recommended = releases.Recommended;

        if (recommended is not null)
        {
            evidence.AddRange(recommended.Reasons.Select(r => new Evidence(r, EvidenceSource.Release)));
        }
        else if (releases.NoRecommendationReason is { Length: > 0 } reason)
        {
            evidence.Add(Evidence.Against(reason, EvidenceSource.Release));
        }

        evidence.Add(releases.HasAnyBinary
            ? new Evidence("Ready-made builds are published, so nothing has to be compiled.",
                EvidenceSource.Release)
            : Evidence.Against("No ready-made builds are published, so it would have to be compiled.",
                EvidenceSource.Release));

        if (analysis.PlatformSupport(_machine.OperatingSystem) == Confidence.Unsupported)
        {
            evidence.Add(Evidence.Against(
                $"The project does not appear to support {_machine.OperatingSystem.DisplayName()}.",
                EvidenceSource.FileContents));
        }

        evidence.AddRange(analysis.Evidence.Where(IsPlatformEvidence));

        return evidence.DistinctBy(e => e.Text).ToList();
    }

    private static bool IsPlatformEvidence(Evidence evidence) =>
        evidence.Text.Contains("Windows", StringComparison.OrdinalIgnoreCase)
        || evidence.Text.Contains("Linux", StringComparison.OrdinalIgnoreCase)
        || evidence.Text.Contains("macOS", StringComparison.OrdinalIgnoreCase);

    private void ApplyRecommendation(ReleaseAnalysis releases)
    {
        RecommendationReasons.Clear();

        var recommended = releases.Recommended;
        HasRecommendation = recommended is not null;

        if (recommended is null)
        {
            NoRecommendationReason = releases.NoRecommendationReason
                                     ?? "RepoDeck found nothing here that it could install.";
            RecommendedAssetName = "";
            RecommendedAssetSize = "";
            return;
        }

        RecommendedAssetName = recommended.Name;
        RecommendedAssetSize = Humanize.FileSize(recommended.Size);
        NoRecommendationReason = "";

        foreach (var reason in recommended.Reasons) RecommendationReasons.Add(reason);

        // State the negative explicitly: it is the mistake RepoDeck most needs to avoid.
        RecommendationReasons.Add("It is a built program, not source code.");
    }

    private void ApplyPlan(InstallPlan plan)
    {
        PlanWarnings.Clear();
        PlanBlockers.Clear();
        ExecutableCandidates.Clear();

        foreach (var warning in plan.Warnings) PlanWarnings.Add(warning);
        foreach (var blocker in plan.BlockingIssues) PlanBlockers.Add(blocker);
        foreach (var candidate in plan.ExecutableCandidates) ExecutableCandidates.Add(candidate);

        HasPlan = plan.CanProceed;
        PlanStrategyText = plan.Strategy.ToDisplayString();
        PlanStepSummary = plan.StepSummary;
        PlanDirectory = plan.ProposedInstallDirectory;

        PlanExtractionText = plan.RequiresExtraction
            ? "Yes - the archive would be unpacked into the folder above."
            : "No - the downloaded file would be kept as it is.";

        PlanElevationText = plan.RequiresElevation
            ? "Yes. RepoDeck will not request administrator rights on your behalf."
            : "No. Nothing outside RepoDeck's own folder would be changed.";

        PlanDownloadSummary = plan.AssetName is null
            ? "Nothing would be downloaded."
            : plan.AssetName + " (" + Humanize.FileSize(plan.AssetSize) + ")";

        PlanReleaseSummary = plan.ReleaseTag is null
            ? ""
            : plan.ReleaseName + " (" + plan.ReleaseTag + "), published "
              + Humanize.RelativeTime(plan.ReleasePublishedAt);
    }
}
