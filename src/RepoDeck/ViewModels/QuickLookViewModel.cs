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
/// The side panel that answers "what is this?" without leaving the search results.
/// </summary>
/// <remarks>
/// Looking at six candidates used to mean six round trips through a full page and six
/// journeys back. Quick Look runs the same analysis the details page runs and shows the
/// answers beside the grid, so comparing things is a matter of clicking along a row.
///
/// Every load is guarded by a generation counter as well as a cancellation token. The
/// token stops the work; the counter stops a slower earlier load from writing its answers
/// over a newer selection if it happens to finish afterwards. Both are needed: a
/// cancelled continuation still runs its catch and finally blocks.
///
/// Quick Look does not install anything itself. Installing goes through the details page,
/// which owns the one audited install state machine and its confirmation gate; a second
/// implementation here would be a second place for that boundary to be got wrong.
/// </remarks>
public sealed partial class QuickLookViewModel : ViewModelBase
{
    private readonly IGitHubClient _github;
    private readonly IRepositoryExplanationService _explanations;
    private readonly IRepositoryAnalyzerService _analyzer;
    private readonly InstallPlanner _planner;
    private readonly IRepositoryMediaService _media;
    private readonly IInstalledAppStore _installedApps;
    private readonly LaunchService _launcher;
    private readonly MachineProfile _machine;
    private readonly ImageLoader? _images;
    private readonly IAppLog _log;

    private CancellationTokenSource? _work;
    private int _generation;
    private InstallPlan? _plan;

    public QuickLookViewModel(
        IGitHubClient github,
        IRepositoryExplanationService explanations,
        IRepositoryAnalyzerService analyzer,
        InstallPlanner planner,
        IRepositoryMediaService media,
        IInstalledAppStore installedApps,
        LaunchService launcher,
        MachineProfile machine,
        IAppLog log,
        ImageLoader? images = null)
    {
        _github = github;
        _explanations = explanations;
        _analyzer = analyzer;
        _planner = planner;
        _media = media;
        _installedApps = installedApps;
        _launcher = launcher;
        _machine = machine;
        _log = log;
        _images = images;
    }

    /// <summary>Raised when the user asks for the full page instead.</summary>
    public event Action<GitHubRepository>? DetailsRequested;

    /// <summary>
    /// Raised when the user asks to install. The shell opens the details page with the
    /// plan on screen and its confirmation step ready, because that is where installing
    /// happens.
    /// </summary>
    public event Action<GitHubRepository>? InstallRequested;

    /// <summary>Raised when a deeper look turned up better artwork than the card had.</summary>
    public event Action<RepositoryCardViewModel, string>? ArtworkFound;

