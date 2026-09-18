using System.IO.Compression;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.History;
using RepoDeck.Services.Install;
using RepoDeck.Services.Update;

namespace RepoDeck.Tests;

/// <summary>
/// The transactional update: a working installation must survive every failure.
/// </summary>
/// <remarks>
/// These are the tests that matter most in this milestone. An update replaces something
/// that was working, so the question is never "does the happy path work" but "what is the
/// user left with when it does not".
/// </remarks>
public class UpdateTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-update-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;
    private readonly ScriptedDownloadService _downloads = new();
    private readonly FakeRunningDetector _running = new();
    private readonly LifecycleHistory _history;

    public UpdateTransactionTests()
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
            // A temp directory that outlives the test is not worth failing over.
        }
    }

    private UpdateService Service(IExtractionService? extraction = null) =>
        new(_downloads,
            extraction ?? new ExtractionService(NullAppLog.Instance),
            _store,
            _running,
            _history,
            MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64),
            _paths,
            NullAppLog.Instance);

    // ---- Fixtures ---------------------------------------------------------

    /// <summary>Puts a working installation on disk and records it, as a real install would.</summary>
    private ApplicationManifest GivenInstalled(string version = "v1.0.0")
    {
        var directory = Path.Combine(_paths.Apps, "someone__tool");
        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "tool.exe"), "the old program " + version);
        File.WriteAllText(Path.Combine(directory, "readme.txt"), "old");

        var manifest = new ApplicationManifest
        {
            Owner = "someone",
            Name = "tool",
            RepositoryUrl = "https://github.com/someone/tool",
            ReleaseTag = version,
            AssetName = "tool-" + version + "-win-x64.zip",
            AssetDownloadUrl = "https://example.invalid/old.zip",
            AssetSize = 100,
            State = InstallationState.Installed,
            InstalledPath = directory,
            ExecutableRelativePath = "tool.exe",
            OwnedEntries = ["readme.txt", "tool.exe"],
            Platform = OsPlatform.Windows,
            Architecture = CpuArchitecture.X64,
            PackageType = PackageType.Zip,
            Strategy = InstallStrategy.PortableArchive,
            InstalledAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        _store.Save(manifest);
        return manifest;
    }

    /// <summary>An archive the download service will hand back, containing a newer program.</summary>
    private string GivenNewRelease(string version = "v1.1.0", bool includeExecutable = true)
    {
        var file = Path.Combine(_paths.Downloads, "tool-" + version + ".zip");
        Directory.CreateDirectory(_paths.Downloads);

        using var archive = ZipFile.Open(file, ZipArchiveMode.Create);

        if (includeExecutable)
        {
            var entry = archive.CreateEntry("tool.exe");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("the new program " + version);
        }
        else
        {
            var entry = archive.CreateEntry("notes.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("nothing runnable here");
        }

        _downloads.NextFile = new DownloadedFile
        {
            Path = file,
            Size = new FileInfo(file).Length,
            Sha256 = "fake"
        };

        return file;
    }

    private UpdatePlan PlanFor(ApplicationManifest current, string targetVersion = "v1.1.0") => new()
    {
        Current = current,
        Owner = current.Owner,
        Name = current.Name,
        InstalledVersionText = current.DisplayVersion,
        TargetReleaseTag = targetVersion,
        TargetReleaseName = targetVersion,
        TargetVersionText = targetVersion,
        AssetName = "tool-" + targetVersion + "-win-x64.zip",
        AssetUrl = "https://example.invalid/new.zip",
        AssetSize = 200,
        Platform = OsPlatform.Windows,
        Architecture = CpuArchitecture.X64,
        PackageType = PackageType.Zip,
        Strategy = UpdateStrategy.ReplaceManagedInstallation,
        InstallStrategy = InstallStrategy.PortableArchive,
        RequiresApplicationClosed = true,
        ProposedInstallDirectory = current.InstalledPath,
        ExecutableCandidates = ["tool.exe"],
        Confidence = Confidence.Likely
    };

    private string LiveExecutable(ApplicationManifest manifest) =>
        Path.Combine(manifest.InstalledPath, "tool.exe");

    private static string Contents(string path) => File.ReadAllText(path);

    // ---- The happy path ---------------------------------------------------

    [Fact]
    public async Task A_successful_update_replaces_the_files_and_the_record()
    {
        var installed = GivenInstalled();
        GivenNewRelease();

        var result = await Service().UpdateAsync(PlanFor(installed));

        Assert.True(result.Succeeded);
        Assert.Contains("the new program", Contents(LiveExecutable(installed)));

        var saved = _store.Find("someone", "tool")!;
        Assert.Equal("v1.1.0", saved.ReleaseTag);
        Assert.NotNull(saved.UpdatedAt);
    }

    [Fact]
    public async Task A_successful_update_keeps_when_it_was_first_installed()
    {
        // "Installed three months ago, updated on Tuesday" is two different facts.
        var installed = GivenInstalled();
        GivenNewRelease();

        await Service().UpdateAsync(PlanFor(installed));

        var saved = _store.Find("someone", "tool")!;
        Assert.Equal(installed.InstalledAt, saved.InstalledAt);
    }

    [Fact]
    public async Task A_successful_update_leaves_no_rollback_copy_behind()
    {
        var installed = GivenInstalled();
        GivenNewRelease();

        await Service().UpdateAsync(PlanFor(installed));

        var rollbacks = Path.Combine(_paths.Apps, ".rollback");

        Assert.True(!Directory.Exists(rollbacks) || !Directory.EnumerateDirectories(rollbacks).Any());
    }

    [Fact]
    public async Task A_successful_update_leaves_no_staging_behind()
    {
        var installed = GivenInstalled();
        GivenNewRelease();

        await Service().UpdateAsync(PlanFor(installed));

        var staging = Path.Combine(_paths.Apps, ".staging");

        Assert.True(!Directory.Exists(staging) || !Directory.EnumerateDirectories(staging).Any());
    }

    // ---- Failures before anything is touched -----------------------------

    [Fact]
    public async Task A_failed_download_leaves_the_working_copy_exactly_as_it_was()
    {
        var installed = GivenInstalled();
        _downloads.Throws = new DownloadException("Boom", "The download did not finish.");

        var result = await Service().UpdateAsync(PlanFor(installed));

        Assert.False(result.Succeeded);
        Assert.True(result.PreviousInstallationRestored);
        Assert.Contains("the old program", Contents(LiveExecutable(installed)));
        Assert.Equal("v1.0.0", _store.Find("someone", "tool")!.ReleaseTag);
    }

    [Fact]
    public async Task A_cancelled_download_leaves_the_working_copy_alone()
    {
        var installed = GivenInstalled();
        _downloads.Throws = new OperationCanceledException();

        var result = await Service().UpdateAsync(PlanFor(installed));

        Assert.True(result.WasCancelled);
        Assert.Contains("the old program", Contents(LiveExecutable(installed)));
    }

    [Fact]
    public async Task A_corrupt_archive_leaves_the_working_copy_alone()
    {
        var installed = GivenInstalled();

        var file = Path.Combine(_paths.Downloads, "corrupt.zip");
        Directory.CreateDirectory(_paths.Downloads);
        File.WriteAllBytes(file, [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF, 0xFF]);

        _downloads.NextFile = new DownloadedFile { Path = file, Size = 8, Sha256 = "fake" };

        var result = await Service().UpdateAsync(PlanFor(installed));

        Assert.False(result.Succeeded);
        Assert.True(result.PreviousInstallationRestored);
        Assert.Contains("the old program", Contents(LiveExecutable(installed)));
    }

    [Fact]
    public async Task A_new_release_with_nothing_runnable_never_replaces_a_working_one()
    {
        // The whole point of checking the staged copy before touching the live one.
        var installed = GivenInstalled();
        GivenNewRelease(includeExecutable: false);

        var result = await Service().UpdateAsync(PlanFor(installed));

        Assert.False(result.Succeeded);
        Assert.True(result.PreviousInstallationRestored);
        Assert.Contains("the old program", Contents(LiveExecutable(installed)));
        Assert.Contains("left alone", result.Message!);
    }

    [Fact]
    public async Task A_zip_slip_entry_cannot_escape_during_an_update()
    {
        var installed = GivenInstalled();

        var file = Path.Combine(_paths.Downloads, "slip.zip");
        Directory.CreateDirectory(_paths.Downloads);

        using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            var escape = archive.CreateEntry("../../escaped.txt");
            using (var writer = new StreamWriter(escape.Open())) writer.Write("should never land");

            var good = archive.CreateEntry("tool.exe");
            using (var writer = new StreamWriter(good.Open())) writer.Write("the new program");
        }

        _downloads.NextFile = new DownloadedFile
        {
            Path = file,
            Size = new FileInfo(file).Length,
            Sha256 = "fake"
        };

        await Service().UpdateAsync(PlanFor(installed));

        Assert.False(File.Exists(Path.Combine(_paths.Root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(_paths.Apps, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_paths.Root)!, "escaped.txt")));
    }

    // ---- Failures after the swap begins ----------------------------------

    [Fact]
    public async Task A_failure_during_replacement_restores_the_previous_installation()
    {
        var installed = GivenInstalled();
        GivenNewRelease();

        // Fails after extraction has produced a usable staged copy, so the failure lands
        // in the window where the live installation has been moved aside.
        var service = Service(new ThrowAfterExtractionService(_paths));

        var result = await service.UpdateAsync(PlanFor(installed));

        Assert.False(result.Succeeded);
        Assert.True(result.PreviousInstallationRestored);
        Assert.False(result.InstallationDamaged);

        Assert.True(File.Exists(LiveExecutable(installed)));
        Assert.Contains("the old program", Contents(LiveExecutable(installed)));
    }

    [Fact]
    public async Task A_failed_update_leaves_the_previous_manifest_in_place()
    {
        var installed = GivenInstalled();
        GivenNewRelease();

        await Service(new ThrowAfterExtractionService(_paths)).UpdateAsync(PlanFor(installed));

        var saved = _store.Find("someone", "tool")!;

        Assert.Equal("v1.0.0", saved.ReleaseTag);
        Assert.Equal(installed.AssetDownloadUrl, saved.AssetDownloadUrl);
        Assert.Null(saved.UpdatedAt);
    }

    [Fact]
    public async Task A_rollback_is_recorded_in_the_history()
    {
        var installed = GivenInstalled();
        GivenNewRelease();

        await Service(new ThrowAfterExtractionService(_paths)).UpdateAsync(PlanFor(installed));

        Assert.Contains(_history.All(), e => e.Kind == LifecycleEventKind.RollbackPerformed);
    }

    [Fact]
    public async Task A_failure_after_the_backup_is_taken_really_restores_the_old_files()
    {
        // The previous test's failure could in principle happen before the live directory
        // was moved aside, in which case nothing needed restoring. This one holds a handle
        // open inside the staged copy, so the backup is definitely taken first and the
        // promotion is definitely the step that fails - which is the only window where the
        // restore path does any work.
        var installed = GivenInstalled();
        GivenNewRelease();

        var extraction = new LockStagedFileService();
        var result = await Service(extraction).UpdateAsync(PlanFor(installed));

        Assert.True(extraction.Locked, "the staged copy was never locked, so this proves nothing");

        Assert.False(result.Succeeded);
        Assert.True(result.PreviousInstallationRestored);
        Assert.False(result.InstallationDamaged);

        // The old program is back where it was, with its contents intact.
        Assert.True(File.Exists(LiveExecutable(installed)));
        Assert.Contains("the old program", Contents(LiveExecutable(installed)));
        Assert.True(File.Exists(Path.Combine(installed.InstalledPath, "readme.txt")));

        extraction.Release();
    }

    [Fact]
    public async Task A_restored_installation_is_still_runnable_afterwards()
    {
        var installed = GivenInstalled();
        GivenNewRelease();

        var extraction = new LockStagedFileService();
        await Service(extraction).UpdateAsync(PlanFor(installed));
        extraction.Release();

        // Everything the Installed page needs to offer Run is still true.
        var saved = _store.Find("someone", "tool")!;

        Assert.True(saved.IsRunnableInstallation);
        Assert.True(File.Exists(saved.ExecutablePath!));
        Assert.Equal("v1.0.0", saved.ReleaseTag);
    }

    // ---- The running application -----------------------------------------

    [Fact]
    public async Task A_running_application_is_not_updated_and_nothing_is_downloaded()
    {
        var installed = GivenInstalled();
        _running.Running = true;

        var result = await Service().UpdateAsync(PlanFor(installed));

        Assert.True(result.ApplicationWasRunning);
        Assert.False(result.Succeeded);
        Assert.Equal(0, _downloads.CallCount);
        Assert.Contains("Close", result.Message!);
    }

    [Fact]
    public async Task The_message_names_the_application_so_the_user_knows_what_to_close()
    {
        var installed = GivenInstalled();
        _running.Running = true;

        var result = await Service().UpdateAsync(PlanFor(installed));

        Assert.Contains("Tool", result.Message!);
    }

    [Fact]
    public async Task Closing_the_application_and_retrying_works()
    {
        var installed = GivenInstalled();
        _running.Running = true;

        var service = Service();
        var first = await service.UpdateAsync(PlanFor(installed));
        Assert.True(first.ApplicationWasRunning);

        // The user closes it and presses Retry.
        _running.Running = false;
        GivenNewRelease();

        var second = await service.UpdateAsync(PlanFor(installed));

        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task An_application_started_during_the_download_is_not_replaced_underneath()
    {
        var installed = GivenInstalled();
        GivenNewRelease();

        // It was closed when the update began and open by the time the files were ready.
        _downloads.OnDownload = () => _running.Running = true;

        var result = await Service().UpdateAsync(PlanFor(installed));

        Assert.True(result.ApplicationWasRunning);
        Assert.Contains("the old program", Contents(LiveExecutable(installed)));
    }

    // ---- Refusals ---------------------------------------------------------

    [Fact]
    public async Task A_plan_that_cannot_proceed_is_refused_without_touching_anything()
    {
        var installed = GivenInstalled();

        var plan = PlanFor(installed) with
        {
            BlockingIssues = ["RepoDeck will not do this."]
        };

        var result = await Service().UpdateAsync(plan);

        Assert.False(result.Succeeded);
        Assert.Equal(0, _downloads.CallCount);
        Assert.Contains("the old program", Contents(LiveExecutable(installed)));
    }

    [Fact]
    public async Task An_installation_recorded_outside_the_managed_folder_is_refused()
    {
        var installed = GivenInstalled();
        GivenNewRelease();

        var outside = Path.Combine(_root, "not-apps");
        Directory.CreateDirectory(outside);

        var plan = PlanFor(installed) with { ProposedInstallDirectory = outside };

        var result = await Service().UpdateAsync(plan);

        Assert.False(result.Succeeded);
        Assert.True(Directory.Exists(outside));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    // ---- Startup cleanup --------------------------------------------------

    [Fact]
    public void Abandoned_rollback_folders_are_cleared_at_startup()
    {
        var rollbacks = Path.Combine(_paths.Apps, ".rollback");
        Directory.CreateDirectory(Path.Combine(rollbacks, "someone__tool-abc123"));
        File.WriteAllText(Path.Combine(rollbacks, "someone__tool-abc123", "leftover.txt"), "x");

        var removed = Service().CleanAbandonedRollbacks();

        Assert.Equal(1, removed);
        Assert.Empty(Directory.EnumerateDirectories(rollbacks));
    }

    [Fact]
    public void Cleaning_rollbacks_when_there_are_none_does_nothing()
    {
        Assert.Equal(0, Service().CleanAbandonedRollbacks());
    }
}

// ---- Doubles ---------------------------------------------------------------

/// <summary>Hands back a file the test prepared, or throws what the test chose.</summary>
internal sealed class ScriptedDownloadService : IDownloadService
{
    public DownloadedFile? NextFile { get; set; }
    public Exception? Throws { get; set; }
    public int CallCount { get; private set; }

    /// <summary>Runs at the moment the download happens, for testing what changes meanwhile.</summary>
    public Action? OnDownload { get; set; }

    public Task<DownloadedFile> DownloadAsync(
        InstallPlan plan,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        OnDownload?.Invoke();

        if (Throws is not null) throw Throws;

        return Task.FromResult(NextFile
            ?? throw new InvalidOperationException("The test did not supply a download."));
    }
}

internal sealed class FakeRunningDetector : IRunningApplicationDetector
{
    public bool Running { get; set; }

    public bool IsRunning(ApplicationManifest manifest) => Running;
}

/// <summary>
/// Extracts normally, then throws - so the failure lands after a usable staged copy
/// exists, which is the only window in which a rollback is needed.
/// </summary>
internal sealed class ThrowAfterExtractionService : IExtractionService
{
    private readonly AppPaths _paths;
    private readonly ExtractionService _real;

    public ThrowAfterExtractionService(AppPaths paths)
    {
        _paths = paths;
        _real = new ExtractionService(NullAppLog.Instance);
    }

    public async Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string destination,
        PackageType packageType,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _real.ExtractAsync(
            archivePath, destination, packageType, progress, cancellationToken);

        // Sabotage the live directory so the move fails the way a locked file would.
        var live = Path.Combine(_paths.Apps, "someone__tool");
        if (Directory.Exists(live)) SabotageHandle = File.OpenRead(Path.Combine(live, "tool.exe"));

        return result;
    }

    /// <summary>Held open so the directory move fails, as a running program would cause.</summary>
    public FileStream? SabotageHandle { get; private set; }
}

/// <summary>
/// Extracts normally, then holds a file inside the staged copy open, so the promotion move
/// fails after the live installation has already been put aside.
/// </summary>
internal sealed class LockStagedFileService : IExtractionService
{
    private readonly ExtractionService _real = new(NullAppLog.Instance);
    private FileStream? _handle;

    public bool Locked => _handle is not null;

    public async Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string destination,
        PackageType packageType,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _real.ExtractAsync(
            archivePath, destination, packageType, progress, cancellationToken);

        var staged = Path.Combine(destination, "tool.exe");

        if (File.Exists(staged))
        {
            _handle = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        return result;
    }

    public void Release()
    {
        _handle?.Dispose();
        _handle = null;
    }
}
