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
/// The Discover page: search GitHub, filter the results, open one.
/// Every network call runs off the UI thread and can be cancelled.
/// </summary>
public sealed partial class DiscoverViewModel : ViewModelBase
{
    private readonly IGitHubClient _github;
    private readonly IRepositoryExplanationService _explanations;
    private readonly IAppLog _log;

    private RepositorySearchQuery? _lastQuery;
    private int _loadedPages;
    private int _totalCount;

    public DiscoverViewModel(IGitHubClient github, IRepositoryExplanationService explanations, IAppLog log)
    {
        _github = github;
        _explanations = explanations;
        _log = log;

        SelectedSort = SortOption.All[0];
        SelectedStars = StarsOption.All[0];
        SelectedUpdated = UpdatedOption.All[0];
        SelectedLanguage = LanguageOption.All[0];
    }

    /// <summary>Raised when the user asks to see a repository in detail.</summary>
    public event Action<GitHubRepository>? RepositoryOpenRequested;

    public ObservableCollection<RepositoryCardViewModel> Results { get; } = [];

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
        var query = BuildQuery(page: 1);
        _lastQuery = query;
        _loadedPages = 0;

        IsBusy = true;
        ErrorMessage = null;
        HasSearched = true;
        Results.Clear();
        RaiseResultStates();

        try
        {
            var result = await _github.SearchRepositoriesAsync(query, cancellationToken);
            _totalCount = result.TotalCount;
            _loadedPages = 1;
            AppendResults(result);
            UpdateSummaries(result);
        }
        catch (OperationCanceledException)
        {
            _log.Info("Discover", "Search cancelled by the user.");
            ResultSummary = "Search cancelled.";
        }
        catch (GitHubApiException ex)
        {
            ErrorMessage = ex.UserMessage;
            _log.Warn("Discover", "Search failed: " + ex.Message);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Something unexpected went wrong. The log file has the details.";
            _log.Error("Discover", "Unexpected search failure", ex);
        }
        finally
        {
            IsBusy = false;
            RaiseResultStates();
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_lastQuery is null || IsLoadingMore || !CanLoadMore) return;

        IsLoadingMore = true;
        try
        {
            var next = _lastQuery with { Page = _loadedPages + 1 };
            var result = await _github.SearchRepositoriesAsync(next);
            _loadedPages++;
            AppendResults(result);
            UpdateSummaries(result);
        }
        catch (GitHubApiException ex)
        {
            ErrorMessage = ex.UserMessage;
            _log.Warn("Discover", "Loading more results failed: " + ex.Message);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Something unexpected went wrong while loading more results.";
            _log.Error("Discover", "Unexpected paging failure", ex);
        }
        finally
        {
            IsLoadingMore = false;
            RaiseResultStates();
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

    /// <summary>Runs a suggested search from the welcome screen.</summary>
    [RelayCommand]
    private Task SearchForAsync(string? term)
    {
        SearchText = term ?? "";
        return SearchCommand.ExecuteAsync(null);
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

    private void AppendResults(RepositorySearchResult result)
    {
        var hidden = 0;

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
            Results.Add(new RepositoryCardViewModel(
                repository, explanation, likelihood, OnRepositoryOpenRequested, _log));
        }

        HiddenByFilterNotice = hidden == 0
            ? null
            : $"{hidden} result{(hidden == 1 ? "" : "s")} hidden because they do not look like applications.";
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
