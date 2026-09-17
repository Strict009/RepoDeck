using RepoDeck.Models;
using RepoDeck.Services.Media;

namespace RepoDeck.Tests;

/// <summary>
/// Choosing a picture is mostly a rejection problem: a typical README opens with eight
/// badges, a sponsor button and a licence shield before it reaches a screenshot.
/// </summary>
public class MediaRankerTests
{
    private static MediaCandidate Readme(string url, string? alt = null) =>
        new() { Url = url, Source = MediaSource.Readme, Description = alt };

    private static MediaCandidate File(string url, long? size = null) =>
        new() { Url = url, Source = MediaSource.RepositoryFile, SizeBytes = size };

    [Theory]
    [InlineData("https://img.shields.io/badge/build-passing-green.svg")]
    [InlineData("https://shields.io/badge/licence-MIT-blue.png")]
    [InlineData("https://badgen.net/badge/version/1.2.3")]
    [InlineData("https://travis-ci.org/someone/tool.svg?branch=main")]
    [InlineData("https://codecov.io/gh/someone/tool/branch/main/graph/badge.png")]
    [InlineData("https://coveralls.io/repos/github/someone/tool/badge.png")]
    [InlineData("https://circleci.com/gh/someone/tool.svg?style=shield")]
    [InlineData("https://github.com/someone/tool/actions/workflows/ci.yml/badge.svg")]
    public void Build_and_coverage_badges_are_never_shown(string url)
    {
        var classified = MediaRanker.Classify(Readme(url));

        Assert.Equal(MediaKind.Badge, classified.Kind);
        Assert.True(classified.IsExcluded);
    }

    [Theory]
    [InlineData("https://img.buymeacoffee.com/button.png")]
    [InlineData("https://ko-fi.com/img/githubbutton_sm.png")]
    [InlineData("https://www.paypalobjects.com/donate.png")]
    [InlineData("https://opencollective.com/tool/backers.png")]
    public void Sponsorship_buttons_are_never_shown(string url)
    {
        Assert.True(MediaRanker.Classify(Readme(url)).IsExcluded);
    }

    [Fact]
    public void An_SVG_is_set_aside_rather_than_shown_broken()
    {
        // Almost always a badge or diagram, and not decodable without another dependency.
        Assert.True(MediaRanker.Classify(Readme("https://example.invalid/diagram.svg")).IsExcluded);
    }

    [Fact]
    public void A_screenshot_outranks_a_logo_which_outranks_a_social_preview()
    {
        var media = MediaRanker.Rank([
            new MediaCandidate { Url = "https://example.invalid/x", Source = MediaSource.SocialPreview },
            Readme("https://example.invalid/logo.png"),
            Readme("https://example.invalid/screenshot-main.png")
        ]);

        Assert.Equal(MediaKind.Screenshot, media.Primary!.Kind);
        Assert.Equal(3, media.Candidates.Count);
        Assert.Equal(MediaKind.Logo, media.Candidates[1].Kind);
        Assert.Equal(MediaKind.SocialPreview, media.Candidates[2].Kind);
    }

    [Theory]
    [InlineData("https://example.invalid/docs/screenshot.png")]
    [InlineData("https://example.invalid/screenshots/main-window.png")]
    [InlineData("https://example.invalid/demo.gif")]
    [InlineData("https://example.invalid/preview-1.jpg")]
    [InlineData("https://example.invalid/app-in-action.png")]
    public void Screenshots_are_recognised_from_their_names(string url)
    {
        Assert.Equal(MediaKind.Screenshot, MediaRanker.Classify(Readme(url)).Kind);
    }

    [Fact]
    public void Alt_text_can_identify_a_screenshot_when_the_name_does_not()
    {
        var classified = MediaRanker.Classify(
            Readme("https://example.invalid/media/1.png", "Screenshot of the main window"));

        Assert.Equal(MediaKind.Screenshot, classified.Kind);
    }

    [Fact]
    public void A_tiny_image_loses_to_a_substantial_one()
    {
        var media = MediaRanker.Rank([
            File("https://example.invalid/images/icon-small.png", 900),
            File("https://example.invalid/images/window.png", 400 * 1024)
        ]);

        Assert.Contains("window.png", media.Primary!.Url);
    }

    [Fact]
    public void A_badge_never_wins_even_when_it_is_the_only_image()
    {
        var media = MediaRanker.Rank([Readme("https://img.shields.io/badge/build-passing.png")]);

        Assert.False(media.HasMedia);
        Assert.Null(media.Primary);
    }

    [Fact]
    public void Non_images_are_dropped()
    {
        var media = MediaRanker.Rank([
            Readme("https://example.invalid/page.html"),
            Readme("https://example.invalid/notes.txt")
        ]);

        Assert.False(media.HasMedia);
    }

    [Fact]
    public void Duplicate_urls_appear_once()
    {
        var media = MediaRanker.Rank([
            Readme("https://example.invalid/screenshot.png"),
            Readme("https://example.invalid/screenshot.png"),
            File("https://example.invalid/screenshot.png", 100_000)
        ]);

        Assert.Single(media.Candidates);
    }

    [Fact]
    public void The_gallery_holds_screenshots_and_previews_but_not_logos()
    {
        var media = MediaRanker.Rank([
            Readme("https://example.invalid/screenshot-1.png"),
            Readme("https://example.invalid/screenshot-2.png"),
            Readme("https://example.invalid/logo.png"),
            new MediaCandidate { Url = "https://example.invalid/social", Source = MediaSource.SocialPreview }
        ]);

        Assert.Equal(3, media.Gallery.Count);
        Assert.DoesNotContain(media.Gallery, c => c.Kind == MediaKind.Logo);
    }

    [Fact]
    public void A_realistic_README_opening_yields_the_screenshot_and_nothing_else_first()
    {
        // The shape of an actual project README.
        var media = MediaRanker.Rank([
            Readme("https://img.shields.io/github/actions/workflow/status/a/b/ci.yml", "build status"),
            Readme("https://img.shields.io/github/license/a/b", "licence"),
            Readme("https://img.shields.io/github/downloads/a/b/total", "downloads"),
            Readme("https://ko-fi.com/img/githubbutton_sm.png", "buy me a coffee"),
            Readme("https://raw.githubusercontent.com/a/b/main/docs/screenshot.png", "The app"),
            Readme("https://raw.githubusercontent.com/a/b/main/assets/logo.png", "logo")
        ]);

        Assert.Equal(2, media.Candidates.Count);
        Assert.Contains("screenshot.png", media.Primary!.Url);
    }
}
