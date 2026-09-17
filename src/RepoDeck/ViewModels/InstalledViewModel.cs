using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;

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

    public InstalledViewModel(
        IInstalledAppStore store,
        IInstallationService installer,
        LaunchService launcher,
        IAppLog log)
    {
        _store = store;
        _installer = installer;
        _launcher = launcher;
        _log = log;

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
                Run, OpenFolder, ViewRepository, UninstallAsync, ChooseExecutableAsync));
        }

        HasApplications = Applications.Count > 0;
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
