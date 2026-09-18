using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.History;
using RepoDeck.Services.Install;

namespace RepoDeck.Services.Update;

public interface IUpdateService
{
    Task<UpdateResult> UpdateAsync(
        UpdatePlan plan,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a damaged installation back by fetching the release it already has, through
    /// the same staged, reversible path an update uses.
    /// </summary>
    Task<UpdateResult> RepairAsync(
        RepairPlan repair,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes rollback copies left by an interrupted update. Called at startup.
    /// </summary>
    int CleanAbandonedRollbacks();
}

/// <summary>
/// Replaces an installed application with a newer release, transactionally.
/// </summary>
/// <remarks>
/// <para>The sequence is:</para>
/// <code>
/// download -> verify -> staging -> validate staged -> back up live -> promote
///          -> validate live -> remove backup
/// </code>
/// <para>
/// A live installation is never written into. The new version is assembled somewhere else
/// entirely and checked before the old one is touched at all, so everything that can fail
/// - the network, a corrupt archive, an archive with nothing runnable in it - fails while
/// the working copy is still sitting there untouched.
/// </para>
/// <para>
/// Once the swap begins, the old directory is <em>moved</em> aside rather than deleted, and
/// it stays there until the new installation has been checked in place. If anything goes
/// wrong in that window - including the promotion itself - the backup goes back. The old
/// manifest is likewise kept until the new one has been written, so a failure leaves both
/// the files and RepoDeck's record of them as they were.
/// </para>
/// <para>
/// The one thing this cannot promise is a machine that loses power mid-move. What it can
/// promise is that the recovery path exists, runs on every failure it can observe, and
/// leaves behind a named directory that startup cleanup will find.
/// </para>
/// </remarks>
public sealed partial class UpdateService : IUpdateService
{
    /// <summary>
    /// Where a displaced installation waits while its replacement is checked. Named so a
    /// human reading the folder can tell what it is, and so startup cleanup can find it.
    /// </summary>
    internal const string RollbackFolderName = ".rollback";

    private readonly IDownloadService _downloads;
    private readonly IExtractionService _extraction;
    private readonly IInstalledAppStore _store;
    private readonly IRunningApplicationDetector _running;
    private readonly ILifecycleHistory _history;
    private readonly ITransferRegistry _transfers;
    private readonly MachineProfile _machine;
    private readonly AppPaths _paths;
    private readonly IAppLog _log;
    private readonly TimeProvider _time;

    public UpdateService(
        IDownloadService downloads,
        IExtractionService extraction,
        IInstalledAppStore store,
        IRunningApplicationDetector running,
        ILifecycleHistory history,
        MachineProfile machine,
        AppPaths paths,
        IAppLog log,
        TimeProvider? timeProvider = null,
        ITransferRegistry? transfers = null)
    {
        _machine = machine;
        _transfers = transfers ?? NullTransferRegistry.Instance;
        _downloads = downloads;
        _extraction = extraction;
        _store = store;
        _running = running;
        _history = history;
        _paths = paths;
        _log = log;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<UpdateResult> UpdateAsync(
        UpdatePlan plan,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!plan.CanProceed)
        {
            var reason = plan.BlockingIssues.Count > 0
                ? string.Join(" ", plan.BlockingIssues)
                : "RepoDeck has no usable update plan for this application.";

            _log.Warn("Update", $"Refused to update {plan.FullName}: {reason}");
            return UpdateResult.Failed(reason);
        }

        // Asked before anything is downloaded, so somebody with the program open is told
        // to close it rather than finding out after waiting for a download.
        if (plan.RequiresApplicationClosed && _running.IsRunning(plan.Current))
        {
            _log.Info("Update", $"{plan.FullName} is running; update not attempted.");
            return UpdateResult.Running(FriendlyNaming.ForRepository(plan.Name));
        }

        if (!plan.IsRepair) _history.Record(LifecycleEvent.UpdateStarted(plan));

        var transfer = _transfers.Begin(
            FriendlyNaming.ForRepository(plan.Name), plan.TargetVersionText, plan.AssetName,
            plan.AssetSize > 0 ? plan.AssetSize : null, isUpdate: true);

        DownloadedFile download;

        try
        {
            download = await DownloadAsync(plan, progress, transfer, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _transfers.Cancel(transfer);
            if (!plan.IsRepair) _history.Record(LifecycleEvent.UpdateCancelled(plan));
            return UpdateResult.Cancelled(restored: true);
        }
        catch (DownloadException ex)
        {
            _log.Warn("Update", $"Download failed for {plan.FullName}: {ex.Message}");
            _transfers.Fail(transfer, ex.UserMessage);
            if (!plan.IsRepair) _history.Record(LifecycleEvent.UpdateFailed(plan, ex.UserMessage));

            // Nothing has been touched: the working copy is exactly as it was.
            return UpdateResult.Failed(ex.UserMessage, restored: true);
        }

        try
        {
            var result = await ReplaceAsync(plan, download, progress, cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded) _transfers.Complete(transfer);
            else if (result.ApplicationWasRunning) _transfers.Cancel(transfer);
            else _transfers.Fail(transfer, result.Message ?? "The update did not work.");

            return result;
        }
        catch (OperationCanceledException)
        {
            if (!plan.IsRepair) _history.Record(LifecycleEvent.UpdateCancelled(plan));
            return UpdateResult.Cancelled(restored: true);
        }
        catch (ExtractionException ex)
        {
            _log.Warn("Update", $"Extraction failed for {plan.FullName}: {ex.Message}");
            if (!plan.IsRepair) _history.Record(LifecycleEvent.UpdateFailed(plan, ex.UserMessage));
            return UpdateResult.Failed(ex.UserMessage, restored: true);
        }
        catch (Exception ex)
        {
            _log.Error("Update", $"Update of {plan.FullName} failed", ex);
            if (!plan.IsRepair) _history.Record(LifecycleEvent.UpdateFailed(plan, "Something went wrong."));

            return UpdateResult.Failed(
                "Something went wrong while updating. Your previous version has been left in place.",
                restored: true);
        }
        finally
        {
            TryDeleteFile(download.Path);
        }
    }

    private async Task<DownloadedFile> DownloadAsync(
        UpdatePlan plan,
        IProgress<UpdateProgress>? progress,
        string transfer,
        CancellationToken cancellationToken)
    {
        var downloadProgress = new Progress<DownloadProgress>(d =>
        {
            progress?.Report(new UpdateProgress { Stage = UpdateStage.Downloading, Download = d });
            _transfers.ReportProgress(transfer, d.BytesReceived, d.TotalBytes);
        });

        progress?.Report(new UpdateProgress { Stage = UpdateStage.Downloading });

        var file = await _downloads
            .DownloadAsync(AsInstallPlan(plan), downloadProgress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new UpdateProgress { Stage = UpdateStage.Verifying });
        return file;
    }

    /// <summary>
    /// The transactional part. Everything before the backup step is reversible by doing
    /// nothing; everything after it is reversible by putting the backup back.
    /// </summary>
    private async Task<UpdateResult> ReplaceAsync(
        UpdatePlan plan,
        DownloadedFile download,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var live = Path.GetFullPath(plan.ProposedInstallDirectory);
        Guard(live, "update");

        using var staging = new ManagedDirectory(NewStagingPath(), _log, _paths.Apps);

        // ---- Prepare: assemble and check the new version, old one untouched ----
        progress?.Report(new UpdateProgress { Stage = UpdateStage.Preparing });

        await AssembleAsync(plan, download, staging.Path, progress, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var selection = Locate(plan, staging.Path);

        if (!selection.Found)
        {
            // The new release has nothing runnable in it. Refusing here is the whole point
            // of staging: the working copy has not been touched.
            var message = $"The new version of {FriendlyNaming.ForRepository(plan.Name)} does not "
                          + "contain a program RepoDeck can run, so your existing version has been "
                          + "left alone.";

            _log.Warn("Update", $"{plan.FullName}: staged {plan.TargetReleaseTag} had no runnable target.");
            if (!plan.IsRepair) _history.Record(LifecycleEvent.UpdateFailed(plan, message));

            return UpdateResult.Failed(message, restored: true);
        }

        // Last chance to notice the program started while the download ran.
        if (plan.RequiresApplicationClosed && _running.IsRunning(plan.Current))
        {
            _log.Info("Update", $"{plan.FullName} started during the download; update not applied.");
            return UpdateResult.Running(FriendlyNaming.ForRepository(plan.Name));
        }

        // ---- Replace: back up, swap, check, then let the backup go ----
        progress?.Report(new UpdateProgress { Stage = UpdateStage.Replacing });

        var previousManifest = _store.Find(plan.Owner, plan.Name) ?? plan.Current;
        var backup = NewRollbackPath(plan);
        var backedUp = false;

        try
        {
            if (Directory.Exists(live))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(live, backup);
                backedUp = true;
            }

            Directory.Move(staging.Path, live);
            staging.Keep();

            // ---- Validate live: the files have to actually be where they were promised ----
            if (!ValidateLive(live, selection, out var validationFailure))
            {
                throw new InvalidOperationException(validationFailure);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Update", $"Update of {plan.FullName} failed during replacement", ex);
            progress?.Report(new UpdateProgress { Stage = UpdateStage.RollingBack });

            var restored = Rollback(live, backup, backedUp);

            if (restored)
            {
                _history.Record(LifecycleEvent.RollbackPerformed(plan));
            }
            else if (!plan.IsRepair)
            {
                _history.Record(
                    LifecycleEvent.UpdateFailed(plan, "The previous version could not be restored."));
            }

            return new UpdateResult
            {
                Succeeded = false,
                PreviousInstallationRestored = restored,
                InstallationDamaged = !restored,
                Manifest = restored ? previousManifest : null,
                Message = restored
                    ? $"The update did not work, so RepoDeck put your previous version back. "
                      + $"{FriendlyNaming.ForRepository(plan.Name)} {plan.InstalledVersionText} is still installed."
                    : $"The update failed and RepoDeck could not restore your previous version. "
                      + $"The files it saved are in {backup}."
            };
        }

        // ---- The new version is in place and checked. Write the record, then let go ----
        var manifest = BuildManifest(plan, download, live, selection, previousManifest);
        _store.Save(manifest);

        RemoveBackup(backup, backedUp);

        _log.Info("Update", $"Updated {plan.FullName} from {plan.InstalledVersionText} "
                            + $"to {plan.TargetVersionText}");

        if (!plan.IsRepair) _history.Record(LifecycleEvent.UpdateCompleted(plan));

        progress?.Report(new UpdateProgress { Stage = UpdateStage.Finished });

        return new UpdateResult
        {
            Succeeded = true,
            Manifest = manifest,
            ExecutableIsAmbiguous = selection.IsAmbiguous,
            Message = selection.IsAmbiguous
                ? "RepoDeck found multiple possible application executables in the new version."
                : null
        };
    }

    /// <summary>
    /// Puts the previous installation back. Returns false only when it genuinely could
    /// not, which the caller reports rather than swallowing.
    /// </summary>
    private bool Rollback(string live, string backup, bool backedUp)
    {
        if (!backedUp) return true;

        try
        {
            // A partially promoted new version is in the way and is worth nothing.
            if (Directory.Exists(live)) DeleteManaged(live);

            if (Directory.Exists(backup))
            {
                Directory.Move(backup, live);
                _log.Info("Update", $"Restored the previous installation at {live}");
                return true;
            }

            _log.Error("Update", $"Rollback found nothing at {backup} to restore.");
            return false;
        }
        catch (Exception ex)
        {
            _log.Error("Update", $"Could not restore the previous installation to {live}", ex);
            return false;
        }
    }

    private void RemoveBackup(string backup, bool backedUp)
    {
        if (!backedUp) return;

        try
        {
            if (Directory.Exists(backup)) DeleteManaged(backup);
        }
        catch (Exception ex)
        {
            // Harmless: startup cleanup will find it. Worth saying so rather than silence.
            _log.Warn("Update", $"The previous version is still at {backup}: {ex.Message}");
        }
    }

    public int CleanAbandonedRollbacks()
    {
        var root = Path.Combine(_paths.Apps, RollbackFolderName);
        if (!Directory.Exists(root)) return 0;

        var removed = 0;

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            try
            {
                DeleteManaged(directory);
                removed++;
            }
            catch (Exception ex)
            {
                _log.Warn("Update", $"Could not remove abandoned rollback {directory}: {ex.Message}");
            }
        }

        if (removed > 0)
        {
            _log.Info("Update", $"Cleared {removed} abandoned rollback folder(s) from a previous run.");
        }

        return removed;
    }
}
