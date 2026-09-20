using RepoDeck.Models;

namespace RepoDeck.Services.Install;

/// <summary>
/// RepoDeck's record of what it has installed.
/// </summary>
/// <remarks>
/// The Installed page reads only from here, never from GitHub, so the library works
/// offline and opening it costs nothing against the rate limit.
/// </remarks>
public interface IInstalledAppStore
{
    /// <summary>
    /// Raised after the library has changed on disk.
    /// </summary>
    /// <remarks>
    /// The status strip counts what is installed, and without this it counted once at
    /// startup and never again - so removing an application left the strip still claiming
    /// it was there. Every change passes through this one place, which is why the signal
    /// belongs here rather than at each of the call sites that might remember to send it.
    /// </remarks>
    event Action? Changed;

    IReadOnlyList<ApplicationManifest> GetAll();

    ApplicationManifest? Find(string owner, string name);

    bool IsInstalled(string owner, string name);

    /// <summary>Adds or replaces the record for one application.</summary>
    void Save(ApplicationManifest manifest);

    /// <summary>Forgets an application. Does not touch files on disk.</summary>
    bool Remove(string owner, string name);
}
