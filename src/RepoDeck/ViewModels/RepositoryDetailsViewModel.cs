using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.GitHub;
using RepoDeck.Services.Install;
using RepoDeck.Services.Media;

namespace RepoDeck.ViewModels;

/// <summary>
/// The repository details page. Assembles metadata, README, languages and releases,
/// then presents them as plain English first and technical detail second.
/// </summary>
public sealed partial class RepositoryDetailsViewModel : ViewModelBase
{
    private readonly IGitHubClient _github;
    private readonly IRepositoryExplanationService _explanations;
    private readonly IRepositoryAnalyzerService _analyzer;
    private readonly InstallPlanner _planner;
    private readonly IRepositoryMediaService _mediaService;
    private readonly ImageLoader? _images;
    private readonly IInstallationService _installer;
    private readonly IInstalledAppStore _installedApps;
    private readonly LaunchService _launcher;
    private readonly MachineProfile _machine;
    private readonly IAppLog _log;

    public RepositoryDetailsViewModel(
        GitHubRepository repository,
        IGitHubClient github,
        IRepositoryExplanationService explanations,
        IRepositoryAnalyzerService analyzer,
        InstallPlanner planner,
        IInstallationService installer,
        IInstalledAppStore installedApps,
        LaunchService launcher,
        IRepositoryMediaService mediaService,
        MachineProfile machine,
        IAppLog log,
        ImageLoader? images = null)
    {
        Repository = repository;
        _github = github;
        _explanations = explanations;
        _analyzer = analyzer;
        _planner = planner;
        _installer = installer;
        _installedApps = installedApps;
        _launcher = launcher;
        _mediaService = mediaService;
        _images = images;
        _machine = machine;
        _log = log;

        // Seed the page from what the card already knows so it is never blank.
        Title = repository.Name;
        Owner = repository.OwnerLogin;
        var seed = explanations.ExplainFromMetadata(repository);
        WhatItIs = seed.WhatItIs;
        WhatYouCanDoWithIt = seed.WhatYouCanDoWithIt;
        Headline = seed.Headline;
    }

    public GitHubRepository Repository { get; private set; }

