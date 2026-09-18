using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.History;
using RepoDeck.Services.Install;
using RepoDeck.Services.Update;
using CommunityToolkit.Mvvm.Input;

namespace RepoDeck.ViewModels;

/// <summary>
/// The library of applications RepoDeck has established as runnable.
/// </summary>
/// <remarks>
/// Only installations appear here. Assets RepoDeck fetched but could not turn into a
/// runnable application live on the Downloads page instead, because a library that mixes
/// the two stops meaning anything.
/// </remarks>
public sealed partial class InstalledViewModel : ViewModelBase
{
    private readonly IInstalledAppStore _store;
    private readonly IInstallationService _installer;
    private readonly LaunchService _launcher;
    private readonly IAppLog _log;

    private readonly IUpdateChecker? _updateChecker;
    private readonly IUpdateService? _updater;
    private readonly IInstallationHealthChecker? _healthChecker;
    private readonly ILifecycleHistory _history;
    private readonly MachineProfile _machine;
    private readonly AppPaths _paths;

    public InstalledViewModel(
        IInstalledAppStore store,
        IInstallationService installer,
        LaunchService launcher,
        IAppLog log,
        IUpdateChecker? updateChecker = null,
        IUpdateService? updater = null,
        IInstallationHealthChecker? healthChecker = null,
        ILifecycleHistory? history = null,
        MachineProfile? machine = null,
        AppPaths? paths = null)
    {
        _store = store;
        _installer = installer;
        _launcher = launcher;
        _log = log;

        _updateChecker = updateChecker;
        _updater = updater;
        _healthChecker = healthChecker;
        _history = history ?? NullLifecycleHistory.Instance;
        _machine = machine ?? PlatformInfo.CurrentMachine();
        _paths = paths ?? new AppPaths();

        Refresh();
    }

    public ObservableCollection<InstalledAppViewModel> Applications { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    private bool _hasApplications;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool ShowEmptyState => !HasApplications;
    public bool ShowList => HasApplications;

    /// <summary>
    /// Enough of a repository for the classifier to draw artwork from. The manifest does
    /// not keep a description or tags, so the kind is worked out from the name alone -
    /// which is less than Discover has, and honest about it.
    /// </summary>
    private static GitHubRepository AsRepository(ApplicationManifest manifest) => new()
    {
        Id = manifest.RepositoryId,
        Name = manifest.Name,
        FullName = manifest.Id,
        HtmlUrl = manifest.RepositoryUrl,
        Owner = new RepositoryOwner { Login = manifest.Owner }
    };

    /// <summary>Rebuilds the list from the store. Called on open and after any change.</summary>
    public void Refresh()
    {
        Applications.Clear();

        var installed = _store.GetAll()
            .Where(m => m.State != InstallationState.Downloaded)
            .OrderByDescending(m => m.LastRunAt ?? m.InstalledAt)
            .ToList();

        foreach (var manifest in installed)
        {
            Applications.Add(new InstalledAppViewModel(
                manifest, _launcher.IsIntact(manifest),
                Run, OpenFolder, ViewRepository, UninstallAsync, ChooseExecutableAsync,
                CheckForUpdatesAsync, UpdateAsync, RepairAsync)
            {
                Kind = ProjectKindClassifier.Classify(AsRepository(manifest)).Kind,
                Health = _healthChecker?.Check(manifest)
            });
        }

        HasApplications = Applications.Count > 0;
    }

    // ---- Update checking --------------------------------------------------

    /// <summary>
    /// Asks GitHub about every installed application, one at a time.
    /// </summary>
    /// <remarks>
    /// Sequential rather than parallel, and deliberately: a library of thirty applications
    /// firing thirty simultaneous requests would exhaust an unauthenticated allowance in
    /// one press and tell the user nothing about most of them. One at a time is slower and
    /// leaves the allowance intact enough to be useful afterwards.
    /// </remarks>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CheckAllForUpdatesAsync(CancellationToken cancellationToken)
    {
        IsCheckingAll = true;
        Message = null;

        var updates = 0;
        var undetermined = 0;

        try
        {
            foreach (var app in Applications.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                await CheckOneAsync(app, cancellationToken).ConfigureAwait(true);

                if (app.HasUpdate) updates++;
                else if (app.IsUpdateUnknown) undetermined++;
            }

            // "Everything is up to date" must not be printed above a row that says
            // RepoDeck could not tell. The summary says only what the rows support.
            Message = (updates, undetermined) switch
            {
                (0, 0) => "Everything is up to date.",
                (0, 1) => "Nothing needs updating, but RepoDeck could not check one of these.",
                (0, _) => $"Nothing needs updating, but RepoDeck could not check {undetermined} of these.",
                (1, 0) => "One application has an update.",
                (_, 0) => $"{updates} applications have updates.",
                (1, 1) => "One application has an update, and RepoDeck could not check another.",
                (1, _) => $"One application has an update, and RepoDeck could not check {undetermined} others.",
                _ => $"{updates} applications have updates, and RepoDeck could not check "
                     + $"{undetermined} others."
            };
        }
        catch (OperationCanceledException)
        {
            Message = "Update check cancelled.";
        }
        finally
        {
            IsCheckingAll = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCheckAllButton))]
    private bool _isCheckingAll;

