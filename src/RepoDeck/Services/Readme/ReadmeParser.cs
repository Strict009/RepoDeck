using System.Text;
using System.Text.RegularExpressions;
using RepoDeck.Models;

namespace RepoDeck.Services.Readme;

/// <summary>
/// A deliberately small markdown reader: enough to show a README as readable text
/// without pulling in a full markdown rendering dependency for the MVP.
/// It is lossy on purpose - links become their text, images and badges disappear.
/// </summary>
public static partial class ReadmeParser
{
    private const int MaxBlocks = 400;

    /// <summary>
    /// README content is untrusted input from a stranger on the internet. It is capped
    /// before parsing, and every regex carries a match timeout, so a hostile or merely
    /// enormous README cannot hang the UI thread.
    /// </summary>
    private const int MaxInputCharacters = 512 * 1024;

    /// <summary>Breaks README markdown into displayable blocks.</summary>
    public static IReadOnlyList<ReadmeBlock> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        if (markdown.Length > MaxInputCharacters) markdown = markdown[..MaxInputCharacters];

        var blocks = new List<ReadmeBlock>();
        var paragraph = new StringBuilder();
        var inFence = false;
        var fenceContent = new StringBuilder();

        foreach (var rawLine in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (blocks.Count >= MaxBlocks) break;
            var line = rawLine.TrimEnd();

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inFence)
                {
                    AddCode(blocks, fenceContent.ToString());
                    fenceContent.Clear();
                    inFence = false;
                }
                else
                {
                    FlushParagraph(blocks, paragraph);
                    inFence = true;
                }
                continue;
            }

            if (inFence)
            {
                if (fenceContent.Length < 4000) fenceContent.Append(line).Append('\n');
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(blocks, paragraph);
                continue;
            }

            var headingMatch = HeadingPattern().Match(line);
            if (headingMatch.Success)
            {
                FlushParagraph(blocks, paragraph);
                var text = CleanInline(headingMatch.Groups[2].Value);
                if (text.Length > 0)
                {
                    blocks.Add(new ReadmeBlock
                    {
                        Kind = ReadmeBlockKind.Heading,
                        Level = Math.Min(headingMatch.Groups[1].Value.Length, 6),
                        Text = text
                    });
                }
                continue;
            }

            // Horizontal rules carry no information once styling is gone.
            if (HorizontalRulePattern().IsMatch(line))
            {
                FlushParagraph(blocks, paragraph);
                continue;
            }

            var listMatch = ListItemPattern().Match(line);
            if (listMatch.Success)
            {
                FlushParagraph(blocks, paragraph);
                var text = CleanInline(listMatch.Groups[1].Value);
                if (text.Length > 0)
                {
                    blocks.Add(new ReadmeBlock { Kind = ReadmeBlockKind.ListItem, Text = text });
                }
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                FlushParagraph(blocks, paragraph);
                var text = CleanInline(line.TrimStart().TrimStart('>').Trim());
                if (text.Length > 0)
                {
                    blocks.Add(new ReadmeBlock { Kind = ReadmeBlockKind.Quote, Text = text });
                }
                continue;
            }

            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line.Trim());
        }

        if (inFence && fenceContent.Length > 0) AddCode(blocks, fenceContent.ToString());
        FlushParagraph(blocks, paragraph);

        return blocks;
    }

    /// <summary>
    /// The first real prose in a README, used as a plain-English summary when the
    /// repository has no description of its own.
    /// </summary>
    public static string? ExtractIntroduction(string? markdown, int maxLength = 400)
    {
        var blocks = Parse(markdown);

        var intro = blocks
            .FirstOrDefault(b => b.IsParagraph && b.Text.Length >= 40 && !LooksLikeBoilerplate(b.Text));

        var text = intro?.Text
                   ?? blocks.FirstOrDefault(b => b.IsParagraph && !LooksLikeBoilerplate(b.Text))?.Text;

        if (string.IsNullOrWhiteSpace(text)) return null;

        return text.Length <= maxLength ? text : Truncate(text, maxLength);
    }

    /// <summary>Top-level section headings, useful for "what can I do with it".</summary>
    public static IReadOnlyList<string> ExtractHeadings(string? markdown, int maxCount = 12)
    {
        return Parse(markdown)
            .Where(b => b.IsHeading && b.Level is >= 1 and <= 3)
            .Select(b => b.Text)
            .Where(t => t.Length is > 1 and < 60)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();
    }

    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;

        var cut = text.LastIndexOf(' ', Math.Min(maxLength, text.Length - 1));
        if (cut < maxLength / 2) cut = maxLength;
        return text[..cut].TrimEnd(',', '.', ';', ':') + "...";
    }

    // ---- Internals --------------------------------------------------------

    private static void FlushParagraph(List<ReadmeBlock> blocks, StringBuilder paragraph)
    {
        if (paragraph.Length == 0) return;

        var text = CleanInline(paragraph.ToString());
        paragraph.Clear();

        if (text.Length == 0) return;
        blocks.Add(new ReadmeBlock { Kind = ReadmeBlockKind.Paragraph, Text = text });
    }

    private static void AddCode(List<ReadmeBlock> blocks, string code)
    {
        var trimmed = code.TrimEnd('\n');
        if (trimmed.Trim().Length == 0) return;
        blocks.Add(new ReadmeBlock { Kind = ReadmeBlockKind.Code, Text = trimmed });
    }

    /// <summary>
    /// Strips the markup that makes a README unreadable as plain text: badges, images,
    /// raw HTML, link syntax and emphasis markers.
    /// </summary>
    public static string CleanInline(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (text.Length > MaxInputCharacters) text = text[..MaxInputCharacters];

        try
        {
            return CleanInlineCore(text);
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological input: fall back to the raw text rather than hanging.
            return text.Trim();
        }
    }

    private static string CleanInlineCore(string text)
    {

        var result = ImagePattern().Replace(text, "");          // ![alt](url) and badges
        result = LinkPattern().Replace(result, "$1");            // [text](url) -> text
        result = HtmlTagPattern().Replace(result, " ");          // <p>, <img ...>, <div>
        result = InlineCodePattern().Replace(result, "$1");      // `code` -> code
        result = EmphasisPattern().Replace(result, "$1");        // **bold** / __bold__
        result = result.Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
                       .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
                       .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
                       .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase);
        result = WhitespacePattern().Replace(result, " ");

        return result.Trim();
    }

    /// <summary>
    /// Recognises the lines that are decoration rather than description - badge rows,
    /// tables of contents and the like.
    /// </summary>
    private static bool LooksLikeBoilerplate(string text)
    {
        if (text.Length < 25) return true;

        var lower = text.ToLowerInvariant();
        string[] markers =
        [
            "table of contents", "build status", "click here", "see the docs",
            "license mit", "all rights reserved"
        ];

        return markers.Any(m => lower.StartsWith(m, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s{0,3}([-*_])(?:\s*\1){2,}\s*$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex HorizontalRulePattern();

    [GeneratedRegex(@"^\s{0,4}(?:[-*+]|\d+\.)\s+(.*)$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"<[^>]{1,200}>", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"`([^`]*)`", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex InlineCodePattern();

    [GeneratedRegex(@"(?:\*\*|__|\*|_)([^*_]+)(?:\*\*|__|\*|_)", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex EmphasisPattern();

    [GeneratedRegex(@"\s{2,}", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex WhitespacePattern();
}