    public ObservableCollection<string> Highlights { get; } = [];
    public ObservableCollection<RepositorySignal> Signals { get; } = [];
    public ObservableCollection<ReadmeBlock> ReadmeBlocks { get; } = [];
    public ObservableCollection<ReleaseSummary> Releases { get; } = [];
    public ObservableCollection<LanguageShare> Languages { get; } = [];

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _owner = "";
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _whatItIs = "";
    [ObservableProperty] private string _whatYouCanDoWithIt = "";

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty] private string? _originalDescription;
    [ObservableProperty] private bool _hasReadme;
    [ObservableProperty] private bool _hasReleasesToShow;
    [ObservableProperty] private bool _hasLanguages;
    [ObservableProperty] private string _readmeNotice = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string ThisComputerText => "Your computer: " + PlatformInfo.CurrentDescription;

    // ---- Technical metadata, shown separately from the plain-English part ----
    public string StarsText => Humanize.Count(Repository.Stars);
    public string ForksText => Humanize.Count(Repository.Forks);
    public string IssuesText => Humanize.Count(Repository.OpenIssuesCount);
    public string CreatedText => Humanize.RelativeTime(Repository.CreatedAt);
    public string UpdatedText => Humanize.RelativeTime(Repository.LastActivity);
    public string LicenseText => Repository.HasLicense
        ? (Repository.LicenseSpdxId ?? Repository.LicenseName)!
        : "None detected";
    public string LanguageText => Repository.Language ?? "Not reported";
    public string DefaultBranchText => Repository.DefaultBranch ?? "Unknown";
    public string SizeText => Humanize.FileSize(Repository.Size * 1024L);
    public string RepositoryUrl => Repository.HtmlUrl;
    public string? Homepage => Repository.Homepage;
    public bool HasHomepage => !string.IsNullOrWhiteSpace(Repository.Homepage);
    public bool IsArchived => Repository.IsArchived;
    public IReadOnlyList<string> Topics => Repository.Topics;
    public bool HasTopics => Repository.Topics.Count > 0;

    // ---- Loading ----------------------------------------------------------

    [RelayCommand(IncludeCancelCommand = true)]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;

        var owner = Repository.OwnerLogin;
        var name = Repository.Name;

        try
        {
            // The repository itself must succeed; the rest degrades gracefully.
            var repositoryTask = _github.GetRepositoryAsync(owner, name, cancellationToken);
            var readmeTask = Tolerate(() => _github.GetReadmeAsync(owner, name, cancellationToken), (string?)null, "README");
            var languagesTask = Tolerate(
                () => _github.GetLanguagesAsync(owner, name, cancellationToken),
                (IReadOnlyDictionary<string, long>)new Dictionary<string, long>(), "languages");
            var releasesTask = Tolerate(
                () => _github.GetReleasesAsync(owner, name, 10, cancellationToken),
                (IReadOnlyList<GitHubRelease>)[], "releases");

            await Task.WhenAll(repositoryTask, readmeTask, languagesTask, releasesTask);

            Repository = await repositoryTask;

            var details = new RepositoryDetails
            {
                Repository = Repository,
                ReadmeMarkdown = await readmeTask,
                Languages = await languagesTask,
                Releases = await releasesTask
            };

            await ApplyAsync(details, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _log.Info("Details", $"Loading {owner}/{name} was cancelled.");
        }
        catch (GitHubApiException ex)
        {
            ErrorMessage = ex.UserMessage;
            _log.Warn("Details", $"Could not load {owner}/{name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            ErrorMessage = "Something unexpected went wrong loading this repository.";
            _log.Error("Details", $"Unexpected failure loading {owner}/{name}", ex);
        }
        finally
        {
            IsLoading = false;
            RaiseMetadataChanged();
        }
    }

    private async Task ApplyAsync(RepositoryDetails details, CancellationToken cancellationToken)
    {
        var explanation = await _explanations.ExplainAsync(details, cancellationToken);

        Headline = explanation.Headline;
        WhatItIs = explanation.WhatItIs;
        WhatYouCanDoWithIt = explanation.WhatYouCanDoWithIt;
        OriginalDescription = explanation.OriginalDescription;

        Highlights.Clear();
        foreach (var highlight in explanation.Highlights) Highlights.Add(highlight);

        Signals.Clear();
        foreach (var signal in RepositorySignalBuilder.Build(details)) Signals.Add(signal);

        Languages.Clear();
        foreach (var (language, share) in details.LanguageShares().Take(6))
        {
            Languages.Add(new LanguageShare(language, share, Humanize.Percent(share)));
        }

        Releases.Clear();
        foreach (var release in details.Releases.Take(5))
        {
            Releases.Add(new ReleaseSummary(
                release.DisplayName,
                release.TagName,
                Humanize.RelativeTime(release.PublishedAt ?? release.CreatedAt),
                release.Prerelease ? "Pre-release" : "Stable release",
                release.Assets.Count,
                release.Assets.Take(8)
                    .Select(a => new ReleaseAssetSummary(a.Name, Humanize.FileSize(a.Size)))
                    .ToList()));
        }

        ReadmeBlocks.Clear();
        var blocks = Services.Readme.ReadmeParser.Parse(details.ReadmeMarkdown);
        foreach (var block in blocks.Take(120)) ReadmeBlocks.Add(block);

        HasReleasesToShow = Releases.Count > 0;
        HasLanguages = Languages.Count > 0;
        HasReadme = ReadmeBlocks.Count > 0;
        ReadmeNotice = HasReadme
            ? "Shown as plain text. Open it on GitHub for the formatted version."
            : "This repository has no README, so there is nothing here to read.";

        await RunAnalysisAsync(details, cancellationToken);
        RaiseMetadataChanged();
    }

    /// <summary>
    /// The Milestone 2 analysis pass: rank the release assets against this machine,
    /// inspect the repository's structure, and produce an installation plan.
    /// Nothing is downloaded and nothing is executed.
    /// </summary>
    private async Task RunAnalysisAsync(RepositoryDetails details, CancellationToken cancellationToken)
    {
        var progress = new Progress<AnalysisStage>(stage => AnalysisStatus = stage.ToDisplayString());

        try
        {
            AnalysisStatus = AnalysisStage.InspectingReleases.ToDisplayString();
            var releaseAnalysis = ReleaseAnalyzer.Analyze(details.Releases, _machine);

            var analysis = await _analyzer
                .AnalyzeAsync(details.Repository, releaseAnalysis, progress, cancellationToken)
                .ConfigureAwait(true);

            var plan = _planner.Create(details.Repository, analysis, releaseAnalysis, _machine);


            ApplyAnalysis(analysis, releaseAnalysis, plan);
            ApplyFriendlySummary(analysis, releaseAnalysis, plan);
            ApplyInstallState(plan);

            // The README and file listing are already in hand, so finding pictures costs
            // no further GitHub requests.
            // The analysis carries the file listing it used, so pictures cost no extra request.
            ApplyMedia(_mediaService.Discover(
                details.Repository, details.ReadmeMarkdown, analysis.FileListing));

            _log.Info("Details", $"{details.Repository.FullName}: {analysis.ApplicationType} "
                                 + $"({analysis.ApplicationTypeConfidence}), plan: {plan.Strategy}");
        }
        catch (OperationCanceledException)
        {
            AnalysisStatus = "";
            throw;
        }
        catch (Exception ex)
        {
            // The rest of the page is still useful, so a failed analysis is reported
            // in place rather than taking the whole details view down.
            _log.Error("Details", $"Analysis failed for {details.Repository.FullName}", ex);
            AnalysisStatus = "";
            AnalysisIncompleteReason = "RepoDeck could not finish analysing this repository.";
            HasAnalysis = true;
        }
    }

    // ---- Commands ---------------------------------------------------------

    [RelayCommand]
    private void OpenOnGitHub() => SystemBrowser.OpenUrl(Repository.HtmlUrl, _log);

    [RelayCommand]
    private void OpenHomepage() => SystemBrowser.OpenUrl(Repository.Homepage, _log);

    [RelayCommand]
    private void OpenReleases() => SystemBrowser.OpenUrl(Repository.HtmlUrl + "/releases", _log);

    // ---- Helpers ----------------------------------------------------------

    /// <summary>
    /// Runs a supplementary request, falling back to a default if it fails. A missing
    /// README or a repository with releases disabled must not break the whole page.
    /// </summary>
    private async Task<T> Tolerate<T>(Func<Task<T>> operation, T fallback, string what)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn("Details", $"Could not load {what} for {Repository.FullName}: {ex.Message}");
            return fallback;
        }
    }

    private void RaiseMetadataChanged()
    {
        OnPropertyChanged(nameof(StarsText));
        OnPropertyChanged(nameof(ForksText));
        OnPropertyChanged(nameof(IssuesText));
        OnPropertyChanged(nameof(CreatedText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(LicenseText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(DefaultBranchText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(RepositoryUrl));
        OnPropertyChanged(nameof(Homepage));
        OnPropertyChanged(nameof(HasHomepage));
        OnPropertyChanged(nameof(IsArchived));
        OnPropertyChanged(nameof(Topics));
        OnPropertyChanged(nameof(HasTopics));
    }
}

/// <summary>A language and its share of the codebase, ready for display.</summary>
public sealed record LanguageShare(string Name, double Share, string PercentText)
{
    /// <summary>Width of the proportion bar, as a fraction of 220 pixels.</summary>
    public double BarWidth => Math.Max(Share * 220, 4);
}

public sealed record ReleaseAssetSummary(string Name, string SizeText);

public sealed record ReleaseSummary(
    string Name,
    string Tag,
    string PublishedText,
    string Kind,
    int AssetCount,
    IReadOnlyList<ReleaseAssetSummary> Assets)
{
    public bool HasAssets => AssetCount > 0;

    public string AssetSummary => AssetCount == 0
        ? "Source code only"
        : $"{AssetCount} downloadable file{(AssetCount == 1 ? "" : "s")}";
}
