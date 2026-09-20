using System.Runtime.InteropServices;
using System.Text;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Turns a fatal failure into evidence somebody can act on.
/// </summary>
/// <remarks>
/// <para>
/// This exists for the machine RepoDeck has never run on. The ordinary log is written by
/// <see cref="FileAppLog"/>, which is built inside <c>AppServices</c>, which is built after
/// Avalonia has started - so anything that goes wrong before that point produced no log, no
/// message and no evidence at all. The process simply vanished.
/// </para>
/// <para>
/// That is the exact shape of the failure this milestone is most likely to meet: a missing
/// native dependency, a graphics stack that will not create a context, an architecture
/// mismatch. The user sees nothing, and can report nothing useful.
/// </para>
/// <para>
/// So this deliberately depends on almost nothing: <c>System.IO</c>, <c>Environment</c>, and
/// one P/Invoke to show a message box without Avalonia. If it cannot write to RepoDeck's own
/// folder it falls back to the temporary directory, and if it cannot write at all it gives
/// up quietly rather than throwing from inside a crash handler.
/// </para>
/// </remarks>
public static class CrashReporter
{
    /// <summary>Where a report was last written, for tests and for telling the user.</summary>
    public static string? LastReportPath { get; private set; }

    /// <summary>
    /// Installs handlers for the failures that would otherwise be silent, and returns a
    /// wrapper to run the application inside.
    /// </summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Report(exception, "An unhandled error");
            }
        };

        // A faulted task nobody awaited would otherwise be swallowed at collection time.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, "A background operation failed");
            e.SetObserved();
        };
    }

    /// <summary>
    /// Writes a report and, for a failure that stopped RepoDeck from starting, tells the
    /// user where it went. Returns the path, or null if nothing could be written.
    /// </summary>
    /// <param name="paths">
    /// Where to write. Null means RepoDeck's real folder, which is what the application
    /// wants and what a test must not have: a test suite writing crash reports into the
    /// developer's live log folder leaves a healthy installation looking like it has been
    /// falling over.
    /// </param>
    public static string? Report(
        Exception exception, string what, bool notifyUser = false, AppPaths? paths = null)
    {
        string? path = null;

        try
        {
            path = Write(exception, what, paths);
            LastReportPath = path;
        }
        catch
        {
            // Throwing from inside a crash handler replaces a diagnosable failure with an
            // undiagnosable one. There is nowhere left to report this to.
        }

        if (notifyUser)
        {
            try
            {
                Notify(exception, path);
            }
            catch
            {
                // Same reasoning. A missing message box is better than a second crash.
            }
        }

        return path;
    }

    private static string Write(Exception exception, string what, AppPaths? paths)
    {
        var directory = ReportDirectory(paths);
        Directory.CreateDirectory(directory);

        var name = $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var path = Path.Combine(directory, name);

        File.WriteAllText(path, Compose(exception, what), Encoding.UTF8);
        return path;
    }

    /// <summary>
    /// The report. Written for somebody who will paste it into an issue, so it leads with
    /// the machine facts that explain most first-run failures.
    /// </summary>
    internal static string Compose(Exception exception, string what)
    {
        var text = new StringBuilder();

        text.AppendLine("RepoDeck crash report");
        text.AppendLine("=====================");
        text.AppendLine();
        text.AppendLine($"{what} stopped RepoDeck.");
        text.AppendLine();
        text.AppendLine($"When:       {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"RepoDeck:   {AppVersion.Current}");
        text.AppendLine($"OS:         {RuntimeInformation.OSDescription}");
        text.AppendLine($"Machine:    {RuntimeInformation.OSArchitecture}");
        text.AppendLine($"Process:    {RuntimeInformation.ProcessArchitecture}");
        text.AppendLine($".NET:       {RuntimeInformation.FrameworkDescription}");
        text.AppendLine();

        // The clean-machine failure has a recognisable shape, and saying so saves somebody
        // reading a stack trace to work out what a message box already told them.
        var hint = Diagnose(exception);
        if (hint is not null)
        {
            text.AppendLine("What this probably means");
            text.AppendLine("------------------------");
            text.AppendLine(hint);
            text.AppendLine();
        }

        text.AppendLine("Error");
        text.AppendLine("-----");

        var current = exception;
        var depth = 0;

        while (current is not null && depth < 10)
        {
            // Every one of these can throw. Message and StackTrace are overridable, and an
            // exception type that misbehaves is exactly the sort of thing that brought
            // RepoDeck down in the first place. A partial report is worth far more than an
            // exception thrown while trying to explain an exception.
            text.AppendLine($"{Safely(() => current.GetType().FullName)}: {Safely(() => current.Message)}");

            var trace = Safely(() => current.StackTrace);
            if (!string.IsNullOrWhiteSpace(trace) && trace != Unavailable)
            {
                text.AppendLine(trace);
            }

            Exception? inner;

            try
            {
                inner = current.InnerException;
            }
            catch
            {
                break;
            }

            current = inner;
            depth++;

            if (current is not null)
            {
                text.AppendLine();
                text.AppendLine("Caused by:");
            }
        }

        text.AppendLine();
        text.AppendLine("This file contains no tokens, credentials or personal data beyond the");
        text.AppendLine("paths above. Please attach it to a report at");
        text.AppendLine("https://github.com/Strict009/RepoDeck/issues");

        return text.ToString();
    }

    private const string Unavailable = "(unavailable)";

    /// <summary>
    /// Reads one field of an exception that may not want to be read.
    /// </summary>
    private static string Safely(Func<string?> read)
    {
        try
        {
            return read() ?? Unavailable;
        }
        catch
        {
            return Unavailable;
        }
    }

    /// <summary>
    /// Recognises the failures a first run on an unprepared machine actually produces.
    /// Returns null rather than guessing when the shape is not familiar.
    /// </summary>
    internal static string? Diagnose(Exception exception)
    {
        var current = exception;
        var depth = 0;

        while (current is not null && depth++ < 10)
        {
            switch (current)
            {
                case DllNotFoundException:
                case EntryPointNotFoundException:
                    return "RepoDeck could not load one of the libraries it ships with. This "
                         + "usually means a file is missing from the installation folder, or "
                         + "that security software removed it. Reinstalling RepoDeck is the "
                         + "first thing to try.";

                case BadImageFormatException:
                    return "RepoDeck tried to load a file built for a different kind of "
                         + "processor. This build is for 64-bit Windows on Intel or AMD.";

                case UnauthorizedAccessException:
                    return "RepoDeck was refused access to a file or folder it owns. This is "
                         + "usually security software, a locked profile, or a folder that has "
                         + "been made read-only.";

                case IOException io when io.Message.Contains("space", StringComparison.OrdinalIgnoreCase):
                    return "The disk appears to be full.";
            }

            try
            {
                current = current.InnerException;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// RepoDeck's own log folder, or the temporary directory if that cannot be reached.
    /// </summary>
    private static string ReportDirectory(AppPaths? paths)
    {
        try
        {
            return (paths ?? new AppPaths()).Logs;
        }
        catch
        {
            // Somewhere is better than nowhere.
            return Path.Combine(Path.GetTempPath(), "RepoDeck");
        }
    }

    /// <summary>
    /// Says something, without Avalonia. A failure before the UI exists cannot use the UI
    /// to explain itself, and exiting silently leaves somebody with a shortcut that
    /// appears to do nothing at all.
    /// </summary>
    private static void Notify(Exception exception, string? reportPath)
    {
        var message = new StringBuilder();
        message.AppendLine("RepoDeck could not start.");
        message.AppendLine();

        var hint = Diagnose(exception);
        message.AppendLine(hint ?? exception.Message);

        if (reportPath is not null)
        {
            message.AppendLine();
            message.AppendLine("A report was saved to:");
            message.AppendLine(reportPath);
        }

        if (OperatingSystem.IsWindows())
        {
            // MB_OK | MB_ICONERROR | MB_SETFOREGROUND
            MessageBoxW(IntPtr.Zero, message.ToString(), "RepoDeck", 0x00000010 | 0x00010000);
        }
        else
        {
            Console.Error.WriteLine(message.ToString());
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
