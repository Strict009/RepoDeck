namespace RepoDeck.Models;

/// <summary>Where an image was found, which is most of what tells you how good it is.</summary>
public enum MediaSource
{
    /// <summary>GitHub's social preview card for the repository.</summary>
    SocialPreview,

    /// <summary>An image referenced from the README.</summary>
    Readme,

    /// <summary>A file seen in the repository's own listing, e.g. under screenshots/.</summary>
    RepositoryFile
}

/// <summary>What an image appears to be.</summary>
public enum MediaKind
{
    /// <summary>A picture of the application running. The most useful thing there is.</summary>
    Screenshot,

    /// <summary>A project logo or icon.</summary>
    Logo,

    /// <summary>GitHub's generated or uploaded preview card.</summary>
    SocialPreview,

    /// <summary>A build badge, coverage shield, sponsor button or similar. Never shown.</summary>
    Badge,

    /// <summary>Something else entirely.</summary>
    Unknown
}

/// <summary>
/// One image RepoDeck might show for a project.
/// </summary>
/// <remarks>
/// Candidates are identified and ranked without downloading anything. Only the winner is
/// ever fetched, because a README can reference dozens of images and most of them are
/// badges.
/// </remarks>
public sealed record MediaCandidate
{
    public required string Url { get; init; }
    public MediaSource Source { get; init; }
    public MediaKind Kind { get; init; } = MediaKind.Unknown;

    /// <summary>Alt text or the surrounding link text, when the README gave one.</summary>
    public string? Description { get; init; }

    /// <summary>Size in bytes when the repository listing reported it, otherwise null.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Ranking value. Internal; the user never sees a number.</summary>
    public int Score { get; init; }

    /// <summary>True for anything RepoDeck will not display whatever its score.</summary>
    public bool IsExcluded => Kind == MediaKind.Badge;
}

/// <summary>The imagery RepoDeck found for one project.</summary>
public sealed record RepositoryMedia
{
    /// <summary>Best first, badges already removed.</summary>
    public IReadOnlyList<MediaCandidate> Candidates { get; init; } = [];

    /// <summary>The best image of any kind, or null when there is nothing worth showing.</summary>
    public MediaCandidate? Primary => Candidates.Count > 0 ? Candidates[0] : null;

    /// <summary>
    /// The best image the project actually supplied: a screenshot, a logo or a picture
    /// from its documentation. Null when the only thing available is GitHub's generated
    /// preview card.
    /// </summary>
    /// <remarks>
    /// Surfaces exist where the generated card is the wrong thing to show. It is a
    /// rendering of the repository name and description in small type, so at the size of
    /// a result card it reads as unreadable text rather than as a picture of a program,
    /// and presenting it as artwork implies the user is expected to squint at it. Those
    /// surfaces ask for artwork and fall back to their own designed tile; surfaces with
    /// room to show it legibly ask for <see cref="Primary"/>.
    /// </remarks>
    public MediaCandidate? PrimaryArtwork =>
        Candidates.FirstOrDefault(c => c.Kind != MediaKind.SocialPreview);

    public bool HasArtwork => PrimaryArtwork is not null;

    /// <summary>Images for the details page gallery, best first.</summary>
    public IReadOnlyList<MediaCandidate> Gallery =>
        Candidates.Where(c => c.Kind is MediaKind.Screenshot or MediaKind.Logo or MediaKind.SocialPreview)
            .Take(6)
            .ToList();

    public bool HasMedia => Candidates.Count > 0;

    public static RepositoryMedia None { get; } = new();
}
