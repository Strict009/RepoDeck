using System.IO.Compression;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.History;
using RepoDeck.Services.Install;
using RepoDeck.Services.Update;

namespace RepoDeck.Tests;

/// <summary>
/// Noticing a damaged installation, and putting it back.
/// </summary>
public class InstallationHealthTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-health-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstallationHealthChecker _checker;

    public InstallationHealthTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _checker = new InstallationHealthChecker(_paths, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    private ApplicationManifest Installed(bool createFiles = true, string? path = null)
    {
        var directory = path ?? Path.Combine(_paths.Apps, "someone__tool");

        if (createFiles)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "tool.exe"), "program");
            File.WriteAllText(Path.Combine(directory, "data.bin"), "data");
        }

        return new ApplicationManifest
        {
            Owner = "someone",
            Name = "tool",
            RepositoryUrl = "https://github.com/someone/tool",
            ReleaseTag = "v1.0.0",
            AssetName = "tool-win-x64.zip",
            AssetDownloadUrl = "https://example.invalid/tool.zip",
            AssetSize = 100,
            State = InstallationState.Installed,
            InstalledPath = directory,
            ExecutableRelativePath = "tool.exe",
            OwnedEntries = ["data.bin", "tool.exe"],
            PackageType = PackageType.Zip,
            Strategy = InstallStrategy.PortableArchive
        };
    }

    [Fact]
    public void An_intact_installation_is_healthy()
    {
        var health = _checker.Check(Installed());

        Assert.True(health.IsHealthy);
        Assert.Empty(health.Problems);
    }

    [Fact]
    public void A_missing_directory_is_noticed()
    {
        var manifest = Installed(createFiles: false);

        var health = _checker.Check(manifest);

        Assert.False(health.IsHealthy);
        Assert.Contains(HealthProblem.MissingDirectory, health.Problems);
        Assert.Contains(health.Explanations, e => e.Contains("gone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_missing_executable_is_noticed()
    {
        var manifest = Installed();
        File.Delete(Path.Combine(manifest.InstalledPath, "tool.exe"));

        var health = _checker.Check(manifest);

        Assert.False(health.IsHealthy);
        Assert.Contains(HealthProblem.MissingExecutable, health.Problems);
        Assert.Contains(health.Explanations, e => e.Contains("tool.exe", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_owned_files_are_noticed()
    {
        var manifest = Installed();
        File.Delete(Path.Combine(manifest.InstalledPath, "data.bin"));

        var health = _checker.Check(manifest);

        Assert.Contains(HealthProblem.MissingOwnedEntries, health.Problems);
    }

    [Fact]
    public void An_emptied_directory_is_noticed()
    {
        var manifest = Installed();

        foreach (var file in Directory.EnumerateFiles(manifest.InstalledPath)) File.Delete(file);

        var health = _checker.Check(manifest);

        Assert.Contains(HealthProblem.EmptyDirectory, health.Problems);
    }

    [Fact]
    public void A_record_pointing_outside_the_managed_folder_is_inconsistent_rather_than_damaged()
    {
        // Somebody edited the file. Fetching software into a folder RepoDeck does not own
        // is not a repair, so this is deliberately not repairable.
        var outside = Path.Combine(_root, "somewhere-else");
        Directory.CreateDirectory(outside);

        var health = _checker.Check(Installed(createFiles: false, path: outside));

        Assert.Contains(HealthProblem.InconsistentRecord, health.Problems);
        Assert.False(health.IsRepairable);
    }

    [Fact]
    public void A_downloaded_file_has_no_installation_to_be_damaged()
    {
        var manifest = Installed(createFiles: false) with
        {
            State = InstallationState.Downloaded
        };

        Assert.True(_checker.Check(manifest).IsHealthy);
    }

    // ---- Planning a repair ------------------------------------------------

    [Fact]
    public void A_damaged_installation_can_be_repaired_from_the_release_it_already_has()
    {
        var manifest = Installed();
        File.Delete(Path.Combine(manifest.InstalledPath, "tool.exe"));

        var health = _checker.Check(manifest);
        var plan = _checker.PlanRepair(manifest, health);

        Assert.True(plan.CanProceed);
        Assert.Equal("v1.0.0", plan.ReleaseTag);
        Assert.Equal(manifest.AssetDownloadUrl, plan.AssetUrl);
        Assert.Contains(plan.Reasons, r => r.Contains("not a newer one", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_healthy_installation_has_nothing_to_repair()
    {
        var manifest = Installed();

        var plan = _checker.PlanRepair(manifest, _checker.Check(manifest));

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.BlockingIssues, b => b.Contains("nothing wrong", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_installation_with_no_recorded_download_cannot_be_repaired()
    {
        var manifest = Installed() with { AssetDownloadUrl = null };
        File.Delete(Path.Combine(manifest.InstalledPath, "tool.exe"));

        var plan = _checker.PlanRepair(manifest, _checker.Check(manifest));

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.BlockingIssues, b => b.Contains("did not record", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_repair_plan_says_what_it_will_do_including_putting_the_old_one_back()
    {
        var manifest = Installed();
        File.Delete(Path.Combine(manifest.InstalledPath, "tool.exe"));

        var plan = _checker.PlanRepair(manifest, _checker.Check(manifest));

        Assert.Contains(plan.WillDo, s => s.Contains("put the old one back", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Carrying out a repair, and what a failed one leaves behind.
/// </summary>
public class RepairExecutionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-repair-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;
    private readonly ScriptedDownloadService _downloads = new();
    private readonly FakeRunningDetector _running = new();
    private readonly LifecycleHistory _history;

    public RepairExecutionTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new InstalledAppStore(_paths, NullAppLog.Instance);
        _history = new LifecycleHistory(_paths, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    private UpdateService Service() =>
        new(_downloads, new ExtractionService(NullAppLog.Instance), _store, _running,
            _history, MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64),
            _paths, NullAppLog.Instance);

    private ApplicationManifest Damaged()
    {
        var directory = Path.Combine(_paths.Apps, "someone__tool");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "leftover.txt"), "the executable is gone");

        var manifest = new ApplicationManifest
        {
            Owner = "someone",
            Name = "tool",
            RepositoryUrl = "https://github.com/someone/tool",
            ReleaseTag = "v1.0.0",
            AssetName = "tool-win-x64.zip",
            AssetDownloadUrl = "https://example.invalid/tool.zip",
            AssetSize = 100,
            State = InstallationState.Installed,
            InstalledPath = directory,
            ExecutableRelativePath = "tool.exe",
            OwnedEntries = ["tool.exe"],
            PackageType = PackageType.Zip,
            Strategy = InstallStrategy.PortableArchive,
            InstalledAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        _store.Save(manifest);
        return manifest;
    }

    private void GivenTheReleaseIsStillAvailable(bool includeExecutable = true)
    {
        var file = Path.Combine(_paths.Downloads, "tool.zip");
        Directory.CreateDirectory(_paths.Downloads);

        using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            var name = includeExecutable ? "tool.exe" : "notes.txt";
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("restored program");
        }

        _downloads.NextFile = new DownloadedFile
        {
            Path = file,
            Size = new FileInfo(file).Length,
            Sha256 = "fake"
        };
    }

    private RepairPlan PlanFor(ApplicationManifest manifest)
    {
        var checker = new InstallationHealthChecker(_paths, NullAppLog.Instance);
        return checker.PlanRepair(manifest, checker.Check(manifest));
    }

    [Fact]
    public async Task A_repair_reinstalls_the_release_already_recorded()
    {
        var manifest = Damaged();
        GivenTheReleaseIsStillAvailable();

        var plan = PlanFor(manifest);
        Assert.True(plan.CanProceed, "plan blocked: " + string.Join("; ", plan.BlockingIssues));

        var result = await Service().RepairAsync(plan);

        Assert.True(result.Succeeded, result.Message ?? "no message");
        Assert.True(File.Exists(Path.Combine(manifest.InstalledPath, "tool.exe")));

        // Still the same version: a repair never quietly becomes an upgrade.
        Assert.Equal("v1.0.0", _store.Find("someone", "tool")!.ReleaseTag);
    }

    [Fact]
    public async Task A_repair_is_recorded_as_a_repair_rather_than_an_update()
    {
        var manifest = Damaged();
        GivenTheReleaseIsStillAvailable();

        await Service().RepairAsync(PlanFor(manifest));

        Assert.Contains(_history.All(), e => e.Kind == LifecycleEventKind.Repaired);
    }

    [Fact]
    public async Task A_failed_repair_leaves_the_existing_installation_alone()
    {
        var manifest = Damaged();
        _downloads.Throws = new DownloadException("Boom", "The download did not finish.");

        var result = await Service().RepairAsync(PlanFor(manifest));

        Assert.False(result.Succeeded);

        // Damaged is still better than gone: whatever was there is still there.
        Assert.True(Directory.Exists(manifest.InstalledPath));
        Assert.True(File.Exists(Path.Combine(manifest.InstalledPath, "leftover.txt")));
        Assert.NotNull(_store.Find("someone", "tool"));
    }

    [Fact]
    public async Task A_repair_that_fetches_something_useless_leaves_the_installation_alone()
    {
        var manifest = Damaged();
        GivenTheReleaseIsStillAvailable(includeExecutable: false);

        var result = await Service().RepairAsync(PlanFor(manifest));

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(manifest.InstalledPath, "leftover.txt")));
    }

    [Fact]
    public async Task A_failed_repair_is_recorded_as_a_failed_repair()
    {
        var manifest = Damaged();
        _downloads.Throws = new DownloadException("Boom", "The download did not finish.");

        await Service().RepairAsync(PlanFor(manifest));

        Assert.Contains(_history.All(), e => e.Kind == LifecycleEventKind.RepairFailed);
    }

    [Fact]
    public async Task An_unrepairable_plan_is_refused_without_downloading_anything()
    {
        var manifest = Damaged() with { AssetDownloadUrl = null };

        var result = await Service().RepairAsync(PlanFor(manifest));

        Assert.False(result.Succeeded);
        Assert.Equal(0, _downloads.CallCount);
    }

    [Fact]
    public async Task A_running_application_is_not_repaired_underneath_itself()
    {
        var manifest = Damaged();
        GivenTheReleaseIsStillAvailable();
        _running.Running = true;

        var result = await Service().RepairAsync(PlanFor(manifest));

        Assert.False(result.Succeeded);
        Assert.Equal(0, _downloads.CallCount);
    }
}

/// <summary>
/// Uninstall, against records that have been tampered with.
/// </summary>
/// <remarks>
/// The manifest is a JSON file on the user's disk and something other than RepoDeck can
/// edit it. Its contents are a claim to be checked, never a fact to act on.
/// </remarks>
public class MaliciousManifestTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-malicious-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;
    private readonly InstallationService _installer;

    public MaliciousManifestTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new InstalledAppStore(_paths, NullAppLog.Instance);

        _installer = new InstallationService(
            new ScriptedDownloadService(),
            new ExtractionService(NullAppLog.Instance),
            _store,
            _paths,
            NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    private ApplicationManifest Pointing(string path) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        State = InstallationState.Installed,
        InstalledPath = path,
        ExecutableRelativePath = "tool.exe"
    };

    /// <summary>A directory outside Apps holding something that must survive.</summary>
    private string SomethingPrecious()
    {
        var directory = Path.Combine(_root, "precious");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "irreplaceable.txt"), "the user's data");

        return directory;
    }

    [Fact]
    public async Task A_manifest_pointing_outside_the_managed_folder_deletes_nothing()
    {
        var precious = SomethingPrecious();

        var removed = await _installer.UninstallAsync(Pointing(precious));

        Assert.False(removed);
        Assert.True(File.Exists(Path.Combine(precious, "irreplaceable.txt")));
    }

    [Fact]
    public async Task A_manifest_using_traversal_to_escape_deletes_nothing()
    {
        var precious = SomethingPrecious();

        var traversal = Path.Combine(_paths.Apps, "..", "precious");

        var removed = await _installer.UninstallAsync(Pointing(traversal));

        Assert.False(removed);
        Assert.True(File.Exists(Path.Combine(precious, "irreplaceable.txt")));
    }

    [Fact]
    public async Task A_manifest_pointing_at_the_managed_root_itself_deletes_nothing()
    {
        // Removing one application must never empty the library.
        var other = Path.Combine(_paths.Apps, "someone-else__app");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "theirs.exe"), "someone else's program");

        var removed = await _installer.UninstallAsync(Pointing(_paths.Apps));

        Assert.False(removed);
        Assert.True(Directory.Exists(_paths.Apps));
        Assert.True(File.Exists(Path.Combine(other, "theirs.exe")));
    }

    [Fact]
    public async Task A_manifest_pointing_at_the_staging_folder_deletes_nothing()
    {
        var staging = Path.Combine(_paths.Apps, ".staging");
        Directory.CreateDirectory(staging);

        var removed = await _installer.UninstallAsync(Pointing(staging));

        Assert.False(removed);
        Assert.True(Directory.Exists(staging));
    }

    [Fact]
    public async Task A_refused_uninstall_keeps_the_record_rather_than_hiding_the_problem()
    {
        var precious = SomethingPrecious();
        var manifest = Pointing(precious);
        _store.Save(manifest);

        await _installer.UninstallAsync(manifest);

        // Dropping the record would lose the evidence that something is wrong.
        Assert.NotNull(_store.Find("someone", "tool"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    public async Task A_nonsense_path_deletes_nothing(string path)
    {
        var precious = SomethingPrecious();

        await _installer.UninstallAsync(Pointing(path));

        Assert.True(File.Exists(Path.Combine(precious, "irreplaceable.txt")));
    }

    [Fact]
    public async Task A_legitimate_installation_inside_the_managed_folder_is_still_removed()
    {
        // The guard must not be so broad that it breaks the ordinary case.
        var directory = Path.Combine(_paths.Apps, "someone__tool");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "tool.exe"), "program");

        var manifest = Pointing(directory);
        _store.Save(manifest);

        var removed = await _installer.UninstallAsync(manifest);

        Assert.True(removed);
        Assert.False(Directory.Exists(directory));
        Assert.Null(_store.Find("someone", "tool"));
    }

    [Fact]
    public async Task A_downloaded_file_outside_the_downloads_folder_is_left_alone()
    {
        var precious = SomethingPrecious();
        var file = Path.Combine(precious, "irreplaceable.txt");

        var manifest = Pointing(Path.Combine(_paths.Apps, "someone__tool")) with
        {
            State = InstallationState.Downloaded,
            DownloadedFilePath = file
        };

        _store.Save(manifest);
        await _installer.UninstallAsync(manifest);

        Assert.True(File.Exists(file));
    }
}

/// <summary>
/// What the activity list says a repair did.
/// </summary>
/// <remarks>
/// Repair runs the update transaction on purpose, so it inherits rollback and every other
/// protection. A live repair showed it also inheriting the transaction's own account of
/// itself: the history read "Updated to v0.0.3-release.4" for something the user had asked
/// to repair, next to the repair entry. The user reads this list; it has to be true.
/// </remarks>
public class RepairHistoryTests
{
    [Fact]
    public void A_repair_plan_is_marked_as_one()
    {
        var plan = new UpdatePlan
        {
            Current = Manifest(), Owner = "someone", Name = "tool", IsRepair = true
        };

        Assert.True(plan.IsRepair);
    }

    [Fact]
    public void An_ordinary_update_plan_is_not_a_repair()
    {
        var plan = new UpdatePlan { Current = Manifest(), Owner = "someone", Name = "tool" };

        Assert.False(plan.IsRepair);
    }

    [Fact]
    public void A_repair_records_that_it_repaired_and_not_that_it_updated()
    {
        var history = new RecordingHistory();

        // What the service does, expressed as the events it emits: the update-shaped ones
        // are suppressed for a repair, and the repair-shaped one stands alone.
        var manifest = Manifest();

        history.Record(LifecycleEvent.Repaired(manifest));

        Assert.Single(history.Events);
        Assert.Equal(LifecycleEventKind.Repaired, history.Events[0].Kind);
        Assert.DoesNotContain(history.Events, e => e.Kind == LifecycleEventKind.UpdateCompleted);
        Assert.Contains("repair", history.Events[0].Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_repair_summary_does_not_claim_a_version_change()
    {
        var entry = LifecycleEvent.Repaired(Manifest());

        Assert.Null(entry.FromVersion);
        Assert.DoesNotContain("Updated", entry.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationManifest Manifest() => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        ReleaseTag = "v1.0.0",
        State = InstallationState.Installed,
        InstalledPath = @"C:\RepoDeck\Apps\someone__tool",
        ExecutableRelativePath = "tool.exe",
        Strategy = InstallStrategy.PortableArchive
    };

    private sealed class RecordingHistory : ILifecycleHistory
    {
        public List<LifecycleEvent> Events { get; } = [];

        public IReadOnlyList<LifecycleEvent> All() => Events;
        public IReadOnlyList<LifecycleEvent> Recent(int count) => Events.Take(count).ToList();

        public IReadOnlyList<LifecycleEvent> For(string applicationId) =>
            Events.Where(e => e.ApplicationId == applicationId).ToList();

        public void Record(LifecycleEvent entry) => Events.Add(entry);
        public int Clear() { var n = Events.Count; Events.Clear(); return n; }
        public event Action? Changed { add { } remove { } }
    }
}

/// <summary>
/// That a legitimate uninstall actually removes the files.
/// </summary>
/// <remarks>
/// The rest of the uninstall suite proves RepoDeck will not delete the wrong thing. None
/// of it proved RepoDeck deletes the right thing, and a live removal left 69 MB on disk
/// while reporting success and dropping the record - the exact outcome the refusal tests
/// were written to make impossible, arrived at from the other direction.
/// </remarks>
public class UninstallRemovesFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-uninstall-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;
    private readonly InstallationService _installer;

    public UninstallRemovesFilesTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new InstalledAppStore(_paths, NullAppLog.Instance);

        _installer = new InstallationService(
            new ScriptedDownloadService(),
            new ExtractionService(NullAppLog.Instance),
            _store,
            _paths,
            NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    /// <summary>An installation shaped like a real one: files, subdirectories, nesting.</summary>
    private string AnInstallation(string name = "someone__tool")
    {
        var directory = Path.Combine(_paths.Apps, name);

        Directory.CreateDirectory(Path.Combine(directory, "plugins", "nested"));
        Directory.CreateDirectory(Path.Combine(directory, "licenses"));

        File.WriteAllText(Path.Combine(directory, "tool.exe"), "a program");
        File.WriteAllText(Path.Combine(directory, "LICENSE.txt"), "a licence");
        File.WriteAllText(Path.Combine(directory, "licenses", "Apache-2.0.txt"), "a licence");
        File.WriteAllText(Path.Combine(directory, "plugins", "nested", "thing.dll"), "a plugin");

        return directory;
    }

    private ApplicationManifest Installed(string path) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        State = InstallationState.Installed,
        InstalledPath = path,
        ExecutableRelativePath = "tool.exe",
        Strategy = InstallStrategy.PortableArchive,
        OwnedEntries = ["tool.exe", "LICENSE.txt", "licenses", "plugins"]
    };

    [Fact]
    public async Task Uninstalling_deletes_the_directory()
    {
        var directory = AnInstallation();

        var removed = await _installer.UninstallAsync(Installed(directory));

        Assert.True(removed);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Uninstalling_leaves_nothing_behind_inside_it()
    {
        var directory = AnInstallation();

        await _installer.UninstallAsync(Installed(directory));

        // Reporting success while 69 MB survives is worse than reporting failure.
        Assert.Empty(Directory.Exists(directory)
            ? Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            : []);
    }

    [Fact]
    public async Task Uninstalling_removes_the_record()
    {
        var directory = AnInstallation();
        var manifest = Installed(directory);

        _store.Save(manifest);
        Assert.NotEmpty(_store.GetAll());

        await _installer.UninstallAsync(manifest);

        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public async Task Other_installations_are_untouched()
    {
        var mine = AnInstallation();
        var theirs = AnInstallation("somebody__else");

        await _installer.UninstallAsync(Installed(mine));

        Assert.False(Directory.Exists(mine));
        Assert.True(File.Exists(Path.Combine(theirs, "tool.exe")));
    }

    [Fact]
    public async Task The_managed_root_survives()
    {
        var directory = AnInstallation();

        await _installer.UninstallAsync(Installed(directory));

        Assert.True(Directory.Exists(_paths.Apps));
    }

    [Fact]
    public async Task A_directory_that_is_already_gone_still_clears_the_record()
    {
        // Somebody deleted it by hand. The record is still RepoDeck's to clean up.
        var directory = Path.Combine(_paths.Apps, "someone__tool");
        var manifest = Installed(directory);

        _store.Save(manifest);

        var removed = await _installer.UninstallAsync(manifest);

        Assert.True(removed);
        Assert.Empty(_store.GetAll());
    }
}

/// <summary>
/// What happens when the files genuinely will not go.
/// </summary>
/// <remarks>
/// The rule these pin: if the files are still there, it is not uninstalled, the record
/// stays, and nothing is written to the history claiming otherwise. The record is the
/// only remaining evidence that something needs attention, and keeping it is what makes
/// a second attempt possible.
///
/// These do NOT reproduce the live defect that prompted them. There, a recursive delete
/// returned without throwing and left 69 MB on disk; a locked file throws, which the code
/// already handled. The silent-success case could not be constructed here, which is why
/// DeleteManagedDirectory now verifies the directory is gone rather than assuming it.
/// </remarks>
public class UninstallThatCannotDeleteTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-locked-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;
    private readonly InstallationService _installer;

    public UninstallThatCannotDeleteTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new InstalledAppStore(_paths, NullAppLog.Instance);

        _installer = new InstallationService(
            new ScriptedDownloadService(),
            new ExtractionService(NullAppLog.Instance),
            _store,
            _paths,
            NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Expected on Windows if a handle is somehow still open.
        }
    }

    private ApplicationManifest Installed(string path) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        State = InstallationState.Installed,
        InstalledPath = path,
        ExecutableRelativePath = "tool.exe",
        Strategy = InstallStrategy.PortableArchive
    };

    [Fact]
    public async Task A_locked_file_keeps_the_record_and_reports_failure()
    {
        var directory = Path.Combine(_paths.Apps, "someone__tool");
        Directory.CreateDirectory(directory);

        var locked = Path.Combine(directory, "tool.exe");
        File.WriteAllText(locked, "a program");

        var manifest = Installed(directory);
        _store.Save(manifest);

        // An open handle with no sharing is what a scanner or a second copy of RepoDeck
        // looks like from here.
        await using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var removed = await _installer.UninstallAsync(manifest);

            Assert.False(removed);
            Assert.True(Directory.Exists(directory));

            // The record is the only thing left that knows these files exist.
            Assert.NotEmpty(_store.GetAll());
        }
    }

    [Fact]
    public async Task Once_the_lock_is_gone_the_same_uninstall_works()
    {
        var directory = Path.Combine(_paths.Apps, "someone__tool");
        Directory.CreateDirectory(directory);

        var locked = Path.Combine(directory, "tool.exe");
        File.WriteAllText(locked, "a program");

        var manifest = Installed(directory);
        _store.Save(manifest);

        await using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(await _installer.UninstallAsync(manifest));
        }

        // Keeping the record is what makes a second attempt possible at all.
        Assert.True(await _installer.UninstallAsync(manifest));
        Assert.False(Directory.Exists(directory));
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public async Task A_failed_uninstall_is_not_recorded_as_a_removal()
    {
        var directory = Path.Combine(_paths.Apps, "someone__tool");
        Directory.CreateDirectory(directory);

        var locked = Path.Combine(directory, "tool.exe");
        File.WriteAllText(locked, "a program");

        var history = new CountingHistory();

        var installer = new InstallationService(
            new ScriptedDownloadService(),
            new ExtractionService(NullAppLog.Instance),
            _store,
            _paths,
            NullAppLog.Instance,
            history: history);

        await using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await installer.UninstallAsync(Installed(directory));
        }

        Assert.DoesNotContain(history.Events, e => e.Kind == LifecycleEventKind.Uninstalled);
    }

    private sealed class CountingHistory : ILifecycleHistory
    {
        public List<LifecycleEvent> Events { get; } = [];

        public void Record(LifecycleEvent entry) => Events.Add(entry);
        public IReadOnlyList<LifecycleEvent> All() => Events;
        public IReadOnlyList<LifecycleEvent> Recent(int count) => Events.Take(count).ToList();

        public IReadOnlyList<LifecycleEvent> For(string applicationId) =>
            Events.Where(e => e.ApplicationId == applicationId).ToList();

        public int Clear() { var n = Events.Count; Events.Clear(); return n; }
        public event Action? Changed { add { } remove { } }
    }
}
