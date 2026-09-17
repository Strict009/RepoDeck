using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

public class ClassificationTests
{
    private static readonly MachineProfile WindowsX64 =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static ProjectStructure Structure(
        RepositoryTree tree, params (string Path, string Content)[] manifests)
    {
        var structure = ProjectStructureDetector.DetectFromTree(tree);
        var contents = manifests.ToDictionary(m => m.Path, m => m.Content);
        return ProjectStructureDetector.RefineWithManifests(structure, contents);
    }

    [Fact]
    public void An_Avalonia_app_with_a_Windows_release_is_classified_as_a_desktop_application()
    {
        var structure = Structure(
            TestTrees.Of("App.sln", "src/App/App.csproj"),
            ("src/App/App.csproj", TestManifests.AvaloniaDesktopProject));

        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v1.0", false, "App-win-x64.zip")], WindowsX64);

        var result = ApplicationClassifier.Classify(structure, releases, TestRepositories.Create());

        Assert.Equal(ApplicationType.DesktopApplication, result.Type);
        Assert.Equal(Confidence.Likely, result.Confidence);
        Assert.Contains("Avalonia", structure.Frameworks);
        Assert.Contains(result.Evidence, e => e.Text.Contains("Avalonia"));
    }

    [Fact]
    public void An_Electron_project_is_classified_as_a_desktop_application()
    {
        var structure = Structure(
            TestTrees.Of("package.json", "src/main.js"),
            ("package.json", TestManifests.ElectronPackage));

        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v2.0", false, "Thing-Setup-win-x64.exe")], WindowsX64);

        var result = ApplicationClassifier.Classify(structure, releases, TestRepositories.Create());

        Assert.Equal(ApplicationType.DesktopApplication, result.Type);
        Assert.Contains("Electron", structure.Frameworks);
    }

    [Fact]
    public void A_popular_library_is_not_mislabelled_as_an_application()
    {
        // Stars are popularity, not evidence of being runnable.
        var structure = Structure(
            TestTrees.Of("package.json", "src/index.ts"),
            ("package.json", TestManifests.TypeScriptLibraryPackage));

        var repository = TestRepositories.Create(
            stars: 250_000, description: "A tiny utility library.", topics: ["library"]);

        var releases = ReleaseAnalyzer.Analyze([], WindowsX64);
        var result = ApplicationClassifier.Classify(structure, releases, repository);

        Assert.Equal(ApplicationType.Library, result.Type);
    }

    [Fact]
    public void A_Python_project_with_console_entry_points_is_a_command_line_tool()
    {
        var structure = Structure(
            TestTrees.Of("pyproject.toml", "src/tool/__main__.py"),
            ("pyproject.toml", TestManifests.PythonCliProject));

        var releases = ReleaseAnalyzer.Analyze([], WindowsX64);
        var result = ApplicationClassifier.Classify(structure, releases,
            TestRepositories.Create(description: "Does a thing on the command line.", topics: ["cli"]));

        Assert.Equal(ApplicationType.CliTool, result.Type);
    }

    [Fact]
    public void A_Unity_repository_is_classified_as_a_game()
    {
        var structure = Structure(TestTrees.WithDirectories(
            ["Assets/Scripts/Player.cs", "ProjectSettings/ProjectVersion.txt"],
            "Assets", "ProjectSettings"));

        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v1.0", false, "Game-win-x64.zip")], WindowsX64);

        var result = ApplicationClassifier.Classify(structure, releases, TestRepositories.Create());

        Assert.Equal(ApplicationType.Game, result.Type);
        Assert.Equal(Confidence.Likely, result.Confidence);
    }

    [Fact]
    public void A_dotnet_library_project_is_classified_as_a_library()
    {
        var structure = Structure(
            TestTrees.Of("Lib.sln", "src/Lib/Lib.csproj"),
            ("src/Lib/Lib.csproj", TestManifests.NuGetLibraryProject));

        var releases = ReleaseAnalyzer.Analyze([], WindowsX64);
        var result = ApplicationClassifier.Classify(structure, releases,
            TestRepositories.Create(topics: ["library"]));

        Assert.Equal(ApplicationType.Library, result.Type);
    }

    [Fact]
    public void Nothing_to_go_on_produces_Unknown_rather_than_a_guess()
    {
        var structure = ProjectStructureDetector.DetectFromTree(RepositoryTree.Empty);
        var releases = ReleaseAnalyzer.Analyze([], WindowsX64);

        var result = ApplicationClassifier.Classify(
            structure, releases, TestRepositories.Create(description: null, topics: []));

        Assert.Equal(ApplicationType.Unknown, result.Type);
        Assert.Equal(Confidence.Unknown, result.Confidence);
        Assert.NotEmpty(result.Unknowns);
    }

    [Fact]
    public void Classification_never_claims_to_be_confirmed()
    {
        // What a project is for is always inferred from how it is built.
        var structure = Structure(
            TestTrees.Of("App.csproj"), ("App.csproj", TestManifests.AvaloniaDesktopProject));

        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v1.0", false, "App-win-x64.zip", "App-linux-x64.tar.gz")],
            WindowsX64);

        var result = ApplicationClassifier.Classify(
            structure, releases, TestRepositories.Create(topics: ["desktop", "gui"]));

        Assert.NotEqual(Confidence.Confirmed, result.Confidence);
    }
}
