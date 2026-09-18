namespace RepoDeck.Models;

/// <summary>
/// What kind of thing a project is, in the terms a person browsing for software uses.
/// </summary>
/// <remarks>
/// Deliberately coarse and deliberately separate from <see cref="ApplicationType"/>, which
/// is the analyzer's finer classification from looking inside a repository. This one is
/// answerable from a search result, because the Discover grid shows thirty cards and
/// cannot afford an extra request each.
///
/// Its two jobs are to pick the artwork for a project with no usable pictures, and to feed
/// the relevance layer. It is never a quality judgement and never a safety judgement.
/// </remarks>
public enum ProjectKind
{
    /// <summary>Not enough evidence to say. The honest default.</summary>
    Unknown,

    /// <summary>A program with a window.</summary>
    DesktopApplication,

    /// <summary>A program you type at.</summary>
    CommandLineTool,

    /// <summary>A game, or something that runs games.</summary>
    GameOrEmulator,

    /// <summary>Music, sound, recording.</summary>
    Audio,

    /// <summary>Video playback, editing, conversion.</summary>
    Video,

    /// <summary>A small thing that does one job: files, backups, screenshots, clipboards.</summary>
    Utility,

    /// <summary>Editors, terminals, debuggers - software for making software.</summary>
    DeveloperTool,

    /// <summary>A building block for programmers rather than a program.</summary>
    Library
}

/// <summary>A classification together with the evidence behind it.</summary>
public sealed record ProjectClassification
{
    public ProjectKind Kind { get; init; } = ProjectKind.Unknown;

    /// <summary>
    /// How sure RepoDeck is. Metadata alone never exceeds <see cref="Confidence.Likely"/>,
    /// because tags and a one-line description are not the same as looking inside.
    /// </summary>
    public Confidence Confidence { get; init; } = Confidence.Unknown;

    /// <summary>Why, in plain English. Never empty for a kind other than Unknown.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>The word shown on generated artwork and in a tooltip.</summary>
    public string Label => Kind switch
    {
        ProjectKind.DesktopApplication => "Desktop app",
        ProjectKind.CommandLineTool => "Command line",
        ProjectKind.GameOrEmulator => "Game",
        ProjectKind.Audio => "Audio",
        ProjectKind.Video => "Video",
        ProjectKind.Utility => "Utility",
        ProjectKind.DeveloperTool => "Developer tool",
        ProjectKind.Library => "Library",
        _ => "Project"
    };

    /// <summary>True for kinds an ordinary person can expect to run.</summary>
    public bool IsRunnableSoftware => Kind is
        ProjectKind.DesktopApplication or ProjectKind.GameOrEmulator or
        ProjectKind.Audio or ProjectKind.Video or ProjectKind.Utility or
        ProjectKind.CommandLineTool;

    public static ProjectClassification Unknown { get; } = new();
}
