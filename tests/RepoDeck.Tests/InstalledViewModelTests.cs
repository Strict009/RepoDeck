using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

public sealed class InstalledViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDeckLibraryTests", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;

    public InstalledViewModelTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new InstalledAppStore(_paths, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Ignore locked temp files.
        }
    }

    private InstalledViewModel NewViewModel(IInstallationService? installer = null) =>
        new(_store,
            installer ?? new NoOpInstallationService(),
            new LaunchService(_paths, _store, NullAppLog.Instance),
            NullAppLog.Instance);

    private InstallationService RealInstaller() =>
        new(new FakeDownloadService(Path.Combine(_root, "unused"), _paths.Downloads),
            new ExtractionService(NullAppLog.Instance), _store, _paths, NullAppLog.Instance);

    private ApplicationManifest Install(
        string name,
        bool createExecutable = true,
        InstallationState state = InstallationState.Installed,
        IReadOnlyList<string>? candidates = null)
    {
        var directory = Path.Combine(_paths.Apps, "someone__" + name);
        Directory.CreateDirectory(directory);

        if (createExecutable) File.WriteAllText(Path.Combine(directory, name + ".exe"), "program");

        var manifest = new ApplicationManifest
        {
            Owner = "someone",
            Name = name,
            RepositoryUrl = $"https://github.com/someone/{name}",
            InstalledPath = directory,
            ExecutableRelativePath = state == InstallationState.Installed ? name + ".exe" : null,
            AlternativeExecutables = candidates ?? [],
            ExecutableIsAmbiguous = state == InstallationState.AwaitingExecutableChoice,
            State = state,
            ReleaseTag = "v1.0.0",
            AssetSize = 1024,
            Platform = OsPlatform.Windows,
            Architecture = CpuArchitecture.X64,
            InstalledAt = DateTimeOffset.UtcNow
        };

        _store.Save(manifest);
        return manifest;
    }

    [Fact]
    public void An_empty_library_shows_the_empty_state()
    {
        var vm = NewViewModel();

        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.ShowList);
        Assert.Empty(vm.Applications);
    }

    [Fact]
    public void Installed_applications_are_listed()
    {
        Install("tool");
        Install("other");

        var vm = NewViewModel();

        Assert.True(vm.ShowList);
        Assert.Equal(2, vm.Applications.Count);
    }

    [Fact]
    public void A_card_shows_the_facts_a_person_needs()
    {
        Install("tool");

        var app = Assert.Single(NewViewModel().Applications);

        Assert.Equal("tool", app.Name);
        Assert.Equal("someone", app.Owner);
        Assert.Equal("v1.0.0", app.VersionText);
        Assert.Contains("Installed", app.InstalledText);
        Assert.Equal("Windows x64", app.PlatformText);
        Assert.True(app.HasPlatformText);
        Assert.Equal("Never run", app.LastRunText);
    }

    [Fact]
    public void An_intact_application_can_be_run()
    {
        Install("tool");

        var app = Assert.Single(NewViewModel().Applications);

        Assert.True(app.IsIntact);
        Assert.True(app.CanRun);
        Assert.Equal("Ready", app.StatusText);
        Assert.False(app.NeedsAttention);
    }

    [Fact]
    public void An_application_whose_program_has_gone_is_shown_as_broken()
    {
        Install("tool", createExecutable: false);

        var app = Assert.Single(NewViewModel().Applications);

        Assert.False(app.IsIntact);
        Assert.False(app.CanRun);
        Assert.Equal("Program missing", app.StatusText);
        Assert.True(app.NeedsAttention);
    }

    [Fact]
    public void A_downloaded_only_record_does_not_appear_in_the_library()
    {
        // The Installed library means "RepoDeck established a runnable application".
        Install("installer-app", createExecutable: false, state: InstallationState.Downloaded);

        var vm = NewViewModel();

        Assert.Empty(vm.Applications);
        Assert.True(vm.ShowEmptyState);
    }

    [Fact]
    public void An_unresolved_installation_is_listed_but_cannot_be_run()
    {
        Install("tool", createExecutable: true,
            state: InstallationState.AwaitingExecutableChoice,
            candidates: ["tool.exe", "tool-updater.exe"]);

        var app = Assert.Single(NewViewModel().Applications);

        Assert.True(app.NeedsExecutableChoice);
        Assert.False(app.CanRun);
        Assert.Equal("Choose which program to run", app.StatusText);
        Assert.Equal(2, app.ExecutableCandidates.Count);
        Assert.Contains("multiple possible application executables", app.AmbiguityMessage);
    }

    [Fact]
    public async Task Choosing_a_candidate_makes_the_row_runnable()
    {
        Install("tool", createExecutable: true,
            state: InstallationState.AwaitingExecutableChoice,
            candidates: ["tool.exe"]);

        var vm = NewViewModel(RealInstaller());
        var app = Assert.Single(vm.Applications);

        app.SelectedCandidate = "tool.exe";
        await app.ChooseExecutableCommand.ExecuteAsync(null);

        var updated = Assert.Single(vm.Applications);
        Assert.True(updated.CanRun);
        Assert.False(updated.NeedsExecutableChoice);
        Assert.Equal("Ready", updated.StatusText);
    }

    [Fact]
    public void Uninstall_asks_before_it_removes_anything()
    {
        Install("tool");

        var app = Assert.Single(NewViewModel().Applications);

        Assert.True(app.ShowUninstallButton);
        Assert.False(app.IsConfirmingUninstall);

        app.BeginUninstallCommand.Execute(null);

        // Asking is all that has happened: the application is still there.
        Assert.True(app.IsConfirmingUninstall);
        Assert.False(app.ShowUninstallButton);
        Assert.True(_store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public void Declining_the_confirmation_keeps_the_application()
    {
        Install("tool");

        var app = Assert.Single(NewViewModel().Applications);
        app.BeginUninstallCommand.Execute(null);
        app.CancelUninstallCommand.Execute(null);

        Assert.False(app.IsConfirmingUninstall);
        Assert.True(_store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public async Task Confirming_the_uninstall_removes_the_row()
    {
        Install("tool");

        var vm = NewViewModel(RealInstaller());
        var app = Assert.Single(vm.Applications);

        app.BeginUninstallCommand.Execute(null);
        await app.ConfirmUninstallCommand.ExecuteAsync(null);

        Assert.Empty(vm.Applications);
        Assert.True(vm.ShowEmptyState);
        Assert.Contains("Removed", vm.Message);
        Assert.False(_store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public void Refreshing_picks_up_an_application_installed_elsewhere()
    {
        var vm = NewViewModel();
        Assert.Empty(vm.Applications);

        Install("tool");
        vm.Refresh();

        Assert.Single(vm.Applications);
    }

    [Fact]
    public void Running_something_that_is_missing_reports_it_and_marks_the_row()
    {
        Install("tool", createExecutable: false);

        var vm = NewViewModel();
        var app = Assert.Single(vm.Applications);

        app.RunCommand.Execute(null);

        Assert.NotNull(vm.Message);
        Assert.Contains("missing", vm.Message);
        Assert.False(app.IsIntact);
    }
}
