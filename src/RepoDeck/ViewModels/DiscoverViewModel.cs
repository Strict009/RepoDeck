using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.GitHub;
using RepoDeck.Services.Media;
using RepoDeck.Services.Preferences;

namespace RepoDeck.ViewModels;

/// <summary>
/// The Discover page: search GitHub, filter the results, open one.
/// Every network call runs off the UI thread and can be cancelled.
/// </summary>
public sealed partial class DiscoverViewModel : ViewModelBase
{
    private readonly IGitHubClient _github;
    private readonly IRepositoryExplanationService _explanations;
    private readonly IRepositoryMediaService _media;
    private readonly ImageLoader? _images;
    private readonly IAppLog _log;

    private CancellationTokenSource? _imageCancellation;

    private RepositorySearchQuery? _lastQuery;
    private int _loadedPages;
    private int _searchGeneration;
    private int _totalCount;
    private readonly IUserPreferences? _preferences;
    private readonly MachineProfile _machine;

    public DiscoverViewModel(
        IGitHubClient github,
        IRepositoryExplanationService explanations,
        IRepositoryMediaService media,
        IAppLog log,
        ImageLoader? images = null,
        IUserPreferences? preferences = null,
        QuickLookViewModel? quickLook = null,
        MachineProfile? machine = null)
    {
        _github = github;
        _explanations = explanations;
        _media = media;
        _images = images;
        _log = log;
        _preferences = preferences;
        _machine = machine ?? PlatformInfo.CurrentMachine();

        QuickLook = quickLook;

        if (quickLook is not null)
        {
            quickLook.ArtworkFound += OnArtworkFound;

            // The panel can be closed from inside itself or by dismissing it, so the
            // page mirrors its state rather than assuming it owns it.
            quickLook.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not nameof(QuickLookViewModel.IsOpen)) return;

                OnPropertyChanged(nameof(IsQuickLookOpen));

                // Closing from inside the panel clears the selection too. Otherwise the
                // grid goes on highlighting a card nothing is describing, and clicking
                // that same card again would change nothing and reopen nothing.
                if (!quickLook.IsOpen) SelectedResult = null;
            };
        }

        SelectedSort = SortOption.All[0];
        SelectedStars = StarsOption.All[0];
        SelectedUpdated = UpdatedOption.All[0];
        SelectedLanguage = LanguageOption.All[0];

        _viewMode = preferences?.Current.ResultsView ?? ResultsViewMode.Card;
        _browseMode = preferences?.Current.BrowseMode ?? BrowseMode.Apps;
    }

    /// <summary>Raised when the user asks to see a repository in detail.</summary>
    public event Action<GitHubRepository>? RepositoryOpenRequested;

    /// <summary>
    /// Raised when the user asks to install from a card. The shell opens the details page
    /// with the plan and its confirmation step showing; nothing is downloaded by this.
    /// </summary>
    public event Action<GitHubRepository>? RepositoryInstallRequested;

    public ObservableCollection<RepositoryCardViewModel> Results { get; } = [];

    /// <summary>Starting points for someone who has not decided what they want yet.</summary>
    public IReadOnlyList<DiscoverCategory> Categories => DiscoverCategory.All;

    public IReadOnlyList<SortOption> SortOptions => SortOption.All;
    public IReadOnlyList<StarsOption> StarsOptions => StarsOption.All;
    public IReadOnlyList<UpdatedOption> UpdatedOptions => UpdatedOption.All;
    public IReadOnlyList<LanguageOption> LanguageOptions => LanguageOption.All;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SortOption _selectedSort;
    [ObservableProperty] private StarsOption _selectedStars;
    [ObservableProperty] private UpdatedOption _selectedUpdated;
    [ObservableProperty] private LanguageOption _selectedLanguage;

    [ObservableProperty] private bool _excludeArchived = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowWelcome))]
    private bool _isBusy;

    [ObservableProperty] private bool _isLoadingMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowWelcome))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowWelcome))]
    private bool _hasSearched;

    [ObservableProperty] private string _resultSummary = "";
    [ObservableProperty] private bool _canLoadMore;
    [ObservableProperty] private string? _hiddenByFilterNotice;
    [ObservableProperty] private string _rateLimitSummary = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // Exactly one of these three is true at a time, so the view never shows two states.
    public bool ShowResults => HasSearched && !IsBusy && !HasError && Results.Count > 0;
    public bool ShowEmptyState => HasSearched && !IsBusy && !HasError && Results.Count == 0;
    public bool ShowWelcome => !HasSearched && !IsBusy && !HasError;

    // ---- Commands ---------------------------------------------------------

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        // Starting a search supersedes any earlier one. The command cancels the previous
        // execution, but a cancelled execution still runs its catch and finally blocks,
        // and without this guard those would write IsBusy, ResultSummary and ErrorMessage
        // over the results of the newer search.
        var generation = Interlocked.Increment(ref _searchGeneration);

        var query = BuildQuery(page: 1);
        _lastQuery = query;
        _loadedPages = 0;

        IsBusy = true;
        ErrorMessage = null;
        HasSearched = true;

        // The previous results are gone, so their pictures and the panel describing one
        // of them are no longer wanted.
        CancelImageLoading();
        SelectedResult = null;
        Results.Clear();
        RaiseResultStates();

        try
        {
            var result = await _github.SearchRepositoriesAsync(query, cancellationToken);
            if (!IsCurrent(generation)) return;

            _totalCount = result.TotalCount;
            _loadedPages = 1;
            AppendResults(result);
            UpdateSummaries(result);
        }
        catch (OperationCanceledException)
        {
            _log.Info("Discover", "Search cancelled.");
            if (IsCurrent(generation)) ResultSummary = "Search cancelled.";
        }
        catch (GitHubApiException ex)
        {
            _log.Warn("Discover", "Search failed: " + ex.Message);
            if (IsCurrent(generation)) ErrorMessage = ex.UserMessage;
        }
        catch (Exception ex)
        {
            _log.Error("Discover", "Unexpected search failure", ex);
            if (IsCurrent(generation))
            {
                ErrorMessage = "Something unexpected went wrong. The log file has the details.";
            }
        }
        finally
        {
            if (IsCurrent(generation))
            {
                IsBusy = false;
                RaiseResultStates();
            }
        }
    }

    /// <summary>True while this execution is still the newest one.</summary>
    private bool IsCurrent(int generation) => Volatile.Read(ref _searchGeneration) == generation;

    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (_lastQuery is null || IsLoadingMore || !CanLoadMore) return;

        // Page 2 of an old query must never be appended to the results of a new one.
        var generation = Volatile.Read(ref _searchGeneration);

        IsLoadingMore = true;
        try
        {
            var next = _lastQuery with { Page = _loadedPages + 1 };
            var result = await _github.SearchRepositoriesAsync(next, cancellationToken);
            if (!IsCurrent(generation)) return;

            _loadedPages++;
            AppendResults(result);
            UpdateSummaries(result);
        }
        catch (OperationCanceledException)
        {
            _log.Info("Discover", "Loading more results was cancelled.");
        }
        catch (GitHubApiException ex)
        {
            _log.Warn("Discover", "Loading more results failed: " + ex.Message);
            if (IsCurrent(generation)) ErrorMessage = ex.UserMessage;
        }
        catch (Exception ex)
        {
            _log.Error("Discover", "Unexpected paging failure", ex);
            if (IsCurrent(generation))
            {
                ErrorMessage = "Something unexpected went wrong while loading more results.";
            }
        }
        finally
        {
            if (IsCurrent(generation))
            {
                IsLoadingMore = false;
                RaiseResultStates();
            }
        }
    }

    [RelayCommand]
    private Task RetryAsync() => SearchCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task ClearFiltersAsync()
    {
        SelectedSort = SortOption.All[0];
        SelectedStars = StarsOption.All[0];
        SelectedUpdated = UpdatedOption.All[0];
        SelectedLanguage = LanguageOption.All[0];
        ExcludeArchived = true;
        return HasSearched ? SearchCommand.ExecuteAsync(null) : Task.CompletedTask;
    }

    /// <summary>Runs a suggested search from the landing page.</summary>
    [RelayCommand]
    private Task SearchForAsync(string? term)
    {
        SearchText = term ?? "";
        return SearchCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Runs a category. The box shows the short label while the richer query does the
    /// actual searching, so the user sees "Games" rather than a keyword soup.
    /// </summary>
    [RelayCommand]
    private Task SearchCategoryAsync(DiscoverCategory? category)
    {
        if (category is null) return Task.CompletedTask;

        SearchText = category.Query;
        ActiveCategory = category.Label;
        return SearchCommand.ExecuteAsync(null);
    }

    /// <summary>Ways of slicing the search: newest, smallest, portable, weird.</summary>
    public IReadOnlyList<DiscoverCollection> Collections => DiscoverCollection.All;

    /// <summary>
    /// Runs a collection. Unlike a category these adjust the sort and filters as well as
    /// the text, because "recently updated" is a question about ordering rather than about
    /// subject matter.
    /// </summary>
    [RelayCommand]
    private Task SearchCollectionAsync(DiscoverCollection? collection)
    {
        if (collection is null) return Task.CompletedTask;

        SearchText = collection.Query;
        ActiveCategory = collection.Label;

        SelectedSort = SortOption.All.FirstOrDefault(o => o.Value == collection.Sort) ?? SelectedSort;
        SelectedUpdated = UpdatedOption.All.FirstOrDefault(o => o.Value == collection.Updated) ?? SelectedUpdated;

        if (collection.MinStars is { } stars)
        {
            SelectedStars = StarsOption.All
                .Where(o => o.MinStars <= stars)
                .MaxBy(o => o.MinStars ?? 0) ?? SelectedStars;
        }

        return SearchCommand.ExecuteAsync(null);
    }

    /// <summary>The category currently being browsed, when the search came from a tile.</summary>
    [ObservableProperty] private string? _activeCategory;


    // ---- View mode --------------------------------------------------------

    /// <summary>
    /// Card or Compact, remembered between runs.
    /// </summary>
    /// <remarks>
    /// Card is the default and always will be: someone who does not know what they want
    /// is helped far more by a picture and a sentence than by a dense list. Compact
    /// exists for the opposite person, who already knows the landscape and wants forty
    /// results on screen rather than nine.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCardView))]
    [NotifyPropertyChangedFor(nameof(IsCompactView))]
    private ResultsViewMode _viewMode;

    public bool IsCardView => ViewMode == ResultsViewMode.Card;
    public bool IsCompactView => ViewMode == ResultsViewMode.Compact;

    partial void OnViewModeChanged(ResultsViewMode value) =>
        _preferences?.Update(p => p with { ResultsView = value });

    [RelayCommand]
    private void UseCardView() => ViewMode = ResultsViewMode.Card;

    [RelayCommand]
    private void UseCompactView() => ViewMode = ResultsViewMode.Compact;

    // ---- Browse mode ------------------------------------------------------

    /// <summary>
    /// Apps or Everything, remembered between runs.
    /// </summary>
    /// <remarks>
    /// Apps prioritises what RepoDeck has evidence is a usable program, and sets aside only
    /// the clearest cases - a library it is sure about, or plainly reading material.
    /// Everything is raw GitHub discovery in GitHub's own order. A result RepoDeck merely
    /// could not classify is shown in both, because "I could not tell" is not a verdict.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppsMode))]
    [NotifyPropertyChangedFor(nameof(IsEverythingMode))]
    [NotifyPropertyChangedFor(nameof(BrowseModeExplanation))]
    private BrowseMode _browseMode;

    public bool IsAppsMode => BrowseMode == BrowseMode.Apps;
    public bool IsEverythingMode => BrowseMode == BrowseMode.Everything;

    public string BrowseModeExplanation => IsAppsMode
        ? "Programs first. RepoDeck puts what it has evidence you can actually run at the top, "
          + "and sets aside libraries and reading material. It is not judging quality or safety."
        : "Everything GitHub returned, in GitHub's own order. Nothing is set aside.";

    /// <summary>Results Apps mode set aside, phrased so the user can get them back.</summary>
    [ObservableProperty] private string? _setAsideNotice;

    partial void OnBrowseModeChanged(BrowseMode value)
    {
        _preferences?.Update(p => p with { BrowseMode = value });

        // The mode changes which results are shown and in what order, so it has to rerun
        // rather than wait for the next search.
        if (HasSearched) _ = SearchCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void UseAppsMode() => BrowseMode = BrowseMode.Apps;

    [RelayCommand]
    private void UseEverythingMode() => BrowseMode = BrowseMode.Everything;

    // ---- Quick Look -------------------------------------------------------

    /// <summary>The side panel, or null when the page was built without one.</summary>
    public QuickLookViewModel? QuickLook { get; }

    public bool HasQuickLook => QuickLook is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private RepositoryCardViewModel? _selectedResult;

    public bool HasSelection => SelectedResult is not null;

    /// <summary>
    /// Selecting a result opens the panel. Selection is the trigger rather than a
    /// separate button, because a panel that needs to be summoned is a panel nobody uses.
    /// </summary>
    partial void OnSelectedResultChanged(RepositoryCardViewModel? value)
    {
        if (QuickLook is null) return;

        if (value is null)
        {
            QuickLook.CloseCommand.Execute(null);
            return;
        }

        // Fire and forget: ShowAsync fills the panel from what the card already knows
        // before the deeper look starts, and it guards its own staleness.
        _ = QuickLook.ShowAsync(value);
    }

    /// <summary>Whether the panel is showing.</summary>
    public bool IsQuickLookOpen => QuickLook?.IsOpen ?? false;

    /// <summary>Opens Quick Look for a card that was activated rather than selected.</summary>
    private void OpenQuickLook(RepositoryCardViewModel card)
    {
        // Activating the card that is already selected has to reopen the panel rather
        // than do nothing, which is what assigning an unchanged selection would do.
        if (ReferenceEquals(SelectedResult, card))
        {
            if (QuickLook is not null) _ = QuickLook.ShowAsync(card);
            return;
        }

        SelectedResult = card;
    }

    /// <summary>
    /// The card asked to install. The page does not install: it asks the shell for the
    /// details page, which shows the plan and requires confirmation before anything is
    /// downloaded. That gate has one implementation and this is not a second one.
    /// </summary>
    private void OnInstallRequested(RepositoryCardViewModel card)
    {
        _log.Info("Discover", "Install requested for " + card.Repository.FullName);
        RepositoryInstallRequested?.Invoke(card.Repository);
    }

    /// <summary>
    /// A deeper look found a real screenshot for a card that only had its designed tile.
    /// Putting it back means the grid improves as someone explores it.
    /// </summary>
    private void OnArtworkFound(RepositoryCardViewModel card, string url)
    {
        var token = _imageCancellation?.Token ?? CancellationToken.None;
        _ = card.AdoptArtworkAsync(url, _images, token);
    }

    // ---- Internals --------------------------------------------------------

    private RepositorySearchQuery BuildQuery(int page) => new()
    {
        Text = SearchText?.Trim() ?? "",
        Language = SelectedLanguage?.Value,
        MinStars = SelectedStars?.MinStars,
        UpdatedWithin = SelectedUpdated?.Value ?? UpdatedWithin.Any,
        Sort = SelectedSort?.Value ?? RepositorySort.BestMatch,
        ExcludeArchived = ExcludeArchived,
        Page = page,
        PerPage = 30
    };

    /// <summary>
    /// Turns a page of search results into cards, scoring each for relevance and, in Apps
    /// mode, ordering by it.
    /// </summary>
    /// <remarks>
    /// Every signal used here is free. The scorer sees only what the search response
    /// already contained plus classifications computed locally from it, so ranking thirty
    /// results costs nothing and a search can never become N+1 requests.
    ///
    /// Apps mode reorders and, for the clearest cases, sets aside. Everything mode leaves
    /// GitHub's own ordering alone: GitHub knows things about text relevance that RepoDeck
    /// cannot see, and overriding that wholesale would be arrogant.
    /// </remarks>
    private void AppendResults(RepositorySearchResult result)
    {
        var query = SearchText?.Trim() ?? "";
        var setAside = 0;
        var added = new List<RepositoryCardViewModel>();

        foreach (var repository in result.Items)
        {
            var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);
            var explanation = _explanations.ExplainFromMetadata(repository);
            var setup = SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood);

            // Metadata only: one predictable address, no API call, no rate limit spent.
            var media = _media.DiscoverFromMetadata(repository);

            // A search result yields nothing but GitHub's generated preview card, which is
            // the repository name and description set in small type. At card size that is
            // unreadable text pretending to be a picture, so the card draws its own artwork
            // instead and asks for the project's own pictures only. Quick Look reads the
            // README and hands back a real screenshot when it finds one.
            var card = new RepositoryCardViewModel(
                repository, explanation, likelihood, setup,
                media.PrimaryArtwork?.Url, OnRepositoryOpenRequested, _log,
                OpenQuickLook, OnInstallRequested);

            card.ApplyRelevance(RelevanceScorer.Score(
                repository, query, card.Classification, card.Installability, _machine));

            // Only the clearest cases are set aside, and only when Apps was chosen. A
            // project RepoDeck merely could not classify is still shown: "I could not tell"
            // is not the same as "not for you".
            if (BrowseMode == BrowseMode.Apps && IsClearlyNotAnApplication(card))
            {
                setAside++;
                continue;
            }

            added.Add(card);
        }

        if (BrowseMode == BrowseMode.Apps)
        {
            // A stable sort: equal scores keep GitHub's order, so the reordering only ever
            // promotes things it has a reason to promote.
            added = added.OrderByDescending(c => c.Relevance.Score).ToList();
        }

        foreach (var card in added) Results.Add(card);

        SetAsideNotice = setAside == 0
            ? null
            : $"{setAside} result{(setAside == 1 ? "" : "s")} set aside as libraries or reading material. "
              + "Switch to Everything to see them.";

        HiddenByFilterNotice = SetAsideNotice;

        // Pictures arrive afterwards and never hold up the results.
        StartLoadingImages(added);
    }

    /// <summary>
    /// Whether Apps mode should set a result aside. Deliberately narrow: a library RepoDeck
    /// is sure about, or something that is plainly reading material. Everything else stays.
    /// </summary>
    private static bool IsClearlyNotAnApplication(RepositoryCardViewModel card)
    {
        if (card.Classification.Kind == ProjectKind.Library
            && card.Classification.Confidence is Confidence.Likely or Confidence.Confirmed)
        {
            return true;
        }

        return card.Relevance.Score <= 30;
    }

    /// <summary>
    /// Fetches card pictures in the background.
    /// </summary>
    /// <remarks>
    /// Deliberately fire-and-forget: search results appear immediately and fill in as the
    /// images arrive. The token is tied to the current result set, so starting a new
    /// search abandons every request still in flight rather than paying for pictures
    /// nobody is looking at any more.
    /// </remarks>
    private void StartLoadingImages(IReadOnlyList<RepositoryCardViewModel> cards)
    {
        if (_images is null || cards.Count == 0) return;

        var token = _imageCancellation?.Token ?? CancellationToken.None;

        foreach (var card in cards)
        {
            _ = card.LoadImageAsync(_images, token);
        }
    }

    /// <summary>Abandons picture requests for cards that are no longer on screen.</summary>
    private void CancelImageLoading()
    {
        var previous = _imageCancellation;
        _imageCancellation = new CancellationTokenSource();

        try
        {
            previous?.Cancel();
            previous?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already gone; nothing to abandon.
        }
    }

    private void OnRepositoryOpenRequested(GitHubRepository repository)
    {
        _log.Info("Discover", "Opening details for " + repository.FullName);
        RepositoryOpenRequested?.Invoke(repository);
    }

    private void UpdateSummaries(RepositorySearchResult result)
    {
        var shown = Results.Count;

        ResultSummary = _totalCount switch
        {
            0 => "Nothing found.",
            _ => $"Showing {shown} of {Humanize.Count(_totalCount)} projects found"
        };

        if (result.IncompleteResults)
        {
            ResultSummary += " (GitHub returned a partial set for this search)";
        }

        CanLoadMore = result.HasMore;
        UpdateRateLimitSummary();
    }

    private void UpdateRateLimitSummary()
    {
        var limit = _github.RateLimit;
        if (!limit.IsKnown)
        {
            RateLimitSummary = "";
            return;
        }

        var suffix = limit.IsAuthenticated ? "" : " (unauthenticated)";
        RateLimitSummary = $"{limit.Remaining} of {limit.Limit} GitHub requests left{suffix}";
    }

    private void RaiseResultStates()
    {
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowWelcome));
    }
}
