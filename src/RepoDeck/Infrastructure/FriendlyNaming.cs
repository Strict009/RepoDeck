using System.Globalization;
using Avalonia.Media;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Turns developer conventions into something a person reads without flinching.
/// </summary>
/// <remarks>
/// Repository names are slugs: lower case, hyphenated, occasionally shouted. They are an
/// address, not a title. The original is never lost - it is still shown as the technical
/// identifier on the details page - but it should not be the first thing on a card.
/// </remarks>
public static class FriendlyNaming
{
    /// <summary>Words that should not be title-cased into nonsense.</summary>
    private static readonly Dictionary<string, string> KnownWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ui"] = "UI", ["cli"] = "CLI", ["gui"] = "GUI", ["api"] = "API",
        ["sdk"] = "SDK", ["os"] = "OS", ["db"] = "DB", ["ide"] = "IDE",
        ["pdf"] = "PDF", ["http"] = "HTTP", ["js"] = "JS", ["css"] = "CSS",
        ["html"] = "HTML", ["sql"] = "SQL", ["ai"] = "AI", ["ml"] = "ML",
        ["tv"] = "TV", ["vpn"] = "VPN", ["ftp"] = "FTP", ["ssh"] = "SSH"
    };

    /// <summary>"obs-studio" becomes "Obs Studio"; "ShareX" is left as it is.</summary>
    public static string ForRepository(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Untitled";

        // A name that already mixes case is the author's own styling. Leave it alone.
        if (name.Any(char.IsUpper) && name.Any(char.IsLower) && !name.Contains('-') && !name.Contains('_'))
        {
            return name;
        }

        var words = name
            .Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalise)
            .ToList();

        return words.Count == 0 ? name : string.Join(' ', words);
    }

    private static string Capitalise(string word)
    {
        if (KnownWords.TryGetValue(word, out var known)) return known;
        if (word.Length == 0) return word;

        // Preserve deliberate internal capitals like "ShareX".
        if (word.Any(char.IsUpper) && word.Any(char.IsLower)) return word;

        return char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..].ToLowerInvariant();
    }

    /// <summary>The letter for a fallback tile.</summary>
    public static string Initial(string name)
    {
        var letter = name.FirstOrDefault(char.IsLetterOrDigit);
        return letter == default ? "?" : char.ToUpper(letter, CultureInfo.InvariantCulture).ToString();
    }

    /// <summary>
    /// A stable colour from the name, so a project looks the same on every visit and a
    /// page of results without screenshots still looks deliberate.
    /// </summary>
    public static IBrush ColourFor(string key)
    {
        Color[] palette =
        [
            Color.FromRgb(0x3E, 0x6B, 0xC4), Color.FromRgb(0x2F, 0x8F, 0x6B),
            Color.FromRgb(0x8A, 0x5C, 0xC4), Color.FromRgb(0xC4, 0x6B, 0x3E),
            Color.FromRgb(0x2F, 0x7F, 0x8F), Color.FromRgb(0xB0, 0x4A, 0x6E),
            Color.FromRgb(0x6B, 0x7A, 0x3E), Color.FromRgb(0x50, 0x5A, 0x8F)
        ];

        var hash = 17;
        foreach (var c in key) hash = hash * 31 + c;

        return new SolidColorBrush(palette[Math.Abs(hash) % palette.Length]);
    }

    /// <summary>
    /// A platform hint from tags alone, phrased so it never reads as a promise. The real
    /// answer needs the releases, which a search result does not carry.
    /// </summary>
    public static string PlatformHint(IReadOnlyList<string> topics, string? language)
    {
        var lower = topics.Select(t => t.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var platforms = new List<string>();

        if (lower.Contains("windows") || lower.Contains("win32")) platforms.Add("Windows");
        if (lower.Contains("linux")) platforms.Add("Linux");
        if (lower.Contains("macos") || lower.Contains("osx") || lower.Contains("mac"))
        {
            platforms.Add("macOS");
        }

        if (platforms.Count > 0) return string.Join(", ", platforms);

        if (lower.Contains("cross-platform") || lower.Contains("crossplatform"))
        {
            return "Cross-platform";
        }

        return "";
    }
}
