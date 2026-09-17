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

    /// <summary>Registers an application, optionally creating the executable on disk.</summary>
    private ApplicationManifest Install(
        string name, bool createExecutable = true, bool downloadOnly = false)
    {
        var directory = Path.Combine(_paths.Apps, "someone__" + name);
        Directory.CreateDirectory(directory);

        var executable = Path.Combine(directory, name + ".exe");
        if (createExecutable) File.WriteAllText(executable, "program");

        var manifest = new ApplicationManifest
        {
            Owner = "someone",
            Name = name,
            RepositoryUrl = $"https://github.com/someone/{name}",
            InstalledPath = directory,
            ExecutableRelativePath = downloadOnly ? null : Path.GetFileName(executable),
            State = downloadOnly ? InstallationState.Downloaded : InstallationState.Installed,
            DownloadedFilePath = downloadOnly ? Path.Combine(_paths.Downloads, name + "-setup.exe") : null,
            ReleaseTag = "v1.0.0",
            AssetSize = 1024,
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
    public void An_intact_application_can_be_run()
    {
        Install("tool");

        var app = Assert.Single(NewViewModel().Applications);

        Assert.True(app.IsIntact);
        Assert.True(app.CanRun);
        Assert.Equal("Ready", app.StatusText);
        Assert.Equal("Run", app.RunButtonText);
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
    public void A_download_only_record_is_not_offered_as_runnable()
    {
        Install("installer-app", createExecutable: false, downloadOnly: true);

        var app = Assert.Single(NewViewModel().Applications);

        Assert.False(app.CanRun);
        Assert.Equal("Downloaded, not installed", app.StatusText);
        Assert.Equal("Open installer", app.RunButtonText);
    }

    [Fact]
    public async Task Uninstalling_removes_the_row()
    {
        Install("tool");

        var installer = new InstallationService(
            new FakeDownloadService(Path.Combine(_root, "unused"), _paths.Downloads),
            new ExtractionService(NullAppLog.Instance),
            _store, _paths, NullAppLog.Instance);

        var vm = NewViewModel(installer);
        var app = Assert.Single(vm.Applications);

        await app.UninstallCommand.ExecuteAsync(null);

        Assert.Empty(vm.Applications);
        Assert.True(vm.ShowEmptyState);
        Assert.Contains("Removed", vm.Message);
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
