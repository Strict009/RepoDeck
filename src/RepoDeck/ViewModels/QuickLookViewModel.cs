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
    private readonly Services.History.IRecentlyViewed _recentlyViewed;
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
        ImageLoader? images = null,
        Services.History.IRecentlyViewed? recentlyViewed = null)
    {
        _recentlyViewed = recentlyViewed ?? Services.History.NullRecentlyViewed.Instance;
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

    /// <summary>Every picture worth showing, best first. The hero is one of these.</summary>
    public ObservableCollection<MediaTileViewModel> Gallery { get; } = [];

    /// <summary>
    /// The large image. Always a member of <see cref="Gallery"/>, so promoting a thumbnail
    /// swaps a reference and never fetches anything again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHero))]
    [NotifyPropertyChangedFor(nameof(ShowHeroFallback))]
    [NotifyPropertyChangedFor(nameof(HeroCaption))]
    private MediaTileViewModel? _hero;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowScreenshotStrip))]
    private bool _hasScreenshots;

    public bool ShowScreenshotStrip => HasScreenshots;
    public bool ShowHero => Hero is { IsLoaded: true };
    public bool ShowHeroFallback => !ShowHero;

    /// <summary>Alt text from the README, when the project gave one.</summary>
    public string? HeroCaption => Hero?.Description;

    public string FallbackInitial => Card is null ? "?" : Card.FallbackInitial;
    public Avalonia.Media.IBrush? FallbackBrush => Card?.FallbackBrush;

    /// <summary>Drives the drawn artwork when there is no picture to show.</summary>
    public ProjectKind FallbackKind => Card?.Kind ?? ProjectKind.Unknown;
    public string FallbackCaption => Card?.FallbackCaption ?? "No picture available";

    /// <summary>
    /// Promotes a thumbnail to the hero. Costs nothing: the tile already holds its bitmap.
    /// </summary>
    [RelayCommand]
    private void SelectHero(MediaTileViewModel? tile)
    {
        if (tile is null || ReferenceEquals(tile, Hero)) return;

        foreach (var candidate in Gallery) candidate.IsSelected = ReferenceEquals(candidate, tile);

        Hero = tile;
    }

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

    // ---- The reasoning, folded away --------------------------------------

    /// <summary>
    /// Everything behind the answers above: which download was chosen and why, what
    /// architecture it is built for, what the release contained, what kind of project this
    /// looks like, and what RepoDeck could not establish.
    /// </summary>
    /// <remarks>
    /// This exists because the evidence was previously spread across the panel as bullet
    /// lists under each answer, which pushed the thing a newcomer actually needs - name,
    /// purpose, does it run here, what does it cost, and the button - below the fold.
    /// Nothing was removed; it moved behind one disclosure, grouped by the question it
    /// answers, and every line that was shown before is still shown here.
    /// </remarks>
    public ObservableCollection<EvidenceGroup> Reasoning { get; } = [];

    [ObservableProperty] private bool _reasoningExpanded;

    /// <summary>
    /// The question the user last asked WHY about. The matching group is highlighted, so
    /// pressing WHY beside "Works on this PC" does not dump six groups on somebody who
    /// wanted one answer.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHighlightedQuestion))]
    private string? _highlightedQuestion;

    public bool HasHighlightedQuestion => HighlightedQuestion is { Length: > 0 };

    /// <summary>The questions the WHY buttons point at. One place, so they cannot drift.</summary>
    public const string CompatibilityQuestion = "Will it work on this PC?";
    public const string SetupQuestion = "How much setup?";
    public const string InstallabilityQuestion = "Can RepoDeck install it?";
    public const string KindQuestion = "What kind of project is this?";

    /// <summary>
    /// Opens the reasoning at the group that answers one particular verdict.
    /// </summary>
    /// <remarks>
    /// This is the whole point of keeping evidence rather than just conclusions: a novice
    /// can ask why about the specific thing that puzzled them and be taught the answer,
    /// rather than having to learn what any of it means first.
    /// </remarks>
    [RelayCommand]
    private void Why(string? question)
    {
        HighlightedQuestion = question;
        ReasoningExpanded = true;

        foreach (var group in Reasoning) group.IsHighlighted = group.Question == question;
    }

    [RelayCommand]
    private void ToggleReasoning()
    {
        ReasoningExpanded = !ReasoningExpanded;

        if (!ReasoningExpanded) ClearHighlight();
    }

    private void ClearHighlight()
    {
        HighlightedQuestion = null;
        foreach (var group in Reasoning) group.IsHighlighted = false;
    }

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

        Gallery.Clear();
        HasScreenshots = false;
        Hero = null;

        Reasoning.Clear();
        TechnicalDetails.Clear();
        OnPropertyChanged(nameof(FallbackInitial));
        OnPropertyChanged(nameof(FallbackBrush));
        OnPropertyChanged(nameof(ShowHero));
        OnPropertyChanged(nameof(ShowHeroFallback));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteTooltip));
        OnPropertyChanged(nameof(FavoriteGlyph));

        // Opening Quick Look is a deliberate look at something, so it counts. The list
        // dedupes and is capped, so arrowing through results cannot turn it into a log.
        _recentlyViewed.Record(card.Repository);

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

    /// <summary>
    /// The star, shared with the card it came from so both always agree.
    /// </summary>
    public bool IsFavorite => Card?.IsFavorite ?? false;

    public string FavoriteTooltip => Card?.FavoriteTooltip ?? "Remember this for later";

    public string FavoriteGlyph => Card?.FavoriteGlyph ?? "\u2606";

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Card is null) return;

        Card.ToggleFavoriteCommand.Execute(null);

        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteTooltip));
        OnPropertyChanged(nameof(FavoriteGlyph));
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

        // One sentence at the top. The evidence behind it moved into the reasoning panel.
        SetupSummary = setup.Summary;

        ApplyCompatibility(analysis, plan);

        Installability = InstallabilityEvaluator.FromPlan(plan, analysis, releases, _machine);

        // The card in the grid gets the authoritative answers too, so the grid stops
        // saying "Unknown" about something the user has just had explained to them.
        card.ApplyAnalysedInstallability(Installability);

        var classification = ProjectKindClassifier.Refine(card.Classification, analysis.ApplicationType);
        card.ApplyRefinedClassification(classification);
        OnPropertyChanged(nameof(FallbackKind));
        OnPropertyChanged(nameof(FallbackCaption));

        DownloadSizeText = plan.AssetSize > 0 ? Humanize.FileSize(plan.AssetSize) : "";

        Action = ResolveAction(card, plan);

        BuildReasoning(setup, classification, analysis, releases, plan);
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

    /// <summary>
    /// Gathers everything behind the four answers, grouped by the question it answers.
    /// </summary>
    /// <remarks>
    /// Nothing is dropped. Filenames, architectures, release contents and the analyzer's
    /// own uncertainties all live here, one disclosure away, so the top of the panel can
    /// be the five things a newcomer needs and the button.
    /// </remarks>
    private void BuildReasoning(
        SetupAssessment setup,
        ProjectClassification classification,
        RepositoryAnalysis analysis,
        ReleaseAnalysis releases,
        InstallPlan plan)
    {
        Reasoning.Clear();

        Add(KindQuestion, [
            classification.Label + ".",
            .. classification.Reasons
        ]);

        Add(CompatibilityQuestion, [
            // The verdict itself leads, so this group is never empty: a WHY button that
            // opened nothing would be worse than no button.
            CompatibilityText,
            .. analysis.Evidence.Where(e => e.Supports).Select(e => e.Text),
            .. releases.Recommended is { } asset
                ? new[]
                {
                    $"The chosen download is {asset.Name}.",
                    $"It is built for {asset.Platform.DisplayName()} {asset.Architecture.DisplayName()}."
                }
                : [],
            .. releases.Recommended?.Reasons ?? []
        ]);

        Add(SetupQuestion, [setup.Summary, .. setup.Reasons]);

        Add(InstallabilityQuestion, [
            Installability.Summary,
            .. Installability.Reasons,
            .. plan.Warnings,
            .. plan.BlockingIssues
        ]);

        Add("What is in the release?", releases.HasRelease
            ?
            [
                $"Release {releases.Release!.TagName} has {releases.SoftwareAssets.Count} "
                + $"download{(releases.SoftwareAssets.Count == 1 ? "" : "s")}.",
                .. releases.SoftwareAssets.Take(6).Select(a =>
                    $"{a.Name} - {a.Platform.DisplayName()} {a.Architecture.DisplayName()}, "
                    + $"{Humanize.FileSize(a.Size)}."),
                .. releases.NoRecommendationReason is { Length: > 0 } why ? new[] { why } : []
            ]
            : ["This project publishes no releases."]);

        Add("What could RepoDeck not work out?", [
            .. analysis.Unknowns,
            .. analysis.Warnings,
            .. analysis.IncompleteReason is { Length: > 0 } incomplete ? new[] { incomplete } : []
        ]);

        void Add(string question, IEnumerable<string> points)
        {
            var kept = points
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            if (kept.Count > 0) Reasoning.Add(new EvidenceGroup(question, kept));
        }
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

    /// <summary>
    /// Builds the gallery. Every picture worth showing becomes a tile; the first is the
    /// hero and the rest are the strip, and selecting one promotes it.
    /// </summary>
    /// <remarks>
    /// Each tile owns its bitmap for the life of the panel, so promoting a thumbnail to
    /// the hero swaps a reference rather than fetching anything. The image loader caches
    /// in memory as well, so even a genuinely new request would not hit the network twice -
    /// but the point here is that selection costs nothing at all.
    /// </remarks>
    private void ApplyMedia(RepositoryCardViewModel card, RepositoryMedia media, CancellationToken token)
    {
        if (_images is null) return;

        // The gallery uses the best image of any kind: this panel is wide enough that even
        // GitHub's generated card is legible here, which it is not at card size.
        var candidates = media.Gallery.Count > 0
            ? media.Gallery
            : media.Primary is { } only ? [only] : (IReadOnlyList<MediaCandidate>)[];

        foreach (var candidate in candidates.Take(6))
        {
            var tile = new MediaTileViewModel(candidate.Url, candidate.Description);

            tile.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(MediaTileViewModel.IsLoaded)) return;
                if (!ReferenceEquals(tile, Hero)) return;

                OnPropertyChanged(nameof(ShowHero));
                OnPropertyChanged(nameof(ShowHeroFallback));
            };

            Gallery.Add(tile);
            _ = tile.LoadAsync(_images, token);
        }

        if (Gallery.Count > 0) SelectHero(Gallery[0]);

        // One picture is a hero, not a gallery: a strip of one thumbnail below the image
        // it duplicates is furniture.
        HasScreenshots = Gallery.Count > 1;

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

/// <summary>
/// A question RepoDeck answered, and the evidence it answered it from.
/// </summary>
/// <remarks>
/// Grouped by question rather than presented as one flat list, because "none of the files
/// in release v1.7 suit Windows x64" and "it looks like a music player" answer different
/// things and reading them in sequence makes neither clearer.
/// </remarks>
public sealed partial class EvidenceGroup : ViewModelBase
{
    public EvidenceGroup(string question, IReadOnlyList<string> points)
    {
        Question = question;
        Points = points;
    }

    public string Question { get; }
    public IReadOnlyList<string> Points { get; }

    public bool HasPoints => Points.Count > 0;

    /// <summary>True for the group a WHY button just pointed at.</summary>
    [ObservableProperty] private bool _isHighlighted;
}
