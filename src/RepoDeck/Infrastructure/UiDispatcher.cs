using Avalonia.Threading;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Marshals work onto the UI thread.
/// </summary>
/// <remarks>
/// This exists because <see cref="Services.GitHub.IGitHubClient.RateLimitChanged"/> can be
/// raised on whichever thread completed the HTTP request. Updating a bound property from
/// a background thread is a latent crash, and it previously worked only by accident -
/// the service layer happened to capture the UI synchronisation context. The service layer
/// now uses ConfigureAwait(false) as library code should, which makes marshalling explicit
/// rather than incidental. It is an interface so ViewModels remain testable without a
/// running Avalonia application.
/// </remarks>
public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}

/// <summary>Runs the action immediately. For unit tests, which have no UI thread.</summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public static ImmediateUiDispatcher Instance { get; } = new();
    public void Post(Action action) => action();
}
