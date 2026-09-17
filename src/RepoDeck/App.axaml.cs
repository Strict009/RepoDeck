using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RepoDeck.Infrastructure;
using RepoDeck.ViewModels;
using RepoDeck.Views;

namespace RepoDeck;

public partial class App : Application
{
    private AppServices? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = new AppServices();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(_services)
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                _services.Log.Info("App", "RepoDeck shutting down.");
                _services.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
