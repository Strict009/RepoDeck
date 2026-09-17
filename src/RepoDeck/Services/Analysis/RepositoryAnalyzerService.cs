using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Inspects a repository's structure and releases and produces a structured analysis.
/// </summary>
/// <remarks>
/// Request budget matters here. The file listing is one request for the entire
/// repository, and at most three manifest files are read afterwards - only the ones the
/// structure pass said would change a conclusion. Deep analysis therefore happens when
/// the user opens a repository, never for every card in a search result.
/// </remarks>
public sealed class RepositoryAnalyzerService : IRepositoryAnalyzerService
{
    private const int MaxManifestReads = 3;

    private readonly IGitHubClient _github;
    private readonly IAppLog _log;

    public RepositoryAnalyzerService(IGitHubClient github, IAppLog log)
    {
        _github = github;
        _log = log;
    }

    public async Task<RepositoryAnalysis> AnalyzeAsync(
        GitHubRepository repository,
        ReleaseAnalysis releases,
        IProgress<AnalysisStage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var owner = repository.OwnerLogin;
        var name = repository.Name;

        var warnings = new List<string>();
        var unknowns = new List<string>();
        var incompleteReason = (string?)null;

        progress?.Report(AnalysisStage.ReadingProjectStructure);

        RepositoryTree tree;
        try
        {
            tree = await _github.GetTreeAsync(owner, name, repository.DefaultBranch, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitHubApiException ex) when (ex.Kind == GitHubErrorKind.RateLimited)
        {
            // An analysis that could not read the repository must not pretend to be one.
            _log.Warn("Analyzer", $"Rate limited while reading {owner}/{name}.");
            return Incomplete(repository,
                "GitHub's request allowance ran out before RepoDeck could read this repository.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn("Analyzer", $"Could not read the file listing for {owner}/{name}: {ex.Message}");
            tree = RepositoryTree.Empty;
            warnings.Add("RepoDeck could not read this repository's file listing.");
        }

        var structure = ProjectStructureDetector.DetectFromTree(tree);

        if (structure.ListingWasTruncated)
        {
            warnings.Add("This repository is very large and GitHub returned only part of its file "
                         + "listing, so some project files may have been missed.");
        }

        if (structure.FilesWorthReading.Count > 0)
        {
            progress?.Report(AnalysisStage.ReadingProjectFiles);
            var contents = await ReadManifestsAsync(owner, name, structure.FilesWorthReading, cancellationToken)
                .ConfigureAwait(false);
            structure = ProjectStructureDetector.RefineWithManifests(structure, contents);
        }

        progress?.Report(AnalysisStage.AnalyzingCompatibility);

        var classification = ApplicationClassifier.Classify(structure, releases, repository);
        var (platforms, platformEvidence) = PlatformSupportAnalyzer.Analyze(structure, releases);

        var evidence = structure.Evidence
            .Concat(classification.Evidence)
            .Concat(platformEvidence)
            .DistinctBy(e => e.Text)
            .ToList();

        unknowns.AddRange(classification.Unknowns);

        if (platforms.Count == 0)
        {
            unknowns.Add("RepoDeck could not determine which operating systems this supports.");
        }

        if (tree.IsEmpty && incompleteReason is null)
        {
            unknowns.Add("RepoDeck could not see inside this repository, so the classification "
                         + "rests on its description and releases alone.");
        }

        var architectures = releases.SoftwareAssets
            .Where(a => a.Architecture != CpuArchitecture.Unknown)
            .Select(a => a.Architecture)
            .Distinct()
            .ToList();

        progress?.Report(AnalysisStage.PreparingInstallationPlan);

        return new RepositoryAnalysis
        {
            Owner = owner,
            Name = name,
            RepositoryUrl = repository.HtmlUrl,
            ProjectTypes = structure.ProjectTypes,
            ApplicationType = classification.Type,
            ApplicationTypeConfidence = classification.Confidence,
            PrimaryLanguage = repository.Language,
            Frameworks = structure.Frameworks,
            BuildSystem = structure.BuildSystem,
            SupportedPlatforms = platforms,
            SupportedArchitectures = architectures,
            HasReleases = releases.HasRelease,
            HasDownloadableBinaries = releases.HasAnyBinary,
            InstallStrategy = InstallStrategy.Unknown,
            LaunchStrategy = LaunchStrategy.Unknown,
            Confidence = classification.Confidence,
            Evidence = evidence,
            Warnings = warnings,
            Unknowns = unknowns,
            IsComplete = true
        };
    }

    /// <summary>
    /// Reads the manifests the structure pass asked for, capped and failure-tolerant.
    /// A manifest that cannot be read costs detail, not the whole analysis.
    /// </summary>
    private async Task<Dictionary<string, string>> ReadManifestsAsync(
        string owner, string name, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var contents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths.Take(MaxManifestReads))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var content = await _github.GetTextFileAsync(owner, name, path, cancellationToken)
                    .ConfigureAwait(false);

                if (content is not null) contents[path] = content;
            }
            catch (GitHubApiException ex) when (ex.Kind == GitHubErrorKind.RateLimited)
            {
                _log.Warn("Analyzer", $"Rate limited while reading {path}; continuing without it.");
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warn("Analyzer", $"Could not read {path} from {owner}/{name}: {ex.Message}");
            }
        }

        return contents;
    }

    private static RepositoryAnalysis Incomplete(GitHubRepository repository, string reason) => new()
    {
        Owner = repository.OwnerLogin,
        Name = repository.Name,
        RepositoryUrl = repository.HtmlUrl,
        PrimaryLanguage = repository.Language,
        Confidence = Confidence.Unknown,
        ApplicationType = ApplicationType.Unknown,
        ApplicationTypeConfidence = Confidence.Unknown,
        IsComplete = false,
        IncompleteReason = reason,
        Unknowns = ["The analysis did not finish, so nothing here should be treated as settled."]
    };
}
