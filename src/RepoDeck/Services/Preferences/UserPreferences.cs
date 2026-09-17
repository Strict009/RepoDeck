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
