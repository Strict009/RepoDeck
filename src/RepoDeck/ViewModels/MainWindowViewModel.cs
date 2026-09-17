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

    public MainWindowViewModel(AppServices services)
    {
        _services = services;

        _discover = new DiscoverViewModel(services.GitHub, services.Explanations, services.Log);
        _discover.RepositoryOpenRequested += ShowRepositoryDetails;

        NavigationItems =
        [
            new NavigationItem("Discover", NavigationIcons.Discover, _discover),
            new NavigationItem("Installed", NavigationIcons.Installed, new PlaceholderViewModel(
                "Installed applications",
                "Applications you install through RepoDeck will be listed here, with buttons to run, "
                + "update or remove them.",
                "Planned for Milestone 3")),
            new NavigationItem("Downloads", NavigationIcons.Downloads, new PlaceholderViewModel(
                "Downloads",
                "Downloads in progress will appear here, with progress, cancellation and a record of "
                + "what has been fetched.",
                "Planned for Milestone 3")),
            new NavigationItem("Favorites", NavigationIcons.Favorites, new PlaceholderViewModel(
                "Favorites",
                "Repositories you save for later will appear here, whether or not you install them.",
                "Planned for Milestone 2")),
            new NavigationItem("Settings", NavigationIcons.Settings, new SettingsViewModel(services))
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
        CurrentPage = value.Page;
        CanGoBack = false;
        StatusText = value.Title;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        CurrentPage = SelectedNavigationItem.Page;
        CanGoBack = false;
        StatusText = SelectedNavigationItem.Title;
    }

    private void ShowRepositoryDetails(GitHubRepository repository)
    {
        var details = new RepositoryDetailsViewModel(
            repository, _services.GitHub, _services.Explanations, _services.Log);

        CurrentPage = details;
        CanGoBack = true;
        StatusText = repository.FullName;

        // Fire and forget: the page shows its own loading and error states.
        _ = details.LoadCommand.ExecuteAsync(null);
    }

    private void UpdateRateLimit(RateLimitStatus status)
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
