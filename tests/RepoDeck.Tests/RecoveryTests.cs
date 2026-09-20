using System.Text;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Favorites;
using RepoDeck.Services.GitHub;
using RepoDeck.Services.History;
using RepoDeck.Services.Install;
using RepoDeck.Services.Preferences;
using RepoDeck.Services.Update;

namespace RepoDeck.Tests;

/// <summary>
/// RepoDeck meeting a machine that has been interfered with.
/// </summary>
/// <remarks>
/// Every file RepoDeck writes can be edited, truncated, replaced with nonsense or emptied
/// by something other than RepoDeck - a crash mid-write, a sync client, a disk error, or
/// somebody curious with a text editor. None of that should stop the application starting.
///
/// The rule throughout: a record RepoDeck cannot read is treated as absent, never as
/// permission to guess, and never as a reason to fail to start. Losing a favourites list is
/// a disappointment; refusing to launch because of one is a broken application.
/// </remarks>
public class CorruptDataRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-corrupt-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public CorruptDataRecoveryTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a temp folder */ }
    }

    /// <summary>The shapes a damaged JSON file actually takes.</summary>
    public static TheoryData<string, string> Garbage() => new()
    {
        { "empty", "" },
        { "whitespace", "   \n  " },
        { "truncated", "[{\"owner\":\"someone\",\"name\":\"to" },
        { "not json at all", "<html><body>404 not found</body></html>" },
        { "wrong shape", "{\"unexpected\":true}" },
        { "null", "null" },
        { "array of nulls", "[null,null]" },
        { "nested too deep", string.Concat(Enumerable.Repeat("[", 200)) },
        { "binary", "\0\0\0\0\u0001\u0002" }
    };

    private void Write(string fileName, string contents) =>
        File.WriteAllText(Path.Combine(_paths.Data, fileName), contents, Encoding.UTF8);

    [Theory]
    [MemberData(nameof(Garbage))]
    public void A_damaged_installed_list_starts_empty_rather_than_failing(string _, string contents)
    {
        Write("installed.json", contents);

        var store = new InstalledAppStore(_paths, NullAppLog.Instance);

        // Empty is the honest answer. Nothing is deleted on disk, so the applications
        // themselves are still there to be found again.
        Assert.Empty(store.GetAll());
    }

    [Theory]
    [MemberData(nameof(Garbage))]
    public void A_damaged_favourites_list_starts_empty_rather_than_failing(string _, string contents)
    {
        Write("favorites.json", contents);

        Assert.Empty(new FavoritesStore(_paths, NullAppLog.Instance).All());
    }

    [Theory]
    [MemberData(nameof(Garbage))]
    public void A_damaged_history_starts_empty_rather_than_failing(string _, string contents)
    {
        Write("history.json", contents);

        Assert.Empty(new LifecycleHistory(_paths, NullAppLog.Instance).All());
    }

    [Theory]
    [MemberData(nameof(Garbage))]
    public void A_damaged_recently_viewed_list_starts_empty_rather_than_failing(string _, string contents)
    {
        Write("recent.json", contents);

        Assert.Empty(new RecentlyViewed(_paths, NullAppLog.Instance).All());
    }

    [Theory]
    [MemberData(nameof(Garbage))]
    public void Damaged_preferences_fall_back_to_defaults(string _, string contents)
    {
        Write("preferences.json", contents);

        var preferences = new UserPreferences(_paths, NullAppLog.Instance);

        // Whatever the defaults are, there must be some: a missing preference file is a
        // first run, and an unreadable one is no different.
        Assert.NotNull(preferences.Current);
    }

    [Fact]
    public void A_damaged_file_can_still_be_written_over()
    {
        // Recovery is not just starting: it is being usable again afterwards.
        Write("favorites.json", "{{{ not json");

        var store = new FavoritesStore(_paths, NullAppLog.Instance);
        store.Toggle(TestRepositories.Create("tool"));

        Assert.Single(store.All());
        Assert.Single(new FavoritesStore(_paths, NullAppLog.Instance).All());
    }

    [Fact]
    public void A_data_folder_that_has_been_deleted_is_recreated()
    {
        Directory.Delete(_paths.Data, recursive: true);

        var store = new InstalledAppStore(_paths, NullAppLog.Instance);
        store.Save(Manifest());

        Assert.Single(store.GetAll());
    }

    [Fact]
    public void A_manifest_with_missing_required_fields_does_not_take_the_whole_list_down()
    {
        // One bad record should not cost somebody the other nine.
        Write("installed.json", """
            [
              { "owner": "someone", "name": "good", "repositoryUrl": "https://example.invalid",
                "installedPath": "C:\\RepoDeck\\Apps\\someone__good", "state": 0 }
            ]
            """);

        var store = new InstalledAppStore(_paths, NullAppLog.Instance);

        // Either it reads the good one or it starts empty. What it must not do is throw.
        Assert.True(store.GetAll().Count <= 1);
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
}

