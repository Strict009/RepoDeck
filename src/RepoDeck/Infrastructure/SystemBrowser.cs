using System.Diagnostics;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Opens a web address in whatever browser the user already uses.
/// </summary>
/// <remarks>
/// This is the only place RepoDeck starts an external process in the MVP, and it only
/// ever hands an http(s) URL to the operating system's default handler. It never runs
/// a downloaded file - that is a later milestone with its own explicit confirmation.
/// </remarks>
public static class SystemBrowser
{
    public static void OpenUrl(string? url, IAppLog log)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            log.Warn("Browser", $"Refused to open a non-web address: {url}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            log.Info("Browser", $"Opened {uri.AbsoluteUri}");
        }
        catch (Exception ex)
        {
            log.Error("Browser", $"Could not open {uri.AbsoluteUri}", ex);
        }
    }
    /// <summary>Opens one of RepoDeck's own folders in the system file manager.</summary>
    public static void OpenFolder(string path, IAppLog log)
    {
        if (!Directory.Exists(path))
        {
            log.Warn("Browser", $"Refused to open a folder that does not exist: {path}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            log.Error("Browser", $"Could not open folder {path}", ex);
        }
    }
}
