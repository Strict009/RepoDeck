using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RepoDeck.ViewModels;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Maps a ViewModel to its View by naming convention:
/// RepoDeck.ViewModels.DiscoverViewModel -> RepoDeck.Views.DiscoverView.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "Nothing to show." };

        var viewModelName = data.GetType().FullName!;
        var viewName = viewModelName
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(viewName);
        if (type is null)
        {
            return new TextBlock { Text = $"View not found: {viewName}" };
        }

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
