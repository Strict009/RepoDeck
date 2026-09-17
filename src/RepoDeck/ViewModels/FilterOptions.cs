using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>Choices shown in the Discover page filter controls.</summary>
public sealed record SortOption(string Name, RepositorySort Value)
{
    public override string ToString() => Name;

    public static IReadOnlyList<SortOption> All { get; } =
    [
        new("Best match", RepositorySort.BestMatch),
        new("Most stars", RepositorySort.Stars),
        new("Recently updated", RepositorySort.RecentlyUpdated),
        new("Most forks", RepositorySort.Forks)
    ];
}

public sealed record StarsOption(string Name, int? MinStars)
{
    public override string ToString() => Name;

    public static IReadOnlyList<StarsOption> All { get; } =
    [
        new("Any popularity", null),
        new("50+ stars", 50),
        new("500+ stars", 500),
        new("5,000+ stars", 5_000),
        new("20,000+ stars", 20_000)
    ];
}

public sealed record UpdatedOption(string Name, UpdatedWithin Value)
{
    public override string ToString() => Name;

    public static IReadOnlyList<UpdatedOption> All { get; } =
    [
        new("Any time", UpdatedWithin.Any),
        new("Past month", UpdatedWithin.PastMonth),
        new("Past 6 months", UpdatedWithin.PastSixMonths),
        new("Past year", UpdatedWithin.PastYear),
        new("Past 2 years", UpdatedWithin.PastTwoYears)
    ];
}

public sealed record LanguageOption(string Name, string? Value)
{
    public override string ToString() => Name;

    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new("Any language", null),
        new("C#", "C#"),
        new("C++", "C++"),
        new("C", "C"),
        new("Rust", "Rust"),
        new("Go", "Go"),
        new("Python", "Python"),
        new("JavaScript", "JavaScript"),
        new("TypeScript", "TypeScript"),
        new("Java", "Java"),
        new("Kotlin", "Kotlin"),
        new("Swift", "Swift"),
        new("Lua", "Lua"),
        new("Shell", "Shell")
    ];
}
