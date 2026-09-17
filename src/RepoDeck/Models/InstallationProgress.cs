namespace RepoDeck.Models;

/// <summary>Where an installation has got to. Reported so the UI never looks stuck.</summary>
public enum InstallationStage
{
    NotStarted,
    Preparing,
    Downloading,
    Verifying,
    Extracting,
    LocatingExecutable,
    Registering,
    Finished,
    Failed,
    Cancelled
}

/// <summary>Progress of a download in flight.</summary>
public sealed record DownloadProgress
{
    public long BytesReceived { get; init; }

    /// <summary>Total size when the server reports it, otherwise null.</summary>
    public long? TotalBytes { get; init; }

    public double? Fraction => TotalBytes is > 0
        ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1)
        : null;

    public int? Percent => Fraction is null ? null : (int)Math.Round(Fraction.Value * 100);
}

/// <summary>Progress of an installation as a whole.</summary>
public sealed record InstallationProgress
{
    public InstallationStage Stage { get; init; } = InstallationStage.NotStarted;
    public DownloadProgress? Download { get; init; }

    /// <summary>Free-text detail, e.g. which file is being extracted.</summary>
    public string? Detail { get; init; }

    public string Describe() => Stage switch
    {
        InstallationStage.Preparing => "Preparing...",
        InstallationStage.Downloading => Download?.Percent is { } percent
            ? $"Downloading {percent}%"
            : "Downloading...",
        InstallationStage.Verifying => "Verifying download...",
        InstallationStage.Extracting => "Extracting...",
        InstallationStage.LocatingExecutable => "Looking for the program...",
        InstallationStage.Registering => "Finishing up...",
        InstallationStage.Finished => "Installed",
        InstallationStage.Failed => "Failed",
        InstallationStage.Cancelled => "Cancelled",
        _ => ""
    };
}
