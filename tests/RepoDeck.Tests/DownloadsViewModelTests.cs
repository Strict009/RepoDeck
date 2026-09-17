using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

public sealed class DownloadsViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDeckDownloadsTests", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;

    public DownloadsViewModelTests()
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

    private DownloadsViewModel NewViewModel(IInstallationService? installer = null) =>
        new(_store, installer ?? new NoOpInstallationService(), NullAppLog.Instance);

    private InstallationService RealInstaller() =>
        new(new FakeDownloadService(Path.Combine(_root, "unused"), _paths.Downloads),
            new ExtractionService(NullAppLog.Instance), _store, _paths, NullAppLog.Instance);

    private ApplicationManifest Downloaded(
        string name, string reason, bool createFile = true, string asset = "setup.exe")
    {
        var file = Path.Combine(_paths.Downloads, name + "-" + asset);
        if (createFile) File.WriteAllText(file, "installer bytes");

        var manifest = new ApplicationManifest
        {
            Owner = "someone",
            Name = name,
            RepositoryUrl = $"https://github.com/someone/{name}",
            InstalledPath = _paths.Downloads,
            DownloadedFilePath = file,
            AssetName = asset,
            AssetSize = 5 * 1024 * 1024,
            State = InstallationState.Downloaded,
            NotInstalledReason = reason,
            ReleaseTag = "v2.1.0",
            InstalledAt = DateTimeOffset.UtcNow
        };

        _store.Save(manifest);
        return manifest;
    }

    [Fact]
    public void An_empty_page_explains_what_would_appear_here()
    {
        var vm = NewViewModel();

        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.ShowList);
        Assert.Empty(vm.Downloads);
    }

    [Fact]
    public void Downloaded_assets_are_listed_with_their_facts()
    {
        Downloaded("tool", "This is a Windows installer. RepoDeck downloaded it but will not run installers on your behalf.");

        var item = Assert.Single(NewViewModel().Downloads);

        Assert.Equal("tool", item.Name);
        Assert.Equal("someone", item.Owner);
        Assert.Equal("v2.1.0", item.ReleaseText);
        Assert.Equal("setup.exe", item.AssetText);
        Assert.Equal("5.0 MB", item.SizeText);
        Assert.Contains("Downloaded", item.DownloadedText);
        Assert.Contains("will not run installers", item.ReasonText);
        Assert.Equal("On disk", item.StatusText);
    }

    [Fact]
    public void An_archive_with_nothing_runnable_explains_itself()
    {
        Downloaded("tool",
            "Download completed, but RepoDeck could not identify a runnable application in this release.",
            asset: "tool-win-x64.zip");

        var item = Assert.Single(NewViewModel().Downloads);

        Assert.Contains("could not identify a runnable application", item.ReasonText);
    }

    [Fact]
    public void Installed_applications_do_not_appear_on_the_downloads_page()
    {
        var directory = Path.Combine(_paths.Apps, "someone__app");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "app.exe"), "program");

        _store.Save(new ApplicationManifest
        {
            Owner = "someone",
            Name = "app",
            RepositoryUrl = "https://github.com/someone/app",
            InstalledPath = directory,
            ExecutableRelativePath = "app.exe",
            State = InstallationState.Installed,
            InstalledAt = DateTimeOffset.UtcNow
        });

        Assert.Empty(NewViewModel().Downloads);
    }

    [Fact]
    public void A_file_that_has_gone_is_reported_rather_than_hidden()
    {
        Downloaded("tool", "A reason.", createFile: false);

        var item = Assert.Single(NewViewModel().Downloads);

        Assert.False(item.FilePresent);
        Assert.Equal("File no longer present", item.StatusText);
    }

    [Fact]
    public void Removing_a_download_asks_first()
    {
        Downloaded("tool", "A reason.");

        var item = Assert.Single(NewViewModel().Downloads);

        Assert.True(item.ShowForgetButton);
        item.BeginForgetCommand.Execute(null);

        Assert.True(item.IsConfirmingForget);
        Assert.False(item.ShowForgetButton);

        // Still there: asking is not doing.
        Assert.True(_store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public async Task Confirming_removes_the_file_and_the_record()
    {
        var manifest = Downloaded("tool", "A reason.");

        var vm = NewViewModel(RealInstaller());
        var item = Assert.Single(vm.Downloads);

        item.BeginForgetCommand.Execute(null);
        await item.ConfirmForgetCommand.ExecuteAsync(null);

        Assert.Empty(vm.Downloads);
        Assert.False(_store.IsInstalled("someone", "tool"));
        Assert.False(File.Exists(manifest.DownloadedFilePath!));
    }

    [Fact]
    public void The_downloads_page_offers_no_way_to_run_anything()
    {
        // Asserted against the view, because the guarantee is about what the user can click.
        var view = File.ReadAllText(ViewPath("DownloadsView.axaml"));

        Assert.DoesNotContain("RunCommand", view);
        Assert.DoesNotContain("Content=\"Run\"", view);
    }

    private static string ViewPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "RepoDeck", "Views", fileName);
    }
}
