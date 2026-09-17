using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>One row in the Installed library.</summary>
public sealed partial class InstalledAppViewModel : ViewModelBase
{
    private readonly Action<InstalledAppViewModel> _run;
    private readonly Action<InstalledAppViewModel> _openFolder;
    private readonly Action<InstalledAppViewModel> _viewRepository;
    private readonly Func<InstalledAppViewModel, Task> _uninstall;

    public InstalledAppViewModel(
        ApplicationManifest manifest,
        bool isIntact,
        Action<InstalledAppViewModel> run,
        Action<InstalledAppViewModel> openFolder,
        Action<InstalledAppViewModel> viewRepository,
        Func<InstalledAppViewModel, Task> uninstall)
    {
        Manifest = manifest;
        _isIntact = isIntact;
        _run = run;
        _openFolder = openFolder;
        _viewRepository = viewRepository;
        _uninstall = uninstall;
    }

    public ApplicationManifest Manifest { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(RunButtonText))]
    private bool _isIntact;

    public string Name => Manifest.Name;
    public string Owner => Manifest.Owner;
    public string VersionText => Manifest.DisplayVersion;
    public string InstalledText => "Installed " + Humanize.RelativeTime(Manifest.InstalledAt);

    public string LastRunText => Manifest.LastRunAt is null
        ? "Never run"
        : "Last run " + Humanize.RelativeTime(Manifest.LastRunAt);

    public string SizeText => Humanize.FileSize(Manifest.AssetSize);

    /// <summary>A download-only record has no program of its own to start.</summary>
    public bool CanRun => IsIntact && !Manifest.IsDownloadOnly;

    public string RunButtonText => Manifest.IsDownloadOnly ? "Open installer" : "Run";

    public string StatusText => Manifest.IsDownloadOnly
        ? "Downloaded, not installed"
        : IsIntact
            ? "Ready"
            : "Program missing";

    public bool IsDownloadOnly => Manifest.IsDownloadOnly;
    public bool NeedsAttention => !IsIntact || Manifest.IsDownloadOnly;

    /// <summary>
    /// Update checking is a later milestone. The row says so rather than leaving a
    /// button that appears to do something and does not.
    /// </summary>
    public string UpdateStatusText => "Update checking arrives in the next milestone";

    public void MarkBroken() => IsIntact = false;

    [RelayCommand]
    private void Run() => _run(this);

    [RelayCommand]
    private void OpenFolder() => _openFolder(this);

    [RelayCommand]
    private void ViewRepository() => _viewRepository(this);

    [RelayCommand]
    private Task Uninstall() => _uninstall(this);
}
