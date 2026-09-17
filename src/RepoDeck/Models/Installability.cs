namespace RepoDeck.Models;

/// <summary>
/// What RepoDeck can actually do with a project, stated as one of five answers.
/// </summary>
/// <remarks>
/// This is a capability statement, never a safety statement. "Ready to install" means
/// RepoDeck found a release asset it understands for this machine and has written a plan
/// for it. It says nothing whatever about whether the software is trustworthy, and no
/// part of the interface may present it as though it did.
///
/// Distinct from <see cref="SetupLevel"/>, which describes how much work the user faces.
/// A project can need some setup and still be installable; a project can be perfectly
/// easy to use and still be something RepoDeck cannot fetch.
/// </remarks>
public enum InstallabilityState
{
    /// <summary>A plan exists, it can proceed, and nothing blocks it.</summary>
    ReadyToInstall,

    /// <summary>RepoDeck can fetch it, but the user has to finish the job.</summary>
    NeedsSetup,

    /// <summary>A library, framework or source-only project. Not something to install.</summary>
    DeveloperFocused,

    /// <summary>Positively ruled out for this machine's platform or architecture.</summary>
    NotCompatible,

    /// <summary>Not enough evidence yet. The honest answer before anything is analysed.</summary>
    Unknown
}

/// <summary>An installability answer together with the evidence behind it.</summary>
/// <remarks>
/// <see cref="Reasons"/> is never empty for a state other than <see cref="InstallabilityState.Unknown"/>.
/// A verdict a person cannot interrogate is worth less than no verdict, so every card and
/// panel that shows a state must also be able to show why.
/// </remarks>
public sealed record Installability
{
    public InstallabilityState State { get; init; } = InstallabilityState.Unknown;

    /// <summary>
    /// How sure RepoDeck is. An answer from search-result metadata alone never exceeds
    /// <see cref="Confidence.Possible"/>, because nothing has been looked at.
    /// </summary>
    public Confidence Confidence { get; init; } = Confidence.Unknown;

    /// <summary>The evidence, in plain English. Shown in a tooltip and in Quick Look.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>Short label for a card.</summary>
    public string Label => State switch
    {
        InstallabilityState.ReadyToInstall => "Ready to install",
        InstallabilityState.NeedsSetup => "Needs setup",
        InstallabilityState.DeveloperFocused => "Developer focused",
        InstallabilityState.NotCompatible => "Not compatible",
        _ => "Unknown"
    };

    /// <summary>A sentence for Quick Look and the details page.</summary>
    public string Summary => State switch
    {
        InstallabilityState.ReadyToInstall =>
            "RepoDeck can download and install this for you.",
        InstallabilityState.NeedsSetup =>
            "RepoDeck can fetch this, but you will have to finish setting it up yourself.",
        InstallabilityState.DeveloperFocused =>
            "This is a building block for software rather than a program to install.",
        InstallabilityState.NotCompatible =>
            "This does not offer a version for your computer.",
        _ => "RepoDeck has not worked out whether it can install this."
    };

    /// <summary>
    /// The one thing this verdict never means. Kept here so every surface that shows the
    /// state can show the same disclaimer without inventing its own wording.
    /// </summary>
    public const string NotASafetyJudgement =
        "This describes what RepoDeck can do, not whether the software is safe. "
        + "RepoDeck cannot tell you that.";

    /// <summary>True when the interface may offer INSTALL rather than DETAILS.</summary>
    public bool AllowsDirectInstall => State == InstallabilityState.ReadyToInstall;

    public static Installability Unknown { get; } = new();
}
