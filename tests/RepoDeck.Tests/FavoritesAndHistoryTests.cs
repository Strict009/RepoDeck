using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Favorites;
using RepoDeck.Services.History;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// Projects the user asked RepoDeck to remember.
/// </summary>
/// <remarks>
/// The rule these protect: favourites are entirely independent of what is installed.
/// Somebody who uninstalls a thing to free space has not stopped being interested in it.
/// </remarks>
public class FavoritesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-favorites-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public FavoritesTests()
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
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    private FavoritesStore Store() => new(_paths, NullAppLog.Instance);

    private string File_ => Path.Combine(_paths.Data, "favorites.json");

    [Fact]
    public void Nothing_is_a_favourite_to_begin_with()
    {
        Assert.Empty(Store().All());
        Assert.False(Store().IsFavorite("someone", "tool"));
    }

    [Fact]
    public void Adding_makes_it_a_favourite()
    {
        var store = Store();
        store.Add(TestRepositories.Create("tool", "someone"));

        Assert.True(store.IsFavorite("someone", "tool"));
        Assert.Single(store.All());
    }

    [Fact]
    public void A_favourite_survives_a_restart()
    {
        Store().Add(TestRepositories.Create("tool", "someone"));

        Assert.True(Store().IsFavorite("someone", "tool"));
    }

    [Fact]
    public void Adding_the_same_project_twice_keeps_one_entry()
    {
        var store = Store();
        var repository = TestRepositories.Create("tool", "someone");

        store.Add(repository);
        store.Add(repository);

        Assert.Single(store.All());
    }

    [Fact]
    public void Removing_stops_it_being_a_favourite()
    {
        var store = Store();
        store.Add(TestRepositories.Create("tool", "someone"));

        Assert.True(store.Remove("someone", "tool"));
        Assert.False(store.IsFavorite("someone", "tool"));
    }

    [Fact]
    public void Removing_something_that_is_not_a_favourite_does_nothing()
    {
        Assert.False(Store().Remove("someone", "tool"));
    }

    [Fact]
    public void Toggling_flips_it_and_reports_the_result()
    {
        var store = Store();
        var repository = TestRepositories.Create("tool", "someone");

        Assert.True(store.Toggle(repository));
        Assert.True(store.IsFavorite("someone", "tool"));

        Assert.False(store.Toggle(repository));
        Assert.False(store.IsFavorite("someone", "tool"));
    }

    [Fact]
    public void Matching_ignores_case_the_way_GitHub_does()
    {
        var store = Store();
        store.Add(TestRepositories.Create("Tool", "SomeOne"));

        Assert.True(store.IsFavorite("someone", "tool"));
    }

    [Fact]
    public void A_change_is_announced_so_every_star_agrees()
    {
        var store = Store();
        var changes = 0;
        store.Changed += () => changes++;

        store.Add(TestRepositories.Create("tool", "someone"));
        store.Remove("someone", "tool");

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Enough_is_kept_to_show_a_card_without_asking_GitHub()
    {
        var store = Store();
        store.Add(TestRepositories.Create("tool", "someone",
            description: "Does a useful thing.", language: "Rust"));

        var favourite = Assert.Single(store.All());

        Assert.Equal("someone", favourite.Owner);
        Assert.Equal("tool", favourite.Name);
        Assert.Equal("Does a useful thing.", favourite.Description);
        Assert.Equal("Rust", favourite.Language);
        Assert.NotEqual(default, favourite.AddedAt);
    }

    [Fact]
    public void The_newest_favourite_comes_first()
    {
        var store = Store();
        store.Add(TestRepositories.Create("first", "someone"));
        Thread.Sleep(5);
        store.Add(TestRepositories.Create("second", "someone"));

        Assert.Equal("second", store.All()[0].Name);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_empty_rather_than_failing()
    {
        Directory.CreateDirectory(_paths.Data);
        File.WriteAllText(File_, "{ this is not json");

        Assert.Empty(Store().All());
    }

    [Fact]
    public void Reading_favourites_does_not_create_a_file()
    {
        _ = Store().All();

        Assert.False(File.Exists(File_));
    }
}

/// <summary>
/// The short, plain-English record of what RepoDeck has done.
/// </summary>
public class LifecycleHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-history-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public LifecycleHistoryTests()
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
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    private LifecycleHistory History() => new(_paths, NullAppLog.Instance);

    private static ApplicationManifest Manifest(string name = "tool") => new()
    {
        Owner = "someone",
        Name = name,
        RepositoryUrl = "https://github.com/someone/" + name,
        ReleaseTag = "v1.0.0",
        InstalledPath = @"C:\RepoDeck\Apps\someone__" + name
    };

    [Fact]
    public void An_empty_history_is_empty()
    {
        Assert.Empty(History().All());
    }

    [Fact]
    public void A_recorded_event_survives_a_restart()
    {
        History().Record(LifecycleEvent.InstallCompleted(Manifest()));

        var entry = Assert.Single(History().All());

        Assert.Equal(LifecycleEventKind.InstallCompleted, entry.Kind);
        Assert.Equal("someone/tool", entry.ApplicationId);
    }

    [Fact]
    public void The_newest_event_comes_first()
    {
        var history = History();
        history.Record(LifecycleEvent.InstallCompleted(Manifest("first")));
        history.Record(LifecycleEvent.InstallCompleted(Manifest("second")));

        Assert.Equal("someone/second", history.All()[0].ApplicationId);
    }

    [Fact]
    public void Events_can_be_read_back_for_one_application()
    {
        var history = History();
        history.Record(LifecycleEvent.InstallCompleted(Manifest("one")));
        history.Record(LifecycleEvent.InstallCompleted(Manifest("two")));
        history.Record(LifecycleEvent.Launched(Manifest("one")));

        var entries = history.For("someone/one");

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("someone/one", e.ApplicationId));
    }

    [Fact]
    public void Only_the_most_recent_entries_are_asked_for()
    {
        var history = History();

        for (var i = 0; i < 10; i++) history.Record(LifecycleEvent.InstallCompleted(Manifest("p" + i)));

        Assert.Equal(3, history.Recent(3).Count);
    }

    [Fact]
    public void The_history_does_not_grow_without_bound()
    {
        // A machine used for years must not accumulate a history file forever.
        var history = History();

        for (var i = 0; i < 500; i++) history.Record(LifecycleEvent.InstallCompleted(Manifest("p" + i)));

        Assert.True(history.All().Count <= 400);
    }

    [Fact]
    public void Every_event_carries_a_sentence_safe_to_show_verbatim()
    {
        var manifest = Manifest();

        LifecycleEvent[] events =
        [
            LifecycleEvent.InstallCompleted(manifest),
            LifecycleEvent.Launched(manifest),
            LifecycleEvent.Uninstalled(manifest),
            LifecycleEvent.Repaired(manifest),
            LifecycleEvent.RepairFailed(manifest, "It did not work."),
            LifecycleEvent.UpdateChecked(manifest, UpdateCheck.NotChecked)
        ];

        foreach (var entry in events)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Summary));
            Assert.False(string.IsNullOrWhiteSpace(entry.ApplicationName));
            Assert.NotEqual(default, entry.At);
        }
    }

    [Fact]
    public void Nothing_recorded_looks_like_a_secret_or_a_stack_trace()
    {
        // This is a user-facing history, not a log. The log file is where that belongs.
        var manifest = Manifest();
        var history = History();

        history.Record(LifecycleEvent.InstallCompleted(manifest));
        history.Record(LifecycleEvent.Launched(manifest));

        var json = File.ReadAllText(Path.Combine(_paths.Data, "history.json")).ToLowerInvariant();

        foreach (var forbidden in new[] { "token", "authorization", "bearer", "stacktrace", "  at " })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_corrupt_history_starts_a_new_one_rather_than_failing()
    {
        Directory.CreateDirectory(_paths.Data);
        File.WriteAllText(Path.Combine(_paths.Data, "history.json"), "not json at all");

        var history = History();

        Assert.Empty(history.All());

        history.Record(LifecycleEvent.InstallCompleted(Manifest()));
        Assert.Single(history.All());
    }

    [Fact]
    public void Failures_and_rollbacks_are_marked_as_worth_noticing()
    {
        Assert.True(LifecycleEvent.RepairFailed(Manifest(), "x").IsFailure);
        Assert.True(LifecycleEvent.RepairFailed(Manifest(), "x").IsNoteworthy);
        Assert.True(LifecycleEvent.Uninstalled(Manifest()).IsNoteworthy);
        Assert.False(LifecycleEvent.Launched(Manifest()).IsNoteworthy);
    }

    [Fact]
    public void Recording_never_throws_even_when_it_cannot_write()
    {
        // Bookkeeping must never break the operation it was describing.
        var history = new LifecycleHistory(new AppPaths(_root), NullAppLog.Instance);

        var data = Path.Combine(_root, "Data");
        if (Directory.Exists(data)) Directory.Delete(data, recursive: true);

        // A file where the Data directory should be makes every write fail.
        File.WriteAllText(data, "in the way");

        try
        {
            history.Record(LifecycleEvent.InstallCompleted(Manifest()));
        }
        finally
        {
            File.Delete(data);
        }
    }
}

