using System.Formats.Tar;
using System.IO.Compression;
using RepoDeck.Infrastructure;

namespace RepoDeck.Services.Install;

public sealed partial class ExtractionService
{
    private async Task<ExtractionResult> ExtractZipAsync(
        string archivePath, string root, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var refused = new List<string>();
        var files = 0;
        long bytes = 0;

        if (archive.Entries.Count > MaxEntries)
        {
            throw new ExtractionException(
                "That archive contains an unreasonable number of files, so RepoDeck refused it.",
                $"{archive.Entries.Count} entries.");
        }

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A trailing separator marks a directory entry, which carries no content.
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var target = ArchivePathGuard.ResolveSafePath(root, entry.FullName);
            if (target is null)
            {
                refused.Add(entry.FullName);
                _log.Warn("Extract", $"Refused archive entry that escapes the destination: {entry.FullName}");
                continue;
            }

            bytes += entry.Length;
            GuardTotalSize(bytes);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using (var source = entry.Open())
            await using (var destination = new FileStream(
                             target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            files++;
            if (files % 25 == 0) progress?.Report($"Extracting {entry.Name}");
        }

        return Finish(root, files, bytes, refused);
    }

    private async Task<ExtractionResult> ExtractTarGzAsync(
        string archivePath, string root, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archivePath);
        await using var decompressed = new GZipStream(file, CompressionMode.Decompress);
        await using var tar = new TarReader(decompressed);

        var refused = new List<string>();
        var files = 0;
        long bytes = 0;

        while (await tar.GetNextEntryAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
               is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (files > MaxEntries)
            {
                throw new ExtractionException(
                    "That archive contains an unreasonable number of files, so RepoDeck refused it.");
            }

            // Links are another way out of the destination folder, so they are refused.
            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                refused.Add(entry.Name + " (link)");
                _log.Warn("Extract", $"Refused link entry: {entry.Name}");
                continue;
            }

            if (entry.EntryType is TarEntryType.Directory) continue;
            if (entry.DataStream is null) continue;

            var target = ArchivePathGuard.ResolveSafePath(root, entry.Name);
            if (target is null)
            {
                refused.Add(entry.Name);
                _log.Warn("Extract", $"Refused archive entry that escapes the destination: {entry.Name}");
                continue;
            }

            bytes += entry.Length;
            GuardTotalSize(bytes);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using (var destination = new FileStream(
                             target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await entry.DataStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            TryPreserveExecutableBit(entry, target);

            files++;
            if (files % 25 == 0) progress?.Report($"Extracting {Path.GetFileName(entry.Name)}");
        }

        return Finish(root, files, bytes, refused);
    }

    /// <summary>
    /// tar records Unix permissions. On Linux the executable bit is what makes the
    /// extracted program runnable at all, so it is carried across when present.
    /// </summary>
    private void TryPreserveExecutableBit(TarEntry entry, string target)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            var mode = entry.Mode;
            if ((mode & UnixFileMode.UserExecute) == 0) return;

            File.SetUnixFileMode(target,
                File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
        }
        catch (Exception ex)
        {
            _log.Warn("Extract", $"Could not set permissions on {target}: {ex.Message}");
        }
    }

    private static void GuardTotalSize(long bytes)
    {
        if (bytes <= MaxTotalBytes) return;

        throw new ExtractionException(
            "That archive unpacks to more than RepoDeck is prepared to write, so it was refused.",
            $"Exceeded {MaxTotalBytes} bytes.");
    }

    private ExtractionResult Finish(string root, int files, long bytes, List<string> refused)
    {
        if (files == 0)
        {
            throw new ExtractionException(
                "The archive contained nothing RepoDeck could safely unpack.",
                refused.Count > 0 ? $"{refused.Count} entries were refused." : "No files.");
        }

        if (refused.Count > 0)
        {
            _log.Warn("Extract", $"{refused.Count} entries were refused while extracting into {root}.");
        }

        _log.Info("Extract", $"Extracted {files} files ({Humanize.FileSize(bytes)}) into {root}");

        return new ExtractionResult
        {
            Directory = root,
            FileCount = files,
            TotalBytes = bytes,
            RefusedEntries = refused
        };
    }
}
