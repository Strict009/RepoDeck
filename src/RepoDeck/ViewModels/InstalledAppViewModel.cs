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
    private readonly Func<InstalledAppViewModel, Task> _checkForUpdates;
    private readonly Func<InstalledAppViewModel, Task> _update;
    private readonly Func<InstalledAppViewModel, Task> _repair;

    public InstalledAppViewModel(
        ApplicationManifest manifest,
        bool isIntact,
        Action<InstalledAppViewModel> run,
        Action<InstalledAppViewModel> openFolder,
        Action<InstalledAppViewModel> viewRepository,
        Func<InstalledAppViewModel, Task> uninstall,
        Func<InstalledAppViewModel, string, Task> chooseExecutable,
        Func<InstalledAppViewModel, Task> checkForUpdates,
        Func<InstalledAppViewModel, Task> update,
        Func<InstalledAppViewModel, Task> repair)
    {
        _checkForUpdates = checkForUpdates;
        _update = update;
        _repair = repair;
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


    // ---- Resolving an ambiguous executable --------------------------------

    // ---- The library row --------------------------------------------------

    /// <summary>"Obs Studio" rather than "obs-studio".</summary>
    public string FriendlyName => FriendlyNaming.ForRepository(Manifest.Name);

    /// <summary>Drawn artwork for this kind of project when there is no picture.</summary>
    public ProjectKind Kind { get; init; } = ProjectKind.Unknown;

    public string FallbackInitial => FriendlyNaming.Initial(Manifest.Name);
    public Avalonia.Media.IBrush FallbackBrush => FriendlyNaming.ColourFor(Manifest.Owner + "/" + Manifest.Name);

    public string UpdatedText => Manifest.UpdatedAt is null
        ? ""
        : "Updated " + Humanize.RelativeTime(Manifest.UpdatedAt);

    public bool HasBeenUpdated => Manifest.UpdatedAt is not null;

    public string SourceUrl => Manifest.RepositoryUrl;

    // ---- Update state -----------------------------------------------------

    /// <summary>
    /// What RepoDeck last found out about newer releases. Held here rather than in the
    /// manifest because an answer about a remote release goes stale, and a stale answer
    /// stored as fact is worse than no answer.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateLabel))]
    [NotifyPropertyChangedFor(nameof(UpdateExplanation))]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    [NotifyPropertyChangedFor(nameof(IsUpToDate))]
    [NotifyPropertyChangedFor(nameof(IsUpdateUnknown))]
    [NotifyPropertyChangedFor(nameof(IsManualUpdate))]
    [NotifyPropertyChangedFor(nameof(AvailableVersionText))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateDetail))]
    [NotifyPropertyChangedFor(nameof(UpdateSizeText))]
    [NotifyPropertyChangedFor(nameof(HasUpdateSize))]
    [NotifyPropertyChangedFor(nameof(UpdateReleasedText))]
    [NotifyPropertyChangedFor(nameof(ReleaseNotes))]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    private UpdateCheck _updateCheck = UpdateCheck.NotChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowCheckButton))]
    private bool _isCheckingForUpdates;

    /// <summary>The plan, once one has been built. Null until then.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateSizeText))]
    [NotifyPropertyChangedFor(nameof(HasUpdateSize))]
    private UpdatePlan? _updatePlan;

    public string UpdateLabel => UpdateCheck.Label;
    public string UpdateExplanation => UpdateCheck.Explanation;

    public bool HasUpdate => UpdateCheck.HasUpdate;
    public bool IsUpToDate => UpdateCheck.State == UpdateState.UpToDate;
    public bool IsUpdateUnknown => UpdateCheck.State == UpdateState.Unknown;
    public bool IsManualUpdate => UpdateCheck.State == UpdateState.ManualUpdateRequired;

    public bool ShowCheckButton => !IsCheckingForUpdates;

    /// <summary>Only a plan that can proceed ever permits an Update button.</summary>
    public bool CanUpdate =>
        !IsCheckingForUpdates
        && UpdateCheck.State == UpdateState.UpdateAvailable
        && UpdatePlan?.CanProceed == true;

    public string AvailableVersionText => UpdateCheck.AvailableVersionText;

    public bool ShowUpdateDetail => HasUpdate;

    /// <summary>
    /// How big the download is. The plan is the authority once one exists, but the size
    /// is known from the check itself, and the panel that asks somebody to agree to a
    /// download is shown before any plan is built - so it falls back to the check rather
    /// than showing them a blank where the number should be.
    /// </summary>
    public string UpdateSizeText =>
        UpdatePlan?.AssetSize > 0 ? Humanize.FileSize(UpdatePlan.AssetSize)
        : UpdateCheck.AssetSize > 0 ? Humanize.FileSize(UpdateCheck.AssetSize)
        : "";

    public bool HasUpdateSize => UpdateSizeText.Length > 0;

    public string UpdateReleasedText => UpdateCheck.LatestRelease is { } release
        ? "Released " + Humanize.RelativeTime(release.PublishedAt ?? release.CreatedAt)
        : "";

    /// <summary>
    /// Release notes as GitHub returned them.
    /// </summary>
    /// <remarks>
    /// Untrusted remote text. It is shown as plain text in a read-only block and never
    /// rendered as markup, so nothing in it can become a link, an image or anything else
    /// active. It is also truncated, because a project that pastes its whole changelog
    /// into every release should not be able to push the Update button off the screen.
    /// </remarks>
    public string ReleaseNotes
    {
        get
        {
            var body = UpdateCheck.LatestRelease?.Body;
            if (string.IsNullOrWhiteSpace(body)) return "";

            var text = body.Trim();
            return text.Length <= 1200 ? text : text[..1200] + "...";
        }
    }

    public bool HasReleaseNotes => ReleaseNotes.Length > 0;

    // ---- Health -----------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDamaged))]
    [NotifyPropertyChangedFor(nameof(HealthSummary))]
    [NotifyPropertyChangedFor(nameof(HealthExplanations))]
    [NotifyPropertyChangedFor(nameof(HasSeveralProblems))]
    [NotifyPropertyChangedFor(nameof(CanRepair))]
    private InstallationHealth? _health;

    public bool IsDamaged => Health is { IsHealthy: false };
    public bool CanRepair => Health is { IsRepairable: true };
    public string HealthSummary => Health?.Summary ?? "";

    /// <summary>The evidence, for the Why? disclosure.</summary>
    public IReadOnlyList<string> HealthExplanations => Health?.Explanations ?? [];

    /// <summary>
    /// Whether to list the problems individually. With one problem the summary already is
    /// that problem, and repeating it would just be the same sentence twice.
    /// </summary>
    public bool HasSeveralProblems => HealthExplanations.Count > 1;

    // ---- Doing things -----------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isUpdating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isRepairing;

    public bool IsBusy => IsUpdating || IsRepairing;

    /// <summary>The four-meter display, shared with installation.</summary>
    public InstallActivityViewModel Activity { get; } = InstallActivityViewModel.ForUpdate();

    /// <summary>
    /// Set when an update was refused because the program is running. Offers Retry rather
    /// than leaving the user to work out what to do.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloseAndRetry))]
    private string? _blockedMessage;

    public bool ShowCloseAndRetry => BlockedMessage is { Length: > 0 };

    /// <summary>Whether the confirmation panel is showing.</summary>
    [ObservableProperty] private bool _isConfirmingUpdate;

    [ObservableProperty] private bool _isConfirmingRepair;

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

    // ---- Lifecycle actions ------------------------------------------------

    [RelayCommand]
    private Task CheckForUpdates() => _checkForUpdates(this);

    [RelayCommand]
    private void BeginUpdate()
    {
        if (!CanUpdate) return;

        BlockedMessage = null;
        IsConfirmingUpdate = true;
    }

    [RelayCommand]
    private void CancelUpdate() => IsConfirmingUpdate = false;

    [RelayCommand]
    private Task ConfirmUpdate()
    {
        IsConfirmingUpdate = false;
        return _update(this);
    }

    /// <summary>
    /// The user has closed the application and wants to try again. The check runs first,
    /// because whatever was true before the wait may not be now.
    /// </summary>
    [RelayCommand]
    private Task RetryUpdate()
    {
        BlockedMessage = null;
        return _update(this);
    }

    [RelayCommand]
    private void DismissBlockedMessage() => BlockedMessage = null;

    [RelayCommand]
    private void BeginRepair() => IsConfirmingRepair = true;

    [RelayCommand]
    private void CancelRepair() => IsConfirmingRepair = false;

    [RelayCommand]
    private Task ConfirmRepair()
    {
        IsConfirmingRepair = false;
        return _repair(this);
    }

    [RelayCommand]
    private Task ChooseExecutable() =>
        SelectedCandidate is { Length: > 0 } candidate
            ? _chooseExecutable(this, candidate)
            : Task.CompletedTask;
}