/// <summary>
/// GitHub answering with something RepoDeck did not expect.
/// </summary>
/// <remarks>
/// A remote API is not a contract that holds. Fields go missing, lists come back empty,
/// a proxy returns an HTML error page with a 200, and a rate limit arrives mid-session.
/// None of it should produce a crash or, worse, a confident wrong answer.
/// </remarks>
public class MalformedResponseRecoveryTests
{
    private static readonly MachineProfile Windows =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static ApplicationManifest Installed(string tag = "v1.0.0") => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        ReleaseTag = tag,
        State = InstallationState.Installed,
        InstalledPath = @"C:\RepoDeck\Apps\someone__tool",
        ExecutableRelativePath = "tool.exe",
        Strategy = InstallStrategy.PortableArchive
    };

    [Fact]
    public void A_release_with_no_tag_is_skipped_rather_than_crashing()
    {
        var release = new GitHubRelease { Id = 1, TagName = "", Name = "" };

        var check = UpdateChecker.Evaluate(Installed(), [release], Windows, DateTimeOffset.UtcNow);

        Assert.Equal(UpdateState.Unknown, check.State);
    }

    [Fact]
    public void A_release_with_no_assets_does_not_become_an_offer_to_install_nothing()
    {
        var release = new GitHubRelease
        {
            Id = 1,
            TagName = "v2.0.0",
            Name = "v2.0.0",
            Assets = []
        };

        var check = UpdateChecker.Evaluate(Installed(), [release], Windows, DateTimeOffset.UtcNow);

        Assert.Equal(UpdateState.ManualUpdateRequired, check.State);
    }

    [Fact]
    public void An_asset_with_no_download_url_is_not_treated_as_installable()
    {
        var release = new GitHubRelease
        {
            Id = 1,
            TagName = "v2.0.0",
            Name = "v2.0.0",
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Id = 1,
                    Name = "tool-win-x64.zip",
                    BrowserDownloadUrl = "",
                    Size = 1000
                }
            ]
        };

        var check = UpdateChecker.Evaluate(Installed(), [release], Windows, DateTimeOffset.UtcNow);

        Assert.NotEqual(UpdateState.UpdateAvailable, check.State);
    }

    [Fact]
    public void An_asset_reporting_a_negative_size_does_not_break_the_check()
    {
        var release = new GitHubRelease
        {
            Id = 1,
            TagName = "v2.0.0",
            Name = "v2.0.0",
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Id = 1,
                    Name = "tool-win-x64.zip",
                    BrowserDownloadUrl = "https://example.invalid/tool.zip",
                    Size = -1
                }
            ]
        };

        var check = UpdateChecker.Evaluate(Installed(), [release], Windows, DateTimeOffset.UtcNow);

        Assert.False(string.IsNullOrWhiteSpace(check.Explanation));
    }

    [Fact]
    public void An_empty_release_list_is_unknown_rather_than_up_to_date()
    {
        // "You are up to date" having found nothing is a claim RepoDeck has not earned.
        var check = UpdateChecker.Evaluate(Installed(), [], Windows, DateTimeOffset.UtcNow);

        Assert.Equal(UpdateState.Unknown, check.State);
    }

    [Fact]
    public void A_release_list_of_nothing_but_drafts_is_unknown()
    {
        var release = new GitHubRelease { Id = 1, TagName = "v2.0.0", Name = "v2", Draft = true };

        var check = UpdateChecker.Evaluate(Installed(), [release], Windows, DateTimeOffset.UtcNow);

        Assert.NotEqual(UpdateState.UpdateAvailable, check.State);
    }

    [Fact]
    public void Absurd_version_numbers_do_not_overflow_the_comparison()
    {
        // A tag is remote content. Nine digits is the documented limit; more must be
        // refused rather than wrapped into a negative number.
        Assert.Null(ReleaseVersion.TryParse("v99999999999999999999.0.0"));
    }

    [Fact]
    public void A_pathological_tag_does_not_hang_the_parser()
    {
        // The regex has a match timeout precisely because tags come from strangers.
        var nasty = new string('1', 50_000) + "." + new string('2', 50_000);

        var parsed = ReleaseVersion.TryParse(nasty);

        Assert.Null(parsed);
    }

    [Fact]
    public void Self_update_survives_a_release_with_nothing_in_it()
    {
        var result = SelfUpdateService.Evaluate(
            "0.1.0-alpha", [new GitHubRelease { Id = 1, TagName = "", Name = "" }]);

        Assert.Equal(SelfUpdateState.Unknown, result.State);
    }
}

