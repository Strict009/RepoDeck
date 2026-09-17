namespace RepoDeck.Tests;

/// <summary>
/// Representative project manifests. Kept as fixtures so classification tests exercise
/// the real parsing path without depending on any live repository.
/// </summary>
internal static class TestManifests
{
    public const string AvaloniaDesktopProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Avalonia" Version="11.0.0" />
            <PackageReference Include="Avalonia.Desktop" Version="11.0.0" />
          </ItemGroup>
        </Project>
        """;

    public const string WpfProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <UseWPF>true</UseWPF>
          </PropertyGroup>
        </Project>
        """;

    public const string ConsoleProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
        </Project>
        """;

    public const string NuGetLibraryProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <PackageId>Lib</PackageId>
            <IsPackable>true</IsPackable>
          </PropertyGroup>
        </Project>
        """;

    public const string ElectronPackage = """
        { "name": "thing", "version": "1.0.0",
          "devDependencies": { "electron": "^30.0.0", "electron-builder": "^24.0.0" } }
        """;

    public const string TypeScriptLibraryPackage = """
        { "name": "tiny-lib", "version": "2.1.0",
          "main": "dist/index.js", "types": "dist/index.d.ts" }
        """;

    public const string NodeCliPackage = """
        { "name": "tool", "version": "1.0.0", "bin": { "tool": "./cli.js" } }
        """;

    public const string PythonCliProject = """
        [project]
        name = "tool"
        version = "1.0.0"

        [project.scripts]
        tool = "tool.cli:main"
        """;

    public const string RustBinaryProject = """
        [package]
        name = "tool"
        version = "1.0.0"

        [[bin]]
        name = "tool"
        path = "src/main.rs"
        """;
}
