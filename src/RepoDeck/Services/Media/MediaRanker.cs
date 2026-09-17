using RepoDeck.Models;

namespace RepoDeck.Services.Media;

/// <summary>
/// Decides what an image is and how much it is worth showing.
/// </summary>
/// <remarks>
/// Pure, so the judgement can be tested against the real shapes of README markup without
/// fetching anything. The problem is almost entirely one of rejection: a typical README
/// opens with eight build badges, a sponsor button and a licence shield before it gets
/// anywhere near a screenshot.
/// </remarks>
public static class MediaRanker
{
    /// <summary>Hosts that exist to serve badges and nothing else.</summary>
    private static readonly string[] BadgeHosts =
    [
        "shields.io", "badgen.net", "badge.fury.io", "travis-ci.org", "travis-ci.com",
        "ci.appveyor.com", "codecov.io", "coveralls.io", "circleci.com", "snyk.io",
        "sonarcloud.io", "codeclimate.com", "David-dm.org", "david-dm.org",
        "forthebadge.com", "herokucdn.com", "gitpod.io", "bestpractices.coreinfrastructure.org",
        "isitmaintained.com", "deepsource.io", "codefactor.io", "app.fossa.com"
    ];

    /// <summary>Funding and sponsorship buttons.</summary>
    private static readonly string[] SponsorHosts =
    [
        "buymeacoffee.com", "ko-fi.com", "patreon.com", "paypal.com", "paypalobjects.com",
        "opencollective.com", "liberapay.com", "issuehunt.io", "bmc-cdn.com"
    ];

    /// <summary>Path or filename fragments that mark a badge even on a neutral host.</summary>
    private static readonly string[] BadgeWords =
    [
        "badge", "shield", "build-status", "buildstatus", "coverage", "licence-",
        "license-", "downloads-", "version-", "chat-", "discord-", "gitter"
    ];

    private static readonly string[] ScreenshotWords =
    [
        // Deliberately specific. "example" was here once and matched every image in an
        // examples folder, plus every URL on example.com.
        "screenshot", "screen-shot", "screen_shot", "screenshots", "demo", "preview",
        "in-action", "gameplay", "app-window", "mainwindow"
    ];

    private static readonly string[] LogoWords =
    [
        "logo", "icon", "banner", "wordmark", "brand", "header"
    ];

    private static readonly string[] ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"
    ];

    /// <summary>Below this an image is decoration, not content.</summary>
    private const long MinimumUsefulBytes = 8 * 1024;

    public static MediaCandidate Classify(MediaCandidate candidate)
    {
        var url = candidate.Url.ToLowerInvariant();
        var description = (candidate.Description ?? "").ToLowerInvariant();

        // Ranked below a logo on purpose: GitHub generates this card automatically when the
        // maintainer has not uploaded one, so it is frequently text on a gradient. Its job
        // is to be the fallback that is never worse than an empty tile.
        if (candidate.Source == MediaSource.SocialPreview)
        {
            return candidate with { Kind = MediaKind.SocialPreview, Score = 250 };
        }

        if (LooksLikeBadge(url, description))
        {
            return candidate with { Kind = MediaKind.Badge, Score = int.MinValue };
        }

        // An SVG is almost always a badge or a diagram, and Avalonia cannot decode one
        // without another dependency, so it is set aside rather than shown broken.
        if (url.Contains(".svg", StringComparison.Ordinal))
        {
            return candidate with { Kind = MediaKind.Badge, Score = int.MinValue };
        }

        if (!HasImageExtension(url))
        {
            return candidate with { Kind = MediaKind.Unknown, Score = int.MinValue };
        }

        var score = 0;
        var kind = MediaKind.Unknown;

        if (ContainsAny(url, ScreenshotWords) || ContainsAny(description, ScreenshotWords))
        {
            // A picture of the thing running is what someone actually wants to see.
            kind = MediaKind.Screenshot;
            score += 1000;
        }
        else if (ContainsAny(url, LogoWords) || ContainsAny(description, LogoWords))
        {
            kind = MediaKind.Logo;
            score += 300;
        }
        else
        {
            score += 200;
        }

        // Images kept in a dedicated folder are usually deliberate documentation.
        if (InMediaFolder(url)) score += 150;

        // A file listing gives real sizes; a README link does not.
        if (candidate.SizeBytes is { } bytes)
        {
            if (bytes < MinimumUsefulBytes) score -= 800;
            else if (bytes > 200 * 1024) score += 120;
            else score += 40;
        }

        // Animated demos are excellent, but can be enormous.
        if (url.EndsWith(".gif", StringComparison.Ordinal)) score -= 60;

        // A file already in the repository is more reliable than an arbitrary remote host.
        if (candidate.Source == MediaSource.RepositoryFile) score += 80;

        return candidate with { Kind = kind, Score = score };
    }

    /// <summary>Classifies, drops what should never be shown, and orders the rest.</summary>
    public static RepositoryMedia Rank(IEnumerable<MediaCandidate> candidates)
    {
        var ranked = candidates
            .Select(Classify)
            .Where(c => !c.IsExcluded && c.Score > int.MinValue)
            .GroupBy(c => c.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .OrderByDescending(c => c.Score)
            .Take(12)
            .ToList();

        return new RepositoryMedia { Candidates = ranked };
    }

    private static bool LooksLikeBadge(string url, string description)
    {
        if (BadgeHosts.Any(h => url.Contains(h, StringComparison.OrdinalIgnoreCase))) return true;
        if (SponsorHosts.Any(h => url.Contains(h, StringComparison.OrdinalIgnoreCase))) return true;
        if (BadgeWords.Any(w => url.Contains(w, StringComparison.Ordinal))) return true;

        // GitHub's own workflow status images.
        if (url.Contains("/actions/workflows/", StringComparison.Ordinal)) return true;
        if (url.Contains("/workflows/", StringComparison.Ordinal)
            && url.Contains("/badge", StringComparison.Ordinal)) return true;

        // A 1x1 tracking pixel announces itself in the query string often enough.
        if (url.Contains("pixel", StringComparison.Ordinal)) return true;

        return description.Contains("badge", StringComparison.Ordinal)
               || description.Contains("build status", StringComparison.Ordinal);
    }

    private static bool HasImageExtension(string url)
    {
        var withoutQuery = url.Split('?', '#')[0];
        return ImageExtensions.Any(e => withoutQuery.EndsWith(e, StringComparison.Ordinal));
    }

    private static bool InMediaFolder(string url)
    {
        string[] folders = ["/screenshots/", "/screenshot/", "/images/", "/img/", "/assets/",
            "/docs/", "/doc/", "/media/", "/.github/"];

        return folders.Any(f => url.Contains(f, StringComparison.Ordinal));
    }

    private static bool ContainsAny(string value, string[] words) =>
        words.Any(w => value.Contains(w, StringComparison.Ordinal));
}
