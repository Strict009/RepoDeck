using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

public class AssetNameParserTests
{
    private static AssetAnalysis Parse(string name) =>
        AssetNameParser.Parse(new GitHubReleaseAsset
        {
            Name = name,
            Size = 1024,
            BrowserDownloadUrl = "https://example.invalid/" + name
        });

    [Theory]
    [InlineData("Tool-win-x64.zip", PackageType.Zip)]
    [InlineData("Tool.7z", PackageType.SevenZip)]
    [InlineData("Tool-linux-x64.tar.gz", PackageType.TarGz)]
    [InlineData("Tool-linux-x64.tar.xz", PackageType.TarXz)]
    [InlineData("Tool.tgz", PackageType.TarGz)]
    [InlineData("Tool.exe", PackageType.WindowsExecutable)]
    [InlineData("Tool.msi", PackageType.WindowsInstaller)]
    [InlineData("Tool-x86_64.AppImage", PackageType.AppImage)]
    [InlineData("tool_1.2.3_amd64.deb", PackageType.DebianPackage)]
    [InlineData("tool-1.2.3.x86_64.rpm", PackageType.RpmPackage)]
    [InlineData("Tool.dmg", PackageType.MacDiskImage)]
    [InlineData("Tool.pkg", PackageType.MacInstallerPackage)]
    public void Package_types_come_from_the_extension(string name, PackageType expected)
    {
        Assert.Equal(expected, Parse(name).PackageType);
    }

    [Theory]
    [InlineData("Source code (zip)")]
    [InlineData("Source code (tar.gz)")]
    [InlineData("tool-1.2.3-src.tar.gz")]
    [InlineData("tool-source.zip")]
    [InlineData("tool-sources.zip")]
    public void Source_archives_are_never_mistaken_for_software(string name)
    {
        var asset = Parse(name);

        Assert.Equal(PackageType.SourceArchive, asset.PackageType);
        Assert.True(asset.IsSourceArchive);
        Assert.False(asset.PackageType.IsRunnableSoftware());
    }

    [Theory]
    [InlineData("tool-win-x64.zip.sha256")]
    [InlineData("checksums.txt")]
    [InlineData("tool.zip.asc")]
    [InlineData("tool.zip.sig")]
    public void Checksums_and_signatures_are_not_software(string name)
    {
        var asset = Parse(name);

        Assert.Equal(PackageType.Metadata, asset.PackageType);
        Assert.True(asset.IsMetadataFile);
    }

    [Theory]
    [InlineData("Tool-win-x64.zip", OsPlatform.Windows)]
    [InlineData("Tool-windows-x64.zip", OsPlatform.Windows)]
    [InlineData("Tool-win32.zip", OsPlatform.Windows)]
    [InlineData("Tool-win64.zip", OsPlatform.Windows)]
    [InlineData("Tool-linux-x64.tar.gz", OsPlatform.Linux)]
    [InlineData("Tool-ubuntu-22.04.tar.gz", OsPlatform.Linux)]
    [InlineData("Tool-macos-arm64.zip", OsPlatform.MacOS)]
    [InlineData("Tool-osx.zip", OsPlatform.MacOS)]
    public void Platforms_are_read_from_explicit_tokens(string name, OsPlatform expected)
    {
        var asset = Parse(name);

        Assert.Equal(expected, asset.Platform);
        Assert.True(asset.PlatformIsExplicit);
    }

    [Fact]
    public void Darwin_is_macOS_and_not_Windows_despite_containing_win()
    {
        // The classic substring trap: "darwin" ends in "win".
        var asset = Parse("tool-x86_64-apple-darwin.tar.gz");

        Assert.Equal(OsPlatform.MacOS, asset.Platform);
        Assert.Equal(CpuArchitecture.X64, asset.Architecture);
    }

    [Theory]
    [InlineData("Tool.zip")]
    [InlineData("Tool-1.2.3.zip")]
    [InlineData("release.zip")]
    public void An_unlabelled_archive_has_an_unknown_platform(string name)
    {
        // A bare ZIP must never be assumed to be a Windows build.
        var asset = Parse(name);

        Assert.Equal(OsPlatform.Unknown, asset.Platform);
        Assert.False(asset.PlatformIsExplicit);
    }

    [Theory]
    [InlineData("tool-x86_64.zip", CpuArchitecture.X64)]
    [InlineData("tool-x64.zip", CpuArchitecture.X64)]
    [InlineData("tool-amd64.zip", CpuArchitecture.X64)]
    [InlineData("tool-win64.zip", CpuArchitecture.X64)]
    [InlineData("tool-x86.zip", CpuArchitecture.X86)]
    [InlineData("tool-i386.zip", CpuArchitecture.X86)]
    [InlineData("tool-i686.zip", CpuArchitecture.X86)]
    [InlineData("tool-win32.zip", CpuArchitecture.X86)]
    [InlineData("tool-arm64.zip", CpuArchitecture.Arm64)]
    [InlineData("tool-aarch64.zip", CpuArchitecture.Arm64)]
    [InlineData("tool-armv7.zip", CpuArchitecture.Arm)]
    [InlineData("tool-armhf.deb", CpuArchitecture.Arm)]
    public void Architectures_survive_their_many_spellings(string name, CpuArchitecture expected)
    {
        Assert.Equal(expected, Parse(name).Architecture);
    }

    [Fact]
    public void X86_64_is_not_read_as_x86()
    {
        // Tokenising on underscores would split x86_64 into "x86" and "64".
        Assert.Equal(CpuArchitecture.X64, Parse("tool-x86_64-linux.tar.gz").Architecture);
        Assert.Equal(CpuArchitecture.X64, Parse("tool.linux-x86-64.tar.gz").Architecture);
    }

    [Fact]
    public void An_unlabelled_architecture_is_unknown_rather_than_assumed()
    {
        var asset = Parse("Tool-windows.zip");

        Assert.Equal(OsPlatform.Windows, asset.Platform);
        Assert.Equal(CpuArchitecture.Unknown, asset.Architecture);
        Assert.False(asset.ArchitectureIsExplicit);
    }

    [Theory]
    [InlineData("Tool-1.2-portable-win-x64.zip", true)]
    [InlineData("Tool-win-x64.zip", false)]
    public void Portable_builds_are_recognised(string name, bool expected)
    {
        Assert.Equal(expected, Parse(name).IsPortable);
    }

    [Theory]
    [InlineData("Tool.msi", true)]
    [InlineData("Tool-Setup-1.2.exe", true)]
    [InlineData("ToolInstaller.exe", false)]
    [InlineData("Tool-installer.exe", true)]
    [InlineData("Tool.exe", false)]
    [InlineData("tool_1.2_amd64.deb", true)]
    [InlineData("tool-1.2.x86_64.rpm", true)]
    [InlineData("Tool-win-x64.zip", false)]
    public void Packages_needing_administrator_rights_are_identified(string name, bool expected)
    {
        Assert.Equal(expected, Parse(name).RequiresElevation);
    }
}
