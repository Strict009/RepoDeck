using RepoDeck.Models;

namespace RepoDeck.Tests;

/// <summary>Builds repository file listings for the structure detector tests.</summary>
internal static class TestTrees
{
    public static RepositoryTree Of(params string[] filePaths) => new()
    {
        Entries = filePaths
            .Select(p => new RepositoryTreeEntry { Path = p, Type = "blob", Size = 100 })
            .ToList()
    };

    public static RepositoryTree WithDirectories(string[] files, params string[] directories) => new()
    {
        Entries = files
            .Select(p => new RepositoryTreeEntry { Path = p, Type = "blob", Size = 100 })
            .Concat(directories.Select(d => new RepositoryTreeEntry { Path = d, Type = "tree" }))
            .ToList()
    };

    public static RepositoryTree Truncated(params string[] filePaths) => new()
    {
        Entries = filePaths
            .Select(p => new RepositoryTreeEntry { Path = p, Type = "blob", Size = 100 })
            .ToList(),
        IsTruncated = true
    };
}
