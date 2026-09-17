using RepoDeck.Models;
using static RepoDeck.Tests.InstallPlanFixtures;

namespace RepoDeck.Tests;

public class InstallPlanStrategyTests
{
    [Fact]
    public void A_Windows_zip_becomes_a_portable_archive_plan()
    {
        var plan = Plan(WindowsX64, "Tool-win-x64.zip");

        Assert.Equal(InstallStrategy.PortableArchive, plan.Strategy);
        Assert.Equal(LaunchStrategy.ExecutableFile, plan.LaunchStrategy);
        Assert.True(plan.RequiresExtraction);
        Assert.False(plan.RequiresElevation);
        Assert.True(plan.CanProceed);
        Assert.Equal("Tool-win-x64.zip", plan.AssetName);
        Assert.Equal("v1.4.2", plan.ReleaseTag);
    }

    [Fact]
    public void An_msi_becomes_a_Windows_installer_plan_that_warns_about_elevation()
    {
        var plan = Plan(WindowsX64, "Tool-1.4.2-x64.msi");

        Assert.Equal(InstallStrategy.WindowsInstaller, plan.Strategy);
        Assert.Equal(LaunchStrategy.SystemInstalled, plan.LaunchStrategy);
        Assert.True(plan.RequiresElevation);
        Assert.False(plan.RequiresExtraction);
        Assert.Contains(plan.Warnings, w => w.Contains("will not run installers on your behalf"));
    }

    [Fact]
    public void A_plain_executable_becomes_a_standalone_plan()
    {
        var plan = Plan(WindowsX64, "Tool-win-x64.exe");

        Assert.Equal(InstallStrategy.StandaloneExecutable, plan.Strategy);
        Assert.False(plan.RequiresExtraction);
        Assert.Contains("Tool-win-x64.exe", plan.ExecutableCandidates);
    }

    [Fact]
    public void A_setup_executable_is_treated_as_an_installer_not_the_program()
    {
        var plan = Plan(WindowsX64, "Tool-Setup-1.4.2.exe");

        Assert.Equal(InstallStrategy.WindowsInstaller, plan.Strategy);
        Assert.True(plan.RequiresElevation);
    }

    [Fact]
    public void An_AppImage_becomes_an_AppImage_plan_on_Linux()
    {
        var plan = Plan(LinuxX64, "Tool-x86_64.AppImage");

        Assert.Equal(InstallStrategy.LinuxAppImage, plan.Strategy);
        Assert.Equal(LaunchStrategy.AppImage, plan.LaunchStrategy);
        Assert.False(plan.RequiresElevation);
    }

    [Fact]
    public void A_deb_becomes_a_package_plan_needing_root()
    {
        var plan = Plan(LinuxX64, "tool_1.4.2_amd64.deb");

        Assert.Equal(InstallStrategy.LinuxPackage, plan.Strategy);
        Assert.True(plan.RequiresElevation);
    }

    [Fact]
    public void Every_plan_can_explain_why_its_asset_was_chosen()
    {
        var plan = Plan(WindowsX64, "Tool-win-x64.zip", "Tool-linux-x64.tar.gz");

        Assert.NotEmpty(plan.Reasons);
        Assert.Contains(plan.Reasons, r => r.Contains("Windows"));
    }

    [Fact]
    public void The_install_directory_is_always_inside_RepoDecks_own_folder()
    {
        var plan = Plan(WindowsX64, "Tool-win-x64.zip");

        Assert.StartsWith(Paths().Apps, plan.ProposedInstallDirectory);
    }
}
