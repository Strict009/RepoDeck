namespace RepoDeck.Models;

public enum ReadmeBlockKind
{
    Heading,
    Paragraph,
    ListItem,
    Code,
    Quote
}

/// <summary>
/// One renderable piece of a README. RepoDeck parses just enough markdown to display
/// a readable document without taking on a full markdown rendering dependency.
/// </summary>
public sealed record ReadmeBlock
{
    public ReadmeBlockKind Kind { get; init; }
    public string Text { get; init; } = "";

    /// <summary>Heading level 1-6. Zero for non-headings.</summary>
    public int Level { get; init; }

    public bool IsHeading => Kind == ReadmeBlockKind.Heading;
    public bool IsParagraph => Kind == ReadmeBlockKind.Paragraph;
    public bool IsListItem => Kind == ReadmeBlockKind.ListItem;
    public bool IsCode => Kind == ReadmeBlockKind.Code;
    public bool IsQuote => Kind == ReadmeBlockKind.Quote;
}
