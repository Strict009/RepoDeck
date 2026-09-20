using RepoDeck.Infrastructure;

namespace RepoDeck.Tests;

/// <summary>
/// Tidying a description without rewriting it.
/// </summary>
/// <remarks>
/// Every one of these came from something real. GitHub descriptions carry emoji shortcodes,
/// newlines, URLs, version numbers and product names, and RepoDeck shows them on a card next
/// to twenty-nine others. The rule these tests protect: remove markup and shorten, never
/// paraphrase, and never invent a description for a project that has none.
/// </remarks>
public class DescriptionCleanerTests
{
    // ---- Nothing in, nothing out ------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public void Nothing_to_show_comes_back_empty(string? input)
    {
        // Not "No description was provided" - that sentence is the view model's decision,
        // and a cleaner that invents text would put words in a stranger's mouth.
        Assert.Equal("", DescriptionCleaner.Clean(input));
    }

    [Fact]
    public void A_description_that_was_only_shortcodes_comes_back_empty()
    {
        Assert.Equal("", DescriptionCleaner.Clean(":rocket: :fire:"));
    }

    // ---- Emoji shortcodes -------------------------------------------------

    [Fact]
    public void A_leading_shortcode_is_removed()
    {
        Assert.Equal(
            "Wow, such a beautiful HTML5 music player.",
            DescriptionCleaner.Clean(":lollipop: Wow, such a beautiful HTML5 music player."));
    }

    [Fact]
    public void Adjacent_shortcodes_are_removed_as_one_run()
    {
        // Seen in production: ":notes::arrow_forward:" on a music player card.
        Assert.Equal(
            "From UI Proposal to Code",
            DescriptionCleaner.Clean("From UI Proposal to Code :notes::arrow_forward:"));
    }

    [Fact]
    public void A_shortcode_before_punctuation_is_removed()
    {
        Assert.Equal("A fast editor.", DescriptionCleaner.Clean("A fast editor :zap:."));
    }

    [Fact]
    public void A_shortcode_in_the_middle_is_removed_without_eating_its_neighbours()
    {
        Assert.Equal("Fast and small", DescriptionCleaner.Clean("Fast :zap: and small"));
    }

    // ---- Things that merely contain colons --------------------------------

    [Fact]
    public void A_url_is_not_mistaken_for_a_shortcode()
    {
        const string text = "Docs at https://example.com/a:b for details";

        Assert.Equal(text, DescriptionCleaner.Clean(text));
    }

    [Fact]
    public void A_namespace_or_time_is_left_alone()
    {
        Assert.Equal("Runs std::vector at 12:30", DescriptionCleaner.Clean("Runs std::vector at 12:30"));
    }

    [Fact]
    public void A_colon_delimited_label_is_left_alone()
    {
        const string text = "Note: this is a library, not an application";

        Assert.Equal(text, DescriptionCleaner.Clean(text));
    }

    // ---- Whitespace -------------------------------------------------------

    [Fact]
    public void Newlines_and_runs_of_spaces_collapse_to_single_spaces()
    {
        Assert.Equal(
            "A video editor that runs in the browser",
            DescriptionCleaner.Clean("  A video   editor\r\n  that runs\tin the browser  "));
    }

    // ---- Sentence casing --------------------------------------------------

    [Fact]
    public void Ordinary_lower_case_prose_gets_its_first_letter_back()
    {
        Assert.Equal(
            "Open source short video automatic generation tool.",
            DescriptionCleaner.Clean("open source short video automatic generation tool."));
    }

    [Fact]
    public void A_description_that_already_has_capitals_is_left_alone()
    {
        // Somebody cased this deliberately, even if oddly.
        const string text = "ai agent video editor use with ElevenLabs Scribe";

        Assert.Equal(text, DescriptionCleaner.Clean(text));
    }

    [Theory]
    [InlineData("ffmpeg wrapper for the terminal")]
    [InlineData("x11-utils replacement")]
    [InlineData("node.js bindings for sqlite")]
    [InlineData("7zip archive tool")]
    [InlineData("https://example.com is the home page")]
    public void A_code_like_first_word_is_never_capitalised(string text)
    {
        Assert.Equal(text, DescriptionCleaner.Clean(text));
    }

    [Fact]
    public void Nothing_else_in_the_sentence_is_recased()
    {
        Assert.Equal(
            "Converts png to webp and avif.",
            DescriptionCleaner.Clean("converts png to webp and avif."));
    }

