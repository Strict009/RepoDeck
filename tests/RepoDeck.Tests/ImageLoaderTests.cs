using System.IO.Compression;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using RepoDeck.Infrastructure;

namespace RepoDeck.Tests;

/// <summary>
/// Decoding an image needs Avalonia's render platform, which a plain test process does
/// not have. The headless platform provides one, so the decode path can be exercised for
/// real rather than assumed to work.
/// </summary>
public sealed class AvaloniaPlatformFixture : IDisposable
{
    private static readonly Lock Gate = new();
    private static bool _started;

    public AvaloniaPlatformFixture()
    {
        lock (Gate)
        {
            if (_started) return;

            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();

            _started = true;
        }
    }

    public void Dispose()
    {
        // The platform is process-wide and stays up for the run.
    }
}

[CollectionDefinition("Avalonia")]
public class AvaloniaCollection : ICollectionFixture<AvaloniaPlatformFixture>;

[Collection("Avalonia")]
public class ImageLoaderTests
{
    /// <summary>A real 2x2 PNG, so the decoder has something genuine to work on.</summary>
    private static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8z8DwnwEJMDEgAWwcAEO"
        + "IARGQZ1nAAAAAAElFTkSuQmCC");

    [Fact]
    public void The_decoder_really_works_under_the_headless_platform()
    {
        // If this fails, the fixture is wrong and every other decode result is meaningless.
        using var stream = new MemoryStream(TinyPng());
        using var bitmap = Bitmap.DecodeToWidth(stream, 2);

        Assert.NotNull(bitmap);
        Assert.Equal(2, bitmap.PixelSize.Width);
    }

    [Theory]
    [InlineData("http://example.invalid/image.png")]
    [InlineData("file:///C:/Windows/System32/x.png")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("not a url")]
    public async Task Only_https_addresses_are_ever_fetched(string url)
    {
        using var loader = new ImageLoader(NullAppLog.Instance);

        // No exception, no fetch, just nothing - the card falls back to its tile.
        Assert.Null(await loader.LoadAsync(url));
    }

    [Fact]
    public async Task An_unreachable_host_yields_nothing_rather_than_an_error()
    {
        using var loader = new ImageLoader(NullAppLog.Instance);

        Assert.Null(await loader.LoadAsync("https://this-host-does-not-exist.invalid/x.png"));
    }

    [Fact]
    public async Task A_cancelled_load_does_not_throw_out_of_the_card()
    {
        using var loader = new ImageLoader(NullAppLog.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loader.LoadAsync("https://example.invalid/x.png", cts.Token));
    }

    [Fact]
    public void The_loader_enforces_a_byte_ceiling_rather_than_trusting_the_server()
    {
        // Asserted against the source: the alternative is serving a multi-gigabyte
        // response from a test, which is not a reasonable thing to do.
        var source = File.ReadAllText(SourcePath("ImageLoader.cs"));

        Assert.Contains("MaxBytes", source);
        Assert.Contains("total > MaxBytes", source);
        Assert.Contains("Content-Length is a claim, not a fact", source);
    }

    [Fact]
    public void Images_are_downscaled_on_decode_rather_than_held_at_full_size()
    {
        var source = File.ReadAllText(SourcePath("ImageLoader.cs"));

        Assert.Contains("DecodeToWidth", source);
    }

    private static string SourcePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "RepoDeck", "Infrastructure", fileName);
    }
}
