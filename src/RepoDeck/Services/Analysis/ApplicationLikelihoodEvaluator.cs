using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Judges, from repository metadata alone, whether something is an application a person
/// could run - as opposed to a library, a course, a list of links or someone's dotfiles.
/// </summary>
/// <remarks>
/// Metadata-only by design: the Discover page shows thirty cards and cannot afford an
/// extra API call each. The file-level classifier (RepositoryAnalyzerService, a later
/// milestone) will look inside the repository and override this verdict when it can.
/// Every adjustment records a reason, because RepoDeck must always be able to explain
/// how it reached a conclusion.
/// </remarks>
public static class ApplicationLikelihoodEvaluator
{
    private static readonly string[] ApplicationTopics =
    [
        "desktop", "desktop-app", "gui", "app", "application", "electron", "tauri",
        "game", "launcher", "client", "tool", "utility", "editor", "player", "emulator",
        "windows", "linux", "cross-platform", "standalone"
    ];

    private static readonly string[] LibraryTopics =
    [
        "library", "framework", "sdk", "api", "package", "plugin", "extension",
        "binding", "bindings", "wrapper", "middleware", "boilerplate", "template",
        "starter", "example", "examples", "sample", "samples"
    ];

    private static readonly string[] NonSoftwareTopics =
    [
        "awesome", "awesome-list", "list", "curated", "resources", "tutorial",
        "tutorials", "course", "learning", "book", "books", "cheatsheet",
        "interview", "roadmap", "documentation", "docs", "blog", "notes", "dotfiles"
    ];

    private static readonly string[] ApplicationWords =
    [
        "app", "application", "desktop", "gui", "client", "launcher", "editor",
        "player", "viewer", "manager", "browser", "game", "emulator", "tool",
        "utility", "standalone", "download", "install"
    ];

    private static readonly string[] LibraryWords =
    [
        "library", "framework", "sdk", "toolkit", "bindings", "wrapper", "plugin",
        "module", "package for", "api for", "implementation of", "boilerplate",
        "starter", "template", "collection of", "awesome list", "curated list",
        "list of", "tutorial", "course", "cheat sheet", "roadmap", "specification"
    ];

    /// <summary>Languages that usually produce a runnable binary rather than a package.</summary>
    private static readonly string[] BinaryLanguages =
    [
        "C#", "C++", "C", "Rust", "Go", "Swift", "Pascal", "Object Pascal", "Delphi", "Zig"
    ];

    public static ApplicationLikelihood Evaluate(GitHubRepository repository)
    {
        var reasons = new List<string>();
        var score = 40; // Neutral starting point: most repositories are ambiguous.

        var topics = repository.Topics
            .Select(t => t.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var appTopics = ApplicationTopics.Where(topics.Contains).ToList();
        if (appTopics.Count > 0)
        {
            score += Math.Min(appTopics.Count * 12, 30);
            reasons.Add($"Tagged with {Join(appTopics)} on GitHub.");
        }

        var libTopics = LibraryTopics.Where(topics.Contains).ToList();
        if (libTopics.Count > 0)
        {
            score -= Math.Min(libTopics.Count * 14, 32);
            reasons.Add($"Tagged with {Join(libTopics)}, which usually means it is for developers.");
        }

        var nonSoftware = NonSoftwareTopics.Where(topics.Contains).ToList();
        if (nonSoftware.Count > 0)
        {
            score -= 40;
            reasons.Add($"Tagged with {Join(nonSoftware)}, which usually means reading material rather than software.");
        }

        var haystack = ((repository.Name ?? "") + " " + (repository.Description ?? "")).ToLowerInvariant();

        var appWordHits = ApplicationWords.Where(w => ContainsWord(haystack, w)).ToList();
        if (appWordHits.Count > 0)
        {
            score += Math.Min(appWordHits.Count * 7, 20);
            reasons.Add($"Describes itself using words like \"{appWordHits[0]}\".");
        }

        var libWordHits = LibraryWords.Where(w => haystack.Contains(w, StringComparison.Ordinal)).ToList();
        if (libWordHits.Count > 0)
        {
            score -= Math.Min(libWordHits.Count * 10, 28);
            reasons.Add($"Describes itself using words like \"{libWordHits[0]}\".");
        }

        if (repository.Language is { Length: > 0 } language)
        {
            if (BinaryLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
            {
                score += 8;
                reasons.Add($"Written in {language}, which normally produces a program you can run.");
            }
            else if (language.Equals("HTML", StringComparison.OrdinalIgnoreCase)
                     || language.Equals("Markdown", StringComparison.OrdinalIgnoreCase)
                     || language.Equals("TeX", StringComparison.OrdinalIgnoreCase))
            {
                score -= 22;
                reasons.Add($"Mostly {language}, so it is probably a document or website rather than a program.");
            }
        }
        else
        {
            score -= 10;
            reasons.Add("GitHub reports no main programming language.");
        }

        if (repository.IsArchived)
        {
            reasons.Add("The repository is archived and no longer maintained.");
        }

        if (string.IsNullOrWhiteSpace(repository.Description))
        {
            score -= 5;
            reasons.Add("The repository has no description, so there is less to go on.");
        }

        score = Math.Clamp(score, 0, 100);

        return new ApplicationLikelihood
        {
            Score = score,
            Confidence = ResolveConfidence(score, reasons.Count),
            Reasons = reasons
        };
    }

    private static Confidence ResolveConfidence(int score, int reasonCount)
    {
        // Confident only when the evidence is both plentiful and one-sided.
        if (reasonCount == 0) return Confidence.Unknown;
        if (score >= 75 || score <= 20) return Confidence.Likely;
        if (score is >= 35 and <= 60) return Confidence.RequiresInspection;
        return Confidence.Unknown;
    }

    private static bool ContainsWord(string haystack, string word)
    {
        var index = haystack.IndexOf(word, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var end = index + word.Length;
            var afterOk = end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (beforeOk && afterOk) return true;
            index = haystack.IndexOf(word, index + 1, StringComparison.Ordinal);
        }
        return false;
    }

    private static string Join(IReadOnlyList<string> values)
    {
        var quoted = values.Take(3).Select(v => "\"" + v + "\"").ToList();
        return quoted.Count switch
        {
            1 => quoted[0],
            2 => quoted[0] + " and " + quoted[1],
            _ => string.Join(", ", quoted.Take(quoted.Count - 1)) + " and " + quoted[^1]
        };
    }
}
