using RepoDeck.Services.Readme;

namespace RepoDeck.Tests;

/// <summary>
/// README content is untrusted markup from a stranger, and it reaches a novice user as
/// the answer to "what is this?". Markup must never survive the trip.
/// </summary>
public class ReadmeHtmlStrippingTests
{
    [Fact]
    public void A_long_real_world_image_tag_is_stripped_rather_than_shown_as_text()
    {
        // Found on screen: this exact shape of tag was 230-odd characters, the strip was
        // bounded at 200, and the whole tag was presented to the user as the project's
        // plain-English description.
        const string tag =
            "<img src=\"https://api.hellogithub.com/v1/widgets/recommend.svg?"
            + "rid=1ad354e5ab404301919665ac7973cd07&claim_uid=CeVqou2T1dIvfQP&theme=neutral\" "
            + "alt=\"Featured | HelloGitHub\" style=\"width: 250px; height: 54px;\" "
            + "width=\"250\" height=\"54\" />";

        Assert.True(tag.Length > 200, "the fixture no longer exercises the old bound");

        var cleaned = ReadmeParser.CleanInline(tag + " A music player for your desktop.");

        Assert.DoesNotContain("<img", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hellogithub", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A music player for your desktop.", cleaned);
    }

    [Fact]
    public void An_introduction_built_from_such_a_readme_is_prose_and_not_markup()
    {
        var markdown = """
            # MusicPlayer2

            <img src="https://api.hellogithub.com/v1/widgets/recommend.svg?rid=1ad354e5ab404301919665ac7973cd07&claim_uid=CeVqou2T1dIvfQP&theme=neutral" alt="Featured | HelloGitHub" style="width: 250px; height: 54px;" width="250" height="54" />

            A powerful local music player with lyric display, tag editing and format conversion.
            """;

        var intro = ReadmeParser.ExtractIntroduction(markdown);

        Assert.NotNull(intro);
        Assert.DoesNotContain("<", intro!, StringComparison.Ordinal);
        Assert.DoesNotContain("svg", intro, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("music player", intro, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<div align=\"center\">Centred text</div>", "Centred text")]
    [InlineData("<p>A paragraph</p>", "A paragraph")]
    [InlineData("<a href=\"https://example.invalid\">A link</a>", "A link")]
    [InlineData("<h1 class=\"title\" id=\"top\" data-thing=\"x\">Heading</h1>", "Heading")]
    public void Ordinary_html_is_reduced_to_its_text(string markup, string expected)
    {
        Assert.Contains(expected, ReadmeParser.CleanInline(markup));
        Assert.DoesNotContain("<", ReadmeParser.CleanInline(markup), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tag_beyond_any_plausible_length_is_left_alone_rather_than_scanned_forever()
    {
        // The bound still exists. Input from a stranger does not get an unbounded scan,
        // and the match timeout is the backstop behind that.
        var absurd = "<img " + new string('x', 8000) + " />";

        var cleaned = ReadmeParser.CleanInline(absurd + " Real text here.");

        Assert.Contains("Real text here.", cleaned);
    }

    [Fact]
    public void Stripping_never_throws_on_hostile_input()
    {
        string[] nasty =
        [
            "<<<<<<<<",
            new string('<', 5000),
            "<img src=\"" + new string('a', 100_000) + "\">",
            "<<>><<>>text<<>>"
        ];

        foreach (var input in nasty)
        {
            var cleaned = ReadmeParser.CleanInline(input);
            Assert.NotNull(cleaned);
        }
    }
}

/// <summary>
/// Table markup reaching a plain-English summary, which it did: a donation table's
/// header and its row of dashes were shown as the answer to "what is this?".
/// </summary>
public class ReadmeTableStrippingTests
{
    [Fact]
    public void A_table_rule_never_becomes_part_of_a_description()
    {
        var markdown = """
            # AlgerMusicPlayer

            | Sponsors | WeChat | Alipay |
            |:---------|:-------|:-------|
            | Yes      | Yes    | Yes    |

            A third-party music player with local services and desktop lyrics.
            """;

        var intro = ReadmeParser.ExtractIntroduction(markdown);

        Assert.NotNull(intro);
        Assert.DoesNotContain("---", intro!, StringComparison.Ordinal);
        Assert.DoesNotContain("|", intro, StringComparison.Ordinal);
        Assert.Contains("music player", intro, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("|:-----|:-----|")]
    [InlineData("| --- | --- |")]
    [InlineData("|---|---|---|")]
    [InlineData("  |:--------|-------:|  ")]
    [InlineData("--- | ---")]
    public void Rows_of_dashes_and_pipes_are_dropped_entirely(string rule)
    {
        var blocks = ReadmeParser.Parse(rule + "\n\nSome genuine prose about the project here.");

        Assert.DoesNotContain(blocks, b => b.Text.Contains('|', StringComparison.Ordinal));
    }

    [Fact]
    public void Ordinary_prose_containing_a_dash_survives()
    {
        // The rule must not eat a sentence merely for containing punctuation.
        const string prose = "A media player - fast, small and free - for your desktop machine.";

        var intro = ReadmeParser.ExtractIntroduction(prose);

        Assert.Equal(prose, intro);
    }

    [Fact]
    public void Prose_containing_a_single_pipe_survives()
    {
        const string prose = "Pipe the output through grep | less to page through the results yourself.";

        Assert.Equal(prose, ReadmeParser.ExtractIntroduction(prose));
    }
}
