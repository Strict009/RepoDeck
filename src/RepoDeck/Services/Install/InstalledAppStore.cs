using System.Text.Json;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

/// <summary>
/// Stores application manifests as a single JSON file under the Data folder.
/// </summary>
/// <remarks>
/// Written to a temporary file and moved into place, so an interrupted save cannot
/// leave a truncated library behind. A library file that has become unreadable is set
/// aside rather than deleted, and RepoDeck carries on with an empty list instead of
/// refusing to start.
/// </remarks>
public sealed class InstalledAppStore : IInstalledAppStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly IAppLog _log;
    private readonly Lock _gate = new();
    private List<ApplicationManifest>? _cache;

    public InstalledAppStore(AppPaths paths, IAppLog log)
    {
        _path = Path.Combine(paths.Data, "installed.json");
        _log = log;
    }

    public IReadOnlyList<ApplicationManifest> GetAll()
    {
        lock (_gate)
        {
            return Load().ToList();
        }
    }

    public ApplicationManifest? Find(string owner, string name)
    {
        lock (_gate)
        {
            return Load().FirstOrDefault(m => Matches(m, owner, name));
        }
    }

    public bool IsInstalled(string owner, string name) => Find(owner, name) is not null;

    public event Action? Changed;

    public void Save(ApplicationManifest manifest)
    {
        lock (_gate)
        {
            var all = Load();
            all.RemoveAll(m => Matches(m, manifest.Owner, manifest.Name));
            all.Add(manifest);
            Persist(all);
        }

        Announce();
    }

    public bool Remove(string owner, string name)
    {
        bool removed;

        lock (_gate)
        {
            var all = Load();
            removed = all.RemoveAll(m => Matches(m, owner, name)) > 0;
            if (removed) Persist(all);
        }

        if (removed) Announce();

        return removed;
    }

    /// <summary>
    /// Tells whoever is listening, outside the lock and after the write succeeded.
    /// </summary>
    /// <remarks>
    /// Outside the lock because a handler will call straight back in to read the library,
    /// and holding the gate across somebody else's work is how a counter becomes a hang.
    /// After the write because <see cref="Persist"/> throws on a full or read-only disk,
    /// and announcing a change that did not happen is worse than announcing nothing.
    /// </remarks>
    private void Announce()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // A listener that throws must not turn a successful save into a failed one.
            _log.Error("Installed", "A listener failed while handling a library change.", ex);
        }
    }

    private static bool Matches(ApplicationManifest manifest, string owner, string name) =>
        string.Equals(manifest.Owner, owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(manifest.Name, name, StringComparison.OrdinalIgnoreCase);

    private List<ApplicationManifest> Load()
    {
        if (_cache is not null) return _cache;

        if (!File.Exists(_path))
        {
            _cache = [];
            return _cache;
        }

        try
        {
            var json = File.ReadAllText(_path);
            // A file that parses but contains nulls - "[null,null]" is valid JSON -
            // would otherwise hand out entries that crash whoever touches them next.
            _cache = JsonSerializer.Deserialize<List<ApplicationManifest>>(json, JsonOptions)
                ?.Where(entry => entry is not null).ToList() ?? [];
        }
        catch (Exception ex)
        {
            // Keep the unreadable file for inspection rather than destroying evidence.
            _log.Error("Installed", $"Could not read {_path}; starting with an empty library.", ex);
            TrySetAside();
            _cache = [];
        }

        return _cache;
    }

    private void Persist(List<ApplicationManifest> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var temporary = _path + ".tmp";

        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
            _cache = all;
        }
        catch (Exception ex)
        {
            _log.Error("Installed", "Could not save the installed application list.", ex);

            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch
            {
                // Nothing further to do about a stray temporary file.
            }

            throw;
        }
    }

    private void TrySetAside()
    {
        try
        {
            var broken = _path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(_path, broken);
            _log.Warn("Installed", $"Moved the unreadable library file to {broken}");
        }
        catch (Exception ex)
        {
            _log.Warn("Installed", $"Could not set aside the unreadable library file: {ex.Message}");
        }
    }
}
