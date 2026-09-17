using System.IO.Compression;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

/// <summary>
/// The installation pipeline end to end, with the download faked from a local file.
/// Nothing here is ever launched.
/// </summary>
public sealed class InstallationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDeckInstallTests", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;

    public InstallationServiceTests()
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
            // A locked temp file must not fail the run.
        }
    }

    private string MakeZip(string name, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(_root, name);
        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    private string MakeFile(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private InstallationService ServiceFor(string sourceFile, out FakeDownloadService downloads)
    {
        downloads = new FakeDownloadService(sourceFile, _paths.Downloads);
        return new InstallationService(
            downloads, new ExtractionService(NullAppLog.Instance), _store,
            _paths, NullAppLog.Instance);
    }

    private InstallPlan Plan(
        string assetName,
        PackageType packageType,
        InstallStrategy strategy,
        params string[] executableCandidates) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        ReleaseTag = "v1.0.0",
        ReleaseName = "Version 1.0.0",
        AssetName = assetName,
        AssetUrl = "https://example.invalid/" + assetName,
        AssetSize = 100,
        Platform = OsPlatform.Windows,
        Architecture = CpuArchitecture.X64,
        PackageType = packageType,
        Strategy = strategy,
        LaunchStrategy = LaunchStrategy.ExecutableFile,
        ProposedInstallDirectory = Path.Combine(_paths.Apps, "someone__tool"),
        RequiresExtraction = packageType.RequiresExtraction(),
        ExecutableCandidates = executableCandidates,
        Confidence = Confidence.Likely
    };

    [Fact]
    public async Task A_portable_archive_is_extracted_identified_and_registered()
    {
        var zip = MakeZip("tool-win-x64.zip",
            ("tool.exe", "program"), ("bin/helper.dll", "support"), ("unins000.exe", "uninstaller"));

        var service = ServiceFor(zip, out _);
        var result = await service.InstallAsync(
            Plan("tool-win-x64.zip", PackageType.Zip, InstallStrategy.PortableArchive, "tool.exe"));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Manifest);

        var manifest = result.Manifest!;
        Assert.Equal("tool.exe", Path.GetFileName(manifest.ExecutablePath));
        Assert.Equal("v1.0.0", manifest.ReleaseTag);
        Assert.NotNull(manifest.AssetSha256);
        Assert.True(File.Exists(manifest.ExecutablePath));

        // And it is remembered.
        Assert.True(_store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public async Task The_installation_lands_inside_RepoDecks_own_folder()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "program"));
        var service = ServiceFor(zip, out _);

        var result = await service.InstallAsync(
            Plan("tool.zip", PackageType.Zip, InstallStrategy.PortableArchive));

        Assert.True(ArchivePathGuard.IsInside(_paths.Apps, result.Manifest!.InstalledPath));
    }

    [Fact]
    public async Task A_plan_that_cannot_proceed_is_refused_and_nothing_is_downloaded()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "program"));
        var service = ServiceFor(zip, out var downloads);

        var blocked = Plan("tool.zip", PackageType.Zip, InstallStrategy.SourceBuild) with
        {
            BlockingIssues = ["This project publishes no releases."]
        };

        var result = await service.InstallAsync(blocked);

        Assert.False(result.Succeeded);
        Assert.Contains("no releases", result.ErrorMessage);
        Assert.Equal(0, downloads.CallCount);
    }

    [Fact]
    public async Task A_Windows_installer_is_downloaded_but_never_run()
    {
        var installer = MakeFile("tool-setup.exe", "pretend installer");
        var service = ServiceFor(installer, out _);

        var result = await service.InstallAsync(
            Plan("tool-setup.exe", PackageType.WindowsInstaller, InstallStrategy.WindowsInstaller));

        Assert.True(result.Succeeded);
        Assert.True(result.DownloadedOnly);

        var manifest = result.Manifest!;
        Assert.True(manifest.IsDownloadOnly);
        Assert.Null(manifest.ExecutablePath);
        Assert.NotNull(manifest.DownloadedFilePath);
        Assert.True(File.Exists(manifest.DownloadedFilePath));
    }

    [Fact]
    public async Task A_standalone_executable_is_moved_into_the_managed_folder()
    {
        var exe = MakeFile("tool.exe", "program");
        var service = ServiceFor(exe, out _);

        var result = await service.InstallAsync(
            Plan("tool.exe", PackageType.WindowsExecutable, InstallStrategy.StandaloneExecutable));

        Assert.True(result.Succeeded);
        Assert.False(result.Manifest!.IsDownloadOnly);
        Assert.True(File.Exists(result.Manifest.ExecutablePath));
        Assert.True(ArchivePathGuard.IsInside(_paths.Apps, result.Manifest.ExecutablePath!));
    }

    [Fact]
    public async Task A_failed_extraction_rolls_the_installation_folder_back()
    {
        var corrupt = MakeFile("tool.zip", "not actually a zip file");
        var service = ServiceFor(corrupt, out _);
        var plan = Plan("tool.zip", PackageType.Zip, InstallStrategy.PortableArchive);

        var result = await service.InstallAsync(plan);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);

        // A half-made folder must not survive to look like a working installation.
        Assert.False(Directory.Exists(plan.ProposedInstallDirectory));
        Assert.False(_store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public async Task A_malicious_archive_cannot_write_outside_the_installation_folder()
    {
        var zip = MakeZip("evil.zip",
            ("tool.exe", "program"), ("../../../escaped.txt", "should never exist"));

        var service = ServiceFor(zip, out _);
        var result = await service.InstallAsync(
            Plan("evil.zip", PackageType.Zip, InstallStrategy.PortableArchive));

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(_paths.Apps, "escaped.txt")));
    }

    [Fact]
    public async Task Cancellation_leaves_nothing_installed()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "program"));
        var service = ServiceFor(zip, out _);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await service.InstallAsync(
            Plan("tool.zip", PackageType.Zip, InstallStrategy.PortableArchive), null, cts.Token);

        Assert.False(result.Succeeded);
        Assert.True(result.WasCancelled);
        Assert.False(_store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public async Task Progress_moves_through_the_stages()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "program"));
        var service = ServiceFor(zip, out _);

        var stages = new List<InstallationStage>();
        var progress = new SynchronousProgress<InstallationProgress>(p => stages.Add(p.Stage));

        await service.InstallAsync(
            Plan("tool.zip", PackageType.Zip, InstallStrategy.PortableArchive), progress);

        Assert.Contains(InstallationStage.Downloading, stages);
        Assert.Contains(InstallationStage.Extracting, stages);
        Assert.Contains(InstallationStage.LocatingExecutable, stages);
        Assert.Contains(InstallationStage.Finished, stages);
    }

    [Fact]
    public async Task Uninstalling_removes_the_folder_and_the_record()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "program"));
        var service = ServiceFor(zip, out _);

        var installed = await service.InstallAsync(
            Plan("tool.zip", PackageType.Zip, InstallStrategy.PortableArchive));

        var directory = installed.Manifest!.InstalledPath;
        Assert.True(Directory.Exists(directory));

        var removed = await service.UninstallAsync(installed.Manifest);

        Assert.True(removed);
        Assert.False(Directory.Exists(directory));
        Assert.False(_store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public async Task Uninstall_refuses_a_manifest_pointing_outside_RepoDecks_folder()
    {
        // A tampered manifest must not turn Uninstall into a way to delete anything.
        var outside = Path.Combine(_root, "not-managed");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "precious.txt"), "do not delete");

        var service = ServiceFor(MakeFile("x.exe", "x"), out _);

        var tampered = new ApplicationManifest
        {
            Owner = "someone",
            Name = "tool",
            RepositoryUrl = "https://github.com/someone/tool",
            InstalledPath = outside,
            InstalledAt = DateTimeOffset.UtcNow
        };

        var removed = await service.UninstallAsync(tampered);

        Assert.False(removed);
        Assert.True(File.Exists(Path.Combine(outside, "precious.txt")));
    }

    [Fact]
    public async Task Reinstalling_replaces_the_previous_installation_cleanly()
    {
        var first = MakeZip("v1.zip", ("tool.exe", "one"), ("stale.txt", "old file"));
        var service = ServiceFor(first, out _);
        await service.InstallAsync(Plan("v1.zip", PackageType.Zip, InstallStrategy.PortableArchive));

        var second = MakeZip("v2.zip", ("tool.exe", "two"));
        var updated = ServiceFor(second, out _);
        var result = await updated.InstallAsync(
            Plan("v2.zip", PackageType.Zip, InstallStrategy.PortableArchive));

        Assert.True(result.Succeeded);

        // The old file is gone rather than left behind alongside the new one.
        Assert.False(File.Exists(Path.Combine(result.Manifest!.InstalledPath, "stale.txt")));
        Assert.Single(_store.GetAll());
    }
}

/// <summary>Reports synchronously, so tests assert on what was reported rather than on timing.</summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public SynchronousProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
}
