using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Works out what kind of thing a project is from its name, description, tags and
/// language.
/// </summary>
/// <remarks>
/// Pure and metadata-only: the Discover grid shows thirty cards and cannot afford a
/// request each. Evidence is scored rather than matched first-wins, because a project
/// called "video-player" tagged `library` is a library that handles video, and the order
/// its words happen to appear in should not decide that.
///
/// This is a classification, not a verdict. It says what something looks like, not
/// whether it is any good and not whether it is safe.
/// </remarks>
public static class ProjectKindClassifier
{
    /// <summary>
    /// A kind, the words that suggest it, and the tags that suggest it harder.
    /// </summary>
    private sealed record Signals(
        ProjectKind Kind,
        string[] Topics,
        string[] Words,
        int TopicWeight = 30,
        int WordWeight = 14);

    private static readonly Signals[] Table =
    [
        new(ProjectKind.GameOrEmulator,
            ["game", "games", "gamedev", "emulator", "emulation", "retrogaming", "retro-games",
             "nes", "snes", "gameboy", "playstation", "roguelike", "game-engine"],
            ["game", "emulator", "emulation", "roguelike", "arcade", "console", "rom"]),

        new(ProjectKind.Audio,
            ["audio", "music", "music-player", "sound", "dsp", "synthesizer", "daw",
             "audio-player", "mp3", "spotify", "podcast"],
            ["music", "audio", "sound", "mp3", "playlist", "synthesiser", "synthesizer",
             "podcast", "equaliser", "equalizer"]),

        new(ProjectKind.Video,
            ["video", "video-player", "video-editor", "streaming", "ffmpeg", "transcoding",
             "screen-recorder", "youtube-dl", "subtitles"],
            ["video", "movie", "streaming", "transcode", "subtitle", "screen recorder",
             "screen recording", "youtube"]),

        new(ProjectKind.DeveloperTool,
            ["developer-tools", "devtools", "ide", "text-editor", "code-editor", "terminal",
             "debugger", "compiler", "linter", "formatter", "git", "devops", "cicd"],
            ["ide", "code editor", "text editor", "terminal emulator", "debugger",
             "compiler", "linter", "formatter", "for developers", "developer tool"]),

        new(ProjectKind.Utility,
            ["utility", "utilities", "productivity", "file-manager", "backup", "sync",
             "clipboard", "screenshot", "launcher", "automation", "compression"],
            ["file manager", "backup", "sync", "clipboard", "screenshot", "launcher",
             "duplicate files", "compress", "archive manager", "password manager",
             "note taking", "todo"]),

        new(ProjectKind.CommandLineTool,
            ["cli", "command-line", "commandline", "terminal-app", "tui", "shell"],
            ["command line", "command-line", "cli tool", "from the terminal",
             "terminal-based", "in your terminal"]),

        new(ProjectKind.DesktopApplication,
            ["desktop", "desktop-app", "desktop-application", "gui", "electron", "tauri",
             "wpf", "winforms", "qt", "gtk", "avalonia", "cross-platform"],
            ["desktop app", "desktop application", "gui", "graphical", "for windows",
             "for linux", "for macos", "cross-platform app"]),

        new(ProjectKind.Library,
            ["library", "framework", "sdk", "bindings", "binding", "wrapper", "api",
             "package", "header-only", "npm-package", "crate", "nuget", "plugin",
             "extension", "middleware", "boilerplate", "template", "starter"],
            ["library", "framework", "sdk", "toolkit", "bindings", "wrapper",
             "header-only", "api for", "implementation of", "package for",
             "boilerplate", "starter kit", "a plugin for", "an extension for"],
            TopicWeight: 34,
            WordWeight: 18)
    ];

    /// <summary>Tags that mean this is reading material rather than software of any kind.</summary>
    private static readonly string[] NotSoftwareTopics =
    [
        "awesome", "awesome-list", "curated", "resources", "tutorial", "tutorials",
        "course", "learning", "book", "books", "cheatsheet", "roadmap", "interview",
        "documentation", "docs", "blog", "notes", "dotfiles"
    ];

    /// <summary>
    /// Below this the evidence is too thin to claim anything. One distinctive phrase in a
    /// description - "music player", "duplicate files", "command line" - is enough to
    /// suggest a kind at Possible confidence, and is usually all a one-line description
    /// gives you.
    /// </summary>
    private const int MinimumScore = 14;

