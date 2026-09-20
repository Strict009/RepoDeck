using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>
/// The application shell: the navigation rail, the current page and the status bar.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly DiscoverViewModel _discover;
    private readonly QuickLookViewModel _quickLook;
    private readonly InstalledViewModel _installed;
    private readonly DownloadsViewModel _downloads;
    private readonly FavoritesViewModel _favorites;
    private readonly IUiDispatcher _dispatcher;
    private readonly NavigationHistory _history = new();
    private RepositoryDetailsViewModel? _activeDetails;

    public MainWindowViewModel(AppServices services, IUiDispatcher? dispatcher = null)
    {
        _services = services;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();

        _quickLook = new QuickLookViewModel(
            services.GitHub, services.Explanations, services.Analyzer, services.InstallPlanner,
            services.Media, services.InstalledApps, services.Launcher, services.Machine,
            services.Log, services.Images, services.RecentlyViewed);

        _quickLook.DetailsRequested += ShowRepositoryDetails;

        // Installing happens on the details page and nowhere else, so Quick Look asks the
        // shell to go there with the plan on screen rather than starting anything itself.
        _quickLook.InstallRequested += repository => ShowRepositoryDetails(repository, offerInstall: true);

        _discover = new DiscoverViewModel(
            services.GitHub, services.Explanations, services.Media, services.Log, services.Images,
            services.Preferences, _quickLook, services.Machine, services.Favorites,
            services.RecentlyViewed);

        _discover.RepositoryOpenRequested += ShowRepositoryDetails;
        _discover.RepositoryInstallRequested += repository => ShowRepositoryDetails(repository, offerInstall: true);

        _installed = new InstalledViewModel(
            services.InstalledApps, services.Installer, services.Launcher, services.Log,
            services.UpdateChecker, services.Updater, services.HealthChecker,
            services.History, services.Machine, services.Paths);

        _favorites = new FavoritesViewModel(
            services.Favorites, services.InstalledApps, services.Log, ShowRepositoryDetails);

        _downloads = new DownloadsViewModel(
            services.InstalledApps, services.Installer, services.Log, services.Transfers);

        NavigationItems =
        [
            new NavigationItem("Discover", NavigationIcons.Discover, _discover),
            new NavigationItem("Installed", NavigationIcons.Installed, _installed),
            new NavigationItem("Downloads", NavigationIcons.Downloads, _downloads),
            new NavigationItem("Favorites", NavigationIcons.Favorites, _favorites),
            new NavigationItem("Settings", NavigationIcons.Settings, new SettingsViewModel(services, _dispatcher))
        ];

        _selectedNavigationItem = NavigationItems[0];
        _currentPage = _discover;
        _statusText = _selectedNavigationItem.Title;

        UpdateRateLimit(services.GitHub.RateLimit);
        services.GitHub.RateLimitChanged += UpdateRateLimit;

        services.InstalledApps.Changed += OnLibraryChanged;
        RefreshLibraryCount();

        // The welcome takes over the whole window on a first run, so nothing else has to
        // know about it: the shell simply has no chrome until it is done.
        if (!services.Preferences.Current.HasSeenWelcome)
        {
            var onboarding = new OnboardingViewModel(services.Preferences);

            onboarding.Finished += () =>
            {
                _discover.BrowseMode = services.Preferences.Current.BrowseMode;
                Onboarding = null;
            };

            _onboarding = onboarding;
        }
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    /// <summary>
    /// The first-run welcome, or null once it is done. While it is here the shell shows
    /// nothing else at all - no sidebar, no status bar - because a welcome competing with
    /// the interface it is introducing helps nobody.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOnboarding))]
    [NotifyPropertyChangedFor(nameof(ShowShell))]
    private OnboardingViewModel? _onboarding;

    public bool ShowOnboarding => Onboarding is not null;
    public bool ShowShell => Onboarding is null;

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private NavigationItem _selectedNavigationItem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    /// <summary>
    /// What the way back is called from here: "Back to results", "Back to Installed".
    /// Named after the screen it returns to, because "Back" on its own leaves somebody
    /// guessing where they will land.
    /// </summary>
    [ObservableProperty] private string _backLabel = "";

    // Set from the selected page in the constructor, so the strip never opens showing
    // a placeholder that does not match what is on screen.
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _rateLimitText = "";

    public string PlatformText => PlatformInfo.CurrentDescription;

    /// <summary>
    /// Goes to a top-level destination, whether or not it is already selected.
    /// </summary>
    /// <remarks>
    /// The selection binding alone is not enough. Somebody on a details page reached from
    /// Discover still has Discover selected, so clicking Discover changed nothing and left
    /// them looking at the page they were trying to leave. A sidebar destination has to be
    /// a way out from anywhere, including from inside itself.
    /// </remarks>
    [RelayCommand]
    private void GoToSection(NavigationItem? item)
    {
        if (item is null) return;

        if (ReferenceEquals(SelectedNavigationItem, item))
        {
            // Already the selected section, so the property setter will not fire. Do the
            // work directly.
            EnterSection(item);
            return;
        }

        SelectedNavigationItem = item;
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem value) => EnterSection(value);

    private void EnterSection(NavigationItem value)
    {
        // Choosing a section always leaves any detail page behind.
        CancelActiveDetailsLoad();

        // The library may have changed while the user was elsewhere in the application.
        if (ReferenceEquals(value.Page, _installed)) _installed.Refresh();
        if (ReferenceEquals(value.Page, _downloads)) _downloads.Refresh();
        if (ReferenceEquals(value.Page, _favorites)) _favorites.Refresh();

        // A top-level destination is a fresh start, not a step deeper. Discover in
        // particular returns to its own root - the home page - rather than to whatever
        // was last on screen inside it.
        _history.Clear();

        if (ReferenceEquals(value.Page, _discover)) _discover.ReturnHome();

        CurrentPage = value.Page;
        SyncBackState();
        StatusText = value.Title;
    }

    private void SyncBackState()
    {
        CanGoBack = _history.CanGoBack;
        BackLabel = _history.BackLabel;
    }

    /// <summary>
    /// Returns to the previous screen, restoring it rather than rebuilding it. A search
    /// is not run again on the way back: the results are already there, the allowance
    /// has already been spent on them once, and repeating the request would be slower
    /// and would sometimes fail.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        var previous = _history.Pop();
        if (previous is null) return;

        CancelActiveDetailsLoad();

        CurrentPage = (ViewModelBase)previous.Page;
        StatusText = previous.Title;
        SyncBackState();
    }

    private void ShowRepositoryDetails(GitHubRepository repository) =>
        ShowRepositoryDetails(repository, offerInstall: false);

    /// <summary>
    /// Opens the details page. With <paramref name="offerInstall"/> the page shows the
    /// installation plan and its confirmation step as soon as the analysis produces one -
    /// the same gate as always, reached in one step instead of three.
    /// </summary>
    private void ShowRepositoryDetails(GitHubRepository repository, bool offerInstall)
    {
        // Abandoning a half-loaded details page must stop its work, not leave four
        // GitHub requests running against a page nobody is looking at.
        CancelActiveDetailsLoad();

        var details = new RepositoryDetailsViewModel(
            repository,
            _services.GitHub,
            _services.Explanations,
            _services.Analyzer,
            _services.InstallPlanner,
            _services.Installer,
            _services.InstalledApps,
            _services.Launcher,
            _services.Media,
            _services.Machine,
            _services.Log,
            _services.Images,
            _services.Favorites);

        details.OfferInstallWhenReady = offerInstall;

        // Recorded on the way out, while the screen being left is still in the state
        // that makes the label true.
        _history.Push(new NavigationEntry(CurrentPage, StatusText, DescribeWayBack()));

        _services.RecentlyViewed.Record(repository);

        _activeDetails = details;
        CurrentPage = details;
        StatusText = repository.FullName;
        SyncBackState();

        // Fire and forget: the page shows its own loading and error states.
        _ = details.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Names the screen being left behind. Discover is two places depending on what is
    /// on it: a page of results somebody wants to get back to, or the home page they
    /// started from.
    /// </summary>
    private string DescribeWayBack()
    {
        if (ReferenceEquals(CurrentPage, _discover))
        {
            return _discover.HasSearched ? "Back to results" : "Back to Discover";
        }

        if (ReferenceEquals(CurrentPage, _installed)) return "Back to Installed";
        if (ReferenceEquals(CurrentPage, _favorites)) return "Back to Favorites";
        if (ReferenceEquals(CurrentPage, _downloads)) return "Back to Downloads";

        return "Back";
    }

    private void CancelActiveDetailsLoad()
    {
        if (_activeDetails is null) return;

        if (_activeDetails.LoadCancelCommand.CanExecute(null))
        {
            _activeDetails.LoadCancelCommand.Execute(null);
        }

        _activeDetails = null;
    }

    /// <summary>
    /// Raised from whichever thread finished the HTTP request, so it is marshalled
    /// onto the UI thread before touching a bound property.
    /// </summary>
    private void UpdateRateLimit(RateLimitStatus status) => _dispatcher.Post(() => ApplyRateLimit(status));

}
