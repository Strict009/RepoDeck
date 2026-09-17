using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The install flow on the details page, with the confirmation gate that stands between
/// looking at a repository and writing to the machine.
/// </summary>
public sealed class DetailsInstallFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDeckFlowTests", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;

    public DetailsInstallFlowTests()
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

    private static FakeGitHubClient InstallableRepository()
    {
        var github = new FakeGitHubClient
        {
            Tree = TestTrees.Of("App.sln", "src/App/App.csproj"),
            Releases = [TestRepositories.Release("v1.4.2", false, "example-win-x64.zip")]
        };

        github.TextFiles["src/App/App.csproj"] = TestManifests.AvaloniaDesktopProject;
        return github;
    }

    private RepositoryDetailsViewModel ViewModel(
        FakeGitHubClient github, IInstallationService installer) =>
        DetailsViewModelFactory.Create(
            github,
            repository: TestRepositories.Create("example", "someone"),
            installer: installer,
            installedApps: _store,
            paths: _paths);

    [Fact]
    public async Task Install_does_not_download_until_the_user_confirms()
    {
        var installer = new RecordingInstallationService();
        var vm = ViewModel(InstallableRepository(), installer);

        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal(InstallState.NotInstalled, vm.InstallState);

        // Pressing Install only shows the plan.
        vm.BeginInstallCommand.Execute(null);

        Assert.Equal(InstallState.AwaitingConfirmation, vm.InstallState);
        Assert.True(vm.ShowConfirmation);
        Assert.Equal(0, installer.InstallCount);
    }

    [Fact]
    public async Task Declining_the_confirmation_installs_nothing()
    {
        var installer = new RecordingInstallationService();
        var vm = ViewModel(InstallableRepository(), installer);

        await vm.LoadAsync(CancellationToken.None);
        vm.BeginInstallCommand.Execute(null);
        vm.CancelConfirmationCommand.Execute(null);

        Assert.Equal(InstallState.NotInstalled, vm.InstallState);
        Assert.Equal(0, installer.InstallCount);
    }

    [Fact]
    public async Task Confirming_installs_and_offers_Run()
    {
        var installer = new RecordingInstallationService
        {
            Result = plan => new InstallationResult
            {
                Succeeded = true,
                Manifest = ManifestFor(plan, withExecutable: true)
            }
        };

        var vm = ViewModel(InstallableRepository(), installer);

        await vm.LoadAsync(CancellationToken.None);
        vm.BeginInstallCommand.Execute(null);
        await vm.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(1, installer.InstallCount);
        Assert.Equal(InstallState.Installed, vm.InstallState);
        Assert.True(vm.ShowRunButton);
        Assert.Equal("Run", vm.RunButtonText);
        Assert.Contains("v1.4.2", vm.InstalledVersionText);
    }

    [Fact]
    public async Task A_failed_installation_returns_to_the_install_button_with_the_reason()
    {
        var installer = new RecordingInstallationService
        {
            Result = _ => InstallationResult.Failed("The download was incomplete.")
        };

        var vm = ViewModel(InstallableRepository(), installer);

        await vm.LoadAsync(CancellationToken.None);
        vm.BeginInstallCommand.Execute(null);
        await vm.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallState.NotInstalled, vm.InstallState);
        Assert.Equal("The download was incomplete.", vm.InstallMessage);
        Assert.False(vm.ShowRunButton);
    }

    [Fact]
    public async Task A_cancelled_installation_says_nothing_was_installed()
    {
        var installer = new RecordingInstallationService { Result = _ => InstallationResult.Cancelled() };
        var vm = ViewModel(InstallableRepository(), installer);

        await vm.LoadAsync(CancellationToken.None);
        vm.BeginInstallCommand.Execute(null);
        await vm.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallState.NotInstalled, vm.InstallState);
        Assert.Contains("Nothing was installed", vm.InstallMessage);
    }

    [Fact]
    public async Task A_downloaded_installer_is_reported_as_not_installed()
    {
        var installer = new RecordingInstallationService
        {
            Result = plan => new InstallationResult
            {
                Succeeded = true,
                DownloadedOnly = true,
                Manifest = ManifestFor(plan, withExecutable: false) with
                {
                    State = InstallationState.Downloaded,
                    NotInstalledReason = "This is a Windows installer. RepoDeck downloaded it "
                                         + "but will not run installers on your behalf."
                }
            }
        };

        var vm = ViewModel(InstallableRepository(), installer);

        await vm.LoadAsync(CancellationToken.None);
        vm.BeginInstallCommand.Execute(null);
        await vm.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallState.DownloadedOnly, vm.InstallState);
        Assert.False(vm.ShowRunButton);
        Assert.Contains("will not run installers", vm.InstallMessage);
    }

    [Fact]
    public async Task A_repository_with_no_usable_plan_offers_no_install_button()
    {
        var github = new FakeGitHubClient
        {
            Tree = TestTrees.Of("CMakeLists.txt"),
            Releases = [TestRepositories.Release("v1.0", false, "Source code (zip)")]
        };

        var vm = ViewModel(github, new RecordingInstallationService());
        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(InstallState.Unavailable, vm.InstallState);
        Assert.False(vm.ShowInstallButton);
        Assert.False(vm.ShowConfirmation);
    }

    [Fact]
    public async Task An_already_installed_application_opens_straight_to_Run()
    {
        var directory = Path.Combine(_paths.Apps, "someone__example");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "example.exe");
        await File.WriteAllTextAsync(executable, "program");

        _store.Save(new ApplicationManifest
        {
            Owner = "someone",
            Name = "example",
            RepositoryUrl = "https://github.com/someone/example",
            InstalledPath = directory,
            ExecutableRelativePath = Path.GetFileName(executable),
            ReleaseTag = "v1.0.0",
            InstalledAt = DateTimeOffset.UtcNow
        });

        var vm = ViewModel(InstallableRepository(), new RecordingInstallationService());
        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(InstallState.Installed, vm.InstallState);
        Assert.True(vm.ShowRunButton);
        Assert.False(vm.ShowInstallButton);
    }

    [Fact]
    public async Task An_installation_whose_program_has_vanished_offers_Repair()
    {
        var directory = Path.Combine(_paths.Apps, "someone__example");
        Directory.CreateDirectory(directory);

        _store.Save(new ApplicationManifest
        {
            Owner = "someone",
            Name = "example",
            RepositoryUrl = "https://github.com/someone/example",
            InstalledPath = directory,
            ExecutableRelativePath = "gone.exe",
            ReleaseTag = "v1.0.0",
            InstalledAt = DateTimeOffset.UtcNow
        });

        var vm = ViewModel(InstallableRepository(), new RecordingInstallationService());
        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(InstallState.Broken, vm.InstallState);
        Assert.Equal("Repair", vm.RunButtonText);
        Assert.Contains("no longer where RepoDeck left it", vm.InstallMessage);
    }

    [Fact]
    public async Task Uninstall_on_the_details_page_asks_before_removing_anything()
    {
        var directory = Path.Combine(_paths.Apps, "someone__example");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "example.exe");
        await File.WriteAllTextAsync(executable, "program");

        _store.Save(new ApplicationManifest
        {
            Owner = "someone",
            Name = "example",
            RepositoryUrl = "https://github.com/someone/example",
            InstalledPath = directory,
            ExecutableRelativePath = "example.exe",
            ReleaseTag = "v1.0.0",
            InstalledAt = DateTimeOffset.UtcNow
        });

        var vm = ViewModel(InstallableRepository(), new RecordingInstallationService());
        await vm.LoadAsync(CancellationToken.None);

        Assert.True(vm.ShowUninstallButton);

        vm.BeginUninstallCommand.Execute(null);

        // Asking is not doing.
        Assert.True(vm.IsConfirmingUninstall);
        Assert.False(vm.ShowUninstallButton);
        Assert.True(_store.IsInstalled("someone", "example"));
        Assert.True(Directory.Exists(directory));

        vm.CancelUninstallCommand.Execute(null);

        Assert.False(vm.IsConfirmingUninstall);
        Assert.True(_store.IsInstalled("someone", "example"));
    }

    [Fact]
    public async Task An_unresolved_installation_offers_no_Run_on_the_details_page()
    {
        var directory = Path.Combine(_paths.Apps, "someone__example");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "example.exe"), "program");

        _store.Save(new ApplicationManifest
        {
            Owner = "someone",
            Name = "example",
            RepositoryUrl = "https://github.com/someone/example",
            InstalledPath = directory,
            ExecutableRelativePath = null,
            AlternativeExecutables = ["example.exe", "example-updater.exe"],
            ExecutableIsAmbiguous = true,
            State = InstallationState.AwaitingExecutableChoice,
            ReleaseTag = "v1.0.0",
            InstalledAt = DateTimeOffset.UtcNow
        });

        var vm = ViewModel(InstallableRepository(), new RecordingInstallationService());
        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(InstallState.NeedsExecutableChoice, vm.InstallState);
        Assert.False(vm.ShowRunButton);
        Assert.Contains("multiple possible application executables", vm.InstallMessage);
    }

    private ApplicationManifest ManifestFor(InstallPlan plan, bool withExecutable)
    {
        var directory = Path.Combine(_paths.Apps, "someone__example");
        Directory.CreateDirectory(directory);

        var executable = Path.Combine(directory, "example.exe");
        if (withExecutable) File.WriteAllText(executable, "program");

        var manifest = new ApplicationManifest
        {
            Owner = plan.Owner,
            Name = plan.Name,
            RepositoryUrl = plan.RepositoryUrl,
            ReleaseTag = plan.ReleaseTag,
            InstalledPath = directory,
            ExecutableRelativePath = withExecutable ? Path.GetFileName(executable) : null,
            InstalledAt = DateTimeOffset.UtcNow
        };

        _store.Save(manifest);
        return manifest;
    }
}

/// <summary>Records what it was asked to do, and returns whatever the test decides.</summary>
internal sealed class RecordingInstallationService : IInstallationService
{
    public int InstallCount { get; private set; }
    public Func<InstallPlan, InstallationResult>? Result { get; set; }

    public Task<InstallationResult> InstallAsync(
        InstallPlan plan,
        IProgress<InstallationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        InstallCount++;

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Downloading });

        return Task.FromResult(Result?.Invoke(plan)
                               ?? InstallationResult.Failed("No result configured."));
    }

    public Task<bool> UninstallAsync(
        ApplicationManifest manifest, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public ExecutableChoiceResult ChooseExecutable(ApplicationManifest manifest, string relativePath) =>
        ExecutableChoiceResult.Failed("Not available in this test.");
}
