namespace RepoDeck.ViewModels;

/// <summary>
/// A page that does not exist yet. It says so plainly rather than showing an empty
/// screen or pretending a feature is merely broken.
/// </summary>
public sealed class PlaceholderViewModel : ViewModelBase
{
    public PlaceholderViewModel(string title, string description, string milestone)
    {
        Title = title;
        Description = description;
        Milestone = milestone;
    }

    public string Title { get; }
    public string Description { get; }
    public string Milestone { get; }
}
