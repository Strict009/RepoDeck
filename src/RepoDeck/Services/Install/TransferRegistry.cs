using RepoDeck.Models;

namespace RepoDeck.Services.Install;

/// <summary>Where one download or install has got to, for the Downloads page.</summary>
public enum TransferState
{
    /// <summary>Happening now.</summary>
    Active,

    /// <summary>Finished. The result is a manifest, so the record here is just history.</summary>
    Completed,

    /// <summary>Did not finish. Retryable when RepoDeck knows how.</summary>
    Failed,

    /// <summary>Stopped by the user.</summary>
    Cancelled
}

/// <summary>What RepoDeck is fetching, or recently tried to.</summary>
/// <remarks>
/// Held in memory only, and deliberately. An "active download" cannot survive the process
/// that was performing it, so writing this to disk would only produce records that lie
/// after a crash. What outlives a run is the manifest the download produced, or nothing.
/// </remarks>
public sealed record TransferRecord
{
    public required string Id { get; init; }
    public required string ApplicationName { get; init; }

    public string? Version { get; init; }
    public string? AssetName { get; init; }

    public TransferState State { get; init; } = TransferState.Active;

    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }

    /// <summary>Plain English. Safe to show verbatim.</summary>
    public string? Message { get; init; }

    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Whether this was an update rather than a first install.</summary>
    public bool IsUpdate { get; init; }

    public double? Fraction => TotalBytes is > 0
        ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1)
        : null;

    public bool IsActive => State == TransferState.Active;
    public bool HasFinished => State != TransferState.Active;
}

public interface ITransferRegistry
{
    IReadOnlyList<TransferRecord> All();

    /// <summary>Notes that something has started, and returns its id.</summary>
    string Begin(string applicationName, string? version, string? assetName, long? totalBytes,
        bool isUpdate = false);

    void ReportProgress(string id, long bytesReceived, long? totalBytes);

    void Complete(string id, string? message = null);
    void Fail(string id, string message);
    void Cancel(string id);

    /// <summary>Forgets a finished record. Active ones are left alone.</summary>
    bool Forget(string id);

    /// <summary>Forgets everything that has finished, whatever the outcome.</summary>
    int ClearFinished();

    /// <summary>Raised whenever anything changes, so the page can follow along.</summary>
    event Action? Changed;
}

/// <summary>
/// The in-memory list of what RepoDeck is fetching, or recently tried to.
/// </summary>
/// <remarks>
/// Small on purpose. This exists so the Downloads page can show something happening and
/// something that failed, rather than only the finished files that are already recorded as
/// manifests. It holds no file contents, no URLs beyond the asset name, and nothing that
/// would be unsafe to put on screen.
/// </remarks>
public sealed class TransferRegistry : ITransferRegistry
{
    /// <summary>Enough to see what just happened without becoming a log.</summary>
    private const int MaxFinishedRecords = 50;

    private readonly Lock _gate = new();
    private readonly List<TransferRecord> _records = [];
    private readonly TimeProvider _time;

    public TransferRegistry(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    public event Action? Changed;

    public IReadOnlyList<TransferRecord> All()
    {
        lock (_gate)
        {
            // Active first, then most recent: what is happening now matters more than
            // what happened earlier.
            return _records
                .OrderByDescending(r => r.IsActive)
                .ThenByDescending(r => r.EndedAt ?? r.StartedAt)
                .ToList();
        }
    }

    public string Begin(
        string applicationName, string? version, string? assetName, long? totalBytes,
        bool isUpdate = false)
    {
        var id = Guid.NewGuid().ToString("N");

        lock (_gate)
        {
            _records.Add(new TransferRecord
            {
                Id = id,
                ApplicationName = applicationName,
                Version = version,
                AssetName = assetName,
                TotalBytes = totalBytes,
                State = TransferState.Active,
                StartedAt = _time.GetUtcNow(),
                IsUpdate = isUpdate
            });

            Trim();
        }

        Changed?.Invoke();
        return id;
    }

    public void ReportProgress(string id, long bytesReceived, long? totalBytes) =>
        Update(id, record => record with
        {
            BytesReceived = bytesReceived,
            TotalBytes = totalBytes ?? record.TotalBytes
        });

    public void Complete(string id, string? message = null) =>
        Update(id, record => record with
        {
            State = TransferState.Completed,
            EndedAt = _time.GetUtcNow(),
            Message = message,
            BytesReceived = record.TotalBytes ?? record.BytesReceived
        });

    public void Fail(string id, string message) =>
        Update(id, record => record with
        {
            State = TransferState.Failed,
            EndedAt = _time.GetUtcNow(),
            Message = message
        });

    public void Cancel(string id) =>
        Update(id, record => record with
        {
            State = TransferState.Cancelled,
            EndedAt = _time.GetUtcNow(),
            Message = "Stopped."
        });

    public bool Forget(string id)
    {
        bool removed;

        lock (_gate)
        {
            // An active transfer is still happening; forgetting it would hide something
            // that is using the network right now.
            removed = _records.RemoveAll(r => r.Id == id && r.HasFinished) > 0;
        }

        if (removed) Changed?.Invoke();
        return removed;
    }

    public int ClearFinished()
    {
        int removed;

        lock (_gate) removed = _records.RemoveAll(r => r.HasFinished);

        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    private void Update(string id, Func<TransferRecord, TransferRecord> change)
    {
        var changed = false;

        lock (_gate)
        {
            var index = _records.FindIndex(r => r.Id == id);

            if (index >= 0)
            {
                _records[index] = change(_records[index]);
                changed = true;
            }
        }

        if (changed) Changed?.Invoke();
    }

    private void Trim()
    {
        var finished = _records.Where(r => r.HasFinished)
            .OrderBy(r => r.EndedAt ?? r.StartedAt)
            .ToList();

        while (finished.Count > MaxFinishedRecords)
        {
            _records.Remove(finished[0]);
            finished.RemoveAt(0);
        }
    }
}

/// <summary>A registry that remembers nothing, for services built without one.</summary>
public sealed class NullTransferRegistry : ITransferRegistry
{
    public static NullTransferRegistry Instance { get; } = new();

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<TransferRecord> All() => [];

    public string Begin(string applicationName, string? version, string? assetName, long? totalBytes,
        bool isUpdate = false) => "";

    public void ReportProgress(string id, long bytesReceived, long? totalBytes) { }
    public void Complete(string id, string? message = null) { }
    public void Fail(string id, string message) { }
    public void Cancel(string id) { }
    public bool Forget(string id) => false;
    public int ClearFinished() => 0;
}
