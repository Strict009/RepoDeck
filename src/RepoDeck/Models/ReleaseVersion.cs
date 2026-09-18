using System.Text.RegularExpressions;

namespace RepoDeck.Models;

/// <summary>
/// A release tag RepoDeck was able to read as a version number.
/// </summary>
/// <remarks>
/// Deliberately narrow. Projects tag releases however they like - "v1.2.3", "2024.03",
/// "release-final-2", "Ünicorn", a date, a codename - and there is no ordering that is
/// correct for all of them. RepoDeck parses the shapes it can be sure about and refuses
/// the rest, because a wrong answer here means offering somebody an "update" that is
/// older than what they have.
///
/// Supported: an optional leading v or V, two to four dot-separated numbers, and an
/// optional pre-release suffix after a hyphen. Anything else is not a version as far as
/// RepoDeck is concerned.
/// </remarks>
public sealed partial record ReleaseVersion : IComparable<ReleaseVersion>
{
    private ReleaseVersion(
        IReadOnlyList<int> numbers, string? prerelease, string original)
    {
        Numbers = numbers;
        PrereleaseLabel = prerelease;
        Original = original;
    }

    /// <summary>The numeric components, most significant first. Two to four of them.</summary>
    public IReadOnlyList<int> Numbers { get; }

    /// <summary>The bit after the hyphen, when there was one: "beta.1", "rc2".</summary>
    public string? PrereleaseLabel { get; }

    /// <summary>The tag exactly as the project wrote it.</summary>
    public string Original { get; }

    /// <summary>
    /// Suffixes that really do mean "not finished yet". Everything else after a hyphen is
    /// the project's own business.
    /// </summary>
    /// <remarks>
    /// Semantic versioning says any suffix is a pre-release, so 1.0.0 outranks 1.0.0-x for
    /// every x. Real projects do not read the specification: "v0.0.3-release.4" is the
    /// fourth build of release 0.0.3, and applying the semver rule to it made RepoDeck
    /// offer v0.0.3 as an "update" to somebody already running v0.0.3-release.4 - a
    /// downgrade, presented as an improvement. Seen happening, not imagined.
    /// </remarks>
    private static readonly string[] PrereleaseWords =
    [
        "alpha", "beta", "rc", "pre", "preview", "dev", "nightly", "snapshot",
        "canary", "insider", "experimental", "test", "unstable", "early"
    ];

    /// <summary>
    /// True when the suffix is a word that genuinely means unfinished. A suffix RepoDeck
    /// does not recognise is not evidence of anything.
    /// </summary>
    public bool LooksLikePrerelease =>
        PrereleaseLabel is { Length: > 0 } label
        && PrereleaseWords.Any(w =>
            label.StartsWith(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when there is a suffix whose meaning RepoDeck cannot judge - a build number, a
    /// codename, a date. Two versions differing only in such a suffix cannot be ordered.
    /// </summary>
    public bool HasUnrecognisedQualifier =>
        PrereleaseLabel is { Length: > 0 } && !LooksLikePrerelease;

    /// <summary>
    /// Reads a tag, or returns null when RepoDeck cannot be confident what it means.
    /// </summary>
    public static ReleaseVersion? TryParse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var trimmed = tag.Trim();

        Match match;

        try
        {
            match = VersionPattern().Match(trimmed);
        }
        catch (RegexMatchTimeoutException)
        {
            // A tag is remote content. A pathological one gets no answer rather than time.
            return null;
        }

        if (!match.Success) return null;

        var numbers = match.Groups["num"].Captures
            .Select(c => int.TryParse(c.Value, out var n) ? n : -1)
            .ToList();

        // A component that did not fit in an int is not a version RepoDeck understands.
        if (numbers.Any(n => n < 0)) return null;

        var prerelease = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;

        return new ReleaseVersion(numbers, prerelease, trimmed);
    }

    /// <summary>
    /// A total order, for sorting a list of releases into a newest-first sequence. It
    /// always produces an answer, including for pairs RepoDeck could not honestly call
    /// one way or the other.
    /// </summary>
    /// <remarks>
    /// Never use this to decide whether to offer somebody an update: it will happily
    /// report that 0.0.3 beats 0.0.3-release.4. <see cref="CompareOrNull"/> is the one
    /// that admits when it does not know, and every user-facing decision goes through it.
    /// </remarks>
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;

        var numeric = CompareNumbers(other);
        if (numeric != 0) return numeric;

        return (LooksLikePrerelease, other.LooksLikePrerelease) switch
        {
            (false, true) => 1,
            (true, false) => -1,
            _ => ComparePrereleaseLabels(PrereleaseLabel ?? "", other.PrereleaseLabel ?? "")
        };
    }

