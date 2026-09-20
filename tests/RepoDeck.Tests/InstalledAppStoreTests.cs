using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

public sealed class InstalledAppStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDeckStoreTests", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public InstalledAppStoreTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
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

    private InstalledAppStore NewStore() => new(_paths, NullAppLog.Instance);

    private ApplicationManifest Manifest(string owner = "someone", string name = "tool") => new()
    {
        Owner = owner,
        Name = name,
        RepositoryUrl = $"https://github.com/{owner}/{name}",
        InstalledPath = Path.Combine(_paths.Apps, $"{owner}__{name}"),
        ExecutableRelativePath = "tool.exe",
        ReleaseTag = "v1.0.0",
        InstalledAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void A_saved_manifest_can_be_found_again()
    {
        var store = NewStore();
        store.Save(Manifest());

        var found = store.Find("someone", "tool");

        Assert.NotNull(found);
        Assert.Equal("v1.0.0", found!.ReleaseTag);
        Assert.True(store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public void Manifests_survive_a_restart()
    {
        NewStore().Save(Manifest());

        // A completely fresh store, as if the application had been closed and reopened.
        var reloaded = NewStore();

        Assert.True(reloaded.IsInstalled("someone", "tool"));
    }

    [Fact]
    public void Saving_the_same_application_twice_replaces_rather_than_duplicates()
    {
        var store = NewStore();
        store.Save(Manifest());
        store.Save(Manifest() with { ReleaseTag = "v2.0.0" });

        Assert.Single(store.GetAll());
        Assert.Equal("v2.0.0", store.Find("someone", "tool")!.ReleaseTag);
    }

    [Fact]
    public void Lookups_ignore_case_the_way_GitHub_does()
    {
        var store = NewStore();
        store.Save(Manifest("SomeOne", "Tool"));

        Assert.True(store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public void Removing_an_application_forgets_it()
    {
        var store = NewStore();
        store.Save(Manifest());

        Assert.True(store.Remove("someone", "tool"));
        Assert.False(store.IsInstalled("someone", "tool"));
        Assert.False(store.Remove("someone", "tool"));
    }

    [Fact]
    public void Several_applications_coexist()
    {
        var store = NewStore();
        store.Save(Manifest("a", "one"));
        store.Save(Manifest("b", "two"));

        Assert.Equal(2, store.GetAll().Count);
    }

    [Fact]
    public void A_corrupt_library_file_is_set_aside_rather_than_stopping_RepoDeck()
    {
        File.WriteAllText(Path.Combine(_paths.Data, "installed.json"), "{ this is not valid json");

        var store = NewStore();

        // RepoDeck starts with an empty library instead of refusing to open.
        Assert.Empty(store.GetAll());

        // And the unreadable file is kept for inspection.
        Assert.NotEmpty(Directory.GetFiles(_paths.Data, "installed.json.broken-*"));
    }

    [Fact]
    public void An_absent_library_file_simply_means_nothing_is_installed()
    {
        Assert.Empty(NewStore().GetAll());
    }

    [Fact]
    public void The_stored_file_contains_no_credentials()
    {
        var store = NewStore();
        store.Save(Manifest());

        var json = File.ReadAllText(Path.Combine(_paths.Data, "installed.json"));

        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Telling the rest of the application ------------------------------
    //
    // The status strip counts what is installed. It used to count once, at startup, and
    // never again - so a lifecycle rehearsal ended with an empty library and a strip
    // still insisting "1 installed". Every change goes through this class, so this is
    // where the signal has to come from.

    [Fact]
    public void Saving_announces_that_the_library_changed()
    {
        var store = NewStore();
        var announcements = 0;
        store.Changed += () => announcements++;

        store.Save(Manifest());

        Assert.Equal(1, announcements);
    }

    [Fact]
    public void Removing_announces_that_the_library_changed()
    {
        var store = NewStore();
        store.Save(Manifest());

        var announcements = 0;
        store.Changed += () => announcements++;

        Assert.True(store.Remove("someone", "tool"));
        Assert.Equal(1, announcements);
    }

    [Fact]
    public void Removing_something_that_was_never_there_announces_nothing()
    {
        var store = NewStore();
        var announcements = 0;
        store.Changed += () => announcements++;

        Assert.False(store.Remove("nobody", "nothing"));
        Assert.Equal(0, announcements);
    }

    [Fact]
    public void A_listener_reads_the_library_as_it_is_after_the_change()
    {
        // A count taken before the write landed would be exactly the bug this fixes.
        var store = NewStore();
        var counted = -1;
        store.Changed += () => counted = store.GetAll().Count;

        store.Save(Manifest());
        Assert.Equal(1, counted);

        store.Remove("someone", "tool");
        Assert.Equal(0, counted);
    }

    [Fact]
    public void A_listener_that_throws_does_not_turn_a_good_save_into_a_failure()
    {
        // The library is on disk by this point. Whatever the listener does about it, the
        // caller asked to record an installation and the installation is recorded.
        var store = NewStore();
        store.Changed += () => throw new InvalidOperationException("the UI fell over");

        var exception = Record.Exception(() => store.Save(Manifest()));

        Assert.Null(exception);
        Assert.True(store.IsInstalled("someone", "tool"));
    }

    [Fact]
    public void Nothing_is_announced_when_the_library_could_not_be_written()
    {
        // Announcing a change that did not happen sends every listener off to re-read a
        // file that still says the old thing.
        var store = NewStore();
        var announced = false;
        store.Changed += () => announced = true;

        // A directory where the file needs to go: the move into place cannot succeed.
        Directory.CreateDirectory(Path.Combine(_paths.Data, "installed.json"));

        Assert.ThrowsAny<Exception>(() => store.Save(Manifest()));
        Assert.False(announced);
    }
}
