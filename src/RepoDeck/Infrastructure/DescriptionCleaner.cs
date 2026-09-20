using System.Globalization;
using System.Text.RegularExpressions;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Removes presentation artefacts from a project description. Never rewrites the words.
/// </summary>
/// <remarks>
/// <para>
/// A GitHub description is written for a repository page, not for a card in somebody else's
/// application. It arrives with emoji shortcodes GitHub would have rendered, with newlines
/// and runs of spaces. Shown raw it produces lines like ":lollipop: Wow, such a beautiful
/// HTML5 music player."
/// </para>
/// <para>
/// The line this draws: it removes things that are <em>markup</em> and normalises
/// <em>whitespace</em>, both of which can be decided objectively. It does not touch letters.
/// An earlier version capitalised the first word of all-lowercase descriptions and was
/// wrong: "ffmpeg wrapper for the terminal." became "Ffmpeg wrapper...", "ripgrep searches
/// files." became "Ripgrep...", and "ios music player." became "Ios...". No rule can tell a
/// lowercase product name from a lowercase word by looking at the word, so RepoDeck does not
/// try. Casing chosen by the people who wrote the project is preserved exactly - ffmpeg,
/// ripgrep, iOS, .NET, C#, C++, acronyms and deliberately lowercase names all survive.
/// </para>
/// <para>
/// The original text is never modified. Callers expose the result as a separate display
/// property, and an empty result stays empty: inventing a description for a project that has
/// none would be putting words in a stranger's mouth.
/// </para>
/// </remarks>
public static class DescriptionCleaner
{
    /// <summary>How long a match may take before RepoDeck gives up on it.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// A run of emoji shortcodes, including any punctuation or spacing holding them
    /// together.
    /// </summary>
    /// <remarks>
    /// The run matters. Descriptions carry ":notes::arrow_forward:" with nothing between,
    /// ":rocket: :fire:" with a space, and ":rocket:,:fire:" with a comma. Matching one
    /// shortcode at a time left the separator and the next shortcode stranded, because the
    /// second one no longer began at a token boundary.
    ///
    /// The leading boundary is what keeps a URL, a namespace like std::vector and a time
    /// like 12:30 out of this: none of them starts a colon-delimited token after whitespace.
    /// </remarks>
    private static readonly Regex ShortcodeRun = new(
        @"(?<=^|\s)(?::[a-z0-9_+\-]+:)(?:[\s,;]*:[a-z0-9_+\-]+:)*(?=$|\s|[.,;:!?)\]])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeout);

    private static readonly Regex Whitespace = new(
        @"\s+", RegexOptions.CultureInvariant, MatchTimeout);

    /// <summary>
    /// A space left stranded in front of punctuation once a shortcode between the two has
    /// gone: "A fast editor :zap:." must not become "A fast editor .".
    /// </summary>
    private static readonly Regex SpaceBeforePunctuation = new(
        @" +([.,;:!?)\]])", RegexOptions.CultureInvariant, MatchTimeout);

    /// <summary>
    /// Cleans a description for display. Returns an empty string when there is nothing left
    /// to show, which is not the same as a sentence saying so.
    /// </summary>
    /// <param name="maxLength">
    /// Zero or less leaves the length alone, which is what every caller in RepoDeck does
    /// today. When a limit is given the result may be one character longer, because the
    /// ellipsis marking the cut is added after trimming to it.
    /// </param>
    public static string Clean(string? description, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";

        string text;

        try
        {
            text = ShortcodeRun.Replace(description, " ");
            text = Whitespace.Replace(text, " ");
            text = SpaceBeforePunctuation.Replace(text, "$1");
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological input from a stranger. Fall back to the plain collapse rather
            // than showing nothing, and never let a description take the page down.
            text = description.Replace('\r', ' ').Replace('\n', ' ');
        }

        text = text.Trim();

        // Whatever is left is the project's own words, in the project's own casing.
        return maxLength > 0 && text.Length > maxLength ? Shorten(text, maxLength) : text;
    }

    /// <summary>
    /// Shortens at a word boundary, and never in the middle of a character.
    /// </summary>
    /// <remarks>
    /// Deliberately simple. An earlier version tried to stop where the author's last
    /// sentence stopped, which meant knowing that "U.S." and "etc." are not sentence
    /// endings - a natural-language problem, taken on for a parameter no production caller
    /// passes. A word boundary and an ellipsis is honest about having cut something.
    /// </remarks>
    private static string Shorten(string text, int maxLength)
    {
        var ceiling = BoundaryAtOrBefore(text, maxLength);
        var space = text.LastIndexOf(' ', Math.Max(0, ceiling - 1));

        // A single very long word: no boundary to fall back to, so cut the word itself -
        // still on a character boundary.
        var cut = space > 0 ? space : ceiling;

        return text[..cut].TrimEnd(' ', ',', ';', ':', '-') + "…";
    }

    /// <summary>
    /// The largest index at or before <paramref name="index"/> that does not fall inside a
    /// character.
    /// </summary>
    /// <remarks>
    /// A .NET string is UTF-16, so an emoji is two chars and a flag or a skin-toned emoji is
    /// several joined together. Slicing at an arbitrary index can split a surrogate pair and
    /// produce text that is not valid Unicode at all. Text elements are the unit a reader
    /// would call a character, so cuts land between them.
    /// </remarks>
    private static int BoundaryAtOrBefore(string text, int index)
    {
        if (index >= text.Length) return text.Length;
        if (index <= 0) return 0;

        var boundary = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);

        while (elements.MoveNext())
        {
            if (elements.ElementIndex > index) break;
            boundary = elements.ElementIndex;
        }

        return boundary;
    }
}
