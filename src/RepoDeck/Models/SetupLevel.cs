namespace RepoDeck.Models;

/// <summary>
/// How much work stands between someone and actually using this.
/// </summary>
/// <remarks>
/// Deliberately independent of whether the software is any good, how popular it is, or
/// whether it is safe. A wildly popular project can be Advanced; an obscure one can be
/// Easy. Conflating difficulty with quality would be the same mistake as treating stars
/// as a safety signal.
/// </remarks>
public enum SetupLevel
{
    /// <summary>RepoDeck can install this and you can run it.</summary>
    Easy,

    /// <summary>Works, but something else has to be installed or configured first.</summary>
    SomeSetup,

    /// <summary>Has to be built or configured by hand before it will run.</summary>
    Advanced,

    /// <summary>Not a program at all - a library or framework for building software with.</summary>
    DeveloperFocused,

    /// <summary>Not enough evidence to say.</summary>
    Unknown
}

/// <summary>A setup classification with the evidence behind it.</summary>
public sealed record SetupAssessment
{
    public SetupLevel Level { get; init; } = SetupLevel.Unknown;

    /// <summary>How sure RepoDeck is. Metadata-only assessments never exceed Possible.</summary>
    public Confidence Confidence { get; init; } = Confidence.Unknown;

    /// <summary>Why. Always populated when the level is anything but Unknown.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>Short label for a card.</summary>
    public string Label => Level switch
    {
        SetupLevel.Easy => "Ready to use",
        SetupLevel.SomeSetup => "Needs some setup",
        SetupLevel.Advanced => "For advanced users",
        SetupLevel.DeveloperFocused => "For developers",
        _ => "Not sure yet"
    };

    /// <summary>A sentence for the details page.</summary>
    public string Summary => Level switch
    {
        SetupLevel.Easy => "RepoDeck can install this for you and you can run it straight away.",
        SetupLevel.SomeSetup =>
            "This should work, but something else needs installing or setting up first.",
        SetupLevel.Advanced =>
            "This has to be built or set up by hand, which takes some technical knowledge.",
        SetupLevel.DeveloperFocused =>
            "This is a building block for software rather than a program you run.",
        _ => "RepoDeck could not work out how much setup this needs."
    };

    public static SetupAssessment Unknown { get; } = new();
}
