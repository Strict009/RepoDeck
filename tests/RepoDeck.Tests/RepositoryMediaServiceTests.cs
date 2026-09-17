using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Media;
using RepoDeck.Services.Readme;

namespace RepoDeck.Tests;

public class ReadmeImageExtractorTests
{
    [Fact]
    public void Markdown_images_are_found_with_their_alt_text()
    {
        var images = ReadmeImageExtractor.Extract(
            "# Tool\n\n![The main window](docs/screenshot.png)\n\nSome prose.");

        var image = Assert.Single(images);
        Assert.Equal("docs/screenshot.png", image.Url);
        Assert.Equal("The main window", image.AltText);
    }

    [Fact]
    public void Html_images_are_found_too()
    {
        // Plenty of READMEs use raw HTML for centring.
        var images = ReadmeImageExtractor.Extract(
            """<p align="center"><img src="assets/logo.png" alt="Logo" /></p>""");

        var image = Assert.Single(images);
        Assert.Equal("assets/logo.png", image.Url);
    }

    [Fact]
    public void A_markdown_title_after_the_url_is_stripped()
    {
        var images = ReadmeImageExtractor.Extract("""![x](docs/shot.png "A title")""");

        Assert.Equal("docs/shot.png", Assert.Single(images).Url);
    }

    [Fact]
    public void Duplicates_appear_once()
    {
        var images = ReadmeImageExtractor.Extract(
            "![a](x.png)\n![b](x.png)\n<img src=\"x.png\">");

        Assert.Single(images);
    }

    [Fact]
    public void An_empty_readme_yields_nothing()
    {
        Assert.Empty(ReadmeImageExtractor.Extract(null));
        Assert.Empty(ReadmeImageExtractor.Extract(""));
        Assert.Empty(ReadmeImageExtractor.Extract("Just words, no pictures."));
    }

    [Fact]
    public void A_hostile_readme_does_not_hang_the_extractor()
    {
        var hostile = string.Concat(Enumerable.Repeat("![", 5000))
                      + string.Concat(Enumerable.Repeat("<img src=\"", 5000));

        var images = ReadmeImageExtractor.Extract(hostile);

        Assert.NotNull(images);
    }
}

public class RepositoryMediaServiceTests
{
    private readonly RepositoryMediaService _service = new(NullAppLog.Instance);

    private static GitHubRepository Repository() => new()
    {
        Name = "tool",
        FullName = "someone/tool",
        HtmlUrl = "https://github.com/someone/tool",
        DefaultBranch = "main",
        Owner = new RepositoryOwner { Login = "someone" }
    };

    [Fact]
    public void There_is_always_something_to_show()
    {
        // GitHub serves a preview card for every repository, so a card is never empty.
        var media = _service.Discover(Repository(), null, RepositoryTree.Empty);

        Assert.True(media.HasMedia);
        Assert.Equal(MediaKind.SocialPreview, media.Primary!.Kind);
        Assert.Contains("opengraph.githubassets.com", media.Primary.Url);
    }

    [Fact]
    public void A_relative_readme_image_becomes_a_fetchable_address()
    {
        var media = _service.Discover(
            Repository(), "![The app](docs/screenshot.png)", RepositoryTree.Empty);

        Assert.Equal(
            "https://raw.githubusercontent.com/someone/tool/main/docs/screenshot.png",
            media.Primary!.Url);
    }

    [Fact]
    public void A_screenshot_in_the_readme_beats_the_social_preview()
    {
        var media = _service.Discover(
            Repository(), "![](docs/screenshot.png)", RepositoryTree.Empty);

        Assert.Equal(MediaKind.Screenshot, media.Primary!.Kind);
    }

    [Fact]
    public void Images_in_the_file_listing_are_found_without_another_request()
    {
        var tree = new RepositoryTree
        {
            Entries =
            [
                new RepositoryTreeEntry { Path = "screenshots/main.png", Type = "blob", Size = 250_000 },
                new RepositoryTreeEntry { Path = "src/Program.cs", Type = "blob", Size = 500 }
            ]
        };

        var media = _service.Discover(Repository(), null, tree);

        Assert.Contains(media.Candidates, c => c.Url.Contains("screenshots/main.png"));
        Assert.Equal(MediaKind.Screenshot, media.Primary!.Kind);
    }

    [Fact]
    public void Test_fixtures_and_vendored_images_are_ignored()
    {
        var tree = new RepositoryTree
        {
            Entries =
            [
                new RepositoryTreeEntry { Path = "tests/fixtures/sample.png", Type = "blob", Size = 90_000 },
                new RepositoryTreeEntry { Path = "node_modules/thing/logo.png", Type = "blob", Size = 90_000 }
            ]
        };

        var media = _service.Discover(Repository(), null, tree);

        Assert.DoesNotContain(media.Candidates, c => c.Url.Contains("fixtures"));
        Assert.DoesNotContain(media.Candidates, c => c.Url.Contains("node_modules"));
    }

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("//evil.invalid/tracker.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/x.png")]
    public void Addresses_that_are_not_plain_web_links_are_refused(string url)
    {
        // A README is untrusted content; RepoDeck will not follow arbitrary schemes.
        var media = _service.Discover(Repository(), $"![x]({url})", RepositoryTree.Empty);

        Assert.DoesNotContain(media.Candidates, c => c.Url.Contains("evil")
                                                     || c.Url.StartsWith("data:")
                                                     || c.Url.StartsWith("javascript:")
                                                     || c.Url.StartsWith("file:"));
    }

    [Fact]
    public void A_readme_full_of_badges_still_falls_back_to_the_preview_card()
    {
        const string readme = """
            [![Build](https://img.shields.io/badge/build-passing-green.svg)](https://ci.invalid)
            [![Coverage](https://codecov.io/gh/a/b/badge.svg)](https://codecov.invalid)
            [![Sponsor](https://ko-fi.com/img/button.png)](https://ko-fi.invalid)
            """;

        var media = _service.Discover(Repository(), readme, RepositoryTree.Empty);

        // Every badge rejected, and the card is still not empty.
        Assert.Equal(MediaKind.SocialPreview, media.Primary!.Kind);
        Assert.DoesNotContain(media.Candidates, c => c.Url.Contains("shields.io"));
        Assert.DoesNotContain(media.Candidates, c => c.Url.Contains("ko-fi"));
    }

    [Fact]
    public void The_cheap_search_result_version_costs_no_repository_knowledge()
    {
        var media = _service.DiscoverFromMetadata(Repository());

        Assert.True(media.HasMedia);
        Assert.Equal(MediaKind.SocialPreview, media.Primary!.Kind);
    }
}
