using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

public class ReleaseAnalyzerTests
{
    private static readonly MachineProfile WindowsX64 =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static readonly MachineProfile LinuxX64 =
        MachineProfile.For(OsPlatform.Linux, CpuArchitecture.X64);

    private static readonly MachineProfile WindowsArm64 =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.Arm64);

    private static GitHubRelease Release(params string[] assets) =>
        TestRepositories.Release("v1.0.0", false, assets);

    [Fact]
    public void On_Windows_x64_the_Windows_x64_build_is_recommended()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("Tool-win-x64.zip", "Tool-linux-x64.tar.gz", "Tool-macos-arm64.zip")],
            WindowsX64);

        Assert.Equal("Tool-win-x64.zip", analysis.Recommended?.Name);
        Assert.Equal(AssetCompatibility.Compatible, analysis.Recommended!.Compatibility);
    }

    [Fact]
    public void A_Linux_package_is_incompatible_on_Windows()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("Tool-win-x64.zip", "tool_1.0_amd64.deb")], WindowsX64);

        var deb = analysis.Assets.Single(a => a.Name.EndsWith(".deb"));
        Assert.Equal(AssetCompatibility.Incompatible, deb.Compatibility);
        Assert.NotEqual(deb.Name, analysis.Recommended?.Name);
    }

    [Fact]
    public void An_ARM64_build_is_incompatible_on_an_x64_machine()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("Tool-win-arm64.zip", "Tool-win-x64.zip")], WindowsX64);

        var arm = analysis.Assets.Single(a => a.Name.Contains("arm64"));
        Assert.Equal(AssetCompatibility.Incompatible, arm.Compatibility);
        Assert.Equal("Tool-win-x64.zip", analysis.Recommended?.Name);
    }

    [Fact]
    public void An_x64_build_runs_on_Windows_ARM64_but_is_marked_as_emulated()
    {
        var analysis = ReleaseAnalyzer.Analyze([Release("Tool-win-x64.zip")], WindowsArm64);

        Assert.Equal("Tool-win-x64.zip", analysis.Recommended?.Name);
        Assert.Equal(AssetCompatibility.CompatibleThroughEmulation, analysis.Recommended!.Compatibility);
    }

    [Fact]
    public void A_native_ARM64_build_beats_an_emulated_x64_one()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("Tool-win-x64.zip", "Tool-win-arm64.zip")], WindowsArm64);

        Assert.Equal("Tool-win-arm64.zip", analysis.Recommended?.Name);
    }

    [Fact]
    public void A_compiled_binary_always_beats_a_source_archive()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("Source code (zip)", "Tool-win-x64.zip")], WindowsX64);

        Assert.Equal("Tool-win-x64.zip", analysis.Recommended?.Name);
    }

    [Fact]
    public void A_source_archive_is_never_recommended_even_when_it_is_the_only_file()
    {
        var analysis = ReleaseAnalyzer.Analyze([Release("Source code (zip)")], WindowsX64);

        Assert.Null(analysis.Recommended);
        Assert.Contains("compiled", analysis.NoRecommendationReason);
    }

    [Fact]
    public void An_AppImage_is_recommended_on_Linux_x64()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("Tool-x86_64.AppImage", "Tool-win-x64.zip")], LinuxX64);

        Assert.Equal("Tool-x86_64.AppImage", analysis.Recommended?.Name);
        Assert.Equal(PackageType.AppImage, analysis.Recommended!.PackageType);
    }

    [Fact]
    public void An_unlabelled_zip_does_not_become_Windows_compatible_by_wishful_thinking()
    {
        var analysis = ReleaseAnalyzer.Analyze([Release("Tool.zip")], WindowsX64);

        var asset = Assert.Single(analysis.Assets);
        Assert.Equal(OsPlatform.Unknown, asset.Platform);
        Assert.Equal(AssetCompatibility.Unknown, asset.Compatibility);
        Assert.Contains(asset.Warnings, w => w.Contains("does not say which system"));
    }

    [Fact]
    public void An_explicit_build_outranks_an_unlabelled_one()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("Tool.zip", "Tool-win-x64.zip")], WindowsX64);

        Assert.Equal("Tool-win-x64.zip", analysis.Recommended?.Name);
    }

    [Fact]
    public void Checksums_are_never_recommended()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("checksums.txt", "Tool-win-x64.zip.sha256", "Tool-win-x64.zip")], WindowsX64);

        Assert.Equal("Tool-win-x64.zip", analysis.Recommended?.Name);
    }

    [Fact]
    public void A_portable_archive_is_preferred_over_an_installer()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("Tool-Setup-win-x64.exe", "Tool-portable-win-x64.zip")], WindowsX64);

        Assert.Equal("Tool-portable-win-x64.zip", analysis.Recommended?.Name);
    }

    [Fact]
    public void Draft_releases_are_ignored()
    {
        var draft = TestRepositories.DraftRelease("v2.0.0", "Tool-win-x64.zip");
        var published = TestRepositories.Release("v1.0.0", false, "Tool-win-x64.zip");

        Assert.Equal("v1.0.0", ReleaseAnalyzer.SelectRelease([draft, published])?.TagName);
    }

    [Fact]
    public void A_stable_release_is_preferred_over_a_newer_prerelease()
    {
        var beta = TestRepositories.Release("v2.0.0-beta", true, "Tool-win-x64.zip");
        var stable = TestRepositories.Release("v1.9.0", false, "Tool-win-x64.zip");

        Assert.Equal("v1.9.0", ReleaseAnalyzer.SelectRelease([beta, stable])?.TagName);
    }

    [Fact]
    public void A_prerelease_is_used_when_it_is_all_there_is()
    {
        var beta = TestRepositories.Release("v0.1.0-beta", true, "Tool-win-x64.zip");

        Assert.Equal("v0.1.0-beta", ReleaseAnalyzer.SelectRelease([beta])?.TagName);
    }

    [Fact]
    public void No_releases_at_all_is_reported_plainly()
    {
        var analysis = ReleaseAnalyzer.Analyze([], WindowsX64);

        Assert.False(analysis.HasRelease);
        Assert.Null(analysis.Recommended);
        Assert.Contains("no releases", analysis.NoRecommendationReason);
    }

    [Fact]
    public void A_release_with_only_other_platforms_says_so_specifically()
    {
        var analysis = ReleaseAnalyzer.Analyze(
            [Release("Tool-linux-x64.tar.gz", "Tool-macos-arm64.dmg")], WindowsX64);

        Assert.Null(analysis.Recommended);
        Assert.Contains("Linux", analysis.NoRecommendationReason);
    }

    [Fact]
    public void Every_recommendation_can_explain_itself()
    {
        var analysis = ReleaseAnalyzer.Analyze([Release("Tool-win-x64.zip")], WindowsX64);

        Assert.NotEmpty(analysis.Recommended!.Reasons);
        Assert.All(analysis.Recommended.Reasons, r => Assert.False(string.IsNullOrWhiteSpace(r)));
    }
}
