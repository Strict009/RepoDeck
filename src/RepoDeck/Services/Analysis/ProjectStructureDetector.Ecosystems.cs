using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

public static partial class ProjectStructureDetector
{
    private static void DetectGameEngines(DetectionContext context)
    {
        var hasUnityMarkerFile = HasPathEnding(context.LowerPaths, "projectsettings/projectversion.txt")
                                 || HasPathEnding(context.LowerPaths, "packages/manifest.json");
        var hasUnityDirectories = context.Directories.Contains("assets")
                                  && context.Directories.Contains("projectsettings");

        if (hasUnityMarkerFile || hasUnityDirectories)
        {
            context.Types.Add(ProjectType.Unity);
            context.SetBuildSystem(BuildSystem.Unity);
            context.Evidence.Add(new Evidence(
                "Has the Unity project layout: Assets and ProjectSettings folders.",
                EvidenceSource.FileStructure));
            context.Hints.Add(new ApplicationTypeHint(ApplicationType.Game, 80,
                new Evidence("Built with the Unity game engine.", EvidenceSource.FileStructure)));
        }

        if (HasFileNamed(context.LowerPaths, "project.godot"))
        {
            context.Types.Add(ProjectType.Godot);
            context.SetBuildSystem(BuildSystem.Godot);
            context.Evidence.Add(new Evidence(
                "Contains project.godot, a Godot game project.", EvidenceSource.FileStructure));
            context.Hints.Add(new ApplicationTypeHint(ApplicationType.Game, 80,
                new Evidence("Built with the Godot game engine.", EvidenceSource.FileStructure)));
        }
    }

    private static void DetectDotNet(DetectionContext context)
    {
        var solutions = WithExtension(context.Paths, ".sln", ".slnx");
        var projects = WithExtension(context.Paths, ".csproj", ".fsproj", ".vbproj");

        if (solutions.Count == 0 && projects.Count == 0) return;

        if (!context.Types.Contains(ProjectType.Unity)) context.Types.Add(ProjectType.DotNet);
        context.SetBuildSystem(BuildSystem.MsBuild);

        context.Evidence.Add(solutions.Count > 0
            ? new Evidence($"Contains a .NET solution file ({FileNameOf(solutions[0])}).",
                EvidenceSource.FileStructure)
            : new Evidence($"Contains a .NET project file ({FileNameOf(projects[0])}).",
                EvidenceSource.FileStructure));

        // The project file states whether this builds a window, a console tool or a library.
        if (projects.Count > 0) context.FilesToRead.Add(Shallowest(projects));
    }

    private static void DetectNode(DetectionContext context)
    {
        var packageJson = FindShallowest(context.Paths, "package.json");
        if (packageJson is null) return;

        context.Types.Add(ProjectType.Node);
        context.SetBuildSystem(BuildSystem.Npm);
        context.Evidence.Add(new Evidence(
            "Contains package.json, a Node.js project.", EvidenceSource.FileStructure));
        context.FilesToRead.Add(packageJson);
    }

    private static void DetectRust(DetectionContext context)
    {
        var cargo = FindShallowest(context.Paths, "Cargo.toml");
        if (cargo is null) return;

        context.Types.Add(ProjectType.Rust);
        context.SetBuildSystem(BuildSystem.Cargo);
        context.Evidence.Add(new Evidence(
            "Contains Cargo.toml, a Rust project.", EvidenceSource.FileStructure));
        context.FilesToRead.Add(cargo);
    }

    private static void DetectPython(DetectionContext context)
    {
        var manifest = FindShallowest(context.Paths, "pyproject.toml")
                       ?? FindShallowest(context.Paths, "setup.py")
                       ?? FindShallowest(context.Paths, "requirements.txt");

        if (manifest is null) return;

        context.Types.Add(ProjectType.Python);
        context.SetBuildSystem(BuildSystem.PythonPackaging);
        context.Evidence.Add(new Evidence(
            $"Contains {FileNameOf(manifest)}, a Python project.", EvidenceSource.FileStructure));

        // requirements.txt lists dependencies but says nothing about what the project is.
        if (!manifest.EndsWith("requirements.txt", StringComparison.OrdinalIgnoreCase))
        {
            context.FilesToRead.Add(manifest);
        }
    }

    private static void DetectJava(DetectionContext context)
    {
        if (HasFileNamed(context.LowerPaths, "pom.xml"))
        {
            context.Types.Add(ProjectType.Java);
            context.SetBuildSystem(BuildSystem.Maven);
            context.Evidence.Add(new Evidence(
                "Contains pom.xml, a Maven project.", EvidenceSource.FileStructure));
        }
        else if (HasFileNamed(context.LowerPaths, "build.gradle")
                 || HasFileNamed(context.LowerPaths, "build.gradle.kts"))
        {
            context.Types.Add(ProjectType.Java);
            context.SetBuildSystem(BuildSystem.Gradle);
            context.Evidence.Add(new Evidence(
                "Contains a Gradle build script.", EvidenceSource.FileStructure));
        }
    }

    private static void DetectNative(DetectionContext context)
    {
        if (HasFileNamed(context.LowerPaths, "cmakelists.txt"))
        {
            context.Types.Add(ProjectType.CPlusPlus);
            context.SetBuildSystem(BuildSystem.CMake);
            context.Evidence.Add(new Evidence(
                "Contains CMakeLists.txt, a CMake project.", EvidenceSource.FileStructure));
        }
        else if (HasFileNamed(context.LowerPaths, "makefile"))
        {
            context.Types.Add(ProjectType.CPlusPlus);
            context.SetBuildSystem(BuildSystem.Make);
            context.Evidence.Add(new Evidence("Contains a Makefile.", EvidenceSource.FileStructure));
        }
    }

    private static void DetectGo(DetectionContext context)
    {
        if (!HasFileNamed(context.LowerPaths, "go.mod")) return;

        context.Types.Add(ProjectType.Go);
        context.Evidence.Add(new Evidence("Contains go.mod, a Go module.", EvidenceSource.FileStructure));
    }

    private static void DetectContainersAndScripts(DetectionContext context)
    {
        if (HasFileNamed(context.LowerPaths, "dockerfile")
            || HasFileNamed(context.LowerPaths, "docker-compose.yml")
            || HasFileNamed(context.LowerPaths, "compose.yml"))
        {
            context.Types.Add(ProjectType.Docker);
            context.Evidence.Add(new Evidence(
                "Contains Docker build or compose files.", EvidenceSource.FileStructure));
            context.Hints.Add(new ApplicationTypeHint(ApplicationType.Server, 25,
                new Evidence("Ships a Docker image, which usually means server software.",
                    EvidenceSource.FileStructure)));
        }

        var scripts = WithExtension(context.Paths, ".ps1", ".sh", ".bat", ".cmd");
        if (scripts.Count > 0 && context.Types.Count == 0)
        {
            context.Types.Add(ProjectType.Shell);
            var plural = scripts.Count == 1 ? "" : "s";
            context.Evidence.Add(new Evidence(
                $"Contains {scripts.Count} shell or batch script{plural} and no other project files.",
                EvidenceSource.FileStructure));
            context.Hints.Add(new ApplicationTypeHint(ApplicationType.ScriptOrAutomation, 50,
                new Evidence("Consists of scripts rather than a compiled program.",
                    EvidenceSource.FileStructure)));
        }
    }
}
