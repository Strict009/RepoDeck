using System.Collections.ObjectModel;
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
    private readonly Func<InstalledAppViewModel, string, Task> _chooseExecutable;

    public InstalledAppViewModel(
        ApplicationManifest manifest,
        bool isIntact,
        Action<InstalledAppViewModel> run,
        Action<InstalledAppViewModel> openFolder,
        Action<InstalledAppViewModel> viewRepository,
        Func<InstalledAppViewModel, Task> uninstall,
        Func<InstalledAppViewModel, string, Task> chooseExecutable)
    {
        Manifest = manifest;
        _isIntact = isIntact;
        _run = run;
        _openFolder = openFolder;
        _viewRepository = viewRepository;
        _uninstall = uninstall;
        _chooseExecutable = chooseExecutable;

        foreach (var candidate in manifest.AlternativeExecutables) ExecutableCandidates.Add(candidate);
        _selectedCandidate = ExecutableCandidates.FirstOrDefault();
    }

    public ApplicationManifest Manifest { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isIntact;

    // ---- Card facts -------------------------------------------------------
    public string Name => Manifest.Name;
    public string Owner => Manifest.Owner;
    public string VersionText => Manifest.DisplayVersion;
    public string InstalledText => "Installed " + Humanize.RelativeTime(Manifest.InstalledAt);
    public string SizeText => Humanize.FileSize(Manifest.AssetSize);

    /// <summary>e.g. "Windows x64". Omitted entirely when RepoDeck does not know.</summary>
    public string PlatformText => Manifest.Platform == OsPlatform.Unknown
        ? ""
        : Manifest.Platform.DisplayName()
          + (Manifest.Architecture == CpuArchitecture.Unknown
              ? ""
              : " " + Manifest.Architecture.DisplayName());

    public bool HasPlatformText => PlatformText.Length > 0;

    public string LastRunText => Manifest.LastRunAt is null
        ? "Never run"
        : "Last run " + Humanize.RelativeTime(Manifest.LastRunAt);

    // ---- State ------------------------------------------------------------

    /// <summary>Run is offered only for an installation RepoDeck established as runnable.</summary>
    public bool CanRun => IsIntact && Manifest.IsRunnableInstallation;

    public bool NeedsExecutableChoice => Manifest.NeedsExecutableChoice;

    public string StatusText => Manifest.NeedsExecutableChoice
        ? "Choose which program to run"
        : !IsIntact
            ? "Program missing"
            : "Ready";

    public bool NeedsAttention => !IsIntact || Manifest.NeedsExecutableChoice;

    /// <summary>
    /// Update checking is a later milestone. The row says so rather than leaving a button
    /// that appears to do something and does not.
    /// </summary>
    public string UpdateStatusText => "Update checking arrives in the next milestone";

    // ---- Resolving an ambiguous executable --------------------------------

    public ObservableCollection<string> ExecutableCandidates { get; } = [];

    [ObservableProperty] private string? _selectedCandidate;

    public string AmbiguityMessage => "RepoDeck found multiple possible application executables.";

    // ---- Uninstall confirmation -------------------------------------------

    /// <summary>
    /// Removing an application is not a one-click operation: the button asks first.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUninstallButton))]
    private bool _isConfirmingUninstall;

    public bool ShowUninstallButton => !IsConfirmingUninstall;

    public void MarkBroken() => IsIntact = false;

    [RelayCommand]
    private void Run() => _run(this);

    [RelayCommand]
    private void OpenFolder() => _openFolder(this);

    [RelayCommand]
    private void ViewRepository() => _viewRepository(this);

    [RelayCommand]
    private void BeginUninstall() => IsConfirmingUninstall = true;

    [RelayCommand]
    private void CancelUninstall() => IsConfirmingUninstall = false;

    [RelayCommand]
    private Task ConfirmUninstall() => _uninstall(this);

    [RelayCommand]
    private Task ChooseExecutable() =>
        SelectedCandidate is { Length: > 0 } candidate
            ? _chooseExecutable(this, candidate)
            : Task.CompletedTask;
}
