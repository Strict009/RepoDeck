using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>
/// The RepoDeck Analysis section of the details page: what the project is, whether it
/// runs here, which file would be downloaded and what installing it would involve.
/// </summary>
public sealed partial class RepositoryDetailsViewModel
{
    public ObservableCollection<Evidence> CompatibilityEvidence { get; } = [];
    public ObservableCollection<string> RecommendationReasons { get; } = [];
    public ObservableCollection<string> PlanWarnings { get; } = [];
    public ObservableCollection<string> PlanBlockers { get; } = [];
    public ObservableCollection<string> ExecutableCandidates { get; } = [];
    public ObservableCollection<string> AnalysisUnknowns { get; } = [];

    /// <summary>Progress text shown while several GitHub requests are in flight.</summary>
    [ObservableProperty] private string _analysisStatus = "";

    [ObservableProperty] private bool _hasAnalysis;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnalysisIsIncomplete))]
    private string? _analysisIncompleteReason;

    public bool AnalysisIsIncomplete => !string.IsNullOrEmpty(AnalysisIncompleteReason);

    [ObservableProperty] private string _projectTypeText = "";
    [ObservableProperty] private string _technologyText = "";
    [ObservableProperty] private string _platformsText = "";
    [ObservableProperty] private string _classificationConfidenceText = "";

    [ObservableProperty] private string _compatibilityHeadline = "";
    [ObservableProperty] private bool _compatibilityIsFavourable;

    [ObservableProperty] private bool _hasRecommendation;
    [ObservableProperty] private string _recommendedAssetName = "";
    [ObservableProperty] private string _recommendedAssetSize = "";
    [ObservableProperty] private string _noRecommendationReason = "";

    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private string _planStrategyText = "";
    [ObservableProperty] private string _planStepSummary = "";
    [ObservableProperty] private string _planDirectory = "";
    [ObservableProperty] private string _planExtractionText = "";
    [ObservableProperty] private string _planElevationText = "";
    [ObservableProperty] private string _planDownloadSummary = "";
    [ObservableProperty] private string _planReleaseSummary = "";

    private void ApplyAnalysis(RepositoryAnalysis analysis, ReleaseAnalysis releases, InstallPlan plan)
    {
        AnalysisIncompleteReason = analysis.IsComplete ? null : analysis.IncompleteReason;

        ApplyProject(analysis);
        ApplyCompatibility(analysis, releases);
        ApplyRecommendation(releases);
        ApplyPlan(plan);

        AnalysisUnknowns.Clear();
        foreach (var unknown in analysis.Unknowns) AnalysisUnknowns.Add(unknown);
        foreach (var warning in analysis.Warnings) AnalysisUnknowns.Add(warning);

        HasAnalysis = true;
        AnalysisStatus = "";
    }
}
