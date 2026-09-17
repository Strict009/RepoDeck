using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.GitHub;
using RepoDeck.Services.Media;

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

    public DiscoverViewModel(
        IGitHubClient github,
        IRepositoryExplanationService explanations,
        IRepositoryMediaService media,
        IAppLog log,
        ImageLoader? images = null)
    {
        _github = github;
        _explanations = explanations;
        _media = media;
        _images = images;
        _log = log;

        SelectedSort = SortOption.All[0];
        SelectedStars = StarsOption.All[0];
        SelectedUpdated = UpdatedOption.All[0];
        SelectedLanguage = LanguageOption.All[0];
    }

    /// <summary>Raised when the user asks to see a repository in detail.</summary>
    public event Action<GitHubRepository>? RepositoryOpenRequested;

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
    [ObservableProperty] private bool _onlyLikelyApplications;

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

        // The previous results are gone, so their pictures are no longer wanted.
        CancelImageLoading();
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
        OnlyLikelyApplications = false;
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

    /// <summary>The category currently being browsed, when the search came from a tile.</summary>
    [ObservableProperty] private string? _activeCategory;

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

    private void AppendResults(RepositorySearchResult result)
    {
        var hidden = 0;
        var added = new List<RepositoryCardViewModel>();

        foreach (var repository in result.Items)
        {
            var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);

            // "Application likelihood" is a client-side filter: GitHub search cannot
            // express it, and it costs no extra request.
            if (OnlyLikelyApplications && !likelihood.LooksLikeApplication)
            {
                hidden++;
                continue;
            }

            var explanation = _explanations.ExplainFromMetadata(repository);
            var setup = SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood);

            // Metadata only: one predictable address, no API call, no rate limit spent.
            var media = _media.DiscoverFromMetadata(repository);

            var card = new RepositoryCardViewModel(
                repository, explanation, likelihood, setup,
                media.Primary?.Url, OnRepositoryOpenRequested, _log);

            Results.Add(card);
            added.Add(card);
        }

        HiddenByFilterNotice = hidden == 0
            ? null
            : $"{hidden} result{(hidden == 1 ? "" : "s")} hidden because they do not look like applications.";

        // Pictures arrive afterwards and never hold up the results.
        StartLoadingImages(added);
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
            0 => "No repositories matched.",
            _ => $"Showing {shown} of {Humanize.Count(_totalCount)} matching repositories"
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
