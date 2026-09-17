using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Builds a structured picture of what a repository is, from its files and releases.
/// </summary>
/// <remarks>
/// An interface because the file-reading strategy is the part most likely to be replaced
/// - by a shallow clone, a cached index or a smarter budget - without the rest of
/// RepoDeck caring how the answer was reached.
/// </remarks>
public interface IRepositoryAnalyzerService
{
    Task<RepositoryAnalysis> AnalyzeAsync(
        GitHubRepository repository,
        ReleaseAnalysis releases,
        IProgress<AnalysisStage>? progress = null,
        CancellationToken cancellationToken = default);
}
