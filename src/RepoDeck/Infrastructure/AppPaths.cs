namespace RepoDeck.Infrastructure;

/// <summary>
/// Everything RepoDeck writes to disk lives under one root that RepoDeck owns.
/// Nothing outside this root is ever modified by RepoDeck.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? ResolveDefaultRoot();
        Apps = Path.Combine(Root, "Apps");
        Cache = Path.Combine(Root, "Cache");
        Downloads = Path.Combine(Root, "Downloads");
        Logs = Path.Combine(Root, "Logs");
        Data = Path.Combine(Root, "Data");
    }

    public string Root { get; }

    /// <summary>Installed applications, one directory per application. Milestone 3.</summary>
    public string Apps { get; }

    /// <summary>Cached GitHub responses and, later, cached avatars/icons.</summary>
    public string Cache { get; }

    /// <summary>In-progress and completed downloads. Milestone 3.</summary>
    public string Downloads { get; }

    public string Logs { get; }

    /// <summary>Application manifests, favourites, settings.</summary>
    public string Data { get; }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { Root, Apps, Cache, Downloads, Logs, Data })
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string ResolveDefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "RepoDeck");
        }

        // Linux/macOS: follow the XDG base directory spec.
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "RepoDeck");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "RepoDeck");
    }
}