    public RepositoryCardViewModel? Card { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPanel))]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBody))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowBody))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool ShowPanel => IsOpen;
    public bool ShowBody => !IsLoading && !HasError;

    /// <summary>What the analysis is doing, so a slow look is not a blank panel.</summary>
    [ObservableProperty] private string _activityText = "";

    // ---- Identity ---------------------------------------------------------
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";

    // ---- Media ------------------------------------------------------------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHero))]
    [NotifyPropertyChangedFor(nameof(ShowHeroFallback))]
    private MediaTileViewModel? _hero;

    public ObservableCollection<MediaTileViewModel> Screenshots { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowScreenshotStrip))]
    private bool _hasScreenshots;

    public bool ShowScreenshotStrip => HasScreenshots;
    public bool ShowHero => Hero is { IsLoaded: true };
    public bool ShowHeroFallback => !ShowHero;

    public string FallbackInitial => Card is null ? "?" : Card.FallbackInitial;
    public Avalonia.Media.IBrush? FallbackBrush => Card?.FallbackBrush;

    // ---- The plain-English answers ---------------------------------------
    [ObservableProperty] private string _whatItIs = "";
    [ObservableProperty] private string _whatYouCanDoWithIt = "";

    [ObservableProperty] private string _setupLabel = "";
    [ObservableProperty] private string _setupSummary = "";

    [ObservableProperty] private string _compatibilityText = "";
    [ObservableProperty] private bool _isCompatible;
    [ObservableProperty] private bool _compatibilityIsKnown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallabilityLabel))]
    [NotifyPropertyChangedFor(nameof(InstallabilitySummary))]
    [NotifyPropertyChangedFor(nameof(InstallabilityEvidence))]
    [NotifyPropertyChangedFor(nameof(HasInstallabilityEvidence))]
    [NotifyPropertyChangedFor(nameof(IsReadyToInstall))]
    [NotifyPropertyChangedFor(nameof(IsNotCompatible))]
    private Installability _installability = Installability.Unknown;

    public string InstallabilityLabel => Installability.Label;
    public string InstallabilitySummary => Installability.Summary;

    public IReadOnlyList<string> InstallabilityEvidence => Installability.Reasons;
    public bool HasInstallabilityEvidence => Installability.Reasons.Count > 0;

    public bool IsReadyToInstall => Installability.State == InstallabilityState.ReadyToInstall;
    public bool IsNotCompatible => Installability.State == InstallabilityState.NotCompatible;

    /// <summary>The disclaimer, in one place, shown wherever the state is shown.</summary>
    public string SafetyDisclaimer => Models.Installability.NotASafetyJudgement;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadSize))]
    private string _downloadSizeText = "";

    public bool HasDownloadSize => DownloadSizeText.Length > 0;

    // ---- The action -------------------------------------------------------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallAction))]
    [NotifyPropertyChangedFor(nameof(ShowRunAction))]
    private QuickLookAction _action = QuickLookAction.Details;

    public bool ShowInstallAction => Action == QuickLookAction.Install;
    public bool ShowRunAction => Action == QuickLookAction.Run;

    // ---- Technical details, folded away ----------------------------------
    public ObservableCollection<TechnicalFact> TechnicalDetails { get; } = [];

    [ObservableProperty] private bool _technicalDetailsExpanded;

    // ---- Opening and closing ---------------------------------------------

    /// <summary>
    /// Shows a card in the panel and starts the deeper look.
    /// </summary>
    /// <remarks>
    /// Returns as soon as the panel has been populated with what the card already knew,
    /// so selecting a result is instant even when the analysis takes a few seconds.
    /// </remarks>
    public Task ShowAsync(RepositoryCardViewModel card)
    {
        // Whatever was loading is no longer wanted.
        var generation = Interlocked.Increment(ref _generation);
        CancelWork();

        Card = card;
        IsOpen = true;
        ErrorMessage = null;
        IsLoading = true;
        ActivityText = "READING PROJECT";

        // Seed from the card so the panel is never blank while the analysis runs.
        Title = card.FriendlyName;
        Subtitle = card.MetadataLine;
        WhatItIs = card.Purpose;
        WhatYouCanDoWithIt = "";
        SetupLabel = card.SetupLabel;
        SetupSummary = card.Setup.Summary;
        Installability = card.Installability;
        CompatibilityText = "Checking whether this runs on " + PlatformInfo.CurrentDescription + "...";
        CompatibilityIsKnown = false;
        IsCompatible = false;
        DownloadSizeText = "";
        Action = QuickLookAction.Details;
        _plan = null;

        Screenshots.Clear();
        HasScreenshots = false;
        Hero = null;

        TechnicalDetails.Clear();
        OnPropertyChanged(nameof(FallbackInitial));
        OnPropertyChanged(nameof(FallbackBrush));
        OnPropertyChanged(nameof(ShowHero));
        OnPropertyChanged(nameof(ShowHeroFallback));

        var token = _work!.Token;
        return LoadAsync(card, generation, token);
    }

    [RelayCommand]
    private void Close()
    {
        Interlocked.Increment(ref _generation);
        CancelWork(startNew: false);

        IsOpen = false;
        IsLoading = false;
        Card = null;
    }

    // ---- Actions ----------------------------------------------------------

    [RelayCommand]
    private void OpenDetails()
    {
        if (Card is not null) DetailsRequested?.Invoke(Card.Repository);
    }

    /// <summary>
    /// Hands over to the details page, which shows the plan and asks for confirmation
    /// before anything is downloaded. Quick Look never starts an installation itself.
    /// </summary>
    [RelayCommand]
    private void Install()
    {
        if (Card is not null) InstallRequested?.Invoke(Card.Repository);
    }

    [RelayCommand]
    private void Run()
    {
        if (Card is null) return;

        var manifest = _installedApps.Find(Card.Repository.OwnerLogin, Card.Repository.Name);
        if (manifest is null) return;

        var result = _launcher.Launch(manifest);
        if (!result.Succeeded) ErrorMessage = result.ErrorMessage;
    }

    [RelayCommand]
    private void OpenOnGitHub()
    {
        if (Card is not null) SystemBrowser.OpenUrl(Card.Repository.HtmlUrl, _log);
    }

    [RelayCommand]
    private void ToggleTechnicalDetails() => TechnicalDetailsExpanded = !TechnicalDetailsExpanded;

    // ---- The work ---------------------------------------------------------

    private async Task LoadAsync(RepositoryCardViewModel card, int generation, CancellationToken token)
    {
        var owner = card.Repository.OwnerLogin;
        var name = card.Repository.Name;

        try
        {
            var readmeTask = Tolerate(() => _github.GetReadmeAsync(owner, name, token), (string?)null);
            var releasesTask = Tolerate(
                () => _github.GetReleasesAsync(owner, name, 5, token),
                (IReadOnlyList<GitHubRelease>)[]);

            await Task.WhenAll(readmeTask, releasesTask);
            if (!IsCurrent(generation)) return;

            var details = new RepositoryDetails
            {
                Repository = card.Repository,
                ReadmeMarkdown = await readmeTask,
                Releases = await releasesTask,
                Languages = new Dictionary<string, long>()
            };

            ActivityText = "CHECKING DOWNLOADS";
            var releases = ReleaseAnalyzer.Analyze(details.Releases, _machine);
            if (!IsCurrent(generation)) return;

            ActivityText = "LOOKING INSIDE";
            var analysis = await _analyzer
                .AnalyzeAsync(card.Repository, releases, progress: null, token)
                .ConfigureAwait(true);

            if (!IsCurrent(generation)) return;

            var plan = _planner.Create(card.Repository, analysis, releases, _machine);
            var explanation = await _explanations.ExplainAsync(details, token).ConfigureAwait(true);

            if (!IsCurrent(generation)) return;

            Apply(card, details, analysis, releases, plan, explanation);

            // The README and file listing are already in hand, so pictures cost nothing more.
            ApplyMedia(card, _media.Discover(card.Repository, details.ReadmeMarkdown, analysis.FileListing), token);
        }
        catch (OperationCanceledException)
        {
            // A newer selection replaced this one. Its own load owns the panel now.
            _log.Info("QuickLook", $"Look at {owner}/{name} was cancelled.");
        }
        catch (GitHubApiException ex)
        {
            if (IsCurrent(generation)) ErrorMessage = ex.UserMessage;
            _log.Warn("QuickLook", $"Could not look at {owner}/{name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            if (IsCurrent(generation))
            {
                ErrorMessage = "RepoDeck could not finish looking at this project.";
            }

            _log.Error("QuickLook", $"Unexpected failure looking at {owner}/{name}", ex);
        }
        finally
        {
            if (IsCurrent(generation))
            {
                IsLoading = false;
                ActivityText = "";
            }
        }
    }

    private void Apply(
        RepositoryCardViewModel card,
        RepositoryDetails details,
        RepositoryAnalysis analysis,
        ReleaseAnalysis releases,
        InstallPlan plan,
        RepositoryExplanation explanation)
    {
        _plan = plan;

        WhatItIs = explanation.WhatItIs;
        WhatYouCanDoWithIt = explanation.WhatYouCanDoWithIt;

        var setup = SetupDifficultyEvaluator.Evaluate(analysis, releases, plan);
        SetupLabel = setup.Label;
        SetupSummary = setup.Reasons.Count == 0
            ? setup.Summary
            : setup.Summary + " " + string.Join(" ", setup.Reasons.Take(2));

        ApplyCompatibility(analysis, plan);

        Installability = InstallabilityEvaluator.FromPlan(plan, analysis, releases, _machine);

        // The card in the grid gets the authoritative answer too, so the grid stops
        // saying "Unknown" about something the user has just had explained to them.
        card.ApplyAnalysedInstallability(Installability);

        DownloadSizeText = plan.AssetSize > 0 ? Humanize.FileSize(plan.AssetSize) : "";

        Action = ResolveAction(card, plan);

        BuildTechnicalDetails(details, analysis, releases, plan);
    }

    private void ApplyCompatibility(RepositoryAnalysis analysis, InstallPlan plan)
    {
        var platform = _machine.OperatingSystem;
        var support = analysis.PlatformSupport(platform);
        var machine = PlatformInfo.CurrentDescription;

        // A plan that can proceed is the strongest evidence there is: a real asset, for
        // this platform, that RepoDeck knows how to handle.
        if (plan.CanProceed)
        {
            CompatibilityIsKnown = true;
            IsCompatible = true;
            CompatibilityText = $"Yes - there is a version for {machine}.";
            return;
        }

        switch (support)
        {
            case Confidence.Confirmed or Confidence.Likely:
                CompatibilityIsKnown = true;
                IsCompatible = true;
                CompatibilityText = $"It supports {platform.DisplayName()}, but RepoDeck found "
                                    + "nothing here it can install for you.";
                break;

            case Confidence.Unsupported:
                CompatibilityIsKnown = true;
                IsCompatible = false;
                CompatibilityText = $"No - this does not support {machine}.";
                break;

            default:
                CompatibilityIsKnown = false;
                IsCompatible = false;
                CompatibilityText = $"RepoDeck could not tell whether this runs on {machine}.";
                break;
        }
    }

    /// <summary>
    /// Which single action to offer. Run wins when it is already installed, because at
    /// that point nothing else on the panel is what the person came for.
    /// </summary>
    private QuickLookAction ResolveAction(RepositoryCardViewModel card, InstallPlan plan)
    {
        if (_installedApps.Find(card.Repository.OwnerLogin, card.Repository.Name) is { } manifest
            && manifest.State == InstallationState.Installed)
        {
            return QuickLookAction.Run;
        }

        return plan.CanProceed && !plan.IsDeliberatelyUnsupported
            ? QuickLookAction.Install
            : QuickLookAction.Details;
    }

    private void BuildTechnicalDetails(
        RepositoryDetails details, RepositoryAnalysis analysis, ReleaseAnalysis releases, InstallPlan plan)
    {
        TechnicalDetails.Clear();

        // GitHub's own vocabulary lives here and nowhere else on this panel.
        Add("Repository", details.Repository.FullName);
        Add("Stars", Humanize.Count(details.Repository.Stars));
        Add("Language", details.Repository.Language ?? "Not reported");
        Add("Licence", details.Repository.HasLicense
            ? (details.Repository.LicenseSpdxId ?? details.Repository.LicenseName)!
            : "None detected");
        Add("Last activity", Humanize.RelativeTime(details.Repository.LastActivity));
        Add("Project type", analysis.ApplicationType.ToDisplayString());
        Add("Build system", analysis.BuildSystem.ToString());

        if (releases.Release is { } release)
        {
            Add("Latest release", release.TagName);
            Add("Published", Humanize.RelativeTime(release.PublishedAt ?? release.CreatedAt));
            Add("Downloads in release", releases.SoftwareAssets.Count.ToString());
        }

        if (plan.AssetName is { Length: > 0 })
        {
            Add("Chosen download", plan.AssetName);
            Add("Download size", Humanize.FileSize(plan.AssetSize));
            Add("Package type", plan.PackageType.ToDisplayString());
            Add("Install method", plan.Strategy.ToDisplayString());
            Add("Install location", plan.ProposedInstallDirectory);
        }

        Add("RepoDeck confidence", plan.Confidence.ToDisplayString());

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) TechnicalDetails.Add(new TechnicalFact(label, value));
        }
    }

    private void ApplyMedia(RepositoryCardViewModel card, RepositoryMedia media, CancellationToken token)
    {
        if (_images is null) return;

        // The hero shows the best image of any kind: this panel is wide enough that even
        // GitHub's generated card is legible here, which it is not at card size.
        if (media.Primary is { } primary)
        {
            var hero = new MediaTileViewModel(primary.Url, primary.Description);
            hero.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(MediaTileViewModel.IsLoaded)) return;

                OnPropertyChanged(nameof(ShowHero));
                OnPropertyChanged(nameof(ShowHeroFallback));
            };

            Hero = hero;
            _ = hero.LoadAsync(_images, token);
        }

        foreach (var candidate in media.Gallery.Skip(1).Take(4))
        {
            var tile = new MediaTileViewModel(candidate.Url, candidate.Description);
            Screenshots.Add(tile);
            _ = tile.LoadAsync(_images, token);
        }

        HasScreenshots = Screenshots.Count > 0;

        // Real artwork found here is worth putting back on the card in the grid.
        if (media.PrimaryArtwork is { } artwork)
        {
            ArtworkFound?.Invoke(card, artwork.Url);
        }
    }

    // ---- Plumbing ---------------------------------------------------------

    private bool IsCurrent(int generation) => Volatile.Read(ref _generation) == generation;

    private void CancelWork(bool startNew = true)
    {
        var previous = _work;
        _work = startNew ? new CancellationTokenSource() : null;

        try
        {
            previous?.Cancel();
            previous?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already gone.
        }
    }

    private async Task<T> Tolerate<T>(Func<Task<T>> operation, T fallback)
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
            _log.Warn("QuickLook", "Optional request failed: " + ex.Message);
            return fallback;
        }
    }
}

/// <summary>The single action Quick Look offers for the selected project.</summary>
public enum QuickLookAction
{
    /// <summary>Open the full page. The honest default when there is nothing to install.</summary>
    Details,

    /// <summary>Go to the plan and its confirmation step.</summary>
    Install,

    /// <summary>Already installed: start it.</summary>
    Run
}

/// <summary>One row in the folded-away technical section.</summary>
public sealed record TechnicalFact(string Label, string Value);
