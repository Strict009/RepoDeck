using RepoDeck.Models;
using RepoDeck.Services.Readme;

namespace RepoDeck.Tests;

public class ReadmeParserTests
{
    [Fact]
    public void Headings_are_recognised_with_their_level()
    {
        var blocks = ReadmeParser.Parse("# Title\n\n## Installation\n");

        Assert.Collection(blocks,
            b => { Assert.Equal(ReadmeBlockKind.Heading, b.Kind); Assert.Equal(1, b.Level); Assert.Equal("Title", b.Text); },
            b => { Assert.Equal(ReadmeBlockKind.Heading, b.Kind); Assert.Equal(2, b.Level); Assert.Equal("Installation", b.Text); });
    }

    [Fact]
    public void Consecutive_lines_join_into_one_paragraph()
    {
        var blocks = ReadmeParser.Parse("This is a sentence\nthat wraps across lines.\n\nA second paragraph.");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("This is a sentence that wraps across lines.", blocks[0].Text);
        Assert.Equal("A second paragraph.", blocks[1].Text);
    }

    [Fact]
    public void Fenced_code_is_kept_verbatim()
    {
        var blocks = ReadmeParser.Parse("Run it:\n\n```bash\ndotnet run\n```\n");

        var code = Assert.Single(blocks, b => b.Kind == ReadmeBlockKind.Code);
        Assert.Equal("dotnet run", code.Text);
    }

    [Fact]
    public void An_unterminated_code_fence_still_yields_its_content()
    {
        var blocks = ReadmeParser.Parse("```\nnot closed\n");

        var code = Assert.Single(blocks, b => b.Kind == ReadmeBlockKind.Code);
        Assert.Equal("not closed", code.Text);
    }

    [Fact]
    public void Bullet_and_numbered_items_become_list_items()
    {
        var blocks = ReadmeParser.Parse("- first\n* second\n1. third\n");

        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(ReadmeBlockKind.ListItem, b.Kind));
        Assert.Equal(["first", "second", "third"], blocks.Select(b => b.Text));
    }

    [Fact]
    public void Block_quotes_are_recognised()
    {
        var blocks = ReadmeParser.Parse("> a warning worth reading\n");

        var quote = Assert.Single(blocks);
        Assert.Equal(ReadmeBlockKind.Quote, quote.Kind);
        Assert.Equal("a warning worth reading", quote.Text);
    }

    [Fact]
    public void Horizontal_rules_are_dropped()
    {
        var blocks = ReadmeParser.Parse("Text above.\n\n---\n\nText below.");

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(ReadmeBlockKind.Paragraph, b.Kind));
    }

    [Theory]
    [InlineData("[the docs](https://example.invalid)", "the docs")]
    [InlineData("![badge](https://img.invalid/badge.svg)", "")]
    [InlineData("**bold** text", "bold text")]
    [InlineData("use `dotnet build` now", "use dotnet build now")]
    [InlineData("<p>html</p>", "html")]
    [InlineData("a &amp; b", "a & b")]
    public void Inline_markup_is_reduced_to_readable_text(string input, string expected)
    {
        Assert.Equal(expected, ReadmeParser.CleanInline(input));
    }

    [Fact]
    public void Badge_rows_do_not_become_the_introduction()
    {
        const string readme = """
        # Project

        [![build](https://img.invalid/b.svg)](https://ci.invalid) [![licence](https://img.invalid/l.svg)](https://licence.invalid)

        Project is a tool that converts one kind of file into another kind of file, quickly.
        """;

        var intro = ReadmeParser.ExtractIntroduction(readme);

        Assert.NotNull(intro);
        Assert.StartsWith("Project is a tool", intro);
    }

    [Fact]
    public void Introduction_is_null_when_there_is_no_prose()
    {
        Assert.Null(ReadmeParser.ExtractIntroduction("# Title\n\n- a\n- b\n"));
        Assert.Null(ReadmeParser.ExtractIntroduction(null));
        Assert.Null(ReadmeParser.ExtractIntroduction("   "));
    }

    [Fact]
    public void Long_introductions_are_truncated_on_a_word_boundary()
    {
        var readme = "x " + string.Join(" ", Enumerable.Repeat("word", 300));

        var intro = ReadmeParser.ExtractIntroduction(readme, maxLength: 100);

        Assert.NotNull(intro);
        Assert.True(intro!.Length <= 103, $"Introduction was {intro.Length} characters.");
        Assert.EndsWith("...", intro);
    }

    [Fact]
    public void Headings_can_be_listed_for_the_explanation_layer()
    {
        const string readme = "# Tool\n\n## Installation\n\n## Usage\n\n#### Too deep\n";

        var headings = ReadmeParser.ExtractHeadings(readme);

        Assert.Equal(["Tool", "Installation", "Usage"], headings);
    }

    [Fact]
    public void Parsing_is_safe_for_empty_input()
    {
        Assert.Empty(ReadmeParser.Parse(null));
        Assert.Empty(ReadmeParser.Parse(""));
        Assert.Empty(ReadmeParser.Parse("    \n\n  "));
    }
}
