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
public sealed record DiscoverCategory(string Label, string Query, string Description)
{
    /// <summary>
    /// The starting set. Ordered by how likely someone is to want it rather than
    /// alphabetically, which would put Development first and mislead everybody.
    /// </summary>
    public static IReadOnlyList<DiscoverCategory> All { get; } =
    [
        new("Video", "video editor player converter", "Editing, playing and converting video"),
        new("Audio", "music player audio editor", "Players, editors and sound tools"),
        new("Games", "game emulator retro gaming", "Games and emulators"),
        new("Utilities", "desktop utility tool windows", "Small tools that do one job well"),
        new("Graphics", "image editor drawing graphics tool", "Drawing, editing and viewing images"),
        new("Files", "file manager backup sync duplicate files", "Managing, moving and tidying files"),
        new("Networking", "file transfer share network tool", "Sharing and transferring between machines"),
        new("Productivity", "note taking todo productivity app", "Notes, tasks and writing"),
        new("Development", "developer tool editor terminal", "Tools for writing software")
    ];
}
