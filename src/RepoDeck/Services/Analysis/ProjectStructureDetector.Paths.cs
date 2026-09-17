namespace RepoDeck.Services.Analysis;

public static partial class ProjectStructureDetector
{
    /// <summary>File name without its directory. GitHub tree paths always use forward slashes.</summary>
    private static string FileNameOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    /// <summary>True when any path is, or ends with, a file of exactly this name.</summary>
    private static bool HasFileNamed(IEnumerable<string> lowerPaths, string lowerFileName) =>
        lowerPaths.Any(p => FileNameOf(p).Equals(lowerFileName, StringComparison.Ordinal));

    private static bool HasPathEnding(IEnumerable<string> lowerPaths, string lowerSuffix) =>
        lowerPaths.Any(p => p.Equals(lowerSuffix, StringComparison.Ordinal)
                            || p.EndsWith("/" + lowerSuffix, StringComparison.Ordinal));

    private static List<string> WithExtension(IEnumerable<string> paths, params string[] extensions) =>
        paths.Where(p => extensions.Any(e => p.EndsWith(e, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>How many directories deep a path sits. A root file has depth 0.</summary>
    private static int DepthOf(string path) => path.Count(c => c == '/');

    /// <summary>
    /// The copy nearest the repository root. A solution file at the top level describes
    /// the project far better than one buried in a samples folder.
    /// </summary>
    private static string Shallowest(IEnumerable<string> paths) =>
        paths.OrderBy(p => p.Count(c => c == '/')).ThenBy(p => p.Length).First();

    private static string? FindShallowest(IEnumerable<string> paths, string fileName)
    {
        var matches = paths
            .Where(p => FileNameOf(p).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            // Vendored dependencies describe someone else's project, not this one.
            .Where(p => !IsVendored(p))
            .ToList();

        return matches.Count == 0 ? null : Shallowest(matches);
    }

    /// <summary>
    /// Third-party code checked into the repository. A package.json inside node_modules
    /// says nothing about what this repository is.
    /// </summary>
    private static bool IsVendored(string path)
    {
        var lower = path.ToLowerInvariant();
        string[] markers =
        [
            "node_modules/", "vendor/", "third_party/", "thirdparty/", "external/",
            "packages/microsoft.", "/bin/", "/obj/", "test/fixtures/", "testdata/"
        ];

        return markers.Any(m => lower.Contains(m, StringComparison.Ordinal));
    }
}