    public bool ShowCheckAllButton => !IsCheckingAll && HasApplications;

    private Task CheckForUpdatesAsync(InstalledAppViewModel app) =>
        CheckOneAsync(app, CancellationToken.None);

    private async Task CheckOneAsync(InstalledAppViewModel app, CancellationToken cancellationToken)
    {
        if (_updateChecker is null) return;

        app.IsCheckingForUpdates = true;
        app.BlockedMessage = null;

        try
        {
            var check = await _updateChecker.CheckAsync(app.Manifest, cancellationToken)
                .ConfigureAwait(true);

            app.UpdateCheck = check;

            // The plan is built here rather than when Update is pressed, so the button is
            // only ever offered when there is something behind it that can actually run.
            app.UpdatePlan = check.State == UpdateState.UpdateAvailable
                ? UpdatePlanner.Create(app.Manifest, check, _machine, _paths)
                : null;

            _history.Record(LifecycleEvent.UpdateChecked(app.Manifest, check));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error("Installed", $"Could not check {app.Manifest.Id} for updates", ex);
            app.UpdateCheck = UpdateCheck.Undetermined("Something went wrong checking for updates.");
        }
        finally
        {
            app.IsCheckingForUpdates = false;
        }
    }

    // ---- Updating ---------------------------------------------------------

    private async Task UpdateAsync(InstalledAppViewModel app)
    {
        if (_updater is null || app.UpdatePlan is not { CanProceed: true } plan) return;

        app.IsUpdating = true;
        app.BlockedMessage = null;
        app.Activity.Reset();
        Message = null;

        try
        {
            var progress = new Progress<UpdateProgress>(app.Activity.Apply);

            var result = await _updater.UpdateAsync(plan, progress).ConfigureAwait(true);

            if (result.ApplicationWasRunning)
            {
                // Not a failure. The user closes it and presses Retry.
                app.BlockedMessage = result.Message;
                return;
            }

            if (result.Succeeded)
            {
                Message = $"{app.FriendlyName} is now {plan.TargetVersionText}.";
                Refresh();
                return;
            }

            Message = result.Message
                      ?? "The update did not work. Your previous version is still installed.";
        }
        finally
        {
            app.IsUpdating = false;
        }
    }

    // ---- Repair -----------------------------------------------------------

    private async Task RepairAsync(InstalledAppViewModel app)
    {
        if (_updater is null || _healthChecker is null) return;

        var health = _healthChecker.Check(app.Manifest);
        var plan = _healthChecker.PlanRepair(app.Manifest, health);

        if (!plan.CanProceed)
        {
            Message = plan.BlockingIssues.FirstOrDefault() ?? "RepoDeck cannot repair this.";
            return;
        }

        app.IsRepairing = true;
        app.Activity.Reset();
        Message = null;

        try
        {
            var progress = new Progress<UpdateProgress>(app.Activity.Apply);

            var result = await _updater.RepairAsync(plan, progress).ConfigureAwait(true);

            if (result.ApplicationWasRunning)
            {
                app.BlockedMessage = result.Message;
                return;
            }

            Message = result.Succeeded
                ? $"{app.FriendlyName} has been put back."
                : result.Message ?? "The repair did not work.";

            if (result.Succeeded) Refresh();
        }
        finally
        {
            app.IsRepairing = false;
        }
    }

    private void Run(InstalledAppViewModel app)
    {
        var result = _launcher.Launch(app.Manifest);

        if (result.Succeeded)
        {
            Message = $"Started {app.Name}.";
            return;
        }

        Message = result.ErrorMessage;
        if (!_launcher.IsIntact(app.Manifest)) app.MarkBroken();
    }

    private void OpenFolder(InstalledAppViewModel app) =>
        SystemBrowser.OpenFolder(app.Manifest.InstalledPath, _log);

    private void ViewRepository(InstalledAppViewModel app) =>
        SystemBrowser.OpenUrl(app.Manifest.RepositoryUrl, _log);

    private async Task UninstallAsync(InstalledAppViewModel app)
    {
        var removed = await _installer.UninstallAsync(app.Manifest).ConfigureAwait(true);

        Message = removed
            ? $"Removed {app.Name}."
            : $"RepoDeck could not remove {app.Name}. The log file has the details.";

        Refresh();
    }

    private Task ChooseExecutableAsync(InstalledAppViewModel app, string candidate)
    {
        var result = _installer.ChooseExecutable(app.Manifest, candidate);

        Message = result.Succeeded
            ? $"{app.Name} will run {Path.GetFileName(candidate)}."
            : result.ErrorMessage;

        Refresh();
        return Task.CompletedTask;
    }
}
