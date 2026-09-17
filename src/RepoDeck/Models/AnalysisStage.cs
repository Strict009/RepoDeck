namespace RepoDeck.Models;

/// <summary>
/// The steps of a repository analysis, reported so the user sees progress rather than
/// a frozen page while several GitHub requests are in flight.
/// </summary>
public enum AnalysisStage
{
    LoadingRepository,
    ReadingProjectStructure,
    ReadingProjectFiles,
    InspectingReleases,
    AnalyzingCompatibility,
    PreparingInstallationPlan,
    Finished
}

public static class AnalysisStageText
{
    public static string ToDisplayString(this AnalysisStage stage) => stage switch
    {
        AnalysisStage.LoadingRepository => "Loading repository...",
        AnalysisStage.ReadingProjectStructure => "Reading project structure...",
        AnalysisStage.ReadingProjectFiles => "Reading project files...",
        AnalysisStage.InspectingReleases => "Inspecting releases...",
        AnalysisStage.AnalyzingCompatibility => "Analysing compatibility...",
        AnalysisStage.PreparingInstallationPlan => "Preparing installation plan...",
        AnalysisStage.Finished => "",
        _ => ""
    };
}