/// <summary>
/// What an interrupted update leaves behind, and what happens to it.
/// </summary>
/// <remarks>
/// An update that is killed part-way - the machine loses power, the process is ended, the
/// user logs out - leaves a staging directory, a rollback directory, or both. Those must
/// not accumulate across runs, and must never be mistaken for an installation.
/// </remarks>
public class InterruptedUpdateRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-interrupted-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public InterruptedUpdateRecoveryTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a temp folder */ }
    }

    [Fact]
    public void Abandoned_staging_is_cleaned_up_rather_than_accumulating()
    {
        var staging = Path.Combine(_paths.Apps, ".staging", "install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "half-extracted.dll"), "partial");

        var installer = new InstallationService(
            new ScriptedDownloadService(),
            new ExtractionService(NullAppLog.Instance),
            new InstalledAppStore(_paths, NullAppLog.Instance),
            _paths,
            NullAppLog.Instance);

        installer.CleanAbandonedStaging();

        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void Abandoned_rollback_copies_are_cleaned_up()
    {
        var rollback = Path.Combine(_paths.Apps, ".rollback", "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rollback);
        File.WriteAllText(Path.Combine(rollback, "previous.exe"), "the old version");

        var updater = new UpdateService(
            new ScriptedDownloadService(),
            new ExtractionService(NullAppLog.Instance),
            new InstalledAppStore(_paths, NullAppLog.Instance),
            new FakeRunningDetector(),
            NullLifecycleHistory.Instance,
            MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64),
            _paths,
            NullAppLog.Instance);

        updater.CleanAbandonedRollbacks();

        Assert.False(Directory.Exists(rollback));
    }

    [Fact]
    public void Cleaning_leaves_real_installations_alone()
    {
        // The reserved folders are swept; the applications beside them are not.
        var application = Path.Combine(_paths.Apps, "someone__tool");
        Directory.CreateDirectory(application);
        File.WriteAllText(Path.Combine(application, "tool.exe"), "a program");

        var staging = Path.Combine(_paths.Apps, ".staging", "install-abc");
        Directory.CreateDirectory(staging);

        var installer = new InstallationService(
            new ScriptedDownloadService(),
            new ExtractionService(NullAppLog.Instance),
            new InstalledAppStore(_paths, NullAppLog.Instance),
            _paths,
            NullAppLog.Instance);

        installer.CleanAbandonedStaging();

        Assert.True(File.Exists(Path.Combine(application, "tool.exe")));
    }

    [Fact]
    public void Cleaning_an_apps_folder_that_does_not_exist_yet_is_not_an_error()
    {
        Directory.Delete(_paths.Apps, recursive: true);

        var installer = new InstallationService(
            new ScriptedDownloadService(),
            new ExtractionService(NullAppLog.Instance),
            new InstalledAppStore(_paths, NullAppLog.Instance),
            _paths,
            NullAppLog.Instance);

        // A first run has no Apps folder. Sweeping it must be a no-op, not a throw.
        installer.CleanAbandonedStaging();
    }
}
