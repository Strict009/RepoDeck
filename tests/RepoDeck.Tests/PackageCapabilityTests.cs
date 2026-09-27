using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

public class PackageCapabilityTests
{
    [Fact]
    public void Every_package_type_has_an_explicit_capability_entry()
    {
        foreach (var type in Enum.GetValues<PackageType>())
        {
            var capability = PackageCapabilities.For(type);

            Assert.Equal(type, capability.Type);
            Assert.Equal(type != PackageType.Unknown, capability.IsRecognized);
        }
    }

    [Theory]
    [InlineData(PackageType.Zip, ArchiveExtractionKind.Zip)]
    [InlineData(PackageType.TarGz, ArchiveExtractionKind.TarGZip)]
    public void Supported_archives_have_a_real_extraction_path(
        PackageType type, ArchiveExtractionKind extraction)
    {
        var capability = PackageCapabilities.For(type);

        Assert.True(capability.CanExtract);
        Assert.True(capability.CanInstallDirectly);
        Assert.True(capability.CanExecutePlan);
        Assert.Equal(InstallStrategy.PortableArchive, capability.Strategy);
        Assert.Equal(extraction, capability.Extraction);
    }

    [Theory]
    [InlineData(PackageType.SevenZip)]
    [InlineData(PackageType.TarXz)]
    [InlineData(PackageType.TarBz2)]
    public void Recognised_archives_without_a_decoder_stay_non_actionable(PackageType type)
    {
        var capability = PackageCapabilities.For(type);

        Assert.True(capability.IsRecognized);
        Assert.True(capability.IsSoftware);
        Assert.True(capability.CanDownload);
        Assert.True(capability.IsArchive);
        Assert.False(capability.CanInspectContents);
        Assert.False(capability.CanExtract);
        Assert.False(capability.CanInstallDirectly);
        Assert.False(capability.CanExecutePlan);
        Assert.True(capability.RequiresManualHandling);
        Assert.Equal(InstallStrategy.Unsupported, capability.Strategy);
    }

    [Fact]
    public void Setup_executables_and_standalone_executables_have_different_capabilities()
    {
        var standalone = PackageCapabilities.For(PackageType.WindowsExecutable);
        var setup = PackageCapabilities.For(PackageType.WindowsExecutable, installerLikeExecutable: true);

        Assert.True(standalone.CanInstallDirectly);
        Assert.Equal(InstallStrategy.StandaloneExecutable, standalone.Strategy);
        Assert.False(setup.CanInstallDirectly);
        Assert.True(setup.RequiresManualHandling);
        Assert.Equal(InstallStrategy.WindowsInstaller, setup.Strategy);
    }

    [Theory]
    [InlineData(PackageType.SourceArchive, InstallStrategy.SourceBuild)]
    [InlineData(PackageType.Metadata, InstallStrategy.Unsupported)]
    public void Non_application_files_are_recognised_without_becoming_installable(
        PackageType type, InstallStrategy strategy)
    {
        var capability = PackageCapabilities.For(type);

        Assert.True(capability.IsRecognized);
        Assert.False(capability.IsSoftware);
        Assert.False(capability.CanInspectContents);
        Assert.False(capability.CanExtract);
        Assert.False(capability.CanInstallDirectly);
        Assert.False(capability.CanExecutePlan);
        Assert.Equal(strategy, capability.Strategy);
    }

    [Fact]
    public void Every_portable_archive_strategy_is_backed_by_an_extractor()
    {
        foreach (var type in Enum.GetValues<PackageType>())
        {
            var capability = PackageCapabilities.For(type);

            Assert.Equal(
                capability.Strategy == InstallStrategy.PortableArchive,
                capability.CanExtract);
        }
    }

    [Fact]
    public void Capability_resolution_for_assets_is_the_same_contract_the_planners_use()
    {
        var setup = new AssetAnalysis
        {
            Name = "Tool-Setup-win-x64.exe",
            DownloadUrl = "https://example.invalid/tool.exe",
            PackageType = PackageType.WindowsExecutable
        };

        Assert.Equal(InstallStrategy.WindowsInstaller, PackageCapabilityResolver.For(setup).Strategy);
    }
}
