using System.IO.Compression;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

/// <summary>
/// Every way an installation can go wrong, and the guarantee that none of them leaves a
/// valid-looking Installed entry behind.
/// </summary>
public sealed class InstallationFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDeckFailTests", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;

    public InstallationFailureTests()
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

    private string StagingRoot => Path.Combine(_paths.Apps, InstallationService.StagingFolderName);

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

    private InstallationService Service(
        IDownloadService downloads, IInstalledAppStore? store = null, IExtractionService? extraction = null) =>
        new(downloads, extraction ?? new ExtractionService(NullAppLog.Instance),
            store ?? _store, _paths, NullAppLog.Instance);

    private InstallationService ServiceFor(string sourceFile) =>
        Service(new FakeDownloadService(sourceFile, _paths.Downloads));

    private InstallPlan Plan(string assetName = "tool.zip", PackageType type = PackageType.Zip) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        RepositoryId = 42,
        ReleaseTag = "v1.0.0",
        ReleaseId = 7,
        AssetName = assetName,
        AssetUrl = "https://example.invalid/" + assetName,
        AssetSize = 100,
        Platform = OsPlatform.Windows,
        Architecture = CpuArchitecture.X64,
        PackageType = type,
        Strategy = InstallStrategy.PortableArchive,
        LaunchStrategy = LaunchStrategy.ExecutableFile,
        ProposedInstallDirectory = Path.Combine(_paths.Apps, "someone__tool"),
        RequiresExtraction = true,
        ExecutableCandidates = ["tool.exe"],
        Confidence = Confidence.Likely
    };

    /// <summary>The whole point: a failure must never look like a success afterwards.</summary>
    private void AssertNothingInstalled(InstallPlan plan)
    {
        Assert.False(_store.IsInstalled(plan.Owner, plan.Name));
        Assert.False(Directory.Exists(plan.ProposedInstallDirectory));
        AssertStagingIsClean();
    }

    private void AssertStagingIsClean()
    {
        if (!Directory.Exists(StagingRoot)) return;
        Assert.Empty(Directory.GetDirectories(StagingRoot));
    }

    // ---- Download ---------------------------------------------------------

    [Fact]
    public async Task A_download_failure_installs_nothing()
    {
        var downloads = new FakeDownloadService(MakeZip("tool.zip", ("tool.exe", "x")), _paths.Downloads)
        {
            Throws = new DownloadException("The download was refused.")
        };

        var plan = Plan();
        var result = await Service(downloads).InstallAsync(plan);

        Assert.False(result.Succeeded);
        Assert.Equal("The download was refused.", result.ErrorMessage);
        AssertNothingInstalled(plan);
    }

    [Fact]
    public async Task Cancellation_during_download_installs_nothing()
    {
        var downloads = new FakeDownloadService(MakeZip("tool.zip", ("tool.exe", "x")), _paths.Downloads);
        var plan = Plan();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Service(downloads).InstallAsync(plan, null, cts.Token);

        Assert.True(result.WasCancelled);
        Assert.False(result.Succeeded);
        AssertNothingInstalled(plan);
    }

    // ---- Archive contents -------------------------------------------------

    [Fact]
    public async Task A_corrupt_archive_installs_nothing()
    {
        var corrupt = Path.Combine(_root, "tool.zip");
        await File.WriteAllTextAsync(corrupt, "this is not a zip file at all");

        var plan = Plan();
        var result = await ServiceFor(corrupt).InstallAsync(plan);

        Assert.False(result.Succeeded);
        Assert.Contains("not a valid archive", result.ErrorMessage);
        AssertNothingInstalled(plan);
    }

    [Fact]
    public async Task A_truncated_archive_installs_nothing()
    {
        // A ZIP whose central directory has been cut off - the shape of a partial download
        // that somehow reached extraction.
        var full = MakeZip("full.zip", ("tool.exe", new string('x', 4096)));
        var bytes = await File.ReadAllBytesAsync(full);
        var truncated = Path.Combine(_root, "tool.zip");
        await File.WriteAllBytesAsync(truncated, bytes[..(bytes.Length / 2)]);

        var plan = Plan();
        var result = await ServiceFor(truncated).InstallAsync(plan);

        Assert.False(result.Succeeded);
        AssertNothingInstalled(plan);
    }

    [Fact]
    public async Task A_zip_slip_archive_cannot_write_outside_staging_or_the_install_folder()
    {
        var zip = MakeZip("tool.zip",
            ("tool.exe", "program"),
            ("../../../escaped.txt", "should never exist"),
            ("../../sibling.txt", "nor this"));

        var plan = Plan();
        var result = await ServiceFor(zip).InstallAsync(plan);

        Assert.True(result.Succeeded);

        // Nothing escaped, at any level above the destination.
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(_paths.Apps, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(_paths.Apps, "sibling.txt")));
        Assert.False(File.Exists(Path.Combine(StagingRoot, "escaped.txt")));

        // And the legitimate file did land.
        Assert.True(File.Exists(Path.Combine(plan.ProposedInstallDirectory, "tool.exe")));
    }

    [Fact]
    public async Task An_archive_with_no_executable_is_downloaded_not_installed()
    {
        // The download succeeded and is worth keeping, but RepoDeck established nothing
        // runnable - so it must not appear in the Installed library as an application.
        var zip = MakeZip("tool.zip", ("readme.txt", "words"), ("data/config.json", "{}"));

        var plan = Plan();
        var result = await ServiceFor(zip).InstallAsync(plan);

        Assert.True(result.Succeeded);
        Assert.True(result.DownloadedOnly);

        var manifest = result.Manifest!;
        Assert.Equal(InstallationState.Downloaded, manifest.State);
        Assert.False(manifest.IsRunnableInstallation);
        Assert.Null(manifest.ExecutableRelativePath);

        Assert.Equal(
            "Download completed, but RepoDeck could not identify a runnable application in this release.",
            manifest.NotInstalledReason);

        // No application directory was created, and the asset itself is preserved.
        Assert.False(Directory.Exists(plan.ProposedInstallDirectory));
        Assert.NotNull(manifest.DownloadedFilePath);
        Assert.True(File.Exists(manifest.DownloadedFilePath));

        // Enough provenance survives for a later version to retry.
        Assert.Equal(42, manifest.RepositoryId);
        Assert.Equal("v1.0.0", manifest.ReleaseTag);
        Assert.NotNull(manifest.AssetDownloadUrl);
        AssertStagingIsClean();
    }

    [Fact]
    public async Task Several_plausible_executables_await_a_choice_rather_than_a_guess()
    {
        // The release every installer author dreads.
        var zip = MakeZip("tool.zip",
            ("CoolApp.exe", "app"),
            ("CoolAppUpdater.exe", "updater"),
            ("CrashReporter.exe", "crash"),
            ("unins000.exe", "uninstaller"),
            ("setup.exe", "setup"),
            ("helper.exe", "helper"));

        var plan = Plan() with { ExecutableCandidates = [] };
        var result = await ServiceFor(zip).InstallAsync(plan);

        Assert.True(result.Succeeded);
        Assert.True(result.ExecutableIsAmbiguous);
        Assert.Equal("RepoDeck found multiple possible application executables.", result.ExecutableNote);

        var manifest = result.Manifest!;

        // The files are installed, but nothing is nominated as the program to run.
        Assert.Equal(InstallationState.AwaitingExecutableChoice, manifest.State);
        Assert.Null(manifest.ExecutableRelativePath);
        Assert.False(manifest.IsRunnableInstallation);
        Assert.True(manifest.NeedsExecutableChoice);

        // The candidates are preserved so the user can settle it.
        Assert.Contains("CoolApp.exe", manifest.AlternativeExecutables);
        Assert.True(manifest.AlternativeExecutables.Count > 1);

        // And the extracted files really are there.
        Assert.True(Directory.Exists(plan.ProposedInstallDirectory));
        AssertStagingIsClean();
    }

    [Fact]
    public async Task An_unresolved_installation_cannot_be_run_until_a_choice_is_made()
    {
        var zip = MakeZip("tool.zip",
            ("CoolApp.exe", "app"), ("CoolAppUpdater.exe", "updater"), ("helper.exe", "helper"));

        var service = ServiceFor(zip);
        var result = await service.InstallAsync(Plan() with { ExecutableCandidates = [] });

        var launcher = new LaunchService(_paths, _store, NullAppLog.Instance);
        var refused = launcher.Launch(result.Manifest!);

        Assert.False(refused.Succeeded);
        Assert.Contains("multiple possible application executables", refused.ErrorMessage);
    }

    [Fact]
    public async Task Choosing_a_candidate_makes_the_installation_runnable()
    {
        var zip = MakeZip("tool.zip",
            ("CoolApp.exe", "app"), ("CoolAppUpdater.exe", "updater"), ("helper.exe", "helper"));

        var service = ServiceFor(zip);
        var installed = await service.InstallAsync(Plan() with { ExecutableCandidates = [] });
        var manifest = installed.Manifest!;

        var chosen = manifest.AlternativeExecutables.First(c => c.Contains("CoolApp.exe"));
        var result = service.ChooseExecutable(manifest, chosen);

        Assert.True(result.Succeeded);

        var updated = result.Manifest!;
        Assert.Equal(InstallationState.Installed, updated.State);
        Assert.Equal(chosen, updated.ExecutableRelativePath);
        Assert.False(updated.ExecutableIsAmbiguous);
        Assert.True(updated.IsRunnableInstallation);
        Assert.Null(updated.NotInstalledReason);

        // The alternatives stay, as provenance and in case the choice was wrong.
        Assert.NotEmpty(updated.AlternativeExecutables);

        // And the change was persisted.
        Assert.Equal(chosen, _store.Find("someone", "tool")!.ExecutableRelativePath);
    }

    [Theory]
    [InlineData("../../../evil.exe")]
    [InlineData(@"..\evil.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData("/bin/sh")]
    [InlineData("not-a-candidate.exe")]
    public async Task Choosing_something_that_was_not_offered_is_refused(string attempt)
    {
        // The chooser must never become a way to point RepoDeck at an arbitrary file.
        var zip = MakeZip("tool.zip",
            ("CoolApp.exe", "app"), ("CoolAppUpdater.exe", "updater"), ("helper.exe", "helper"));

        var service = ServiceFor(zip);
        var installed = await service.InstallAsync(Plan() with { ExecutableCandidates = [] });

        var result = service.ChooseExecutable(installed.Manifest!, attempt);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(InstallationState.AwaitingExecutableChoice,
            _store.Find("someone", "tool")!.State);
    }

    [Fact]
    public async Task Choosing_on_an_installation_that_is_not_waiting_is_refused()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "program"));
        var service = ServiceFor(zip);
        var installed = await service.InstallAsync(Plan());

        Assert.Equal(InstallationState.Installed, installed.Manifest!.State);

        var result = service.ChooseExecutable(installed.Manifest, "tool.exe");

        Assert.False(result.Succeeded);
        Assert.Contains("not waiting", result.ErrorMessage);
    }

    [Fact]
    public async Task An_unsupported_archive_format_installs_nothing()
    {
        var plan = Plan("tool.7z", PackageType.SevenZip);
        var result = await ServiceFor(MakeZip("tool.7z", ("tool.exe", "x"))).InstallAsync(plan);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot unpack", result.ErrorMessage);
        AssertNothingInstalled(plan);
    }

    // ---- Mid-flight failures ----------------------------------------------

    [Fact]
    public async Task An_extraction_failure_part_way_through_leaves_nothing_behind()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "x"));
        var extraction = new FailingExtractionService(afterFiles: 3);

        var plan = Plan();
        var result = await Service(
            new FakeDownloadService(zip, _paths.Downloads), extraction: extraction).InstallAsync(plan);

        Assert.False(result.Succeeded);
        AssertNothingInstalled(plan);
    }

    [Fact]
    public async Task A_manifest_write_failure_does_not_leave_a_registered_application()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "x"));
        var store = new FailingStore();

        var plan = Plan();
        var result = await Service(
            new FakeDownloadService(zip, _paths.Downloads), store).InstallAsync(plan);

        Assert.False(result.Succeeded);

        // The registration never happened, so the application is not installed as far as
        // RepoDeck is concerned - which is the only definition that matters.
        Assert.False(store.Saved);
        Assert.False(_store.IsInstalled(plan.Owner, plan.Name));
    }

    [Fact]
    public async Task Cancellation_after_the_download_leaves_nothing_behind()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "x"));
        var plan = Plan();

        using var cts = new CancellationTokenSource();

        // Cancel once the download has completed and extraction is under way.
        var progress = new SynchronousProgress<InstallationProgress>(p =>
        {
            if (p.Stage == InstallationStage.Extracting) cts.Cancel();
        });

        var result = await ServiceFor(zip).InstallAsync(plan, progress, cts.Token);

        Assert.False(result.Succeeded);
        AssertNothingInstalled(plan);
    }

    // ---- Existing state ---------------------------------------------------

    [Fact]
    public async Task An_existing_destination_directory_is_replaced_cleanly()
    {
        var destination = Plan().ProposedInstallDirectory;
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "stale.txt"), "from an older version");

        var zip = MakeZip("tool.zip", ("tool.exe", "new"));
        var plan = Plan();
        var result = await ServiceFor(zip).InstallAsync(plan);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(Path.Combine(destination, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "tool.exe")));
        AssertStagingIsClean();
    }

    [Fact]
    public async Task A_failed_reinstall_leaves_the_previous_installation_in_place()
    {
        // Staging exists precisely so a bad new version cannot destroy a working old one.
        var plan = Plan();
        await ServiceFor(MakeZip("good.zip", ("tool.exe", "working"))).InstallAsync(plan);

        Assert.True(File.Exists(Path.Combine(plan.ProposedInstallDirectory, "tool.exe")));

        var corrupt = Path.Combine(_root, "bad.zip");
        await File.WriteAllTextAsync(corrupt, "not a zip");

        var second = await ServiceFor(corrupt).InstallAsync(plan with { AssetName = "bad.zip" });

        Assert.False(second.Succeeded);

        // The working installation survived the failed attempt.
        Assert.True(File.Exists(Path.Combine(plan.ProposedInstallDirectory, "tool.exe")));
        Assert.Equal("working",
            await File.ReadAllTextAsync(Path.Combine(plan.ProposedInstallDirectory, "tool.exe")));
        Assert.True(_store.IsInstalled(plan.Owner, plan.Name));
        AssertStagingIsClean();
    }

    [Fact]
    public async Task Staging_left_by_an_interrupted_run_is_cleared()
    {
        // Simulate a previous process that died mid-installation.
        var abandoned = Path.Combine(StagingRoot, "abandoned1");
        Directory.CreateDirectory(abandoned);
        await File.WriteAllTextAsync(Path.Combine(abandoned, "half.exe"), "partial");

        var displaced = Path.Combine(_paths.Apps, "someone__tool.replacing-deadbeef");
        Directory.CreateDirectory(displaced);

        var removed = ServiceFor(MakeZip("tool.zip", ("tool.exe", "x"))).CleanAbandonedStaging();

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(abandoned));
        Assert.False(Directory.Exists(displaced));
    }

    [Fact]
    public async Task A_successful_install_leaves_no_staging_behind()
    {
        var plan = Plan();
        var result = await ServiceFor(MakeZip("tool.zip", ("tool.exe", "x"))).InstallAsync(plan);

        Assert.True(result.Succeeded);
        AssertStagingIsClean();
    }

    [Fact]
    public async Task The_installation_never_lands_in_the_staging_folder()
    {
        var plan = Plan();
        var result = await ServiceFor(MakeZip("tool.zip", ("tool.exe", "x"))).InstallAsync(plan);

        Assert.True(result.Succeeded);
        Assert.False(ArchivePathGuard.IsInside(StagingRoot, result.Manifest!.InstalledPath));
    }
}

/// <summary>Fails part-way through, as a disk filling up or a locked file would.</summary>
internal sealed class FailingExtractionService : IExtractionService
{
    private readonly int _afterFiles;

    public FailingExtractionService(int afterFiles) => _afterFiles = afterFiles;

    public async Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string destinationDirectory,
        PackageType packageType,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Write some files first, so the failure happens with work already on disk.
        for (var i = 0; i < _afterFiles; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(destinationDirectory, $"partial{i}.dat"), "half written", cancellationToken);
        }

        throw new ExtractionException("The archive could not be unpacked completely.");
    }
}

/// <summary>Refuses to persist, as a full or read-only disk would.</summary>
internal sealed class FailingStore : IInstalledAppStore
{
    public bool Saved { get; private set; }

    /// <summary>This one never gets far enough to have anything to announce.</summary>
    public event Action? Changed { add { } remove { } }

    public IReadOnlyList<ApplicationManifest> GetAll() => [];
    public ApplicationManifest? Find(string owner, string name) => null;
    public bool IsInstalled(string owner, string name) => false;
    public bool Remove(string owner, string name) => false;

    public void Save(ApplicationManifest manifest) =>
        throw new IOException("There is not enough space on the disk.");
}
