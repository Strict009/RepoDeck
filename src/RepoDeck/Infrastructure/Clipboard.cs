using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

// SetTextAsync is an extension method in Avalonia 12; the interface itself only exposes
// SetDataAsync. Without this using the call silently resolves against the wrong type.
using Avalonia.Input.Platform;

namespace RepoDeck.Infrastructure;

/// <summary>Putting text on the clipboard, behind an interface so it can be tested.</summary>
public interface IClipboardWriter
{
    /// <summary>Returns whether the text actually got there. Never throws.</summary>
    Task<bool> SetTextAsync(string text);
}

/// <summary>
/// The real clipboard, reached through whichever window is currently the main one.
/// </summary>
/// <remarks>
/// Avalonia exposes the clipboard per top-level window rather than as an application-wide
/// service, so this has to find a window first. That can legitimately fail - during startup,
/// during shutdown, or on a headless run - and a diagnostic feature refusing to work is a
/// disappointment rather than an error, so failure is reported as false rather than thrown.
/// </remarks>
public sealed class AvaloniaClipboardWriter : IClipboardWriter
{
    public async Task<bool> SetTextAsync(string text)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return false;
            }

            var clipboard = desktop.MainWindow?.Clipboard;  // Avalonia's, not ours
            if (clipboard is null) return false;

            await clipboard.SetTextAsync(text).ConfigureAwait(true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>A clipboard that keeps what it was given, for tests.</summary>
public sealed class RecordingClipboardWriter : IClipboardWriter
{
    public string? Text { get; private set; }

    /// <summary>Set to false to rehearse a machine where the clipboard is unavailable.</summary>
    public bool Succeeds { get; set; } = true;

    public Task<bool> SetTextAsync(string text)
    {
        if (!Succeeds) return Task.FromResult(false);

        Text = text;
        return Task.FromResult(true);
    }
}