    public static ProjectClassification Classify(GitHubRepository repository)
    {
        var topics = repository.Topics
            .Select(t => t.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        // Reading material is not a kind of program, and calling it Unknown is more
        // honest than calling it a utility because it has the word "tool" in its name.
        var notSoftware = NotSoftwareTopics.Where(topics.Contains).ToList();
        if (notSoftware.Count > 0)
        {
            return new ProjectClassification
            {
                Kind = ProjectKind.Unknown,
                Confidence = Confidence.Possible,
                Reasons = [$"Tagged {string.Join(", ", notSoftware.Take(3))}, which usually means "
                           + "reading material rather than software."]
            };
        }

        var name = (repository.Name ?? "").ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
        var description = (repository.Description ?? "").ToLowerInvariant();
        var haystack = name + " " + description;

        // Single words are matched whole. Substring matching found "rom" inside "from"
        // and classified a command-line search tool as an emulator.
        var words = Tokenise(haystack);

        var scores = new Dictionary<ProjectKind, int>();
        var evidence = new Dictionary<ProjectKind, List<string>>();

        foreach (var signals in Table)
        {
            var score = 0;
            var why = new List<string>();

            var matchedTopics = signals.Topics.Where(topics.Contains).ToList();
            if (matchedTopics.Count > 0)
            {
                score += Math.Min(matchedTopics.Count * signals.TopicWeight, signals.TopicWeight * 2);
                why.Add($"Tagged {string.Join(", ", matchedTopics.Take(3))} on GitHub.");
            }

            var matchedWords = signals.Words.Where(w => Mentions(haystack, words, w)).ToList();
            if (matchedWords.Count > 0)
            {
                score += Math.Min(matchedWords.Count * signals.WordWeight, signals.WordWeight * 2);
                why.Add($"Its description mentions {string.Join(", ", matchedWords.Take(3))}.");
            }

            if (score == 0) continue;

            scores[signals.Kind] = score;
            evidence[signals.Kind] = why;
        }

        if (scores.Count == 0)
        {
            return new ProjectClassification
            {
                Kind = ProjectKind.Unknown,
                Confidence = Confidence.Unknown,
                Reasons = ["Nothing in its name, description or tags says what kind of thing this is."]
            };
        }

        var ordered = scores.OrderByDescending(p => p.Value).ToList();
        var (kind, top) = (ordered[0].Key, ordered[0].Value);

        // A clear winner is worth stating plainly; a near-tie is not. "Video editor
        // library" should not confidently become Video.
        var runnerUp = ordered.Count > 1 ? ordered[1].Value : 0;
        var margin = top - runnerUp;

        if (top < MinimumScore)
        {
            return new ProjectClassification
            {
                Kind = ProjectKind.Unknown,
                Confidence = Confidence.Unknown,
                Reasons = ["The evidence for any particular kind of project is thin."]
            };
        }

        var confidence = (top, margin) switch
        {
            ( >= 48, >= 20) => Confidence.Likely,
            (_, >= 14) => Confidence.Possible,
            _ => Confidence.Possible
        };

        var reasons = new List<string>(evidence[kind]);

        if (margin < 14 && ordered.Count > 1)
        {
            reasons.Add($"It could also be a {Describe(ordered[1].Key)}.");
        }

        return new ProjectClassification
        {
            Kind = kind,
            Confidence = confidence,
            Reasons = reasons
        };
    }

    /// <summary>
    /// Whether the text mentions a signal. A phrase is looked for as written; a single word
    /// has to be a word in its own right, not a fragment of a longer one.
    /// </summary>
    private static bool Mentions(string haystack, IReadOnlySet<string> words, string signal) =>
        signal.Contains(' ', StringComparison.Ordinal)
            ? haystack.Contains(signal, StringComparison.Ordinal)
            : words.Contains(signal);

    private static IReadOnlySet<string> Tokenise(string text) =>
        text.Split([' ', '-', '_', '.', ',', ':', ';', '!', '?', '(', ')', '/', '\\', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    private static string Describe(ProjectKind kind) => kind switch
    {
        ProjectKind.DesktopApplication => "desktop application",
        ProjectKind.CommandLineTool => "command line tool",
        ProjectKind.GameOrEmulator => "game or emulator",
        ProjectKind.Audio => "audio program",
        ProjectKind.Video => "video program",
        ProjectKind.Utility => "utility",
        ProjectKind.DeveloperTool => "developer tool",
        ProjectKind.Library => "library",
        _ => "project of some other kind"
    };

    /// <summary>
    /// Refines a metadata classification once the analyzer has looked inside. The
    /// analyzer's answer wins where it has one, because it has seen the files.
    /// </summary>
    public static ProjectClassification Refine(
        ProjectClassification fromMetadata, ApplicationType applicationType)
    {
        var refined = applicationType switch
        {
            ApplicationType.DesktopApplication => ProjectKind.DesktopApplication,
            ApplicationType.CliTool => ProjectKind.CommandLineTool,
            ApplicationType.Game => ProjectKind.GameOrEmulator,
            ApplicationType.Library or ApplicationType.Framework => ProjectKind.Library,
            ApplicationType.DeveloperTool => ProjectKind.DeveloperTool,
            _ => ProjectKind.Unknown
        };

        if (refined == ProjectKind.Unknown) return fromMetadata;

        // Audio, Video and Utility are subject matter rather than shape, so an analyzer
        // that says "desktop application" does not contradict a metadata answer of
        // "audio" - it confirms the metadata answer was about what the program is for.
        if (refined == ProjectKind.DesktopApplication
            && fromMetadata.Kind is ProjectKind.Audio or ProjectKind.Video or ProjectKind.Utility
                or ProjectKind.GameOrEmulator)
        {
            return fromMetadata with
            {
                Confidence = Confidence.Likely,
                Reasons = [.. fromMetadata.Reasons, "RepoDeck looked inside and found a desktop application."]
            };
        }

        return new ProjectClassification
        {
            Kind = refined,
            Confidence = Confidence.Confirmed,
            Reasons = [$"RepoDeck looked inside the project and found a "
                       + $"{applicationType.ToDisplayString().ToLowerInvariant()}."]
        };
    }
}
