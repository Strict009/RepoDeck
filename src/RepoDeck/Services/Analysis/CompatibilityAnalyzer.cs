using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Ranks release assets against a particular machine.
/// </summary>
/// <remarks>
/// Deterministic and pure, so the ranking can be tested exhaustively for machines other
/// than the one the tests run on. The numeric score exists only to make ordering total
/// and reproducible; it is never shown to the user, who sees the reasons instead.
/// </remarks>
public static class CompatibilityAnalyzer
{
    // Platform agreement dominates everything else: a Linux build on Windows is not
    // "slightly worse", it simply does not run.
    private const int PlatformMatchScore = 1000;
    private const int NativeArchitectureScore = 400;
    private const int EmulatedArchitectureScore = 150;
    private const int UnknownArchitectureScore = 40;
    private const int RunnableFormatScore = 120;
    private const int PortableBonus = 60;
    private const int NoElevationBonus = 40;

    public static AssetAnalysis Evaluate(AssetAnalysis asset, MachineProfile machine)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();

        if (asset.IsMetadataFile)
        {
            return asset with
            {
                Compatibility = AssetCompatibility.NotApplicable,
                Score = int.MinValue,
                Reasons = ["This is a checksum or signature file, not software."]
            };
        }

        if (asset.IsSourceArchive)
        {
            return asset with
            {
                Compatibility = AssetCompatibility.NotApplicable,
                Score = int.MinValue + 1,
                Reasons = ["This is source code, which would have to be compiled before it could run."],
                Warnings = ["Source archive"]
            };
        }

        var score = 0;
        var compatibility = AssetCompatibility.Compatible;

        // ---- Platform ----------------------------------------------------
        if (asset.Platform == OsPlatform.Unknown)
        {
            warnings.Add("The file name does not say which system it is for.");
            reasons.Add("No platform stated in the file name, so RepoDeck cannot confirm it runs here.");
            compatibility = AssetCompatibility.Unknown;
        }
        else if (asset.Platform != machine.OperatingSystem)
        {
            return asset with
            {
                Compatibility = AssetCompatibility.Incompatible,
                Score = int.MinValue + 2,
                Reasons = [$"Built for {asset.Platform.DisplayName()}, and this is a {machine.OperatingSystem.DisplayName()} computer."],
                Warnings = ["Wrong platform"]
            };
        }
        else
        {
            score += PlatformMatchScore;
            reasons.Add($"Explicitly built for {asset.Platform.DisplayName()}, matching this computer.");
        }

        // ---- Architecture ------------------------------------------------
        if (asset.Architecture == CpuArchitecture.Unknown)
        {
            score += UnknownArchitectureScore;
            warnings.Add("The file name does not say which processor type it is for.");
            if (compatibility == AssetCompatibility.Compatible)
            {
                compatibility = AssetCompatibility.LikelyCompatible;
            }
        }
        else if (asset.Architecture == machine.Architecture)
        {
            score += NativeArchitectureScore;
            reasons.Add($"Built for {asset.Architecture.DisplayName()}, matching this computer.");
        }
        else if (machine.CanRun(asset.Architecture))
        {
            score += EmulatedArchitectureScore;
            reasons.Add($"Built for {asset.Architecture.DisplayName()}, which this "
                        + $"{machine.Architecture.DisplayName()} computer can run, though not natively.");
            warnings.Add($"Runs through compatibility support rather than natively.");
            if (compatibility == AssetCompatibility.Compatible)
            {
                compatibility = AssetCompatibility.CompatibleThroughEmulation;
            }
        }
        else
        {
            return asset with
            {
                Compatibility = AssetCompatibility.Incompatible,
                Score = int.MinValue + 2,
                Reasons = [$"Built for {asset.Architecture.DisplayName()}, which this "
                           + $"{machine.Architecture.DisplayName()} computer cannot run."],
                Warnings = ["Wrong processor type"]
            };
        }

        // ---- Package shape -----------------------------------------------
        if (!asset.PackageType.IsRunnableSoftware())
        {
            warnings.Add("RepoDeck does not recognise this kind of file.");
            reasons.Add("The file type is not one RepoDeck knows how to install.");
            compatibility = AssetCompatibility.Unknown;
        }
        else
        {
            score += RunnableFormatScore;
            reasons.Add($"{asset.PackageType.ToDisplayString()}, which RepoDeck understands.");
        }

        if (asset.IsPortable)
        {
            score += PortableBonus;
            reasons.Add("Described as portable, so it runs without changing the system.");
        }

        if (asset.RequiresElevation)
        {
            warnings.Add("Installing this would need administrator rights.");
        }
        else if (asset.PackageType.IsRunnableSoftware())
        {
            score += NoElevationBonus;
        }

        return asset with
        {
            Compatibility = compatibility,
            Score = score,
            Reasons = reasons,
            Warnings = warnings
        };
    }
}
