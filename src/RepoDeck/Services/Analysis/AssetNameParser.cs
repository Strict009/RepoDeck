using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Reads a release asset's file name and works out what it is, what it runs on and
/// what architecture it targets.
/// </summary>
/// <remarks>
/// Pure and deterministic, because this is where the dangerous mistakes live. Two traps
/// in particular: "darwin" contains "win", and "x86_64" contains "x86". Both are handled
/// by normalising known compound spellings first and then matching whole tokens, never
/// loose substrings. When a name does not say what it targets, the answer is Unknown -
/// a bare "Tool.zip" must never be reported as a Windows build.
/// </remarks>
public static class AssetNameParser
{
    public static AssetAnalysis Parse(GitHubReleaseAsset asset)
    {
        var name = asset.Name ?? "";
        var lower = name.ToLowerInvariant();
        var normalised = NormaliseCompoundTokens(lower);
        var tokens = Tokenise(normalised);

        var packageType = DetectPackageType(lower, tokens);
        var (platform, platformExplicit) = DetectPlatform(packageType, tokens);
        var (architecture, architectureExplicit) = DetectArchitecture(tokens);

        return new AssetAnalysis
        {
            Name = name,
            DownloadUrl = asset.BrowserDownloadUrl,
            Size = asset.Size,
            PackageType = packageType,
            Platform = platform,
            PlatformIsExplicit = platformExplicit,
            Architecture = architecture,
            ArchitectureIsExplicit = architectureExplicit,
            IsPortable = tokens.Contains("portable"),
            RequiresElevation = NeedsElevation(packageType, tokens)
        };
    }

    /// <summary>
    /// Rewrites compound spellings to a single canonical token so that tokenising on
    /// separators cannot split "x86_64" into "x86" and "64".
    /// </summary>
    private static string NormaliseCompoundTokens(string value)
    {
        Span<(string From, string To)> replacements =
        [
            ("x86_64", "x64"), ("x86-64", "x64"), ("x8664", "x64"),
            ("amd64", "x64"), ("win64", "windows x64"), ("win32", "windows x86"),
            ("aarch64", "arm64"), ("arm64ec", "arm64"),
            ("i686", "x86"), ("i586", "x86"), ("i386", "x86"),
            ("armv7l", "arm"), ("armv7", "arm"), ("armhf", "arm"), ("armv6", "arm"),
            ("apple-silicon", "arm64"), ("applesilicon", "arm64"),
            ("universal2", "universal")
        ];

        foreach (var (from, to) in replacements)
        {
            value = value.Replace(from, to, StringComparison.Ordinal);
        }

        return value;
    }

