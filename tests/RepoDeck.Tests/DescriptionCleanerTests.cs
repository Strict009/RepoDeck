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

    // Adjacent shortcodes with punctuation between them were the defect: matching one at a
    // time removed the first, after which the second no longer began at a token boundary and
    // survived. ":rocket:,:fire:" left ",:fire:" on the card.

    [Theory]
    [InlineData(":rocket:,:fire:")]
    [InlineData(":rocket::fire:")]
    [InlineData(":rocket: :fire:")]
    [InlineData(":rocket:, :fire:")]
    [InlineData(":rocket:;:fire:")]
    [InlineData(":rocket:  :fire:  :zap:")]
    [InlineData(":a::b::c::d:")]
    public void A_run_of_shortcodes_leaves_nothing_behind(string text)
    {
        Assert.Equal("", DescriptionCleaner.Clean(text));
    }

    [Theory]
    [InlineData(":rocket:,:fire: A fast thing", "A fast thing")]
    [InlineData("A fast thing :rocket:,:fire:", "A fast thing")]
    [InlineData("Fast :rocket:,:fire: and small", "Fast and small")]
    public void A_run_of_shortcodes_beside_real_words_leaves_only_the_words(
        string text, string expected)
    {
        Assert.Equal(expected, DescriptionCleaner.Clean(text));
    }

    [Fact]
    public void Punctuation_that_belongs_to_the_sentence_survives_a_neighbouring_run()
    {
        // The comma here separates clauses; the one inside a run does not.
        Assert.Equal(
            "Fast, small and free",
            DescriptionCleaner.Clean("Fast :zap:, small and free"));
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

    // ---- Casing is never touched ------------------------------------------
    //
    // An earlier version capitalised the first word of all-lowercase descriptions, guarded
    // by "only if it ends in sentence punctuation". That guard was not enough: it renamed
    // ffmpeg, ripgrep and ios, all of which write themselves in lower case and all of which
    // punctuate their descriptions. No rule can tell a lowercase product name from a
    // lowercase word by looking at the word, so RepoDeck stopped trying.

    [Theory]
    [InlineData("ffmpeg wrapper for the terminal.")]
    [InlineData("ripgrep searches files.")]
    [InlineData("ios music player.")]
    [InlineData("npm install helper.")]
    [InlineData("curl for humans.")]
    [InlineData("youtube-dl fork with extra features.")]
    [InlineData("nginx configuration generator!")]
    [InlineData("kubectl plugin manager?")]
    public void A_punctuated_lower_case_product_name_is_never_recased(string text)
    {
        Assert.Equal(text, DescriptionCleaner.Clean(text));
    }

    [Theory]
    [InlineData("iOS and iPadOS media player.")]
    [InlineData(".NET tooling for the command line.")]
    [InlineData("C# source generator.")]
    [InlineData("C++ bindings for SQLite.")]
    [InlineData("HTTP/2 and gRPC client.")]
    [InlineData("eBPF observability toolkit.")]
    [InlineData("macOS menu bar utility.")]
    public void Mixed_case_names_and_acronyms_survive_exactly(string text)
    {
        Assert.Equal(text, DescriptionCleaner.Clean(text));
    }

    [Fact]
    public void Ordinary_lower_case_prose_is_also_left_alone()
    {
        // RepoDeck would rather show a sentence that starts in lower case than rename a
        // project. The author's casing is the author's business.
        Assert.Equal(
            "open source short video automatic generation tool.",
            DescriptionCleaner.Clean("open source short video automatic generation tool."));
    }

    [Fact]
    public void Removing_a_leading_shortcode_does_not_recase_what_follows()
    {
        Assert.Equal(
            "ffmpeg wrapper.",
            DescriptionCleaner.Clean(":rocket: ffmpeg wrapper."));
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

    [Fact]
    public void An_abbreviation_near_the_cut_is_not_treated_as_anything_special()
    {
        // Shortening no longer hunts for sentence endings, so "U.S." and "etc." cannot be
        // mistaken for one. It cuts at a word and says so.
        const string text = "Works across the U.S. and several other regions besides this one.";

        var result = DescriptionCleaner.Clean(text, 40);

        Assert.EndsWith("…", result);
        Assert.True(result.Length <= 41, $"was {result.Length}");
    }

    // ---- Shortening never produces broken text ----------------------------

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        // Each rocket is two UTF-16 chars. A naive slice at an odd index produces a lone
        // surrogate, which is not valid Unicode and renders as a replacement character.
        var text = string.Concat(Enumerable.Repeat("\U0001F680", 40));

        for (var limit = 1; limit <= 40; limit++)
        {
            var result = DescriptionCleaner.Clean(text, limit);

            Assert.False(
                result.Any(char.IsSurrogate) && !IsWellFormed(result),
                $"limit {limit} produced a broken pair: {result.Length} chars");
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void A_cut_around_an_emoji_boundary_stays_well_formed(int limit)
    {
        var result = DescriptionCleaner.Clean("abc \U0001F680\U0001F680 def ghi jkl", limit);

        Assert.True(IsWellFormed(result), $"limit {limit} gave {result}");
    }

    [Fact]
    public void A_cut_does_not_split_a_joined_emoji_sequence()
    {
        // A family emoji is several code points joined by zero-width joiners. Cutting inside
        // it leaves orphaned members rather than one character.
        const string family = "\U0001F468‍\U0001F469‍\U0001F467";
        var text = family + " a family of tools for doing things";

        var result = DescriptionCleaner.Clean(text, 12);

        Assert.True(IsWellFormed(result));
        Assert.DoesNotContain('‍', result[^1..]);
    }

    [Fact]
    public void Non_latin_text_is_cut_without_damage()
    {
        var result = DescriptionCleaner.Clean("一个第三方音乐播放器", 5);

        Assert.True(IsWellFormed(result));
    }

    private static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
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
