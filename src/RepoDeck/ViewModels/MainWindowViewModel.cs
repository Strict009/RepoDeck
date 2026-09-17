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
    private readonly InstalledViewModel _installed;
    private readonly DownloadsViewModel _downloads;
    private readonly IUiDispatcher _dispatcher;
    private RepositoryDetailsViewModel? _activeDetails;

    public MainWindowViewModel(AppServices services, IUiDispatcher? dispatcher = null)
    {
        _services = services;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();

        _discover = new DiscoverViewModel(services.GitHub, services.Explanations, services.Log);
        _discover.RepositoryOpenRequested += ShowRepositoryDetails;

        _installed = new InstalledViewModel(
            services.InstalledApps, services.Installer, services.Launcher, services.Log);

        _downloads = new DownloadsViewModel(
            services.InstalledApps, services.Installer, services.Log);

        NavigationItems =
        [
            new NavigationItem("Discover", NavigationIcons.Discover, _discover),
            new NavigationItem("Installed", NavigationIcons.Installed, _installed),
            new NavigationItem("Downloads", NavigationIcons.Downloads, _downloads),
            new NavigationItem("Favorites", NavigationIcons.Favorites, new PlaceholderViewModel(
                "Favorites",
                "Repositories you save for later will appear here, whether or not you install them.",
                "Planned for Milestone 2")),
            new NavigationItem("Settings", NavigationIcons.Settings, new SettingsViewModel(services, _dispatcher))
        ];

        _selectedNavigationItem = NavigationItems[0];
        _currentPage = _discover;

        UpdateRateLimit(services.GitHub.RateLimit);
        services.GitHub.RateLimitChanged += UpdateRateLimit;
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private NavigationItem _selectedNavigationItem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _rateLimitText = "";

    public string PlatformText => PlatformInfo.CurrentDescription;

    partial void OnSelectedNavigationItemChanged(NavigationItem value)
    {
        // Choosing a section always leaves any detail page behind.
        CancelActiveDetailsLoad();

        // The library may have changed while the user was elsewhere in the application.
        if (ReferenceEquals(value.Page, _installed)) _installed.Refresh();
        if (ReferenceEquals(value.Page, _downloads)) _downloads.Refresh();

        CurrentPage = value.Page;
        CanGoBack = false;
        StatusText = value.Title;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        CancelActiveDetailsLoad();
        CurrentPage = SelectedNavigationItem.Page;
        CanGoBack = false;
        StatusText = SelectedNavigationItem.Title;
    }

    private void ShowRepositoryDetails(GitHubRepository repository)
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
            _services.Machine,
            _services.Log);

        _activeDetails = details;
        CurrentPage = details;
        CanGoBack = true;
        StatusText = repository.FullName;

        // Fire and forget: the page shows its own loading and error states.
        _ = details.LoadCommand.ExecuteAsync(null);
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

    private void ApplyRateLimit(RateLimitStatus status)
    {
        if (!status.IsKnown)
        {
            RateLimitText = "";
            return;
        }

        var suffix = status.IsAuthenticated ? "" : " (no token)";
        RateLimitText = $"GitHub requests left: {status.Remaining}/{status.Limit}{suffix}";
    }
}
