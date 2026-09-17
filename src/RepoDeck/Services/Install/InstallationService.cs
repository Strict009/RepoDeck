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

    public InstallationService(
        IDownloadService downloads,
        IExtractionService extraction,
        IInstalledAppStore store,
        AppPaths paths,
        IAppLog log,
        TimeProvider? timeProvider = null)
    {
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

        DownloadedFile download;

        try
        {
            download = await DownloadAsync(plan, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Info("Install", $"Installation of {plan.FullName} cancelled during download.");
            return InstallationResult.Cancelled();
        }
        catch (DownloadException ex)
        {
            _log.Warn("Install", $"Download failed for {plan.FullName}: {ex.Message}");
            return InstallationResult.Failed(ex.UserMessage);
        }

        try
        {
            return await CompleteAsync(plan, download, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RollBack(plan);
            _log.Info("Install", $"Installation of {plan.FullName} cancelled; folder removed.");
            return InstallationResult.Cancelled();
        }
        catch (ExtractionException ex)
        {
            RollBack(plan);
            _log.Warn("Install", $"Extraction failed for {plan.FullName}: {ex.Message}");
            return InstallationResult.Failed(ex.UserMessage);
        }
        catch (Exception ex)
        {
            RollBack(plan);
            _log.Error("Install", $"Installation of {plan.FullName} failed", ex);
            return InstallationResult.Failed(
                "Something went wrong while installing. RepoDeck has cleaned up after itself.");
        }
    }

    private async Task<DownloadedFile> DownloadAsync(
        InstallPlan plan, IProgress<InstallationProgress>? progress, CancellationToken cancellationToken)
    {
        var downloadProgress = new Progress<DownloadProgress>(d =>
            progress?.Report(new InstallationProgress
            {
                Stage = InstallationStage.Downloading,
                Download = d
            }));

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Downloading });

        var file = await _downloads.DownloadAsync(plan, downloadProgress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new InstallationProgress { Stage = InstallationStage.Verifying });
        return file;
    }
}
