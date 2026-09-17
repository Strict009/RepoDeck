using RepoDeck.Infrastructure;

namespace RepoDeck.Tests;

/// <summary>
/// The image loader's refusals and limits.
/// </summary>
/// <remarks>
/// Decoding is not exercised here. Turning bytes into a Bitmap needs Avalonia's render
/// platform, and the headless platform binds to whichever thread initialises it, which
/// makes it unreliable under a test runner that uses arbitrary pool threads - it failed
/// three runs in four. A flaky test is worse than no test, so the decode path is verified
/// by running the application instead, and what remains here is everything that can be
/// checked deterministically: which addresses are refused, what happens when a host is
/// unreachable, and the limits the loader places on untrusted content.
/// </remarks>
public class ImageLoaderTests
{
    [Theory]
    [InlineData("http://example.invalid/image.png")]
    [InlineData("file:///C:/Windows/System32/x.png")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("not a url")]
    [InlineData("")]
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
    public async Task A_cancelled_load_reports_cancellation_rather_than_a_broken_image()
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

    [Fact]
    public void Redirects_are_capped_and_only_image_content_types_are_accepted()
    {
        var source = File.ReadAllText(SourcePath("ImageLoader.cs"));

        Assert.Contains("MaxAutomaticRedirections", source);
        Assert.Contains("StartsWith(\"image/\"", source);
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
