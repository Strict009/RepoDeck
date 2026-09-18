using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

/// <summary>
/// Executes an install plan: download, verify, extract, identify, register.
/// </summary>
/// <remarks>
/// Re-decides nothing - every choice was made during analysis, and this class only
/// carries it out. Two rules are absolute: nothing downloaded is ever executed, and
/// nothing outside RepoDeck's own Apps folder is ever written to or removed. A failure
/// at any stage rolls the installation folder back, so a broken install never lingers
/// looking like a working one.
/// </remarks>
public sealed partial class InstallationService : IInstallationService
{
    private readonly IDownloadService _downloads;
    private readonly IExtractionService _extraction;
    private readonly IInstalledAppStore _store;
    private readonly AppPaths _paths;
    private readonly IAppLog _log;
    private readonly TimeProvider _time;
    private readonly ITransferRegistry _transfers;
    private readonly History.ILifecycleHistory _history;

    public InstallationService(
        IDownloadService downloads,
        IExtractionService extraction,
        IInstalledAppStore store,
        AppPaths paths,
        IAppLog log,
        TimeProvider? timeProvider = null,
        ITransferRegistry? transfers = null,
        History.ILifecycleHistory? history = null)
    {
        _transfers = transfers ?? NullTransferRegistry.Instance;
        _history = history ?? History.NullLifecycleHistory.Instance;
        _downloads = downloads;
        _extraction = extraction;
        _store = store;
        _paths = paths;
        _log = log;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<InstallationResult> InstallAsync(
        InstallPlan plan,
        IProgress<InstallationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The plan is the authority on whether this is allowed at all.
        if (!plan.CanProceed)
        {
            var reason = plan.BlockingIssues.Count > 0
                ? string.Join(" ", plan.BlockingIssues)
                : "RepoDeck has no usable installation plan for this project.";

            _log.Warn("Install", $"Refused to install {plan.FullName}: {reason}");
            return InstallationResult.Failed(reason);
        }

        _log.Info("Install", $"Starting {plan.Strategy} installation of {plan.FullName} "
                             + $"from {plan.AssetName}");

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Preparing });

        _history.Record(LifecycleEvent.InstallStarted(plan));

        var transfer = _transfers.Begin(
            FriendlyNaming.ForRepository(plan.Name), plan.ReleaseTag, plan.AssetName,
            plan.AssetSize > 0 ? plan.AssetSize : null);

        DownloadedFile download;

        try
        {
            download = await DownloadAsync(plan, progress, transfer, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Info("Install", $"Installation of {plan.FullName} cancelled during download.");
            _transfers.Cancel(transfer);
            return InstallationResult.Cancelled();
        }
        catch (DownloadException ex)
        {
            _log.Warn("Install", $"Download failed for {plan.FullName}: {ex.Message}");
            _transfers.Fail(transfer, ex.UserMessage);
            _history.Record(LifecycleEvent.InstallFailed(plan, ex.UserMessage));
            return InstallationResult.Failed(ex.UserMessage);
        }

        try
        {
            var result = await CompleteAsync(plan, download, progress, cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                _transfers.Complete(transfer);

                if (result.Manifest is { } manifest)
                {
                    _history.Record(LifecycleEvent.InstallCompleted(manifest));
                }
            }
            else
            {
                _transfers.Fail(transfer, result.ErrorMessage ?? "The installation did not work.");
                _history.Record(LifecycleEvent.InstallFailed(plan, result.ErrorMessage ?? "It did not work."));
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            // Staging disposal has already removed the partial work.
            _log.Info("Install", $"Installation of {plan.FullName} cancelled; staging removed.");
            _transfers.Cancel(transfer);
            return InstallationResult.Cancelled();
        }
        catch (ExtractionException ex)
        {
            _log.Warn("Install", $"Extraction failed for {plan.FullName}: {ex.Message}");
            _transfers.Fail(transfer, ex.UserMessage);
            _history.Record(LifecycleEvent.InstallFailed(plan, ex.UserMessage));
            return InstallationResult.Failed(ex.UserMessage);
        }
        catch (Exception ex)
        {
            _log.Error("Install", $"Installation of {plan.FullName} failed", ex);

            const string message =
                "Something went wrong while installing. RepoDeck has cleaned up after itself.";

            _transfers.Fail(transfer, message);
            _history.Record(LifecycleEvent.InstallFailed(plan, message));

            return InstallationResult.Failed(message);
        }
    }

    private async Task<DownloadedFile> DownloadAsync(
        InstallPlan plan,
        IProgress<InstallationProgress>? progress,
        string transfer,
        CancellationToken cancellationToken)
    {
        var downloadProgress = new Progress<DownloadProgress>(d =>
        {
            progress?.Report(new InstallationProgress
            {
                Stage = InstallationStage.Downloading,
                Download = d
            });

            _transfers.ReportProgress(transfer, d.BytesReceived, d.TotalBytes);
        });

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Downloading });

        var file = await _downloads.DownloadAsync(plan, downloadProgress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Verifying });
        return file;
    }
}