    private static HashSet<string> Tokenise(string value)
    {
        var tokens = value.Split(['-', '_', '.', ' ', '+', '(', ')', ',', '/'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new HashSet<string>(tokens, StringComparer.Ordinal);
    }

    // ---- Package type -----------------------------------------------------

    public static PackageType DetectPackageType(string lowerName, HashSet<string> tokens)
    {
        // GitHub names its generated archives "Source code (zip)", which has no usable
        // file extension at all, so this is checked before any extension logic.
        if (lowerName.Contains("source code", StringComparison.Ordinal)) return PackageType.SourceArchive;

        // Longest extensions first so ".tar.gz" is not read as ".gz".
        if (lowerName.EndsWith(".tar.gz", StringComparison.Ordinal)
            || lowerName.EndsWith(".tgz", StringComparison.Ordinal))
        {
            return IsSourceArchiveName(lowerName, tokens) ? PackageType.SourceArchive : PackageType.TarGz;
        }

        if (lowerName.EndsWith(".tar.xz", StringComparison.Ordinal)
            || lowerName.EndsWith(".txz", StringComparison.Ordinal))
        {
            return IsSourceArchiveName(lowerName, tokens) ? PackageType.SourceArchive : PackageType.TarXz;
        }

        if (lowerName.EndsWith(".tar.bz2", StringComparison.Ordinal))
        {
            return IsSourceArchiveName(lowerName, tokens) ? PackageType.SourceArchive : PackageType.TarGz;
        }

        if (IsMetadataName(lowerName)) return PackageType.Metadata;

        var extension = ExtensionOf(lowerName);

        return extension switch
        {
            ".zip" => IsSourceArchiveName(lowerName, tokens) ? PackageType.SourceArchive : PackageType.Zip,
            ".7z" => IsSourceArchiveName(lowerName, tokens) ? PackageType.SourceArchive : PackageType.SevenZip,
            ".exe" => PackageType.WindowsExecutable,
            ".msi" => PackageType.WindowsInstaller,
            ".msix" or ".appx" => PackageType.WindowsInstaller,
            ".appimage" => PackageType.AppImage,
            ".deb" => PackageType.DebianPackage,
            ".rpm" => PackageType.RpmPackage,
            ".dmg" => PackageType.MacDiskImage,
            ".pkg" => PackageType.MacInstallerPackage,
            _ => PackageType.Unknown
        };
    }

    private static string ExtensionOf(string lowerName)
    {
        var dot = lowerName.LastIndexOf('.');
        return dot < 0 ? "" : lowerName[dot..];
    }

    /// <summary>
    /// Recognises source archives by name. GitHub's own generated source archives are not
    /// returned as release assets at all, but maintainers upload their own often enough,
    /// and calling one of those an application would be a serious mistake.
    /// </summary>
    private static bool IsSourceArchiveName(string lowerName, HashSet<string> tokens)
    {
        if (lowerName.Contains("source code", StringComparison.Ordinal)) return true;

        string[] sourceTokens = ["source", "sources", "src", "sourcecode"];
        return sourceTokens.Any(tokens.Contains);
    }

    private static bool IsMetadataName(string lowerName)
    {
        string[] endings =
        [
            ".sha1", ".sha256", ".sha512", ".md5", ".asc", ".sig", ".sigstore",
            ".pem", ".cert", ".sbom", ".spdx.json", ".intoto.jsonl", ".txt", ".sha256sum"
        ];

        if (endings.Any(e => lowerName.EndsWith(e, StringComparison.Ordinal))) return true;

        string[] names = ["checksums", "sha256sums", "sha512sums", "md5sums"];
        return names.Any(n => lowerName.Contains(n, StringComparison.Ordinal));
    }

    // ---- Platform ---------------------------------------------------------

    private static readonly string[] MacTokens =
        ["mac", "macos", "osx", "darwin", "apple", "macosx"];

    private static readonly string[] WindowsTokens =
        ["win", "windows", "msvc", "mingw", "winnt", "msys"];

    private static readonly string[] LinuxTokens =
        ["linux", "ubuntu", "debian", "fedora", "alpine", "musl", "appimage", "gnu"];

    /// <summary>
    /// Works out the target platform. The file extension is the strongest signal because
    /// it is unambiguous; tokens in the name are checked afterwards, macOS first, since
    /// "darwin" would otherwise be mistaken for a Windows build.
    /// </summary>
    public static (OsPlatform Platform, bool Explicit) DetectPlatform(
        PackageType packageType, HashSet<string> tokens)
    {
        switch (packageType)
        {
            case PackageType.WindowsExecutable:
            case PackageType.WindowsInstaller:
                return (OsPlatform.Windows, true);
            case PackageType.AppImage:
            case PackageType.DebianPackage:
            case PackageType.RpmPackage:
                return (OsPlatform.Linux, true);
            case PackageType.MacDiskImage:
            case PackageType.MacInstallerPackage:
                return (OsPlatform.MacOS, true);
        }

        // Checked before Windows on purpose: "darwin" contains "win".
        if (MacTokens.Any(tokens.Contains)) return (OsPlatform.MacOS, true);
        if (WindowsTokens.Any(tokens.Contains)) return (OsPlatform.Windows, true);
        if (LinuxTokens.Any(tokens.Contains)) return (OsPlatform.Linux, true);

        // Nothing in the name says what this targets. Say so rather than guessing.
        return (OsPlatform.Unknown, false);
    }

    // ---- Architecture -----------------------------------------------------

    /// <summary>
    /// Compound spellings were already normalised, so these are exact token matches.
    /// "x64" here may have arrived as x86_64, amd64 or win64.
    /// </summary>
    public static (CpuArchitecture Architecture, bool Explicit) DetectArchitecture(HashSet<string> tokens)
    {
        if (tokens.Contains("arm64")) return (CpuArchitecture.Arm64, true);
        if (tokens.Contains("x64")) return (CpuArchitecture.X64, true);
        if (tokens.Contains("x86")) return (CpuArchitecture.X86, true);
        if (tokens.Contains("arm")) return (CpuArchitecture.Arm, true);

        return (CpuArchitecture.Unknown, false);
    }

    // ---- Elevation --------------------------------------------------------

    /// <summary>
    /// Whether installing this would need administrator or root rights. RepoDeck never
    /// requests elevation itself; this exists so the plan can say plainly that a package
    /// would require it.
    /// </summary>
    private static bool NeedsElevation(PackageType packageType, HashSet<string> tokens)
    {
        if (packageType is PackageType.WindowsInstaller
            or PackageType.DebianPackage
            or PackageType.RpmPackage
            or PackageType.MacInstallerPackage)
        {
            return true;
        }

        // A .exe named "setup" or "installer" is a system installer rather than the app.
        if (packageType == PackageType.WindowsExecutable)
        {
            string[] installerWords = ["setup", "installer", "install"];
            return installerWords.Any(tokens.Contains);
        }

        return false;
    }

    /// <summary>True when a .exe looks like an installer rather than the program itself.</summary>
    public static bool LooksLikeInstaller(string name)
    {
        var tokens = Tokenise(NormaliseCompoundTokens(name.ToLowerInvariant()));
        string[] installerWords = ["setup", "installer", "install"];
        return installerWords.Any(tokens.Contains);
    }
}
