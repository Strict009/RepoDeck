using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>Resolves the one filename-sensitive package capability: a setup-style .exe.</summary>
public static class PackageCapabilityResolver
{
    public static PackageCapability For(AssetAnalysis asset) =>
        PackageCapabilities.For(
            asset.PackageType,
            asset.PackageType == PackageType.WindowsExecutable
            && AssetNameParser.LooksLikeInstaller(asset.Name));
}