    [Fact]
    public void A_lower_case_label_that_is_not_a_sentence_keeps_its_case()
    {
        // "ffmpeg wrapper for the terminal" has no closing punctuation and is a label, not
        // a sentence. Capitalising it would rename the tool.
        Assert.Equal(
            "converts png to webp and avif",
            DescriptionCleaner.Clean("converts png to webp and avif"));
    }

    // ---- Shortening -------------------------------------------------------

    [Fact]
    public void Text_within_the_limit_is_untouched()
    {
        const string text = "A small tool.";

        Assert.Equal(text, DescriptionCleaner.Clean(text, 100));
    }

    [Fact]
    public void A_limit_of_zero_means_do_not_shorten()
    {
        var text = new string('a', 500);

        Assert.Equal(500, DescriptionCleaner.Clean(text).Length);
    }

    [Fact]
    public void Shortening_prefers_to_stop_where_a_sentence_stopped()
    {
        const string text = "A lightweight video editor. It also serves as a showcase for the engine.";

        var result = DescriptionCleaner.Clean(text, 40);

        Assert.Equal("A lightweight video editor.", result);
        Assert.DoesNotContain("…", result);
    }

    [Fact]
    public void A_sentence_that_would_cost_too_much_is_trimmed_at_a_word_instead()
    {
        // The first sentence ends at 8 characters of a 60 character budget. Stopping there
        // would throw away most of what the project said, so trim later and mark it.
        const string text = "Hi there. A capable and pleasant editor for people who edit video often.";

        var result = DescriptionCleaner.Clean(text, 60);

        Assert.EndsWith("…", result);
        Assert.True(result.Length <= 61, $"was {result.Length}: {result}");
    }

    [Fact]
    public void Trimming_happens_at_a_word_boundary()
    {
        const string text = "An extremely capable and thoroughly pleasant application for editing";

        var result = DescriptionCleaner.Clean(text, 30);

        Assert.EndsWith("…", result);
        Assert.DoesNotContain("thorou…", result);
    }

    [Fact]
    public void One_very_long_word_is_still_cut()
    {
        var result = DescriptionCleaner.Clean(new string('x', 200), 40);

        Assert.EndsWith("…", result);
        Assert.True(result.Length <= 41);
    }

    // ---- What must not count as the end of a sentence ---------------------

    [Fact]
    public void A_decimal_point_does_not_end_a_sentence()
    {
        const string text = "Requires version 1.5 or newer and a reasonably modern machine.";

        var result = DescriptionCleaner.Clean(text, 30);

        Assert.DoesNotContain("1.", result[^3..]);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void A_dot_inside_a_domain_does_not_end_a_sentence()
    {
        const string text = "Download from example.com or build it yourself from source today.";

        var result = DescriptionCleaner.Clean(text, 32);

        Assert.EndsWith("…", result);
        Assert.DoesNotContain("example.com or build it yourself from", result);
    }

    [Theory]
    [InlineData("Works with Node.js, npm, etc. and a few other things besides that one.")]
    [InlineData("Supports PNG, JPEG, e.g. the common ones, and quite a few rarer formats.")]
    public void A_common_abbreviation_does_not_end_a_sentence(string text)
    {
        var result = DescriptionCleaner.Clean(text, 40);

        Assert.EndsWith("…", result);
    }

    [Fact]
    public void A_real_sentence_ending_is_still_found_after_an_abbreviation()
    {
        const string text = "Handles PNG, JPEG, etc. quickly. And it does a great deal more besides.";

        Assert.Equal("Handles PNG, JPEG, etc. quickly.", DescriptionCleaner.Clean(text, 40));
    }

    // ---- The source is never touched --------------------------------------

    [Fact]
    public void Cleaning_does_not_mutate_or_reformat_beyond_its_remit()
    {
        const string original = ":rocket: A tool for people";

        var cleaned = DescriptionCleaner.Clean(original);

        Assert.Equal(":rocket: A tool for people", original);
        Assert.Equal("A tool for people", cleaned);
    }

    [Fact]
    public void Punctuation_and_wording_inside_the_sentence_survive_intact()
    {
        const string text = "A free, open-source, quickly evolving file manager (explorer / browser)";

        Assert.Equal(text, DescriptionCleaner.Clean(text));
    }

    [Fact]
    public void A_hostile_description_does_not_take_the_page_down()
    {
        // 20k characters of colons is the shape that kills a careless regex.
        var nasty = string.Concat(Enumerable.Repeat(":a", 10_000));

        var exception = Record.Exception(() => DescriptionCleaner.Clean(nasty, 80));

        Assert.Null(exception);
    }
}
