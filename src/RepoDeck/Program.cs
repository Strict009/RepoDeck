using Avalonia;
using RepoDeck.Infrastructure;
using System;

namespace RepoDeck;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Installed before anything else, because the failures worth catching here are the
        // ones that happen before RepoDeck has a log, a window, or any other way to say
        // what went wrong. On a machine RepoDeck has never run on, a silent exit leaves
        // somebody with a shortcut that appears to do nothing.
        CrashReporter.Install();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Report(ex, "A problem", notifyUser: true);
            return 1;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
