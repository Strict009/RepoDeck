using System.Text.Json;
using System.Text.Json.Serialization;
using RepoDeck.Infrastructure;

namespace RepoDeck.Services.Preferences;

/// <summary>How results are laid out on the Discover page.</summary>
public enum ResultsViewMode
{
    /// <summary>Picture-led cards. The default, and what a newcomer should meet first.</summary>
    Card,

    /// <summary>One dense row per project, for someone scanning a long list.</summary>
    Compact
}

/// <summary>
/// Which results a search shows.
/// </summary>
/// <remarks>
/// Apps prioritises projects RepoDeck has evidence are usable programs. Everything is raw
/// GitHub discovery, unreordered beyond what GitHub itself returned.
///
/// Uncertain results are never hidden unless Apps was explicitly chosen: "RepoDeck could
/// not tell what this is" is not the same as "this is not for you", and a mode the user
/// did not pick must not quietly decide it is.
/// </remarks>
public enum BrowseMode
{
    /// <summary>Programs first. The default, for someone looking for software to use.</summary>
    Apps,

    /// <summary>Everything GitHub returned, in GitHub's own order.</summary>
    Everything
}

/// <summary>The small set of choices RepoDeck remembers between runs.</summary>
/// <remarks>
/// Deliberately tiny. This is for interface preferences only: nothing here affects what
/// RepoDeck will install, download or run, so a corrupt or missing file can safely fall
/// back to the defaults without asking anybody anything.
/// </remarks>
public sealed record PreferencesSnapshot
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResultsViewMode ResultsView { get; init; } = ResultsViewMode.Card;

    /// <summary>Whether the Quick Look panel opens when a result is selected.</summary>
    public bool QuickLookEnabled { get; init; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BrowseMode BrowseMode { get; init; } = BrowseMode.Apps;

    /// <summary>
    /// Whether the welcome has been shown. Losing this means seeing a welcome screen
    /// again, which is why it is safe to keep here rather than anywhere more careful.
    /// </summary>
    public bool HasSeenWelcome { get; init; }
}

public interface IUserPreferences
{
    PreferencesSnapshot Current { get; }

    /// <summary>Stores a change. Failure to write is logged and otherwise ignored.</summary>
    void Update(Func<PreferencesSnapshot, PreferencesSnapshot> change);
}

/// <summary>
/// Preferences as a single small JSON file under the Data folder.
/// </summary>
/// <remarks>
/// Written to a temporary file and moved into place, so an interrupted save cannot leave
/// a truncated file behind. An unreadable file is replaced by the defaults rather than
/// treated as an error: losing a view preference is not worth a dialog.
/// </remarks>
public sealed class UserPreferences : IUserPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly IAppLog _log;
    private readonly Lock _gate = new();

    private PreferencesSnapshot? _cache;

    public UserPreferences(AppPaths paths, IAppLog log)
    {
        _path = Path.Combine(paths.Data, "preferences.json");
        _log = log;
    }

    public PreferencesSnapshot Current
    {
        get
        {
            lock (_gate) return _cache ??= Load();
        }
    }

    public void Update(Func<PreferencesSnapshot, PreferencesSnapshot> change)
    {
        lock (_gate)
        {
            var updated = change(_cache ??= Load());
            if (updated == _cache) return;

            _cache = updated;
            Persist(updated);
        }
    }

    private PreferencesSnapshot Load()
    {
        try
        {
            if (!File.Exists(_path)) return new PreferencesSnapshot();

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<PreferencesSnapshot>(json, JsonOptions)
                   ?? new PreferencesSnapshot();
        }
        catch (Exception ex)
        {
            _log.Warn("Preferences", "Could not read preferences, using defaults: " + ex.Message);
            return new PreferencesSnapshot();
        }
    }

    private void Persist(PreferencesSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            // A preference that failed to save is a nuisance, not a failure worth
            // interrupting anyone over.
            _log.Warn("Preferences", "Could not save preferences: " + ex.Message);
        }
    }
}
