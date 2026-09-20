using System.Text.Json;
using System.Text.Json.Serialization;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.History;

public interface ILifecycleHistory
{
    /// <summary>Records something that happened. Never throws.</summary>
    void Record(LifecycleEvent entry);

    /// <summary>Everything remembered, newest first.</summary>
    IReadOnlyList<LifecycleEvent> All();

    /// <summary>Everything remembered about one application, newest first.</summary>
    IReadOnlyList<LifecycleEvent> For(string applicationId);

    /// <summary>The most recent entries, for showing a few without loading a page.</summary>
    IReadOnlyList<LifecycleEvent> Recent(int count);

    /// <summary>
    /// Forgets everything and returns how many entries went. Nothing installed is
    /// touched: this list is a record of what happened, not the thing that happened.
    /// </summary>
    int Clear();

    /// <summary>Raised whenever an entry is added or the list is cleared.</summary>
    event Action? Changed;
}

/// <summary>
/// A short, persistent record of what RepoDeck has done to somebody's machine.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small and deliberately user-facing. The log file is where stack traces,
/// request URLs and byte counts belong; this answers "what has this program been doing?"
/// in the same plain English as the rest of the interface, and nothing is written here
/// that would be unsafe or useless to put on screen.
/// </para>
/// <para>
/// Capped at a few hundred entries and trimmed on write, so it cannot grow without bound
/// on a machine that is used for years. Written to a temporary file and moved into place,
/// so an interrupted save cannot truncate it, and an unreadable file is set aside rather
/// than being allowed to stop RepoDeck starting - losing a history is a nuisance, not a
/// failure worth refusing to run over.
/// </para>
/// <para>
/// The interface is deliberately wider than M4's UI needs: recording is called from every
/// lifecycle operation now, so a history page later is a view over data that already
/// exists rather than a retrofit.
/// </para>
/// </remarks>
public sealed class LifecycleHistory : ILifecycleHistory
{
    /// <summary>Enough to cover a long time on a normal machine, small enough to load instantly.</summary>
    private const int MaxEntries = 400;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly IAppLog _log;
    private readonly Lock _gate = new();

    private List<LifecycleEvent>? _cache;

    public event Action? Changed;

    public LifecycleHistory(AppPaths paths, IAppLog log)
    {
        _path = Path.Combine(paths.Data, "history.json");
        _log = log;
    }

    public void Record(LifecycleEvent entry)
    {
        try
        {
            lock (_gate)
            {
                var all = Load();
                all.Insert(0, entry);

                if (all.Count > MaxEntries) all.RemoveRange(MaxEntries, all.Count - MaxEntries);

                _cache = all;
                Persist(all);
            }

            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // A history that fails to write must never break the operation it was
            // describing. This is bookkeeping, not the job.
            _log.Warn("History", "Could not record an event: " + ex.Message);
        }
    }

    public IReadOnlyList<LifecycleEvent> All()
    {
        lock (_gate) return Load().ToList();
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
            // Same rule as recording: bookkeeping never becomes the failure.
            _log.Warn("History", "Could not clear the history: " + ex.Message);
            return 0;
        }

        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    public IReadOnlyList<LifecycleEvent> For(string applicationId)
    {
        lock (_gate)
        {
            return Load()
                .Where(e => string.Equals(e.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public IReadOnlyList<LifecycleEvent> Recent(int count)
    {
        lock (_gate) return Load().Take(Math.Max(count, 0)).ToList();
    }

    private List<LifecycleEvent> Load()
    {
        if (_cache is not null) return _cache;

        try
        {
            if (!File.Exists(_path)) return _cache = [];

            var json = File.ReadAllText(_path);
            var entries = JsonSerializer.Deserialize<List<LifecycleEvent>>(json, JsonOptions);

            // A file that parses but contains nulls - "[null,null]" is valid JSON -
            // would otherwise hand out entries that crash whoever touches them next.
            return _cache = entries?.Where(entry => entry is not null).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _log.Warn("History", "Could not read the history, starting a new one: " + ex.Message);
            return _cache = [];
        }
    }

    private void Persist(List<LifecycleEvent> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }
}

/// <summary>A history that remembers nothing, for tests and for services built without one.</summary>
public sealed class NullLifecycleHistory : ILifecycleHistory
{
    public static NullLifecycleHistory Instance { get; } = new();

    public void Record(LifecycleEvent entry) { }
    public IReadOnlyList<LifecycleEvent> All() => [];
    public IReadOnlyList<LifecycleEvent> For(string applicationId) => [];
    public IReadOnlyList<LifecycleEvent> Recent(int count) => [];
    public int Clear() => 0;

    public event Action? Changed
    {
        add { }
        remove { }
    }
}
