namespace RepoDeck.Models;

/// <summary>The archive decoder, if any, that RepoDeck actually has.</summary>
public enum ArchiveExtractionKind
{
    None,
    Zip,
    TarGZip
}

/// <summary>What RepoDeck can genuinely do with one kind of published file.</summary>
/// <remarks>
/// Recognition and execution are deliberately separate. Knowing that a file is a 7-Zip
/// archive is useful evidence; it does not imply that this RepoDeck version can unpack it.
/// </remarks>
public sealed record PackageCapability
{
    public required PackageType Type { get; init; }
    public bool IsRecognized { get; init; }
    public bool IsSoftware { get; init; }
    public bool CanDownload { get; init; }
    public bool CanInspectContents { get; init; }
    public bool IsArchive { get; init; }
    public ArchiveExtractionKind Extraction { get; init; }
    public InstallStrategy Strategy { get; init; } = InstallStrategy.Unsupported;

    /// <summary>RepoDeck can finish the installation without handing work to the user.</summary>
    public bool CanInstallDirectly { get; init; }

    /// <summary>The file is understood, but RepoDeck will not complete its system/manual step.</summary>
    public bool RequiresManualHandling { get; init; }

    /// <summary>A plan may safely execute, including the deliberate download-only paths.</summary>
    public bool CanExecutePlan => CanInstallDirectly || Strategy is
        InstallStrategy.WindowsInstaller or InstallStrategy.LinuxPackage;

    public bool CanExtract => Extraction != ArchiveExtractionKind.None;
}

/// <summary>
/// The single capability table shared by release selection, install/update planning and
/// extraction. Adding a recognised package here never silently adds execution support.
/// </summary>
public static class PackageCapabilities
{
    public static PackageCapability For(PackageType type, bool installerLikeExecutable = false) => type switch
    {
        PackageType.Zip => Archive(type, ArchiveExtractionKind.Zip),
        PackageType.TarGz => Archive(type, ArchiveExtractionKind.TarGZip),

        // Recognised archives with no decoder in the current runtime. They remain useful
        // evidence, but may never produce a PortableArchive plan.
        PackageType.SevenZip or PackageType.TarXz or PackageType.TarBz2 =>
            UnsupportedArchive(type),

        PackageType.WindowsExecutable when installerLikeExecutable => new PackageCapability
        {
            Type = type,
            IsRecognized = true,
            IsSoftware = true,
            CanDownload = true,
            Strategy = InstallStrategy.WindowsInstaller,
            RequiresManualHandling = true
        },
        PackageType.WindowsExecutable => Direct(type, InstallStrategy.StandaloneExecutable),

        PackageType.WindowsInstaller => DownloadOnly(type, InstallStrategy.WindowsInstaller),
        PackageType.AppImage => Direct(type, InstallStrategy.LinuxAppImage),
        PackageType.DebianPackage or PackageType.RpmPackage =>
            DownloadOnly(type, InstallStrategy.LinuxPackage),

        // RepoDeck recognises these, but has no safe managed installation route for them.
        PackageType.MacDiskImage or PackageType.MacInstallerPackage => ManualUnsupported(type),

        PackageType.SourceArchive => new PackageCapability
        {
            Type = type,
            IsRecognized = true,
            CanDownload = true,
            CanInspectContents = false,
            IsArchive = true,
            Strategy = InstallStrategy.SourceBuild,
            RequiresManualHandling = true
        },
        PackageType.Metadata => new PackageCapability
        {
            Type = type,
            IsRecognized = true,
            CanDownload = true,
            Strategy = InstallStrategy.Unsupported
        },
        _ => new PackageCapability { Type = type }
    };

    private static PackageCapability Archive(PackageType type, ArchiveExtractionKind extraction) => new()
    {
        Type = type,
        IsRecognized = true,
        IsSoftware = true,
        CanDownload = true,
        CanInspectContents = true,
        IsArchive = true,
        Extraction = extraction,
        Strategy = InstallStrategy.PortableArchive,
        CanInstallDirectly = true
    };

    private static PackageCapability UnsupportedArchive(PackageType type) => new()
    {
        Type = type,
        IsRecognized = true,
        IsSoftware = true,
        CanDownload = true,
        IsArchive = true,
        Strategy = InstallStrategy.Unsupported,
        RequiresManualHandling = true
    };

    private static PackageCapability Direct(PackageType type, InstallStrategy strategy) => new()
    {
        Type = type,
        IsRecognized = true,
        IsSoftware = true,
        CanDownload = true,
        Strategy = strategy,
        CanInstallDirectly = true
    };

    private static PackageCapability DownloadOnly(PackageType type, InstallStrategy strategy) => new()
    {
        Type = type,
        IsRecognized = true,
        IsSoftware = true,
        CanDownload = true,
        Strategy = strategy,
        RequiresManualHandling = true
    };

    private static PackageCapability ManualUnsupported(PackageType type) => new()
    {
        Type = type,
        IsRecognized = true,
        IsSoftware = true,
        CanDownload = true,
        Strategy = InstallStrategy.Unsupported,
        RequiresManualHandling = true
    };
}
