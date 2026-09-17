using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>What the main button on the details page is currently offering.</summary>
public enum InstallState
{
    /// <summary>Nothing to offer: no usable plan.</summary>
    Unavailable,

    NotInstalled,

    /// <summary>The plan is on screen and RepoDeck is waiting for a yes.</summary>
    AwaitingConfirmation,

    Installing,
    Installed,

    /// <summary>Fetched, but no runnable application was established from it.</summary>
    DownloadedOnly,

    /// <summary>Files installed, but which one to run is unresolved.</summary>
    NeedsExecutableChoice,

    /// <summary>Registered, but the program it points at is no longer there.</summary>
    Broken
}

/// <summary>
/// The install and run controls.
/// </summary>
/// <remarks>
/// Nothing is fetched until the user has seen the plan and said yes. The confirmation
/// step is not a formality: it is the point at which RepoDeck stops being a browser and
/// starts writing to the machine.
/// </remarks>
public sealed partial class RepositoryDetailsViewModel
{
    private InstallPlan? _plan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallButton))]
    [NotifyPropertyChangedFor(nameof(ShowConfirmation))]
    [NotifyPropertyChangedFor(nameof(ShowRunButton))]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    [NotifyPropertyChangedFor(nameof(ShowOpenFolder))]
    [NotifyPropertyChangedFor(nameof(ShowUninstallButton))]
    private InstallState _installState = InstallState.Unavailable;

    [ObservableProperty] private string _installProgressText = "";
    [ObservableProperty] private double _installProgressFraction;
    [ObservableProperty] private bool _installProgressIsIndeterminate = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallMessage))]
    private string? _installMessage;

    [ObservableProperty] private string _installedVersionText = "";

    public bool HasInstallMessage => !string.IsNullOrEmpty(InstallMessage);

    public bool ShowInstallButton => InstallState == InstallState.NotInstalled;
    public bool ShowConfirmation => InstallState == InstallState.AwaitingConfirmation;
    public bool ShowProgress => InstallState == InstallState.Installing;
    public bool ShowRunButton => InstallState is InstallState.Installed or InstallState.Broken;
    public bool ShowOpenFolder => InstallState is
        InstallState.Installed or InstallState.DownloadedOnly
        or InstallState.Broken or InstallState.NeedsExecutableChoice;

    public string RunButtonText => InstallState == InstallState.Broken ? "Repair" : "Run";

    /// <summary>Records the plan and works out what the button should offer.</summary>
    private void ApplyInstallState(InstallPlan plan)
    {
        _plan = plan;

        var existing = _installedApps.Find(plan.Owner, plan.Name);

        if (existing is not null)
        {
            InstalledVersionText = $"Installed: {existing.DisplayVersion}";

            InstallState = existing.IsDownloadOnly
                ? InstallState.DownloadedOnly
                : existing.NeedsExecutableChoice
                    ? InstallState.NeedsExecutableChoice
                    : _launcher.IsIntact(existing)
                        ? InstallState.Installed
                        : InstallState.Broken;

            InstallMessage = InstallState switch
            {
                InstallState.DownloadedOnly =>
                    existing.NotInstalledReason
                    ?? "RepoDeck downloaded this but did not establish a runnable application.",
                InstallState.NeedsExecutableChoice =>
                    "RepoDeck found multiple possible application executables. "
                    + "Choose which one to run on the Installed page.",
                InstallState.Broken =>
                    "The installed program is no longer where RepoDeck left it. Installing again will replace it.",
                _ => null
            };

            return;
        }

        InstalledVersionText = "";
        InstallMessage = null;
        InstallState = plan.CanProceed ? InstallState.NotInstalled : InstallState.Unavailable;

        // Arriving here from Quick Look's INSTALL means the user has already said what
        // they want, so the plan is put in front of them rather than a button that shows
        // it. The confirmation step itself is not skipped: nothing is downloaded until
        // they confirm what the plan says.
        if (OfferInstallWhenReady && InstallState == InstallState.NotInstalled)
        {
            BeginInstall();
        }
    }

    /// <summary>
    /// Set by the shell when the page was opened by someone who had already asked to
    /// install. It advances to the confirmation step; it never bypasses it.
    /// </summary>
    public bool OfferInstallWhenReady { get; set; }

    /// <summary>Shows the plan and waits. Nothing is downloaded by this.</summary>
    [RelayCommand]
    private void BeginInstall()
    {
        if (_plan?.CanProceed != true) return;

        InstallMessage = null;
        InstallState = InstallState.AwaitingConfirmation;
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        if (InstallState != InstallState.AwaitingConfirmation) return;
        InstallState = InstallState.NotInstalled;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ConfirmInstallAsync(CancellationToken cancellationToken)
    {
        var plan = _plan;
        if (plan?.CanProceed != true) return;

        InstallState = InstallState.Installing;
        InstallMessage = null;
        InstallProgressText = "Preparing...";
        InstallProgressIsIndeterminate = true;
        InstallProgressFraction = 0;

        var progress = new Progress<InstallationProgress>(ReportInstallProgress);

        var result = await _installer.InstallAsync(plan, progress, cancellationToken)
            .ConfigureAwait(true);

        if (result.WasCancelled)
        {
            InstallState = InstallState.NotInstalled;
            InstallMessage = "Installation cancelled. Nothing was installed.";
            return;
        }

        if (!result.Succeeded)
        {
            InstallState = InstallState.NotInstalled;
            InstallMessage = result.ErrorMessage ?? "The installation did not succeed.";
            return;
        }

        var manifest = result.Manifest!;
        InstalledVersionText = $"Installed: {manifest.DisplayVersion}";

        if (result.DownloadedOnly)
        {
            // Either a system installer RepoDeck will not run, or an archive with nothing
            // runnable in it. Both are downloads, neither is an installation.
            InstallState = InstallState.DownloadedOnly;
            InstallMessage = manifest.NotInstalledReason ?? result.ExecutableNote
                ?? "RepoDeck downloaded this but could not establish a runnable application.";
            return;
        }

        if (manifest.NeedsExecutableChoice)
        {
            // Installed, but RepoDeck will not guess which file is the program.
            InstallState = InstallState.NeedsExecutableChoice;
            InstallMessage = "RepoDeck found multiple possible application executables. "
                             + "Choose which one to run on the Installed page.";
            return;
        }

        InstallState = manifest.IsRunnableInstallation ? InstallState.Installed : InstallState.Broken;
        InstallMessage = null;
    }

    private void ReportInstallProgress(InstallationProgress progress)
    {
        InstallProgressText = progress.Describe();

        if (progress.Download?.Fraction is { } fraction)
        {
            InstallProgressIsIndeterminate = false;
            InstallProgressFraction = fraction * 100;
            return;
        }

        InstallProgressIsIndeterminate = true;
    }

    [RelayCommand]
    private void Run()
    {
        var manifest = _installedApps.Find(Repository.OwnerLogin, Repository.Name);
        if (manifest is null) return;

        var result = _launcher.Launch(manifest);

        if (result.Succeeded)
        {
            InstallMessage = null;
            return;
        }

        InstallMessage = result.ErrorMessage;

        // A program that will not start is a broken installation, and reinstalling fixes it.
        if (!_launcher.IsIntact(manifest)) InstallState = InstallState.Broken;
    }

    [RelayCommand]
    private void OpenInstallFolder()
    {
        var manifest = _installedApps.Find(Repository.OwnerLogin, Repository.Name);
        if (manifest is null) return;

        var folder = manifest.IsDownloadOnly && manifest.DownloadedFilePath is { Length: > 0 } file
            ? Path.GetDirectoryName(file)
            : manifest.InstalledPath;

        if (folder is { Length: > 0 }) SystemBrowser.OpenFolder(folder, _log);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUninstallButton))]
    private bool _isConfirmingUninstall;

    /// <summary>Removing an application is never a single click.</summary>
    public bool ShowUninstallButton => ShowOpenFolder && !IsConfirmingUninstall;

    [RelayCommand]
    private void BeginUninstall() => IsConfirmingUninstall = true;

    [RelayCommand]
    private void CancelUninstall() => IsConfirmingUninstall = false;

    [RelayCommand]
    private async Task UninstallAsync()
    {
        IsConfirmingUninstall = false;

        var manifest = _installedApps.Find(Repository.OwnerLogin, Repository.Name);
        if (manifest is null) return;

        var removed = await _installer.UninstallAsync(manifest).ConfigureAwait(true);

        if (!removed)
        {
            InstallMessage = "RepoDeck could not remove this installation. The log file has the details.";
            return;
        }

        InstalledVersionText = "";
        InstallMessage = "Removed.";
        InstallState = _plan?.CanProceed == true ? InstallState.NotInstalled : InstallState.Unavailable;
    }
}
