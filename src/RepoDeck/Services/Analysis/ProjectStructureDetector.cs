using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Classifies a repository from its file listing, and says which manifest files are
/// worth reading to sharpen the answer.
/// </summary>
/// <remarks>
/// Pure: it takes a file listing and returns findings, with no network access of its own.
/// Deliberately ignores the repository name - a repository called "awesome-editor" is not
/// an editor, and a project called "lib" may well be an application.
/// </remarks>
public static partial class ProjectStructureDetector
{
    public static ProjectStructure DetectFromTree(RepositoryTree tree)
    {
        if (tree.IsEmpty)
        {
            return ProjectStructure.Unknown with { ListingWasTruncated = tree.IsTruncated };
        }

        var paths = tree.Entries.Where(e => e.IsFile).Select(f => f.Path).ToList();
        var lowerPaths = paths.Select(p => p.ToLowerInvariant()).ToList();
        var directories = tree.Entries
            .Where(e => e.IsDirectory)
            .Select(e => e.Path.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var context = new DetectionContext(paths, lowerPaths, directories);

        DetectGameEngines(context);
        DetectDotNet(context);
        DetectNode(context);
        DetectRust(context);
        DetectPython(context);
        DetectJava(context);
        DetectNative(context);
        DetectGo(context);
        DetectContainersAndScripts(context);

        return new ProjectStructure
        {
            ProjectTypes = context.Types,
            BuildSystem = context.BuildSystem,
            Evidence = context.Evidence,
            FilesWorthReading = context.FilesToRead.Take(3).ToList(),
            ListingWasTruncated = tree.IsTruncated,
            ApplicationHints = context.Hints
        };
    }

    private sealed class DetectionContext(
        List<string> paths, List<string> lowerPaths, HashSet<string> directories)
    {
        public List<string> Paths { get; } = paths;
        public List<string> LowerPaths { get; } = lowerPaths;
        public HashSet<string> Directories { get; } = directories;

        /// <summary>
        /// Detected types with the depth of the marker file that proved each one. The
        /// marker nearest the repository root identifies what the project actually is:
        /// a package.json in a docs folder does not make a C++ project a Node project.
        /// </summary>
        private readonly List<(ProjectType Type, int Depth)> _typed = [];

        public List<ProjectType> Types =>
            _typed.OrderBy(t => t.Depth).Select(t => t.Type).Distinct().ToList();

        public void AddType(ProjectType type, int depth) => _typed.Add((type, depth));

        public bool HasType(ProjectType type) => _typed.Any(t => t.Type == type);

        public int TypeCount => _typed.Count;
        public List<Evidence> Evidence { get; } = [];
        public List<ApplicationTypeHint> Hints { get; } = [];
        public List<string> FilesToRead { get; } = [];
        public BuildSystem BuildSystem { get; set; } = BuildSystem.Unknown;

        public void SetBuildSystem(BuildSystem system)
        {
            if (BuildSystem == BuildSystem.Unknown) BuildSystem = system;
        }
    }
}
