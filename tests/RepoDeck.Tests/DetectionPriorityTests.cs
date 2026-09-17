using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

/// <summary>
/// Regression tests for misclassifications found by running real repositories through
/// the analyzer. Both rules here are general, not adjustments for particular projects.
/// </summary>
public class DetectionPriorityTests
{
    private static readonly MachineProfile WindowsX64 =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    [Fact]
    public void A_root_marker_outranks_a_deeper_one_from_another_ecosystem()
    {
        // A Rust project with a Node-based documentation site is a Rust project.
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of(
            "Cargo.toml", "src/main.rs", "doc/site/package.json"));

        Assert.Equal(ProjectType.Rust, structure.PrimaryProjectType);
    }

    [Fact]
    public void A_cpp_project_with_python_tooling_is_still_a_cpp_project()
    {
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of(
            "CMakeLists.txt", "include/thing.hpp", "tools/generate/setup.py"));

        Assert.Equal(ProjectType.CPlusPlus, structure.PrimaryProjectType);
    }

    [Fact]
    public void A_cpp_project_with_a_stray_solution_file_is_still_a_cpp_project()
    {
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of(
            "CMakeLists.txt", "src/main.cpp", "tests/interop/Interop.sln"));

        Assert.Equal(ProjectType.CPlusPlus, structure.PrimaryProjectType);
    }

    [Fact]
    public void Docker_never_becomes_the_primary_type_when_a_real_ecosystem_is_present()
    {
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of(
            "Dockerfile", "go.mod", "main.go"));

        Assert.Equal(ProjectType.Go, structure.PrimaryProjectType);
        Assert.Contains(ProjectType.Docker, structure.ProjectTypes);
    }

    [Fact]
    public void Equal_depth_markers_both_survive()
    {
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of(
            "Cargo.toml", "package.json"));

        Assert.Contains(ProjectType.Rust, structure.ProjectTypes);
        Assert.Contains(ProjectType.Node, structure.ProjectTypes);
    }

    [Fact]
    public void Unlabelled_release_archives_are_not_evidence_of_an_application()
    {
        // A header-only C++ library shipping its headers in a ZIP is not a desktop app.
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of(
            "CMakeLists.txt", "include/thing.hpp"));

        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v3.12.0", false, "include.zip", "thing.hpp")], WindowsX64);

        var result = ApplicationClassifier.Classify(structure, releases,
            TestRepositories.Create(description: "A header-only library.", language: "C++"));

        Assert.NotEqual(ApplicationType.DesktopApplication, result.Type);
    }

    [Fact]
    public void Platform_labelled_builds_remain_evidence_of_an_application()
    {
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of("CMakeLists.txt"));

        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v1.0", false, "tool-win-x64.zip", "tool-linux-x64.tar.gz")],
            WindowsX64);

        var result = ApplicationClassifier.Classify(structure, releases, TestRepositories.Create());

        Assert.Contains(result.Evidence, e => e.Text.Contains("platform-specific build"));
    }
}