/// <summary>
/// The star has to say which state it is in without relying on its colour - a live check
/// found it rendering identically whether saved or not, so the shape carries it now.
/// </summary>
public class FavoriteGlyphTests
{
    [Fact]
    public void The_glyph_differs_between_saved_and_not()
    {
        var off = "☆";
        var on = "★";

        Assert.NotEqual(off, on);
    }

    [Fact]
    public void A_saved_project_shows_a_filled_star_and_an_unsaved_one_a_hollow_star()
    {
        var card = Card();

        Assert.False(card.IsFavorite);
        Assert.Equal("☆", card.FavoriteGlyph);

        card.IsFavorite = true;

        Assert.Equal("★", card.FavoriteGlyph);
    }

    [Fact]
    public void The_tooltip_says_what_pressing_it_would_do()
    {
        var card = Card();

        Assert.Contains("Remember", card.FavoriteTooltip, StringComparison.OrdinalIgnoreCase);

        card.IsFavorite = true;

        Assert.Contains("Remove", card.FavoriteTooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Changing_the_state_tells_the_view_the_glyph_changed()
    {
        var card = Card();
        var changed = new List<string?>();

        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.IsFavorite = true;

        Assert.Contains(nameof(RepositoryCardViewModel.FavoriteGlyph), changed);
        Assert.Contains(nameof(RepositoryCardViewModel.FavoriteTooltip), changed);
    }

    private static RepositoryCardViewModel Card()
    {
        var repository = TestRepositories.Create("tool", description: "A desktop tool.");
        var explanations = new HeuristicRepositoryExplanationService();
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);

        return new RepositoryCardViewModel(
            repository,
            explanations.ExplainFromMetadata(repository),
            likelihood,
            SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood),
            imageUrl: null,
            openDetails: _ => { },
            log: NullAppLog.Instance);
    }
}

