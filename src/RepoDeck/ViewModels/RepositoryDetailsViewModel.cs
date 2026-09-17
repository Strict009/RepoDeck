using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.GitHub;

namespace RepoDeck.ViewModels;

/// <summary>
/// The repository details page. Assembles metadata, README, languages and releases,
/// then presents them as plain English first and technical detail second.
/// </summary>
public sealed partial class RepositoryDetailsViewModel : ViewModelBase
{
    private readonly IGitHubClient _github;
    private readonly IRepositoryExplanationService _explanations;
    private readonly IAppLog _log;

    public RepositoryDetailsViewModel(
        GitHubRepository repository,
        IGitHubClient github,
        IRepositoryExplanationService explanations,
        IAppLog log)
    {
        Repository = repository;
        _github = github;
        _explanations = explanations;
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

    [ObservableProperty] private string _installationStatus = "";
    [ObservableProperty] private string _installationDetail = "";
    [ObservableProperty] private string _compatibilityStatus = "";
    [ObservableProperty] private string _compatibilityDetail = "";

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

    [RelayCommand]
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

        ApplyInstallationAndCompatibility(details);
        RaiseMetadataChanged();
    }

    /// <summary>
    /// States plainly what RepoDeck does and does not yet know. Milestone 1 does not
    /// analyse release assets, so it must not imply that it has.
    /// </summary>
    private void ApplyInstallationAndCompatibility(RepositoryDetails details)
    {
        if (!details.HasReleases)
        {
            InstallationStatus = "Not available";
            InstallationDetail =
                "This project publishes no releases, so there is nothing pre-built to download. "
                + "Using it would mean building it from source, which RepoDeck does not do.";
        }
        else
        {
            var latest = details.LatestStableRelease ?? details.Releases[0];
            var assets = latest.Assets.Count;

            if (assets == 0)
            {
                InstallationStatus = "Source only";
                InstallationDetail =
                    $"Release {latest.TagName} contains source code but no ready-made files, "
                    + "so it cannot be installed without building it first.";
            }
            else
            {
                InstallationStatus = "Requires inspection";
                InstallationDetail =
                    $"Release {latest.TagName} has {assets} attached file{(assets == 1 ? "" : "s")}. "
                    + "RepoDeck does not yet examine these to work out which one suits your computer, "
                    + "so installing from here is not available in this version. "
                    + "The files are listed below exactly as GitHub reports them.";
            }
        }

        CompatibilityStatus = "Requires inspection";
        CompatibilityDetail =
            "RepoDeck does not yet inspect release files or project contents, so it cannot say "
            + "whether this runs on your computer. Nothing below should be read as a compatibility claim.";
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
