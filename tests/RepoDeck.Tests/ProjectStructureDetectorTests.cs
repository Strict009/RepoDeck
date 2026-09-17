using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

public class ProjectStructureDetectorTests
{
    [Fact]
    public void A_dotnet_solution_is_recognised()
    {
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.Of("MyApp.sln", "src/MyApp/MyApp.csproj", "src/MyApp/Program.cs"));

        Assert.Contains(ProjectType.DotNet, structure.ProjectTypes);
        Assert.Equal(BuildSystem.MsBuild, structure.BuildSystem);
        Assert.Contains(structure.Evidence, e => e.Text.Contains("solution file"));
        Assert.Contains("src/MyApp/MyApp.csproj", structure.FilesWorthReading);
    }

    [Fact]
    public void A_Unity_project_is_recognised_from_its_folder_layout()
    {
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.WithDirectories(
                ["Assets/Scripts/Player.cs", "ProjectSettings/ProjectVersion.txt"],
                "Assets", "ProjectSettings"));

        Assert.Contains(ProjectType.Unity, structure.ProjectTypes);
        Assert.Equal(BuildSystem.Unity, structure.BuildSystem);
        Assert.Contains(structure.ApplicationHints, h => h.Type == ApplicationType.Game);
    }

    [Fact]
    public void A_Unity_project_does_not_also_report_itself_as_a_plain_dotnet_project()
    {
        // Unity repositories contain .csproj files, but calling one "a .NET project"
        // would be technically true and completely unhelpful.
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.WithDirectories(
                ["Assets/Scripts/Player.cs", "ProjectSettings/ProjectVersion.txt", "MyGame.csproj"],
                "Assets", "ProjectSettings"));

        Assert.Equal(ProjectType.Unity, structure.PrimaryProjectType);
        Assert.DoesNotContain(ProjectType.DotNet, structure.ProjectTypes);
    }

    [Fact]
    public void A_Godot_project_is_recognised()
    {
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.Of("project.godot", "scenes/main.tscn"));

        Assert.Contains(ProjectType.Godot, structure.ProjectTypes);
        Assert.Contains(structure.ApplicationHints, h => h.Type == ApplicationType.Game);
    }

    [Theory]
    [InlineData("package.json", ProjectType.Node, BuildSystem.Npm)]
    [InlineData("Cargo.toml", ProjectType.Rust, BuildSystem.Cargo)]
    [InlineData("pyproject.toml", ProjectType.Python, BuildSystem.PythonPackaging)]
    [InlineData("setup.py", ProjectType.Python, BuildSystem.PythonPackaging)]
    [InlineData("requirements.txt", ProjectType.Python, BuildSystem.PythonPackaging)]
    [InlineData("pom.xml", ProjectType.Java, BuildSystem.Maven)]
    [InlineData("build.gradle", ProjectType.Java, BuildSystem.Gradle)]
    [InlineData("build.gradle.kts", ProjectType.Java, BuildSystem.Gradle)]
    [InlineData("CMakeLists.txt", ProjectType.CPlusPlus, BuildSystem.CMake)]
    [InlineData("Makefile", ProjectType.CPlusPlus, BuildSystem.Make)]
    public void Ecosystem_marker_files_are_recognised(string file, ProjectType type, BuildSystem build)
    {
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of(file, "README.md"));

        Assert.Contains(type, structure.ProjectTypes);
        Assert.Equal(build, structure.BuildSystem);
    }

    [Fact]
    public void Docker_files_are_recognised_and_hint_at_server_software()
    {
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.Of("Dockerfile", "docker-compose.yml", "src/main.go", "go.mod"));

        Assert.Contains(ProjectType.Docker, structure.ProjectTypes);
        Assert.Contains(ProjectType.Go, structure.ProjectTypes);
        Assert.Contains(structure.ApplicationHints, h => h.Type == ApplicationType.Server);
    }

    [Fact]
    public void A_repository_of_only_scripts_is_recognised_as_automation()
    {
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.Of("backup.ps1", "restore.sh", "README.md"));

        Assert.Contains(ProjectType.Shell, structure.ProjectTypes);
        Assert.Contains(structure.ApplicationHints, h => h.Type == ApplicationType.ScriptOrAutomation);
    }

    [Fact]
    public void Scripts_alongside_a_real_project_do_not_make_it_a_script_repository()
    {
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.Of("MyApp.sln", "src/MyApp/MyApp.csproj", "build.sh", "publish.ps1"));

        Assert.DoesNotContain(ProjectType.Shell, structure.ProjectTypes);
    }

    [Fact]
    public void Vendored_dependencies_are_ignored()
    {
        // A package.json inside node_modules describes somebody else's project.
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.Of("node_modules/left-pad/package.json", "vendor/thing/Cargo.toml", "README.md"));

        Assert.DoesNotContain(ProjectType.Node, structure.ProjectTypes);
        Assert.DoesNotContain(ProjectType.Rust, structure.ProjectTypes);
    }

    [Fact]
    public void The_manifest_nearest_the_root_is_the_one_chosen_for_reading()
    {
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.Of("samples/demo/package.json", "package.json"));

        Assert.Equal("package.json", Assert.Single(structure.FilesWorthReading));
    }

    [Fact]
    public void At_most_three_files_are_ever_queued_for_reading()
    {
        // Each file read is another GitHub request against a tight rate limit.
        var structure = ProjectStructureDetector.DetectFromTree(
            TestTrees.Of("App.csproj", "package.json", "Cargo.toml", "pyproject.toml", "go.mod"));

        Assert.True(structure.FilesWorthReading.Count <= 3);
    }

    [Fact]
    public void A_truncated_listing_is_reported_so_absence_is_not_read_as_proof()
    {
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Truncated("README.md"));

        Assert.True(structure.ListingWasTruncated);
    }

    [Fact]
    public void An_empty_repository_yields_nothing_rather_than_a_guess()
    {
        var structure = ProjectStructureDetector.DetectFromTree(RepositoryTree.Empty);

        Assert.True(structure.IsEmpty);
        Assert.Equal(ProjectType.Unknown, structure.PrimaryProjectType);
    }
}
