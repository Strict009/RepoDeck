using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.History;

namespace RepoDeck.ViewModels;

/// <summary>One entry on the Recently viewed shelf.</summary>
/// <remarks>
/// Drawn entirely from what was stored locally when the project was opened - a name, an
/// owner, a one-line description. Nothing here asks GitHub anything, which is what makes
/// the shelf free to show: a row of six of these would otherwise cost six requests every
/// time somebody returned to Discover.
///
/// The artwork is the same drawn kind mark used everywhere else. No screenshot is cached
/// and none is invented.
/// </remarks>
public sealed partial class RecentProjectViewModel : ViewModelBase
{
    private readonly Action<RecentProjectViewModel> _open;

    public RecentProjectViewModel(RecentProject entry, Action<RecentProjectViewModel> open)
    {
        Entry = entry;
        _open = open;

        // Classified from what was stored, so the mark matches the one the card carried.
        // Unknown is a perfectly good answer and draws a plain tile.
        Kind = ProjectKindClassifier.Classify(new GitHubRepository
        {
            Id = 0,
            Name = entry.Name,
            FullName = entry.FullName,
            HtmlUrl = "",
            Description = entry.Description
        }).Kind;
    }

    public RecentProject Entry { get; }

    public string FullName => Entry.FullName;
    public string Owner => Entry.Owner;
    public string FriendlyName => FriendlyNaming.ForRepository(Entry.Name);

    /// <summary>
    /// The remembered description, tidied for display. Empty stays empty here - the shelf
    /// simply omits the line rather than explaining the absence.
    /// </summary>
    public string Description => _description ??= DescriptionCleaner.Clean(Entry.Description);
    public bool HasDescription => Description.Length > 0;

    private string? _description;

    public string WhenText => Humanize.RelativeTime(Entry.ViewedAt);

    public ProjectKind Kind { get; }

    public IBrush FallbackBrush => FriendlyNaming.ColourFor(FullName);

    public string FallbackInitial =>
        FriendlyName.Length > 0 ? FriendlyName[..1].ToUpperInvariant() : "?";

    [RelayCommand]
    private void Open() => _open(this);

}
