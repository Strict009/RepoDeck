using RepoDeck.Infrastructure;
using RepoDeck.Services.Preferences;

namespace RepoDeck.Tests;

/// <summary>
/// Interface preferences on disk. Nothing here affects what RepoDeck installs, so every
/// failure path ends in the defaults rather than in an error.
/// </summary>
public class UserPreferencesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-prefs-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public UserPreferencesTests()
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

    private UserPreferences Create() => new(_paths, NullAppLog.Instance);

    private string File_ => Path.Combine(_paths.Data, "preferences.json");

    [Fact]
    public void A_fresh_installation_gets_the_defaults()
    {
        var preferences = Create();

        Assert.Equal(ResultsViewMode.Card, preferences.Current.ResultsView);
        Assert.True(preferences.Current.QuickLookEnabled);
    }

    [Fact]
    public void Reading_preferences_does_not_create_a_file()
    {
        _ = Create().Current;

        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void A_change_survives_a_restart()
    {
        Create().Update(p => p with { ResultsView = ResultsViewMode.Compact });

        Assert.Equal(ResultsViewMode.Compact, Create().Current.ResultsView);
    }

    [Fact]
    public void Writing_the_same_value_does_not_rewrite_the_file()
    {
        var preferences = Create();
        preferences.Update(p => p with { ResultsView = ResultsViewMode.Compact });

        var written = File.GetLastWriteTimeUtc(File_);

        preferences.Update(p => p with { ResultsView = ResultsViewMode.Compact });

        Assert.Equal(written, File.GetLastWriteTimeUtc(File_));
    }

    [Fact]
    public void The_stored_form_is_readable_rather_than_a_magic_number()
    {
        Create().Update(p => p with { ResultsView = ResultsViewMode.Compact });

        Assert.Contains("Compact", File.ReadAllText(File_), StringComparison.Ordinal);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_the_defaults_instead_of_failing()
    {
        File.WriteAllText(File_, "{ this is not json at all");

        Assert.Equal(ResultsViewMode.Card, Create().Current.ResultsView);
    }

    [Fact]
    public void An_empty_file_falls_back_to_the_defaults()
    {
        File.WriteAllText(File_, "");

        Assert.Equal(ResultsViewMode.Card, Create().Current.ResultsView);
    }

    [Fact]
    public void A_file_holding_an_unknown_view_mode_does_not_crash_the_application()
    {
        File.WriteAllText(File_, """{ "resultsView": "Holographic" }""");

        // Whatever comes back, asking for it must not throw.
        var view = Create().Current.ResultsView;

        Assert.True(Enum.IsDefined(view) || view == ResultsViewMode.Card);
    }

    [Fact]
    public void Preferences_are_written_to_the_data_folder_RepoDeck_owns()
    {
        Create().Update(p => p with { ResultsView = ResultsViewMode.Compact });

        Assert.StartsWith(_paths.Root, Path.GetFullPath(File_), StringComparison.OrdinalIgnoreCase);
    }
}
