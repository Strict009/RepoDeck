using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

public static partial class ProjectStructureDetector
{
    /// <summary>
    /// Second pass: read the manifests named in <see cref="ProjectStructure.FilesWorthReading"/>.
    /// This is where a .NET project becomes "an Avalonia desktop application" rather than
    /// "something built with MSBuild", and where a Node package is separated into a
    /// command-line tool, an Electron application or a library.
    /// </summary>
    public static ProjectStructure RefineWithManifests(
        ProjectStructure structure, IReadOnlyDictionary<string, string> contents)
    {
        if (contents.Count == 0) return structure;

        var frameworks = structure.Frameworks.ToList();
        var evidence = structure.Evidence.ToList();
        var hints = structure.ApplicationHints.ToList();

        foreach (var (path, content) in contents)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;

            var fileName = FileNameOf(path).ToLowerInvariant();

            if (fileName.EndsWith(".csproj") || fileName.EndsWith(".fsproj") || fileName.EndsWith(".vbproj"))
            {
                InspectDotNetProject(content, frameworks, evidence, hints);
            }
            else if (fileName == "package.json")
            {
                InspectPackageJson(content, frameworks, evidence, hints);
            }
            else if (fileName == "cargo.toml")
            {
                InspectCargoToml(content, hints);
            }
            else if (fileName is "pyproject.toml" or "setup.py")
            {
                InspectPythonManifest(content, hints);
            }
        }

        return structure with
        {
            Frameworks = frameworks.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Evidence = evidence,
            ApplicationHints = hints
        };
    }

    private static bool Mentions(string content, string needle) =>
        content.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
