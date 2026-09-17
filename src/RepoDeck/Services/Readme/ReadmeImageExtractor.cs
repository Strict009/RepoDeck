using System.Text.RegularExpressions;

namespace RepoDeck.Services.Readme;

/// <summary>One image reference found in a README, before anything is known about it.</summary>
public readonly record struct ReadmeImage(string Url, string? AltText);

/// <summary>
/// Pulls image references out of README markdown.
/// </summary>
/// <remarks>
/// Both markdown and raw HTML, because READMEs use whichever suits. Relative URLs are
/// left as written and resolved by the caller, which is the only thing that knows the
/// repository and branch. Every pattern carries a match timeout: this is content written
/// by a stranger.
/// </remarks>
public static partial class ReadmeImageExtractor
{
    private const int MaxImages = 40;
    private const int MaxInputCharacters = 512 * 1024;

    public static IReadOnlyList<ReadmeImage> Extract(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        if (markdown.Length > MaxInputCharacters) markdown = markdown[..MaxInputCharacters];

        var found = new List<ReadmeImage>();

        try
        {
            foreach (Match match in MarkdownImagePattern().Matches(markdown))
            {
                if (found.Count >= MaxImages) break;

                var url = CleanUrl(match.Groups["url"].Value);
                if (url is null) continue;

                found.Add(new ReadmeImage(url, NullIfBlank(match.Groups["alt"].Value)));
            }

            foreach (Match match in HtmlImagePattern().Matches(markdown))
            {
                if (found.Count >= MaxImages) break;

                var url = CleanUrl(match.Groups["url"].Value);
                if (url is null) continue;

                found.Add(new ReadmeImage(url, NullIfBlank(match.Groups["alt"].Value)));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological input: return whatever was gathered rather than failing the page.
        }

        return found
            .GroupBy(i => i.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Markdown permits a title after the URL, and plenty of READMEs wrap the URL in
    /// angle brackets. Both are stripped here so the caller sees a bare address.
    /// </summary>
    private static string? CleanUrl(string raw)
    {
        var url = raw.Trim();
        if (url.Length == 0) return null;

        var space = url.IndexOf(' ');
        if (space > 0) url = url[..space];

        url = url.Trim('<', '>', '"', '\'');

        return url.Length == 0 ? null : url;
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"!\[(?<alt>[^\]]{0,200})\]\((?<url>[^)\s]{1,500}[^)]{0,200})\)",
        RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex MarkdownImagePattern();

    [GeneratedRegex(
        """<img[^>]{0,400}?src\s*=\s*["'](?<url>[^"']{1,500})["'][^>]{0,400}?(?:alt\s*=\s*["'](?<alt>[^"']{0,200})["'])?[^>]{0,200}>""",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex HtmlImagePattern();
}