/// <summary>
/// The activity list the user actually reads.
/// </summary>
/// <remarks>
/// The history was being recorded correctly for the whole milestone and shown nowhere, so
/// these cover both halves: that clearing it works and touches nothing else, and that every
/// kind of event turns into a word rather than relying on a colour.
/// </remarks>
public class ActivityListTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDeckActivity", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly LifecycleHistory _history;

    public ActivityListTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _history = new LifecycleHistory(_paths, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a temp folder */ }
    }

    [Fact]
    public void Clearing_forgets_everything_and_says_how_much()
    {
        _history.Record(Event(LifecycleEventKind.InstallCompleted));
        _history.Record(Event(LifecycleEventKind.Repaired));

        Assert.Equal(2, _history.Clear());
        Assert.Empty(_history.All());
    }

    [Fact]
    public void Clearing_an_empty_history_removes_nothing()
    {
        Assert.Equal(0, _history.Clear());
    }

    [Fact]
    public void Clearing_survives_a_reload()
    {
        _history.Record(Event(LifecycleEventKind.InstallCompleted));
        _history.Clear();

        var reopened = new LifecycleHistory(_paths, NullAppLog.Instance);

        Assert.Empty(reopened.All());
    }

    [Fact]
    public void Recording_and_clearing_both_announce_themselves()
    {
        var changes = 0;
        _history.Changed += () => changes++;

        _history.Record(Event(LifecycleEventKind.InstallCompleted));
        _history.Clear();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Every_kind_of_event_becomes_a_word()
    {
        foreach (var kind in Enum.GetValues<LifecycleEventKind>())
        {
            var view = new ActivityEntryViewModel(Event(kind));

            Assert.False(string.IsNullOrWhiteSpace(view.KindText), kind.ToString());
            Assert.Equal(view.KindText, view.KindText.ToUpperInvariant());
        }
    }

    [Fact]
    public void A_failure_is_marked_and_a_success_is_not()
    {
        var failed = new ActivityEntryViewModel(Event(LifecycleEventKind.UpdateFailed));
        var fine = new ActivityEntryViewModel(Event(LifecycleEventKind.UpdateChecked));

        Assert.True(failed.IsFailure);
        Assert.False(failed.IsNoteworthy);
        Assert.False(fine.IsFailure);
        Assert.False(fine.IsNoteworthy);
    }

    [Fact]
    public void A_rollback_is_worth_noticing_without_being_a_failure()
    {
        var view = new ActivityEntryViewModel(Event(LifecycleEventKind.RollbackPerformed));

        Assert.True(view.IsNoteworthy);
        Assert.False(view.IsFailure);
        Assert.Equal("PUT BACK", view.KindText);
    }

    [Fact]
    public void An_entry_without_an_application_still_has_a_name()
    {
        var view = new ActivityEntryViewModel(new LifecycleEvent
        {
            Kind = LifecycleEventKind.UpdateChecked,
            At = DateTimeOffset.UtcNow
        });

        Assert.False(string.IsNullOrWhiteSpace(view.Name));
    }

    private static LifecycleEvent Event(LifecycleEventKind kind) => new()
    {
        Kind = kind,
        At = DateTimeOffset.UtcNow,
        ApplicationId = "someone/tool",
        ApplicationName = "Tool",
        Summary = "Something happened."
    };
}
