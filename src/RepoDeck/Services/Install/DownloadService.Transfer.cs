using System.Security.Cryptography;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed partial class DownloadService
{
    private async Task<DownloadedFile> TransferAsync(
        Uri url,
        string partialPath,
        InstallPlan plan,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new DownloadException(
                $"The download was refused ({(int)response.StatusCode}). Nothing has been installed.",
                $"{(int)response.StatusCode} {response.ReasonPhrase} for {url}");
        }

        var expected = response.Content.Headers.ContentLength
                       ?? (plan.AssetSize > 0 ? plan.AssetSize : null);

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long received;

        await using (var destination = new FileStream(
                         partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                         BufferSize, useAsync: true))
        {
            received = await CopyAsync(source, destination, hasher, expected, progress, cancellationToken)
                .ConfigureAwait(false);

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        VerifySize(received, expected);

        progress?.Report(new DownloadProgress
        {
            BytesReceived = received,
            TotalBytes = expected ?? received
        });

        return new DownloadedFile
        {
            Path = partialPath,
            Size = received,
            Sha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant()
        };
    }

    private static async Task<long> CopyAsync(
        Stream source,
        Stream destination,
        IncrementalHash hasher,
        long? expected,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        long received = 0;
        long lastReport = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            hasher.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            if (received - lastReport < ProgressInterval) continue;

            lastReport = received;
            progress?.Report(new DownloadProgress { BytesReceived = received, TotalBytes = expected });
        }

        return received;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        try
        {
            return await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new DownloadException(
                "RepoDeck could not reach the download. Check your internet connection.",
                ex.Message, ex);
        }
    }

    /// <summary>
    /// A transfer that stopped early looks exactly like a successful one on disk, so the
    /// byte count is checked against what the server promised.
    /// </summary>
    private static void VerifySize(long received, long? expected)
    {
        if (expected is null or <= 0) return;
        if (received == expected) return;

        throw new DownloadException(
            "The download was incomplete, so RepoDeck discarded it. Nothing has been installed.",
            $"Expected {expected} bytes but received {received}.");
    }

    /// <summary>Only an https address carried by the plan is ever fetched.</summary>
    private static Uri ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new DownloadException(
                "That download address is not one RepoDeck is willing to fetch.",
                $"Refused URL: {url}");
        }

        return uri;
    }

    /// <summary>
    /// Asset names come from GitHub and are untrusted: a name containing a path
    /// separator must not be able to write outside the Downloads folder.
    /// </summary>
    internal static string SafeFileName(string assetName)
    {
        var name = Path.GetFileName(assetName);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "download";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        return cleaned.Length > 180 ? cleaned[^180..] : cleaned;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _log.Warn("Download", $"Could not remove {path}: {ex.Message}");
        }
    }
}
