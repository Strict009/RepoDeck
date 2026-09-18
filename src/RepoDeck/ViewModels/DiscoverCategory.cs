using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>
/// A starting point on the Discover landing page.
/// </summary>
/// <remarks>
/// Each one runs an ordinary search - there is no curated catalogue behind them and
/// RepoDeck does not pretend otherwise. They exist because an empty search box is a poor
/// thing to greet someone with, and because "Video" is a better prompt than nothing at
/// all for a person who has not yet decided what they want.
///
/// The query is deliberately more specific than the label: searching the word "Games"
/// alone returns game engines and tutorials, so the label stays short and the query does
/// the work.
/// </remarks>
public sealed record DiscoverCategory(
    string Label, string Query, string Description, ProjectKind Kind = ProjectKind.Unknown)
{
    /// <summary>
    /// The nine categories, ordered by how likely somebody is to want one rather than
    /// alphabetically, which would put Developer Tools first and mislead everybody.
    /// </summary>
    public static IReadOnlyList<DiscoverCategory> All { get; } =
    [
        new("Utilities", "desktop utility tool windows", "Small tools that do one job well",
            ProjectKind.Utility),

        new("Media", "music player video editor media", "Playing and editing music and video",
            ProjectKind.Audio),

        new("Games & Emulation", "game emulator retro gaming", "Games, and the things that run them",
            ProjectKind.GameOrEmulator),

        new("Graphics", "image editor drawing graphics tool", "Drawing, editing and viewing images",
            ProjectKind.Video),

        new("File Tools", "file manager backup sync duplicate files",
            "Managing, moving and tidying files", ProjectKind.DesktopApplication),

        new("Networking", "file transfer share network tool",
            "Sharing and transferring between machines", ProjectKind.CommandLineTool),

        new("Privacy", "privacy encryption password manager secure",
            "Passwords, encryption and staying private", ProjectKind.Utility),

        new("Productivity", "note taking todo productivity app", "Notes, tasks and writing",
            ProjectKind.Library),

        new("Developer Tools", "developer tool editor terminal", "Tools for writing software",
            ProjectKind.DeveloperTool)
    ];
}

/// <summary>
/// A way of slicing the search rather than a subject: newest, smallest, portable.
/// </summary>
/// <remarks>
/// These are searches too. Nothing here is hand-picked by anybody, and the descriptions
/// say what each one actually asks GitHub for, because "Popular on GitHub" could easily be
/// read as "RepoDeck recommends" - which it is not and must never become. Popularity is
/// GitHub's own number, reported, not endorsed.
/// </remarks>
public sealed record DiscoverCollection(
    string Label,
    string Query,
    string Description,
    RepositorySort Sort = RepositorySort.BestMatch,
    int? MinStars = null,
    UpdatedWithin Updated = UpdatedWithin.Any,
    ProjectKind Kind = ProjectKind.Unknown,
    bool Featured = false)
{
    /// <summary>
    /// The collections that lead, shown as large cards rather than chips.
    /// </summary>
    /// <remarks>
    /// Featured means "worth putting first for somebody who does not know what to search
    /// for", not "better". Three of the four are about how a program is delivered, which is
    /// the thing a beginner has no way to judge and the thing that decides whether RepoDeck
    /// can help. The fourth exists because browsing should be allowed to be fun.
    ///
    /// Popular on GitHub is deliberately not among them. Promoting it would turn a star
    /// count owned by GitHub into a recommendation owned by RepoDeck, which is exactly the
    /// line this project does not cross.
    /// </remarks>
    /// <remarks>
    /// Computed on each call rather than stored in a static field. A field initialiser here
    /// runs before the one for <see cref="All"/> further down the file, which made every
    /// collection test throw a TypeInitializationException; deriving it on demand removes
    /// the ordering hazard rather than depending on the declarations staying in order.
    /// </remarks>
    public static IReadOnlyList<DiscoverCollection> FeaturedOnly =>
        All.Where(c => c.Featured).ToList();

    /// <summary>The rest, as ordinary chips.</summary>
    public static IReadOnlyList<DiscoverCollection> SecondaryOnly =>
        All.Where(c => !c.Featured).ToList();

    public static IReadOnlyList<DiscoverCollection> All { get; } =
    [
        new("Popular on GitHub", "desktop application tool",
            "The most starred. GitHub's own number, not a recommendation.",
            Sort: RepositorySort.Stars, MinStars: 1000),

        new("Recently updated", "desktop application tool",
            "Worked on in the last month, so somebody is still there.",
            Sort: RepositorySort.RecentlyUpdated, Updated: UpdatedWithin.PastMonth),

        new("Portable apps", "portable app no installation single executable",
            "Nothing to install: unpack it and run it.",
            Kind: ProjectKind.Utility, Featured: true),

        new("No installation needed", "portable standalone executable zip release",
            "Published as an archive or a single file rather than an installer.",
            Kind: ProjectKind.DesktopApplication, Featured: true),

        new("Small & useful", "small simple lightweight utility",
            "Projects that describe themselves as small, simple or lightweight.",
            Kind: ProjectKind.CommandLineTool, Featured: true),

        // The one that could only exist here. A search, like the rest, and it will return
        // some rubbish - which is rather the point of looking.
        new("Weird & useful", "unusual quirky niche tool oddity",
            "Odd little programs somebody made because they wanted them to exist.",
            Kind: ProjectKind.GameOrEmulator, Featured: true)
    ];
}
