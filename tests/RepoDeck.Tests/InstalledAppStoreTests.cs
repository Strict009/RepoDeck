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
}
