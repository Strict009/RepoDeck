namespace RepoDeck.Services.Install;

/// <summary>
/// Decides where, if anywhere, an archive entry is allowed to be written.
/// </summary>
/// <remarks>
/// This is the "zip slip" defence. An archive entry named <c>../../../evil.exe</c>, or
/// one with an absolute path, would otherwise escape the destination folder and write
/// anywhere the process can reach. The check is a pure function so it can be tested
/// exhaustively without creating a single file.
/// </remarks>
public static class ArchivePathGuard
{
    /// <summary>
    /// Resolves an entry to an absolute path inside <paramref name="destinationDirectory"/>,
    /// or returns null when the entry must be refused.
    /// </summary>
    public static string? ResolveSafePath(string destinationDirectory, string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath)) return null;

        // Archives may use either separator regardless of the platform that made them.
        var normalised = entryPath.Replace('\\', '/').Trim();

        // A rooted entry is never acceptable: it ignores the destination entirely.
        if (normalised.StartsWith('/') || normalised.StartsWith('~')) return null;
        if (normalised.Length >= 2 && normalised[1] == ':') return null;
        if (normalised.StartsWith("//", StringComparison.Ordinal)) return null;

        // A "." segment just means "here" and is harmless, so it is dropped rather than refused.
        var segments = normalised
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != ".")
            .ToArray();

        if (segments.Length == 0) return null;

        // Traversal is refused outright rather than normalised away, because an entry
        // containing ".." has no legitimate reason to exist. Longer runs of dots are
        // refused too: they are never a real file name and some file systems fold them.
        if (segments.Any(s => s.All(c => c == '.'))) return null;

        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var combined = Path.GetFullPath(Path.Combine(destinationRoot, Path.Combine(segments)));

        // Belt and braces: whatever the segments looked like, the result must be inside.
        return IsInside(destinationRoot, combined) ? combined : null;
    }

    /// <summary>True when <paramref name="candidate"/> sits inside <paramref name="root"/>.</summary>
    public static bool IsInside(string root, string candidate)
    {
        var normalisedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var normalisedCandidate = Path.GetFullPath(candidate);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return normalisedCandidate.StartsWith(normalisedRoot, comparison);
    }
}
