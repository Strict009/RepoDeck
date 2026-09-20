using System.Runtime.InteropServices;
using System.Text;
using RepoDeck.Models;

namespace RepoDeck.Infrastructure;

/// <summary>
/// The report a user copies into a bug report.
/// </summary>
/// <remarks>
/// <para>
/// "It doesn't work" is almost impossible to act on. The same report with the version, the
/// OS, both architectures, where RepoDeck keeps its files and what the GitHub allowance
/// looked like is usually enough to know where to look. Asking a non-technical user to find
/// that themselves is not reasonable, so RepoDeck assembles it and puts it on the clipboard.
/// </para>
/// <para>
/// Everything here is chosen to be safe to paste in public. No token, no authorisation
/// header, no file contents, no search history and no list of installed applications - only
/// how many there are. The user profile name appears inside the paths, which is unavoidable
/// when the point is to say where files are, and the report says so plainly rather than
/// pretending otherwise.
/// </para>
/// <para>
/// A test asserts a token set in the environment does not reach the output, because the one
/// failure that matters here is a helpful diagnostic quietly publishing somebody's
/// credentials.
/// </para>
/// </remarks>
public static class DiagnosticReport
{
    /// <summary>
    /// Assembles the report. Never throws: a diagnostic that fails while describing a
    /// failure is worse than useless.
    /// </summary>
    public static string Compose(
        AppPaths paths,
        RateLimitStatus rateLimit,
        bool hasToken,
        int installedCount,
        string? lastCrashReport = null)
    {
        var text = new StringBuilder();

        text.AppendLine("RepoDeck diagnostic report");
        text.AppendLine("==========================");
        text.AppendLine();
        text.AppendLine($"Generated:  {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine();

        text.AppendLine("Application");
        text.AppendLine("-----------");
        text.AppendLine($"Version:    {AppVersion.Current}");
        text.AppendLine($"Build:      {(AppVersion.IsPrerelease ? "pre-release" : "release")}");

        try
        {
            var location = Environment.ProcessPath;
            text.AppendLine($"Running:    {Describe(location)}");
        }
        catch
        {
            text.AppendLine("Running:    unknown");
        }

        text.AppendLine();
        text.AppendLine("Computer");
        text.AppendLine("--------");
        text.AppendLine($"OS:         {RuntimeInformation.OSDescription}");
        text.AppendLine($"Machine:    {RuntimeInformation.OSArchitecture}");
        text.AppendLine($"Process:    {RuntimeInformation.ProcessArchitecture}");
        text.AppendLine($".NET:       {RuntimeInformation.FrameworkDescription}");

        try
        {
            text.AppendLine($"Locale:     {System.Globalization.CultureInfo.CurrentCulture.Name}");
        }
        catch
        {
            // Not worth failing a report over.
        }

        text.AppendLine();
        text.AppendLine("Where RepoDeck keeps its files");
        text.AppendLine("------------------------------");
        text.AppendLine("(these contain your Windows user name, which is why they are shown as paths)");
        text.AppendLine();

        try
        {
            text.AppendLine($"Root:       {paths.Root}");
            text.AppendLine($"Logs:       {paths.Logs}");
            text.AppendLine($"Exists:     {Directory.Exists(paths.Root)}");
            text.AppendLine($"Writable:   {Describe(CanWrite(paths.Root))}");
        }
        catch (Exception ex)
        {
            text.AppendLine($"Could not inspect the data folder: {ex.Message}");
        }

        text.AppendLine();
        text.AppendLine("GitHub");
        text.AppendLine("------");

        // Whether a token exists, never the token. The distinction is the whole point.
        text.AppendLine($"Token:      {(hasToken ? "configured" : "not configured")}");

        if (rateLimit.IsKnown)
        {
            text.AppendLine($"Allowance:  {rateLimit.Remaining} of {rateLimit.Limit} remaining");
            text.AppendLine($"Resets:     {rateLimit.ResetsAt:yyyy-MM-dd HH:mm:ss zzz}");
        }
        else
        {
            text.AppendLine("Allowance:  not contacted yet this session");
        }

        text.AppendLine();
        text.AppendLine("Library");
        text.AppendLine("-------");

        // A count, not a list. What somebody has installed is their business.
        text.AppendLine($"Installed:  {installedCount} application(s)");

        text.AppendLine();
        text.AppendLine("Logs");
        text.AppendLine("----");

        try
        {
            AppendLogSummary(text, paths.Logs);
        }
        catch (Exception ex)
        {
            text.AppendLine($"Could not read the log folder: {ex.Message}");
        }

        if (lastCrashReport is { Length: > 0 })
        {
            text.AppendLine();
            text.AppendLine($"Most recent crash report: {lastCrashReport}");
        }

        text.AppendLine();
        text.AppendLine("This report contains no tokens, credentials, file contents, search");
        text.AppendLine("history or list of installed applications. It is safe to paste into");
        text.AppendLine($"an issue at {Services.Update.RepoDeckProject.IssuesUrl}");

        return text.ToString();
    }

    private static void AppendLogSummary(StringBuilder text, string logDirectory)
    {
        if (!Directory.Exists(logDirectory))
        {
            text.AppendLine("No log folder yet.");
            return;
        }

        text.AppendLine($"Folder:     {logDirectory}");

        var files = new DirectoryInfo(logDirectory)
            .GetFiles()
            .OrderByDescending(f => f.LastWriteTime)
            .Take(5)
            .ToList();

        if (files.Count == 0)
        {
            text.AppendLine("No log files yet.");
            return;
        }

        foreach (var file in files)
        {
            text.AppendLine(
                $"  {file.LastWriteTime:yyyy-MM-dd HH:mm}  {file.Length,8:N0} bytes  {file.Name}");
        }

        var crashes = files.Count(f => f.Name.StartsWith("crash-", StringComparison.OrdinalIgnoreCase));
        if (crashes > 0)
        {
            text.AppendLine();
            text.AppendLine($"{crashes} crash report(s) in the five most recent files.");
        }
    }

    /// <summary>
    /// Whether RepoDeck can write where it thinks it can. A read-only or redirected profile
    /// explains a whole class of failures at once.
    /// </summary>
    private static bool? CanWrite(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return null;

            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Describe(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        _ => "unknown"
    };

    private static string Describe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value;
}
