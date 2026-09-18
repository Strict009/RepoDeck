using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Favorites;
using RepoDeck.Services.Install;

namespace RepoDeck.ViewModels;

/// <summary>Projects the user asked RepoDeck to remember.</summary>
/// <remarks>
/// Entirely separate from Installed. A favourite may be installed, uninstalled, or never
/// installed at all - the page says which, because "you already have this" is the most
/// useful thing it can tell somebody looking at a list of things they liked.
/// </remarks>
public sealed partial class FavoritesViewModel : ViewModelBase
{
    private readonly IFavoritesStore _favorites;
    private readonly IInstalledAppStore _installed;
    private readonly IAppLog _log;
    private readonly Action<GitHubRepository>? _openDetails;

    public FavoritesViewModel(
        IFavoritesStore favorites,
        IInstalledAppStore installed,
        IAppLog log,
        Action<GitHubRepository>? openDetails = null)
    {
        _favorites = favorites;
        _installed = installed;
        _log = log;
        _openDetails = openDetails;

        _favorites.Changed += Refresh;
        Refresh();
    }

    public ObservableCollection<FavoriteViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    private bool _hasFavorites;

    public bool ShowEmptyState => !HasFavorites;
    public bool ShowList => HasFavorites;

    public void Refresh()
    {
        Items.Clear();

        foreach (var entry in _favorites.All())
        {
            Items.Add(new FavoriteViewModel(
                entry,
                _installed.IsInstalled(entry.Owner, entry.Name),
                Open,
                Forget,
                _log));
        }

        HasFavorites = Items.Count > 0;
    }

    private void Open(FavoriteViewModel favorite) => _openDetails?.Invoke(favorite.AsRepository());

    private void Forget(FavoriteViewModel favorite)
    {
        _favorites.Remove(favorite.Owner, favorite.Name);
        Refresh();
    }
}

/// <summary>One remembered project.</summary>
public sealed partial class FavoriteViewModel : ViewModelBase
{
    private readonly Action<FavoriteViewModel> _open;
    private readonly Action<FavoriteViewModel> _forget;
    private readonly IAppLog _log;

    public FavoriteViewModel(
        FavoriteEntry entry,
        bool isInstalled,
        Action<FavoriteViewModel> open,
        Action<FavoriteViewModel> forget,
        IAppLog log)
    {
        Entry = entry;
        IsInstalled = isInstalled;
        _open = open;
        _forget = forget;
        _log = log;

        Kind = ProjectKindClassifier.Classify(AsRepository()).Kind;
    }

    public FavoriteEntry Entry { get; }

    public string Owner => Entry.Owner;
    public string Name => Entry.Name;
    public string FriendlyName => FriendlyNaming.ForRepository(Entry.Name);

    public string Description => string.IsNullOrWhiteSpace(Entry.Description)
        ? "No description was provided for this project."
        : Entry.Description!;

    public string MetadataLine => Entry.Language is { Length: > 0 } language
        ? Entry.Owner + "  /  " + language
        : Entry.Owner;

    public string AddedText => "Saved " + Humanize.RelativeTime(Entry.AddedAt);

    public ProjectKind Kind { get; }
    public string FallbackInitial => FriendlyNaming.Initial(Entry.Name);
    public Avalonia.Media.IBrush FallbackBrush => FriendlyNaming.ColourFor(Entry.Id);

    /// <summary>
    /// Whether this is currently installed. Shown because it is the most useful thing to
    /// know about something on a list of things you liked - and it can change either way
    /// without affecting the bookmark.
    /// </summary>
    public bool IsInstalled { get; }

    public string InstalledText => IsInstalled ? "Installed" : "Not installed";

    [RelayCommand]
    private void Open() => _open(this);

    [RelayCommand]
    private void Forget() => _forget(this);

    [RelayCommand]
    private void OpenOnGitHub() => SystemBrowser.OpenUrl(Entry.RepositoryUrl, _log);

    /// <summary>
    /// Enough of a repository to open the details page, which will fetch the rest. The
    /// bookmark deliberately does not try to be a cached copy of the project.
    /// </summary>
    public GitHubRepository AsRepository() => new()
    {
        Id = Entry.RepositoryId,
        Name = Entry.Name,
        FullName = Entry.Id,
        Description = Entry.Description,
        Language = Entry.Language,
        HtmlUrl = Entry.RepositoryUrl,
        Owner = new RepositoryOwner { Login = Entry.Owner }
    };
}
