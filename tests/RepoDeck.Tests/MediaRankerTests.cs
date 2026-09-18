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

    [Theory]
    [InlineData("https://fdroid.gitlab.io/artwork/badge/get-it-on.png")]
    [InlineData("https://f-droid.org/badge/get-it-on.png")]
    [InlineData("https://play.google.com/intl/en_us/badges/images/generic/en_badge_web_generic.png")]
    [InlineData("https://example.invalid/assets/google-play-badge.png")]
    [InlineData("https://example.invalid/images/available-on-f-droid.png")]
    [InlineData("https://example.invalid/docs/download-on-the-app-store.png")]
    [InlineData("https://snapcraft.io/static/images/badges/en/snap-store-black.png")]
    public void App_store_buttons_are_never_shown(string url)
    {
        // "Get it on F-Droid" is a picture of somebody else's logo. It was being adopted
        // as a project's screenshot and filling the card with a store banner.
        Assert.True(MediaRanker.Classify(Readme(url)).IsExcluded, url);
    }

    [Theory]
    [InlineData("Get it on F-Droid")]
    [InlineData("Available on the App Store")]
    [InlineData("Download on the Mac App Store")]
    public void A_store_button_is_recognised_from_its_alt_text_alone(string alt)
    {
        var classified = MediaRanker.Classify(Readme("https://example.invalid/media/1.png", alt));

        Assert.True(classified.IsExcluded, alt);
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/a/b/master/art/google_play_badge.png")]
    [InlineData("https://raw.githubusercontent.com/a/b/master/art/googleplay.png")]
    [InlineData("https://example.invalid/assets/get_it_on_f_droid.png")]
    [InlineData("https://example.invalid/assets/app%20store%20badge.png")]
    [InlineData("https://developer.android.com/images/brand/en_generic_rgb_wo_60.png")]
    [InlineData("https://developer.android.com/images/brand/en_app_rgb_wo_45.png")]
    public void A_store_badge_is_recognised_whichever_separators_it_uses(string url)
    {
        // A Play Store badge saved as "google_play_badge.png" slipped past a word list
        // written with hyphens and became a project's hero image.
        Assert.True(MediaRanker.Classify(Readme(url)).IsExcluded, url);
    }

    [Fact]
    public void A_vendor_badge_in_a_brand_folder_is_not_the_projects_logo()
    {
        // Google serves its Play badge from developer.android.com/images/brand/ under a
        // filename that says nothing about stores. The word "brand" in the path made it
        // look like the project's own logo, and it filled the card.
        var classified = MediaRanker.Classify(
            Readme("https://developer.android.com/images/brand/en_generic_rgb_wo_60.png"));

        Assert.True(classified.IsExcluded);
        Assert.NotEqual(MediaKind.Logo, classified.Kind);
    }

    [Fact]
    public void A_store_badge_never_becomes_a_cards_artwork()
    {
        // The whole failure, end to end: a README whose only image is a store banner must
        // leave the card showing its own designed tile.
        var media = MediaRanker.Rank([
            Readme("https://fdroid.gitlab.io/artwork/badge/get-it-on.png", "Get it on F-Droid"),
            new MediaCandidate { Url = "https://example.invalid/social", Source = MediaSource.SocialPreview }
        ]);

        Assert.Null(media.PrimaryArtwork);
        Assert.False(media.HasArtwork);
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
    public void The_gallery_holds_the_projects_own_pictures_with_the_generated_card_last()
    {
        // A logo is the project's own artwork and belongs in the strip. GitHub's generated
        // card is not the project's artwork at all, so it brings up the rear.
        var media = MediaRanker.Rank([
            Readme("https://example.invalid/screenshot-1.png"),
            Readme("https://example.invalid/screenshot-2.png"),
            Readme("https://example.invalid/logo.png"),
            new MediaCandidate { Url = "https://example.invalid/social", Source = MediaSource.SocialPreview }
        ]);

        Assert.Equal(4, media.Gallery.Count);
        Assert.Equal(MediaKind.SocialPreview, media.Gallery[^1].Kind);
        Assert.Contains(media.Gallery, c => c.Kind == MediaKind.Logo);
    }

    [Fact]
    public void A_generated_preview_card_loses_to_any_picture_the_project_supplied()
    {
        // GitHub's card is a rendering of the name and description the user has already
        // read. Anything the project chose to publish tells them more.
        string[] ordinary =
        [
            "https://example.invalid/images/window.png",
            "https://example.invalid/logo.png",
            "https://example.invalid/docs/figure-3.png",
            "https://example.invalid/anything-at-all.jpg"
        ];

        foreach (var url in ordinary)
        {
            var media = MediaRanker.Rank([
                new MediaCandidate { Url = "https://example.invalid/social", Source = MediaSource.SocialPreview },
                Readme(url)
            ]);

            Assert.NotEqual(MediaKind.SocialPreview, media.Primary!.Kind);
            Assert.Equal(url, media.Primary.Url);
        }
    }

    [Fact]
    public void The_generated_card_is_still_used_when_there_is_nothing_else()
    {
        // Fallback media, not preferred media. It still beats an empty tile on a surface
        // big enough to read it.
        var media = MediaRanker.Rank([
            new MediaCandidate { Url = "https://example.invalid/social", Source = MediaSource.SocialPreview }
        ]);

        Assert.Equal(MediaKind.SocialPreview, media.Primary!.Kind);
        Assert.True(media.HasMedia);
    }

    [Fact]
    public void Artwork_means_the_projects_own_pictures_and_never_the_generated_card()
    {
        // The card grid asks for artwork, because at 300px wide the generated card is
        // unreadable text pretending to be a screenshot.
        var onlySocial = MediaRanker.Rank([
            new MediaCandidate { Url = "https://example.invalid/social", Source = MediaSource.SocialPreview }
        ]);

        Assert.Null(onlySocial.PrimaryArtwork);
        Assert.False(onlySocial.HasArtwork);
        Assert.NotNull(onlySocial.Primary);

        var withScreenshot = MediaRanker.Rank([
            new MediaCandidate { Url = "https://example.invalid/social", Source = MediaSource.SocialPreview },
            Readme("https://example.invalid/screenshot.png")
        ]);

        Assert.True(withScreenshot.HasArtwork);
        Assert.Contains("screenshot.png", withScreenshot.PrimaryArtwork!.Url);
    }

    [Fact]
    public void A_tiny_image_still_loses_to_the_generated_card()
    {
        // Demoting the card must not promote a 900-byte spacer above it.
        var media = MediaRanker.Rank([
            new MediaCandidate { Url = "https://example.invalid/social", Source = MediaSource.SocialPreview },
            File("https://example.invalid/images/spacer.png", 900)
        ]);

        Assert.Equal(MediaKind.SocialPreview, media.Primary!.Kind);
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
