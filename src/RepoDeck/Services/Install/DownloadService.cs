using System.Security.Cryptography;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

/// <summary>
/// Downloads a release asset to a temporary file, verifies it, and only then gives it
/// its real name.
/// </summary>
/// <remarks>
/// The central rule is that a partial download must never masquerade as a finished one.
/// Bytes go to a <c>.part</c> file; the move to the final name happens only after the
/// transfer completed and the size was checked. Cancellation and every failure path
/// delete the partial file.
/// </remarks>
public sealed partial class DownloadService : IDownloadService
{
    private const string PartialSuffix = ".part";
    private const int BufferSize = 81920;

    /// <summary>Reporting every chunk floods the UI; 64 KB still looks live.</summary>
    private const long ProgressInterval = 65536;

    private readonly HttpClient _http;
    private readonly AppPaths _paths;
    private readonly IAppLog _log;

    public DownloadService(HttpClient http, AppPaths paths, IAppLog log)
    {
        _http = http;
        _paths = paths;
        _log = log;
    }

    public async Task<DownloadedFile> DownloadAsync(
        InstallPlan plan,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var url = ValidateUrl(plan.AssetUrl);
        var fileName = SafeFileName(plan.AssetName ?? "download");

        Directory.CreateDirectory(_paths.Downloads);

        var finalPath = Path.Combine(_paths.Downloads, fileName);
        var partialPath = finalPath + PartialSuffix;

        // A leftover partial from a previous failure is never resumed or trusted.
        TryDelete(partialPath);

        try
        {
            var result = await TransferAsync(url, partialPath, plan, progress, cancellationToken)
                .ConfigureAwait(false);

            // Only now does the file get the name the rest of RepoDeck looks for.
            TryDelete(finalPath);
            File.Move(partialPath, finalPath);

            _log.Info("Download",
                $"Downloaded {fileName} ({Humanize.FileSize(result.Size)}) for {plan.FullName}");

            return result with { Path = finalPath };
        }
        catch (OperationCanceledException)
        {
            TryDelete(partialPath);
            _log.Info("Download", $"Download of {fileName} cancelled; partial file removed.");
            throw;
        }
        catch (DownloadException)
        {
            TryDelete(partialPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(partialPath);
            _log.Error("Download", $"Download of {fileName} failed", ex);
            throw new DownloadException(
                "The download did not finish. Nothing has been installed.", ex.Message, ex);
        }
    }
}
