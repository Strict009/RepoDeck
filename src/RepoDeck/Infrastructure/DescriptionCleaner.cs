using System.Text.RegularExpressions;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Tidies a project description for display, without ever rewriting what the project said.
/// </summary>
/// <remarks>
/// <para>
/// A GitHub description is written for a repository page, not for a card in somebody else's
/// application. It arrives with emoji shortcodes GitHub would have rendered, with newlines
/// and runs of spaces, and at whatever length the author felt like. Shown raw it produces
/// lines like ":lollipop: Wow, such a beautiful HTML5 music player." and sentences that stop
/// mid-clause.
/// </para>
/// <para>
/// This is deliberately conservative. It removes things that are markup rather than words,
/// and it shortens. It does not paraphrase, does not title-case, and never invents a
/// description for a project that has none: an empty result comes back empty, and the view
/// model decides what to say about that. The original text is never modified - callers
/// expose the result as a separate display property.
/// </para>
/// </remarks>
public static class DescriptionCleaner
{
    /// <summary>How long a match may take before RepoDeck gives up on it.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// One or more emoji shortcodes standing as their own token.
    /// </summary>
    /// <remarks>
    /// Anchored to whitespace on both sides so that a colon inside a URL, a namespace or a
    /// time cannot be mistaken for one. The inner group repeats because GitHub descriptions
    /// really do carry ":notes::arrow_forward:" as a single run.
    /// </remarks>
    private static readonly Regex Shortcodes = new(
        @"(?<=^|\s)(?::[a-z0-9_+\-]+:)+(?=$|\s|[.,;:!?)\]])",
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
    /// Words that end in a full stop without ending a sentence. Cutting after one of these
    /// produces "Works with Node.js, npm, etc." followed by nothing.
    /// </summary>
    private static readonly string[] Abbreviations =
    [
        "e.g.", "i.e.", "etc.", "vs.", "cf.", "al.", "approx.", "fig.", "no.", "vol.",
        "dr.", "mr.", "mrs.", "ms.", "prof.", "st.", "inc.", "ltd.", "co.", "jr.", "sr."
    ];

    /// <summary>
    /// A sentence may be dropped to reach the limit only if what remains is at least this
    /// much of it. Below that, trimming mid-sentence keeps more meaning than cutting early.
    /// </summary>
    private const double SentenceKeepRatio = 0.6;

    /// <summary>
    /// Cleans a description for display. Returns an empty string when there is nothing to
    /// show, which is not the same as a sentence saying so.
    /// </summary>
    /// <param name="maxLength">Zero or less leaves the length alone.</param>
    public static string Clean(string? description, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";

        string text;

        try
        {
            text = Shortcodes.Replace(description, " ");
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

        if (text.Length == 0) return "";

        text = SentenceCase(text);

        return maxLength > 0 && text.Length > maxLength ? Shorten(text, maxLength) : text;
    }

    /// <summary>
    /// Capitalises the first letter, but only where the text is plainly ordinary prose that
    /// happens to have been typed in lower case.
    /// </summary>
    /// <remarks>
    /// The risk here is damage, not missed polish: "ffmpeg wrapper for the terminal" must
    /// never become "Ffmpeg wrapper for the terminal", and no rule can tell a lowercase
    /// product name from a lowercase word by looking at the word. So this asks for three
    /// things at once, and leaves the text alone when any of them is missing:
    /// <list type="number">
    /// <item>no capital anywhere - one capital means somebody cased this deliberately;</item>
    /// <item>a first word that is nothing but letters, ruling out slugs, versions and URLs;</item>
    /// <item>closing sentence punctuation, which is what separates a sentence somebody wrote
    /// from a label like "ffmpeg wrapper" that was never meant to be one.</item>
    /// </list>
    /// The third is the one that does the real work, and it is why a lowercase label keeps
    /// its case while a lowercase sentence gets its capital back.
    /// </remarks>
    private static string SentenceCase(string text)
    {
        if (text.Any(char.IsUpper)) return text;
        if (text[^1] is not ('.' or '!' or '?')) return text;

        var firstWord = text.AsSpan(0, IndexOfSpaceOrEnd(text));

        // Anything that is not purely alphabetic is a name, a slug, a version or a path.
        if (firstWord.Length < 2) return text;
        foreach (var c in firstWord)
        {
            if (!char.IsLetter(c)) return text;
        }

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static int IndexOfSpaceOrEnd(string text)
    {
        var space = text.IndexOf(' ');
        return space < 0 ? text.Length : space;
    }

    /// <summary>
    /// Shortens to the limit, preferring to stop where the author stopped.
    /// </summary>
    private static string Shorten(string text, int maxLength)
    {
        var sentenceEnd = LastSentenceEnd(text, maxLength);

        if (sentenceEnd > 0 && sentenceEnd >= (int)(maxLength * SentenceKeepRatio))
        {
            return text[..sentenceEnd].TrimEnd();
        }

        var cut = text.LastIndexOf(' ', Math.Min(maxLength, text.Length - 1));

        // One very long word, so there is no word boundary to fall back to.
        if (cut <= 0) return text[..maxLength].TrimEnd() + "…";

        return text[..cut].TrimEnd(' ', ',', ';', ':', '-') + "…";
    }

    /// <summary>
    /// The end of the last sentence that finishes at or before <paramref name="limit"/>,
    /// or 0 when there is none. The returned index is just past the punctuation.
    /// </summary>
    private static int LastSentenceEnd(string text, int limit)
    {
        var best = 0;
        var ceiling = Math.Min(limit, text.Length);

        for (var i = 0; i < ceiling; i++)
        {
            if (text[i] is not ('.' or '!' or '?')) continue;

            // A sentence ends where the next thing is a space, or nothing at all. This is
            // also what keeps "1.5", "example.com" and "Node.js" from splitting.
            var isLast = i == text.Length - 1;
            if (!isLast && text[i + 1] != ' ') continue;

            if (text[i] == '.' && EndsWithAbbreviation(text, i)) continue;

            best = i + 1;
        }

        return best;
    }

    private static bool EndsWithAbbreviation(string text, int dotIndex)
    {
        var wordStart = text.LastIndexOf(' ', dotIndex) + 1;
        var word = text[wordStart..(dotIndex + 1)];

        return Abbreviations.Contains(word, StringComparer.OrdinalIgnoreCase);
    }
}
