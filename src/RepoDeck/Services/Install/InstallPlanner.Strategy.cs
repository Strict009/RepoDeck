using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Services.Install;

public sealed partial class InstallPlanner
{
    private static InstallStrategy ResolveStrategy(AssetAnalysis asset) => asset.PackageType switch
    {
        PackageType.Zip or PackageType.SevenZip or PackageType.TarGz or PackageType.TarXz =>
            InstallStrategy.PortableArchive,

        // A .exe called "setup" installs itself; one that is not is the program.
        PackageType.WindowsExecutable =>
            AssetNameParser.LooksLikeInstaller(asset.Name)
                ? InstallStrategy.WindowsInstaller
                : InstallStrategy.StandaloneExecutable,

        PackageType.WindowsInstaller => InstallStrategy.WindowsInstaller,
        PackageType.AppImage => InstallStrategy.LinuxAppImage,
        PackageType.DebianPackage or PackageType.RpmPackage => InstallStrategy.LinuxPackage,
        PackageType.SourceArchive => InstallStrategy.SourceBuild,
        _ => InstallStrategy.Unsupported
    };

    private static LaunchStrategy ResolveLaunchStrategy(InstallStrategy strategy) => strategy switch
    {
        InstallStrategy.PortableArchive or InstallStrategy.StandaloneExecutable => LaunchStrategy.ExecutableFile,
        InstallStrategy.LinuxAppImage => LaunchStrategy.AppImage,
        InstallStrategy.WindowsInstaller or InstallStrategy.LinuxPackage => LaunchStrategy.SystemInstalled,
        _ => LaunchStrategy.NotLaunchable
    };

    private static Confidence ResolveConfidence(AssetAnalysis asset) => asset.Compatibility switch
    {
        AssetCompatibility.Compatible => Confidence.Likely,
        AssetCompatibility.CompatibleThroughEmulation => Confidence.Possible,
        AssetCompatibility.LikelyCompatible => Confidence.Possible,
        _ => Confidence.Unknown
    };

    /// <summary>
    /// Names RepoDeck expects to find after extracting. These are predictions from the
    /// repository and asset names - nothing has been downloaded, so they are candidates
    /// to look for, never a promise about what is inside.
    /// </summary>
    private static IReadOnlyList<string> PredictExecutables(
        GitHubRepository repository, AssetAnalysis asset, MachineProfile machine)
    {
        // A single-file download is itself the thing that runs.
        if (asset.PackageType is PackageType.AppImage or PackageType.WindowsExecutable)
        {
            return [asset.Name];
        }

        if (!asset.PackageType.RequiresExtraction()) return [];

        var suffix = machine.OperatingSystem == OsPlatform.Windows ? ".exe" : "";
        var baseName = repository.Name;
        var candidates = new List<string> { baseName + suffix };

        // Projects often capitalise the executable differently from the repository.
        var parts = baseName.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var pascal = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

        if (!string.Equals(pascal, baseName, StringComparison.Ordinal))
        {
            candidates.Add(pascal + suffix);
        }

        return candidates;
    }

    /// <summary>One folder per repository, always inside RepoDeck's own managed directory.</summary>
    private static string SafeFolderName(string owner, string name)
    {
        var raw = owner + "__" + name;
        var invalid = Path.GetInvalidFileNameChars();
        return new string(raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
