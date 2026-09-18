using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.ViewModels;

/// <summary>
/// Assets RepoDeck fetched but did not turn into a runnable application.
/// </summary>
/// <remarks>
/// Deliberately small: a record of what is on disk and why it is not an installation.
/// It offers no way to run anything. A system installer sitting here is the user's to
/// run, from their own file manager, having decided to.
/// </remarks>
public sealed partial class DownloadsViewModel : ViewModelBase
{
    private readonly IInstalledAppStore _store;
    private readonly IInstallationService _installer;
    private readonly IAppLog _log;

    private readonly ITransferRegistry _transfers;

    public DownloadsViewModel(
        IInstalledAppStore store,
        IInstallationService installer,
        IAppLog log,
        ITransferRegistry? transfers = null)
    {
        _store = store;
        _installer = installer;
        _log = log;
        _transfers = transfers ?? NullTransferRegistry.Instance;

        _transfers.Changed += RefreshTransfers;

        Refresh();
    }

    public ObservableCollection<DownloadedAssetViewModel> Downloads { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    private bool _hasDownloads;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool ShowEmptyState => !HasDownloads && !HasActive && !HasFinished;
    public bool ShowList => HasDownloads;

    // ---- Live transfers ---------------------------------------------------

    /// <summary>
    /// What RepoDeck is fetching now, or recently tried to.
    /// </summary>
    /// <remarks>
    /// Separate from the completed list below because they answer different questions.
    /// This one is "what is happening"; the list below is "what did RepoDeck end up with
    /// that it could not install". Mixing them would make neither legible.
    /// </remarks>
    public ObservableCollection<TransferViewModel> Active { get; } = [];

    public ObservableCollection<TransferViewModel> Finished { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActive))]
    private bool _hasActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFinished))]
    private bool _hasFinished;

    public bool ShowActive => HasActive;
    public bool ShowFinished => HasFinished;

    public bool ShowAnything => HasActive || HasFinished || HasDownloads;

    private void RefreshTransfers()
    {
        Active.Clear();
        Finished.Clear();

        foreach (var record in _transfers.All())
        {
            var item = new TransferViewModel(record, ForgetTransfer);

            if (record.IsActive) Active.Add(item);
            else Finished.Add(item);
        }

        HasActive = Active.Count > 0;
        HasFinished = Finished.Count > 0;

        OnPropertyChanged(nameof(ShowAnything));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void ForgetTransfer(TransferViewModel item)
    {
        _transfers.Forget(item.Id);
        RefreshTransfers();
    }

    /// <summary>Forgets every finished record. Anything still running is left alone.</summary>
    [RelayCommand]
    private void ClearFinished()
    {
        var removed = _transfers.ClearFinished();

        Message = removed switch
        {
            0 => null,
            1 => "Cleared one record.",
            _ => $"Cleared {removed} records."
        };

        RefreshTransfers();
    }

    public void Refresh()
    {
        RefreshTransfers();

        Downloads.Clear();

        var downloaded = _store.GetAll()
            .Where(m => m.State == InstallationState.Downloaded)
            .OrderByDescending(m => m.InstalledAt)
            .ToList();

        foreach (var manifest in downloaded)
        {
            Downloads.Add(new DownloadedAssetViewModel(
                manifest, FileStillPresent(manifest), OpenFolder, ViewRepository, ForgetAsync));
        }

        HasDownloads = Downloads.Count > 0;
    }

    private static bool FileStillPresent(ApplicationManifest manifest) =>
        manifest.DownloadedFilePath is { Length: > 0 } path && File.Exists(path);

    private void OpenFolder(DownloadedAssetViewModel item)
    {
        var folder = item.Manifest.DownloadedFilePath is { Length: > 0 } file
            ? Path.GetDirectoryName(file)
            : item.Manifest.InstalledPath;

        if (folder is { Length: > 0 }) SystemBrowser.OpenFolder(folder, _log);
    }

    private void ViewRepository(DownloadedAssetViewModel item) =>
        SystemBrowser.OpenUrl(item.Manifest.RepositoryUrl, _log);

    private async Task ForgetAsync(DownloadedAssetViewModel item)
    {
        var removed = await _installer.UninstallAsync(item.Manifest).ConfigureAwait(true);

        Message = removed
            ? $"Removed the download for {item.Name}."
            : $"RepoDeck could not remove that download. The log file has the details.";

        Refresh();
    }
}

/// <summary>One fetched asset that is not an installation.</summary>
public sealed partial class DownloadedAssetViewModel : ViewModelBase
{
    private readonly Action<DownloadedAssetViewModel> _openFolder;
    private readonly Action<DownloadedAssetViewModel> _viewRepository;
    private readonly Func<DownloadedAssetViewModel, Task> _forget;

    public DownloadedAssetViewModel(
        ApplicationManifest manifest,
        bool filePresent,
        Action<DownloadedAssetViewModel> openFolder,
        Action<DownloadedAssetViewModel> viewRepository,
        Func<DownloadedAssetViewModel, Task> forget)
    {
        Manifest = manifest;
        FilePresent = filePresent;
        _openFolder = openFolder;
        _viewRepository = viewRepository;
        _forget = forget;
    }

    public ApplicationManifest Manifest { get; }
    public bool FilePresent { get; }

    public string Name => Manifest.Name;
    public string Owner => Manifest.Owner;
    public string ReleaseText => Manifest.DisplayVersion;
    public string AssetText => Manifest.AssetName ?? "unknown file";
    public string SizeText => Humanize.FileSize(Manifest.AssetSize);
    public string DownloadedText => "Downloaded " + Humanize.RelativeTime(Manifest.InstalledAt);

    /// <summary>Why this is not in the Installed library. Always stated, never implied.</summary>
    public string ReasonText => Manifest.NotInstalledReason
                                ?? "RepoDeck could not establish a runnable application from this file.";

    public string StatusText => FilePresent ? "On disk" : "File no longer present";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForgetButton))]
    private bool _isConfirmingForget;

    public bool ShowForgetButton => !IsConfirmingForget;

    [RelayCommand]
    private void OpenFolder() => _openFolder(this);

    [RelayCommand]
    private void ViewRepository() => _viewRepository(this);

    [RelayCommand]
    private void BeginForget() => IsConfirmingForget = true;

    [RelayCommand]
    private void CancelForget() => IsConfirmingForget = false;

    [RelayCommand]
    private Task ConfirmForget() => _forget(this);
}
