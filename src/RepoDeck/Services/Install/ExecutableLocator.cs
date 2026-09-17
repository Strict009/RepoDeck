using RepoDeck.Models;

namespace RepoDeck.Services.Install;

/// <summary>One extracted file, as the locator sees it.</summary>
public readonly record struct CandidateFile(string RelativePath, bool HasExecutableBit, long Size);

/// <summary>Which file RepoDeck would run, the runners-up, and why.</summary>
public sealed record ExecutableSelection
{
    public string? Chosen { get; init; }
    public IReadOnlyList<string> Alternatives { get; init; } = [];
    public string? Reason { get; init; }

    public bool Found => Chosen is not null;
}

/// <summary>
/// Picks the program to run out of an extracted folder.
/// </summary>
/// <remarks>
/// The install plan predicted some names before anything was downloaded; this confirms
/// or corrects that prediction against what is actually there. Pure over a file list so
/// the ranking can be tested without unpacking anything.
///
/// The interesting problem is not finding executables but rejecting the wrong ones: an
/// extracted application folder is usually full of uninstallers, crash handlers,
/// updaters and bundled redistributables, any of which would "work" and all of which
/// would be the wrong thing to launch.
/// </remarks>
public static class ExecutableLocator
{
    /// <summary>Archives made on Windows use this separator whatever platform reads them.</summary>
    private const char Backslash = (char)92;

    private static readonly string[] WindowsExtensions = [".exe", ".bat", ".cmd"];

    /// <summary>Names that are executables but are never the application itself.</summary>
    private static readonly string[] DisqualifyingWords =
    [
        "unins", "uninstall", "setup", "install", "vcredist", "vc_redist", "dotnet-install",
        "updater", "update", "crashpad", "crashreport", "crashhandler", "reporter",
        "helper", "service", "daemon", "debug", "test", "sample", "example", "benchmark"
    ];

    /// <summary>Folders that hold supporting binaries rather than the program.</summary>
    private static readonly string[] DisqualifyingFolders =
    [
        "runtime", "runtimes", "redist", "vendor", "third_party", "thirdparty",
        "docs", "doc", "samples", "examples", "tests", "test", "tools/uninstall"
    ];

    public static ExecutableSelection Select(
        IReadOnlyList<CandidateFile> files,
        IReadOnlyList<string> planCandidates,
        string repositoryName,
        OsPlatform platform)
    {
        var runnable = files.Where(f => IsRunnable(f, platform)).ToList();

        if (runnable.Count == 0)
        {
            return new ExecutableSelection
            {
                Reason = "RepoDeck did not find a program it recognises inside the download."
            };
        }

        var ranked = runnable
            .Select(f => (File: f, Score: Score(f, planCandidates, repositoryName, platform)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Depth(x.File.RelativePath))
            .ThenBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var best = ranked[0];

        return new ExecutableSelection
        {
            Chosen = best.File.RelativePath,
            Alternatives = ranked.Skip(1).Take(8).Select(x => x.File.RelativePath).ToList(),
            Reason = DescribeChoice(best.File, best.Score, planCandidates, repositoryName)
        };
    }

    private static bool IsRunnable(CandidateFile file, OsPlatform platform)
    {
        var name = FileName(file.RelativePath);

        if (platform == OsPlatform.Windows)
        {
            return WindowsExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        if (name.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase)) return true;

        // On Unix the executable bit is what makes a file runnable. An extensionless
        // file is the usual shape of a compiled program, so it counts as a candidate
        // even when the archive did not preserve permissions.
        if (file.HasExecutableBit) return true;

        return !Path.GetFileName(name).Contains('.');
    }

    private static int Score(
        CandidateFile file, IReadOnlyList<string> planCandidates, string repositoryName, OsPlatform platform)
    {
        var name = FileName(file.RelativePath);
        var stem = Path.GetFileNameWithoutExtension(name);
        var score = 0;

        // The plan's prediction, confirmed against reality, is the strongest signal.
        if (planCandidates.Any(c => string.Equals(FileName(c), name, StringComparison.OrdinalIgnoreCase)))
        {
            score += 1000;
        }

        if (string.Equals(stem, repositoryName, StringComparison.OrdinalIgnoreCase)) score += 500;
        else if (stem.Contains(repositoryName, StringComparison.OrdinalIgnoreCase)) score += 200;
        else if (repositoryName.Contains(stem, StringComparison.OrdinalIgnoreCase) && stem.Length > 2) score += 120;

        // Sitting at the top of the folder usually means it is the thing to run.
        score -= Depth(file.RelativePath) * 40;

        if (platform == OsPlatform.Windows
            && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        var lowerStem = stem.ToLowerInvariant();
        if (DisqualifyingWords.Any(w => lowerStem.Contains(w, StringComparison.Ordinal)))
        {
            score -= 700;
        }

        var lowerPath = file.RelativePath.Replace(Backslash, '/').ToLowerInvariant();
        if (DisqualifyingFolders.Any(f => lowerPath.Contains(f + "/", StringComparison.Ordinal)))
        {
            score -= 300;
        }

        return score;
    }

    private static string DescribeChoice(
        CandidateFile file, int score, IReadOnlyList<string> planCandidates, string repositoryName)
    {
        var name = FileName(file.RelativePath);

        if (planCandidates.Any(c => string.Equals(FileName(c), name, StringComparison.OrdinalIgnoreCase)))
        {
            return $"{name} is the file RepoDeck expected to find before downloading.";
        }

        if (name.Contains(repositoryName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} matches the project name.";
        }

        return score < 0
            ? $"{name} was the only thing resembling a program, and RepoDeck is not confident about it."
            : $"{name} is the most likely program in the download.";
    }

    private static string FileName(string path)
    {
        var normalised = path.Replace(Backslash, '/');
        var slash = normalised.LastIndexOf('/');
        return slash >= 0 ? normalised[(slash + 1)..] : normalised;
    }

    private static int Depth(string path) => path.Replace(Backslash, '/').Count(c => c == '/');
}
