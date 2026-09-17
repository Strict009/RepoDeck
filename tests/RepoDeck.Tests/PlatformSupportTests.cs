using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

public class PlatformSupportTests
{
    private static readonly MachineProfile WindowsX64 =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static ProjectStructure Structure(string projectPath, string content)
    {
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of(projectPath));
        return ProjectStructureDetector.RefineWithManifests(
            structure, new Dictionary<string, string> { [projectPath] = content });
    }

    [Fact]
    public void A_Windows_only_toolkit_rules_other_platforms_out()
    {
        var structure = Structure("App.csproj", TestManifests.WpfProject);

        var (platforms, evidence) = PlatformSupportAnalyzer.Analyze(
            structure, ReleaseAnalyzer.Analyze([], WindowsX64));

        Assert.Equal(Confidence.Likely, platforms[OsPlatform.Windows]);
        Assert.Equal(Confidence.Unsupported, platforms[OsPlatform.Linux]);
        Assert.Equal(Confidence.Unsupported, platforms[OsPlatform.MacOS]);
        Assert.Contains(evidence, e => e.Text.Contains("only runs on Windows"));
    }

    [Fact]
    public void A_published_build_confirms_platform_support()
    {
        var structure = ProjectStructureDetector.DetectFromTree(TestTrees.Of("App.csproj"));
        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v1.0", false, "App-win-x64.zip", "App-linux-x64.tar.gz")],
            WindowsX64);

        var (platforms, _) = PlatformSupportAnalyzer.Analyze(structure, releases);

        Assert.Equal(Confidence.Confirmed, platforms[OsPlatform.Windows]);
        Assert.Equal(Confidence.Confirmed, platforms[OsPlatform.Linux]);
    }

    [Fact]
    public void A_published_build_outweighs_a_toolkit_based_assumption()
    {
        // If a Linux build exists, a WPF reference elsewhere must not rule Linux out.
        var structure = Structure("App.csproj", TestManifests.WpfProject);
        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v1.0", false, "App-linux-x64.tar.gz")], WindowsX64);

        var (platforms, _) = PlatformSupportAnalyzer.Analyze(structure, releases);

        Assert.Equal(Confidence.Confirmed, platforms[OsPlatform.Linux]);
    }

    [Fact]
    public void A_cross_platform_toolkit_suggests_all_three_platforms()
    {
        var structure = Structure("App.csproj", TestManifests.AvaloniaDesktopProject);

        var (platforms, _) = PlatformSupportAnalyzer.Analyze(
            structure, ReleaseAnalyzer.Analyze([], WindowsX64));

        Assert.Equal(Confidence.Likely, platforms[OsPlatform.Windows]);
        Assert.Equal(Confidence.Likely, platforms[OsPlatform.Linux]);
        Assert.Equal(Confidence.Likely, platforms[OsPlatform.MacOS]);
    }

    [Fact]
    public void Nothing_known_produces_no_platform_claims_at_all()
    {
        var structure = ProjectStructureDetector.DetectFromTree(RepositoryTree.Empty);

        var (platforms, _) = PlatformSupportAnalyzer.Analyze(
            structure, ReleaseAnalyzer.Analyze([], WindowsX64));

        Assert.Empty(platforms);
    }
}
