namespace RepoDeck.Models;

/// <summary>
/// One entry from a repository's file listing.
/// </summary>
/// <remarks>
/// Obtained from GitHub's git tree endpoint, which returns the whole listing in a
/// single request. That is what lets RepoDeck classify a repository by its structure
/// without cloning it and without one request per file.
/// </remarks>
public sealed class RepositoryTreeEntry
{
    public string Path { get; init; } = "";

    /// <summary>"blob" for a file, "tree" for a directory.</summary>
    public string Type { get; init; } = "";

    public long? Size { get; init; }

    public bool IsFile => string.Equals(Type, "blob", StringComparison.Ordinal);
    public bool IsDirectory => string.Equals(Type, "tree", StringComparison.Ordinal);

    /// <summary>File name without any directory part.</summary>
    public string FileName
    {
        get
        {
            var slash = Path.LastIndexOf('/');
            return slash >= 0 ? Path[(slash + 1)..] : Path;
        }
    }

    /// <summary>How deep the entry sits. A file at the root has depth 0.</summary>
    public int Depth => Path.Count(c => c == '/');
}

/// <summary>A repository's file listing, plus whether GitHub gave us all of it.</summary>
public sealed record RepositoryTree
{
    public IReadOnlyList<RepositoryTreeEntry> Entries { get; init; } = [];

    /// <summary>
    /// GitHub truncates very large trees. When true, absence of a file proves nothing,
    /// and RepoDeck must not conclude "no solution file" from an incomplete listing.
    /// </summary>
    public bool IsTruncated { get; init; }

    public static RepositoryTree Empty { get; } = new();

    public bool IsEmpty => Entries.Count == 0;
}
