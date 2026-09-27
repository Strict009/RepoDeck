using System.Formats.Tar;
using System.IO.Compression;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

/// <summary>
/// Unpacks ZIP and tar.gz archives into RepoDeck's managed Apps folder.
/// </summary>
/// <remarks>
/// Every entry is passed through <see cref="ArchivePathGuard"/> before anything is
/// written, and refused entries are recorded rather than silently skipped, so an
/// archive that tried to escape is visible afterwards. Symbolic and hard links are
/// refused entirely: RepoDeck has no reason to create them and they are another way
/// out of the destination folder.
/// </remarks>
public sealed partial class ExtractionService : IExtractionService
{
    /// <summary>Guards against a small archive that expands to fill the disk.</summary>
    internal const long MaxTotalBytes = 4L * 1024 * 1024 * 1024;

    internal const int MaxEntries = 200_000;

    private readonly IAppLog _log;

    public ExtractionService(IAppLog log)
    {
        _log = log;
    }

    public async Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string destinationDirectory,
        PackageType packageType,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new ExtractionException("The downloaded file is missing, so nothing was installed.");
        }

        var capability = PackageCapabilities.For(packageType);
        if (!capability.CanExtract)
        {
            throw new ExtractionException(
                $"RepoDeck cannot unpack a {packageType.ToDisplayString()} automatically.");
        }

        Directory.CreateDirectory(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory);

        try
        {
            return capability.Extraction switch
            {
                ArchiveExtractionKind.Zip =>
                    await ExtractZipAsync(archivePath, root, progress, cancellationToken)
                    .ConfigureAwait(false),
                ArchiveExtractionKind.TarGZip =>
                    await ExtractTarGZipAsync(archivePath, root, progress, cancellationToken)
                        .ConfigureAwait(false),
                _ => throw new ExtractionException(
                    $"RepoDeck cannot unpack a {packageType.ToDisplayString()} automatically.")
            };
        }
        catch (ExtractionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new ExtractionException(
                "The downloaded file is not a valid archive, so nothing was installed.", ex.Message, ex);
        }
        catch (Exception ex)
        {
            _log.Error("Extract", $"Extraction of {archivePath} failed", ex);
            throw new ExtractionException(
                "RepoDeck could not unpack the download, so nothing was installed.", ex.Message, ex);
        }
    }
}
