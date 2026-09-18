using System.Text.Json;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Favorites;

/// <summary>One project the user asked RepoDeck to remember.</summary>
/// <remarks>
/// Enough to show a card without contacting GitHub, and no more. A favourite is a bookmark
/// rather than a copy of the project: the description will go stale, and that is fine,
/// because the point is to find the thing again rather than to know its current state.
/// </remarks>
public sealed record FavoriteEntry
{
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string RepositoryUrl { get; init; }

    public long RepositoryId { get; init; }
    public string? Description { get; init; }
    public string? Language { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    public string Id => Owner + "/" + Name;
}

public interface IFavoritesStore
{
    IReadOnlyList<FavoriteEntry> All();

    bool IsFavorite(string owner, string name);

    /// <summary>Adds, or does nothing if it is already there. Returns the resulting state.</summary>
    bool Add(GitHubRepository repository);

    bool Remove(string owner, string name);

    /// <summary>Flips it. Returns true when the project is now a favourite.</summary>
    bool Toggle(GitHubRepository repository);

    /// <summary>Raised whenever the set changes, so every surface showing a star agrees.</summary>
    event Action? Changed;
}

/// <summary>
/// Projects the user asked RepoDeck to remember, kept as a JSON file under Data.
/// </summary>
/// <remarks>
/// Entirely independent of what is installed. A favourite may be installed, uninstalled,
/// or never installed at all, and removing an application does not remove the bookmark -
/// somebody who uninstalls a thing to free space has not stopped being interested in it.
///
/// Written to a temporary file and moved into place, so an interrupted save cannot leave a
/// truncated list. An unreadable file falls back to empty rather than stopping RepoDeck:
/// losing a bookmark list is a nuisance, not a reason to refuse to start.
/// </remarks>
public sealed class FavoritesStore : IFavoritesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly IAppLog _log;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    private List<FavoriteEntry>? _cache;

    public FavoritesStore(AppPaths paths, IAppLog log, TimeProvider? timeProvider = null)
    {
        _path = Path.Combine(paths.Data, "favorites.json");
        _log = log;
        _time = timeProvider ?? TimeProvider.System;
    }

    public event Action? Changed;

    public IReadOnlyList<FavoriteEntry> All()
    {
        lock (_gate) return Load().OrderByDescending(f => f.AddedAt).ToList();
    }

    public bool IsFavorite(string owner, string name)
    {
        lock (_gate) return Load().Any(f => Matches(f, owner, name));
    }

    public bool Add(GitHubRepository repository)
    {
        lock (_gate)
        {
            var all = Load();

            if (all.Any(f => Matches(f, repository.OwnerLogin, repository.Name))) return true;

            all.Add(new FavoriteEntry
            {
                Owner = repository.OwnerLogin,
                Name = repository.Name,
                RepositoryUrl = repository.HtmlUrl,
                RepositoryId = repository.Id,
                Description = repository.Description,
                Language = repository.Language,
                AddedAt = _time.GetUtcNow()
            });

            _cache = all;
            Persist(all);
        }

        Changed?.Invoke();
        return true;
    }

    public bool Remove(string owner, string name)
    {
        bool removed;

        lock (_gate)
        {
            var all = Load();
            removed = all.RemoveAll(f => Matches(f, owner, name)) > 0;

            if (removed)
            {
                _cache = all;
                Persist(all);
            }
        }

        if (removed) Changed?.Invoke();
        return removed;
    }

    public bool Toggle(GitHubRepository repository)
    {
        if (IsFavorite(repository.OwnerLogin, repository.Name))
        {
            Remove(repository.OwnerLogin, repository.Name);
            return false;
        }

        Add(repository);
        return true;
    }

    private static bool Matches(FavoriteEntry entry, string owner, string name) =>
        string.Equals(entry.Owner, owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase);

    private List<FavoriteEntry> Load()
    {
        if (_cache is not null) return _cache;

        try
        {
            if (!File.Exists(_path)) return _cache = [];

            var json = File.ReadAllText(_path);
            return _cache = JsonSerializer.Deserialize<List<FavoriteEntry>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _log.Warn("Favorites", "Could not read favourites, starting empty: " + ex.Message);
            return _cache = [];
        }
    }

    private void Persist(List<FavoriteEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.Warn("Favorites", "Could not save favourites: " + ex.Message);
        }
    }
}
