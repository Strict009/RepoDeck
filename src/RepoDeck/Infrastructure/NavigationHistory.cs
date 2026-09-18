namespace RepoDeck.Infrastructure;

/// <summary>One screen somebody can go back to.</summary>
/// <param name="Page">The page view model. Held, not recreated, so returning to it restores it.</param>
/// <param name="Title">What the status strip calls this screen.</param>
/// <param name="BackLabel">
/// What a child screen calls the way back here - "Back to results", "Back to Installed".
/// Recorded when leaving rather than worked out when returning, because by then the screen
/// may no longer be in the state that made the label true.
/// </param>
public sealed record NavigationEntry(object Page, string Title, string BackLabel);

/// <summary>
/// Where the user has been, so that Back means something specific.
/// </summary>
/// <remarks>
/// RepoDeck previously had a single boolean and one hardcoded label reading "BACK TO
/// RESULTS", which was wrong whenever somebody had arrived from Favorites, or from Discover
/// before searching for anything. A stack costs almost nothing and lets the way back be
/// named honestly.
///
/// Pages are held rather than rebuilt. Returning to a search must not re-run it: the results
/// are already there, the user has already spent the GitHub allowance on them once, and
/// fetching them again would be slower and would sometimes fail.
///
/// Bounded, because a session that opens two hundred projects should not keep two hundred
/// view models alive. Dropping the oldest entries is safe - somebody pressing Back that many
/// times has long since stopped meaning "the previous screen".
/// </remarks>
public sealed class NavigationHistory
{
    /// <summary>Deep enough that nobody reaches the bottom by accident.</summary>
    public const int MaxDepth = 20;

    private readonly List<NavigationEntry> _entries = [];

    /// <summary>True when there is somewhere to go back to.</summary>
    public bool CanGoBack => _entries.Count > 0;

    public int Depth => _entries.Count;

    /// <summary>
    /// What the way back should be called, or empty when there is nowhere to go.
    /// </summary>
    public string BackLabel => _entries.Count > 0 ? _entries[^1].BackLabel : "";

    /// <summary>Records the screen being left behind.</summary>
    public void Push(NavigationEntry entry)
    {
        _entries.Add(entry);

        // Oldest first: the far end of a long history is the part nobody is going to walk
        // back to one press at a time.
        while (_entries.Count > MaxDepth) _entries.RemoveAt(0);
    }

    /// <summary>
    /// Returns the previous screen and removes it from the history, or null when there
    /// is none.
    /// </summary>
    public NavigationEntry? Pop()
    {
        if (_entries.Count == 0) return null;

        var entry = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        return entry;
    }

    /// <summary>
    /// Forgets everything. Choosing a top-level destination is a fresh start, not a step
    /// deeper: pressing Back afterwards should not walk into the section somebody just
    /// deliberately left.
    /// </summary>
    public void Clear() => _entries.Clear();
}
