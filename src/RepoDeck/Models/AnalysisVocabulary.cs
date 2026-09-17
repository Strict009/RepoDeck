namespace RepoDeck.Models;

/// <summary>The build ecosystem a repository belongs to.</summary>
public enum ProjectType
{
    Unknown,
    DotNet,
    Python,
    Node,
    Rust,
    Java,
    CPlusPlus,
    Go,
    Unity,
    Godot,
    Shell,
    Docker
}

/// <summary>What the project is <em>for</em>, as opposed to what it is built with.</summary>
public enum ApplicationType
{
    Unknown,
    DesktopApplication,
    CliTool,
    Game,
    Server,
    Library,
    Framework,
    PluginOrExtension,
    ScriptOrAutomation,
    WebApplication,
    MobileApplication,
    DeveloperTool
}

public enum BuildSystem
{
    Unknown,
    None,
    MsBuild,
    Cargo,
    Npm,
    Maven,
    Gradle,
    CMake,
    Make,
    PythonPackaging,
    Unity,
    Godot
}

/// <summary>How RepoDeck would install something, if it were installing yet.</summary>
public enum InstallStrategy
{
    Unknown,

    /// <summary>Download an archive, extract it, run the executable inside. No system changes.</summary>
    PortableArchive,

    /// <summary>A single executable file that runs as downloaded.</summary>
    StandaloneExecutable,

    /// <summary>An .msi or setup .exe that installs itself. Needs the user's consent and often elevation.</summary>
    WindowsInstaller,

    /// <summary>A Linux AppImage: one file, made executable, run directly.</summary>
    LinuxAppImage,

    /// <summary>A .deb or .rpm handled by the system package manager. Needs root.</summary>
    LinuxPackage,

    /// <summary>Would have to be compiled first. RepoDeck does not do this.</summary>
    SourceBuild,

    /// <summary>Nothing here RepoDeck could install.</summary>
    Unsupported
}

public enum LaunchStrategy
{
    Unknown,

    /// <summary>Start an executable file inside RepoDeck's managed folder.</summary>
    ExecutableFile,

    /// <summary>Mark an AppImage executable and run it.</summary>
    AppImage,

    /// <summary>Installed into the system; launched by whatever the installer registered.</summary>
    SystemInstalled,

    /// <summary>There is nothing to launch - a library, or source only.</summary>
    NotLaunchable
}

/// <summary>The shape of a downloadable file.</summary>
public enum PackageType
{
    Unknown,
    Zip,
    SevenZip,
    TarGz,
    TarXz,
    WindowsExecutable,
    WindowsInstaller,
    AppImage,
    DebianPackage,
    RpmPackage,
    MacDiskImage,
    MacInstallerPackage,

    /// <summary>An archive of source code. Never an application, however tempting the name.</summary>
    SourceArchive,

    /// <summary>A checksum, signature, changelog or similar - not software.</summary>
    Metadata
}

public static class AnalysisVocabularyText
{
    public static string ToDisplayString(this ProjectType value) => value switch
    {
        ProjectType.DotNet => ".NET",
        ProjectType.CPlusPlus => "C / C++",
        ProjectType.Node => "Node.js",
        _ => value.ToString()
    };

    public static string ToDisplayString(this ApplicationType value) => value switch
    {
        ApplicationType.DesktopApplication => "Desktop application",
        ApplicationType.CliTool => "Command-line tool",
        ApplicationType.Server => "Server software",
        ApplicationType.PluginOrExtension => "Plugin or extension",
        ApplicationType.ScriptOrAutomation => "Script or automation",
        ApplicationType.WebApplication => "Web application",
        ApplicationType.MobileApplication => "Mobile application",
        ApplicationType.DeveloperTool => "Developer tool",
        ApplicationType.Unknown => "Not determined",
        _ => value.ToString()
    };

    public static string ToDisplayString(this InstallStrategy value) => value switch
    {
        InstallStrategy.PortableArchive => "Portable archive",
        InstallStrategy.StandaloneExecutable => "Standalone executable",
        InstallStrategy.WindowsInstaller => "Windows installer",
        InstallStrategy.LinuxAppImage => "Linux AppImage",
        InstallStrategy.LinuxPackage => "Linux package",
        InstallStrategy.SourceBuild => "Source build required",
        InstallStrategy.Unsupported => "Not supported",
        _ => "Unknown"
    };

    public static string ToDisplayString(this PackageType value) => value switch
    {
        PackageType.Zip => "ZIP archive",
        PackageType.SevenZip => "7-Zip archive",
        PackageType.TarGz => "tar.gz archive",
        PackageType.TarXz => "tar.xz archive",
        PackageType.WindowsExecutable => "Windows executable",
        PackageType.WindowsInstaller => "Windows installer",
        PackageType.AppImage => "Linux AppImage",
        PackageType.DebianPackage => "Debian package",
        PackageType.RpmPackage => "RPM package",
        PackageType.MacDiskImage => "macOS disk image",
        PackageType.MacInstallerPackage => "macOS installer package",
        PackageType.SourceArchive => "Source code archive",
        PackageType.Metadata => "Checksum or signature",
        _ => "Unrecognised file"
    };

    /// <summary>True for package types that contain a runnable program rather than source or notes.</summary>
    public static bool IsRunnableSoftware(this PackageType value) => value is
        PackageType.Zip or PackageType.SevenZip or PackageType.TarGz or PackageType.TarXz or
        PackageType.WindowsExecutable or PackageType.WindowsInstaller or PackageType.AppImage or
        PackageType.DebianPackage or PackageType.RpmPackage or PackageType.MacDiskImage or
        PackageType.MacInstallerPackage;

    public static bool RequiresExtraction(this PackageType value) => value is
        PackageType.Zip or PackageType.SevenZip or PackageType.TarGz or PackageType.TarXz;
}
