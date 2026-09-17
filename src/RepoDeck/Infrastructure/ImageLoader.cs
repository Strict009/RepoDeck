using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Avalonia.Media.Imaging;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Fetches and decodes images for the interface.
/// </summary>
/// <remarks>
/// Everything here is untrusted: the addresses come from READMEs written by strangers and
/// point at hosts RepoDeck knows nothing about. So: https only, a hard byte ceiling
/// enforced while streaming rather than trusting Content-Length, a content type check, a
/// decode inside a try, and a short timeout. A failure is never an error the user sees -
/// the card simply shows its fallback tile.
///
/// Decoded images are downscaled on load. A README screenshot can be several thousand
/// pixels wide, and a 96-pixel card tile does not need that in memory.
/// </remarks>
public sealed class ImageLoader : IDisposable
{
    /// <summary>A generous ceiling for a screenshot, and a firm one for anything hostile.</summary>
    private const long MaxBytes = 8 * 1024 * 1024;

    /// <summary>Card tiles and gallery thumbnails never need more width than this.</summary>
    private const int DecodeWidth = 640;

    private const int MaxCachedImages = 120;

    private readonly HttpClient _http;
    private readonly IAppLog _log;
    private readonly ConcurrentDictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageLoader(IAppLog log)
    {
        _log = log;

        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),

            // A README image host is not worth chasing through a long redirect chain.
            MaxAutomaticRedirections = 3
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppServices.UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
    }

    /// <summary>Loads a picture for a Discover card. Never throws for a bad image.</summary>
    public Task<Bitmap?> LoadImageForCardAsync(string url, CancellationToken cancellationToken = default) =>
        LoadAsync(url, cancellationToken);

    /// <summary>
    /// Returns a decoded image, or null when it could not be fetched or decoded.
    /// Failures are cached too, so a broken address is not retried on every scroll.
    /// </summary>
    public async Task<Bitmap?> LoadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(url, out var cached)) return cached;

        var bitmap = await FetchAsync(url, cancellationToken).ConfigureAwait(false);

        // Nothing is cached after a cancellation: the next attempt should try again.
        if (cancellationToken.IsCancellationRequested) return bitmap;

        if (_cache.Count >= MaxCachedImages) Trim();
        _cache[url] = bitmap;

        return bitmap;
    }

    private async Task<Bitmap?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            _log.Warn("Images", $"Refused a non-https image address: {url}");
            return null;
        }

        try
        {
            using var response = await _http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return null;

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null
                && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                _log.Warn("Images", $"Ignored {url}: served as {mediaType} rather than an image.");
                return null;
            }

            // Content-Length is a claim, not a fact, so the ceiling is enforced while reading.
            if (response.Content.Headers.ContentLength > MaxBytes) return null;

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            using var buffer = new MemoryStream();
            await CopyWithLimitAsync(stream, buffer, cancellationToken).ConfigureAwait(false);

            buffer.Position = 0;
            return Bitmap.DecodeToWidth(buffer, DecodeWidth);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A picture failing to load is a cosmetic problem, never a user-facing error.
            _log.Warn("Images", $"Could not load {url}: {ex.Message}");
            return null;
        }
    }

    private static async Task CopyWithLimitAsync(
        Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            total += read;
            if (total > MaxBytes)
            {
                throw new InvalidOperationException($"Image exceeded {MaxBytes} bytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Drops roughly half the cache. Crude, and entirely adequate for pictures.</summary>
    private void Trim()
    {
        foreach (var key in _cache.Keys.Take(_cache.Count / 2))
        {
            if (_cache.TryRemove(key, out var bitmap)) bitmap?.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var bitmap in _cache.Values) bitmap?.Dispose();
        _cache.Clear();
        _http.Dispose();
    }
}