    /// <summary>
    /// Compares two versions, or says it cannot. Null means the two differ only in a
    /// suffix whose meaning RepoDeck has no way to judge, so neither is demonstrably
    /// newer. Callers deciding whether to offer an update must use this rather than
    /// <see cref="CompareTo"/>, which is a total order for sorting and always answers.
    /// </summary>
    /// <remarks>
    /// This is the difference between "2.0.0 is newer than 2.0.0-beta.1", which is true,
    /// and "0.0.3 is newer than 0.0.3-release.4", which is a downgrade wearing the word
    /// update. RepoDeck says nothing rather than guess.
    /// </remarks>
    public int? CompareOrNull(ReleaseVersion? other)
    {
        if (other is null) return 1;

        var numeric = CompareNumbers(other);
        if (numeric != 0) return numeric;

        // Same numbers. Only a recognised pre-release word carries ordering information.
        if (HasUnrecognisedQualifier || other.HasUnrecognisedQualifier)
        {
            return string.Equals(PrereleaseLabel, other.PrereleaseLabel,
                StringComparison.OrdinalIgnoreCase)
                ? 0
                : null;
        }

        return (LooksLikePrerelease, other.LooksLikePrerelease) switch
        {
            (false, true) => 1,
            (true, false) => -1,
            (false, false) => 0,

            // Two recognised pre-releases of the same version. "beta.2" against "beta.1"
            // has an answer, and somebody following a beta series should be offered the
            // next one, so this does not bail out the way an unknown suffix does.
            _ => ComparePrereleaseLabels(PrereleaseLabel!, other.PrereleaseLabel!)
        };
    }

    /// <summary>True only when RepoDeck can show this is newer. Unsure counts as no.</summary>
    public bool IsNewerThan(ReleaseVersion other) => CompareOrNull(other) is > 0;

    /// <summary>True when the two can be ordered at all.</summary>
    public bool CanCompareWith(ReleaseVersion other) => CompareOrNull(other) is not null;

    /// <summary>
    /// Orders two recognised pre-release labels the way semantic versioning specifies:
    /// dot-separated identifiers, numbers compared as numbers, anything else as text, and
    /// a number ranking below a word. "beta.2" is newer than "beta.1"; "beta.10" is newer
    /// than "beta.9", which plain text comparison would get backwards.
    /// </summary>
    private static int ComparePrereleaseLabels(string mine, string theirs)
    {
        var left = mine.Split('.');
        var right = theirs.Split('.');

        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            // Having run out of identifiers first makes this the lower one: 1.0.0-beta
            // precedes 1.0.0-beta.1.
            if (i >= left.Length) return -1;
            if (i >= right.Length) return 1;

            var a = left[i];
            var b = right[i];

            var aNumeric = int.TryParse(a, out var an);
            var bNumeric = int.TryParse(b, out var bn);

            var result = (aNumeric, bNumeric) switch
            {
                (true, true) => an.CompareTo(bn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)
            };

            if (result != 0) return result;
        }

        return 0;
    }

    /// <summary>
    /// Compares only the numeric components, ignoring any suffix entirely. Zero means
    /// the two are the same version as far as its numbers go - 1.2.3-anything against
    /// 1.2.3-anything-else - which is exactly the case where the suffix decides nothing
    /// and something other than the version string has to.
    /// </summary>
    public int CompareNumericTo(ReleaseVersion other) => CompareNumbers(other);

    private int CompareNumbers(ReleaseVersion other)
    {
        var length = Math.Max(Numbers.Count, other.Numbers.Count);

        for (var i = 0; i < length; i++)
        {
            // A missing component is zero: 1.2 and 1.2.0 are the same version.
            var mine = i < Numbers.Count ? Numbers[i] : 0;
            var theirs = i < other.Numbers.Count ? other.Numbers[i] : 0;

            if (mine != theirs) return mine.CompareTo(theirs);
        }

        return 0;
    }

    /// <summary>
    /// How the two differ, for saying so in words: "a new major version" reads better
    /// than "1.2.3 to 2.0.0" for somebody who does not think in version numbers.
    /// </summary>
    public VersionStep StepFrom(ReleaseVersion previous)
    {
        if (CompareOrNull(previous) is not > 0) return VersionStep.NotNewer;

        var mineMajor = Numbers.Count > 0 ? Numbers[0] : 0;
        var theirsMajor = previous.Numbers.Count > 0 ? previous.Numbers[0] : 0;

        if (mineMajor != theirsMajor) return VersionStep.Major;

        var mineMinor = Numbers.Count > 1 ? Numbers[1] : 0;
        var theirsMinor = previous.Numbers.Count > 1 ? previous.Numbers[1] : 0;

        return mineMinor != theirsMinor ? VersionStep.Minor : VersionStep.Patch;
    }

    public override string ToString() => Original;

    /// <summary>
    /// Optional v, then 2-4 dotted numbers, then an optional pre-release label. Anchored
    /// at both ends so a tag with anything else in it is refused rather than half-read.
    /// </summary>
    [GeneratedRegex(
        @"^[vV]?(?<num>\d{1,9})(?:\.(?<num>\d{1,9})){1,3}(?:[-+](?<pre>[0-9A-Za-z.\-]{1,64}))?$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex VersionPattern();
}

/// <summary>How big a jump a version change is, in words a person uses.</summary>
public enum VersionStep
{
    NotNewer,
    Patch,
    Minor,
    Major
}
