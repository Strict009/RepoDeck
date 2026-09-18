using System.Text.Json;
using System.Text.Json.Serialization;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.History;

/// <summary>One project somebody looked at.</summary>
public sealed record RecentProject
{
    public required string Owner { get; init; }
    public required string Name { get; init; }

    public string FullName => $"{Owner}/{Name}";

    /// <summary>Enough to draw a card without asking GitHub again.</summary>
    public string? Description { get; init; }

    public DateTimeOffset ViewedAt { get; init; }
}

public interface IRecentlyViewed
{
    /// <summary>Most recently looked at first.</summary>
    IReadOnlyList<RecentProject> All();

    /// <summary>Notes that somebody opened this project. Never throws.</summary>
    void Record(GitHubRepository repository);

    /// <summary>Forgets everything. Returns how many entries went.</summary>
    int Clear();

    event Action? Changed;
}

/// <summary>
/// The last few projects somebody opened, so Discover can offer them the way back.
/// </summary>
/// <remarks>
/// Deliberately small and deliberately local. This is a convenience - "what was that thing
/// I was looking at" - not a profile. It records an owner, a name, a one-line description
/// and a timestamp: enough to draw a card and nothing more. No accounts, no synchronisation,
/// nothing leaves the machine, and the whole thing can be cleared.
///
/// Capped hard. A list that grew without bound would eventually be a record of everything
/// somebody had ever been curious about, which is more than a "recently viewed" shelf has
/// any business keeping.
/// </remarks>
public sealed class RecentlyViewed : IRecentlyViewed
{
    /// <summary>Enough to fill a shelf, few enough that it stays a shelf.</summary>
    public const int MaxEntries = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly IAppLog _log;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    private List<RecentProject>? _cache;

    public RecentlyViewed(AppPaths paths, IAppLog log, TimeProvider? timeProvider = null)
    {
        _path = Path.Combine(paths.Data, "recent.json");
        _log = log;
        _time = timeProvider ?? TimeProvider.System;
    }

    public event Action? Changed;

    public IReadOnlyList<RecentProject> All()
    {
        lock (_gate) return Load().ToList();
    }

    public void Record(GitHubRepository repository)
    {
        if (string.IsNullOrWhiteSpace(repository.OwnerLogin) || string.IsNullOrWhiteSpace(repository.Name))
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                var all = Load();

                // Looking at something twice moves it to the front rather than listing it
                // twice. A shelf of the same project six times is not a history.
                all.RemoveAll(r =>
                    string.Equals(r.Owner, repository.OwnerLogin, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Name, repository.Name, StringComparison.OrdinalIgnoreCase));

                all.Insert(0, new RecentProject
                {
                    Owner = repository.OwnerLogin,
                    Name = repository.Name,
                    Description = Trim(repository.Description),
                    ViewedAt = _time.GetUtcNow()
                });

                if (all.Count > MaxEntries) all.RemoveRange(MaxEntries, all.Count - MaxEntries);

                _cache = all;
                Persist(all);
            }

            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // Failing to remember what somebody looked at must never interrupt them
            // looking at it.
            _log.Warn("Recent", "Could not record a viewed project: " + ex.Message);
        }
    }

    public int Clear()
    {
        int removed;

        try
        {
            lock (_gate)
            {
                removed = Load().Count;
                _cache = [];
                Persist([]);
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Recent", "Could not clear the recently viewed list: " + ex.Message);
            return 0;
        }

        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    /// <summary>
    /// A description is a one-line reminder here, not the project's full pitch. Remote text
    /// of unknown length has no business being stored unbounded.
    /// </summary>
    private static string? Trim(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var text = description.Trim();
        return text.Length <= 200 ? text : text[..200];
    }

    private List<RecentProject> Load()
    {
        if (_cache is not null) return _cache;

        try
        {
            if (!File.Exists(_path)) return _cache = [];

            var json = File.ReadAllText(_path);
            var entries = JsonSerializer.Deserialize<List<RecentProject>>(json, JsonOptions);

            return _cache = entries ?? [];
        }
        catch (Exception ex)
        {
            _log.Warn("Recent", "Could not read the recently viewed list, starting a new one: " + ex.Message);
            return _cache = [];
        }
    }

    private void Persist(List<RecentProject> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        // Written aside and moved into place, so an interrupted write cannot leave a
        // half-file that fails to parse next time.
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }
}

/// <summary>A list that remembers nothing, for services built without one.</summary>
public sealed class NullRecentlyViewed : IRecentlyViewed
{
    public static NullRecentlyViewed Instance { get; } = new();

    public IReadOnlyList<RecentProject> All() => [];
    public void Record(GitHubRepository repository) { }
    public int Clear() => 0;

    public event Action? Changed
    {
        add { }
        remove { }
    }
}
