# Clean-machine test for RepoDeck 0.1.0-alpha

The one thing 0.1.0-alpha claims but has never demonstrated: that it runs on a Windows
machine with no development tooling on it. Everything else was tested on a computer that has
had the .NET SDK installed for months.

Run this on a machine or VM that has **never had RepoDeck on it** and has no .NET SDK, no
Visual Studio, no Git, no GitHub CLI.

Anything that fails here becomes 0.1.1.

## Before you start

Take a VM snapshot. Several steps are easier to repeat than to undo, and step 12 deliberately
removes things.

Collect the machine's state first, so a failure can be described rather than guessed at:

```powershell
irm https://raw.githubusercontent.com/Strict009/RepoDeck/main/build/clean-machine-report.ps1 | iex
```

Or copy `build/clean-machine-report.ps1` across and run it. Keep the output.

## The run

Work through these in order. For each, write down what actually happened, not what should
have happened.

| # | Step | What counts as passing |
|---|---|---|
| 1 | Download the installer from the release page | Downloads without a browser warning beyond the usual unsigned-file notice |
| 2 | Run it | SmartScreen appears; **More info → Run anyway** proceeds |
| 3 | Complete the wizard | No elevation prompt at any point. Installs to `%LOCALAPPDATA%\Programs\RepoDeck` |
| 4 | Launch RepoDeck | A window appears. **This is the step that has never been proven.** |
| 5 | First-run welcome | Appears, explains itself, and finishing it does not show it again |
| 6 | Search for something ordinary, e.g. `music player` | Results appear within a few seconds |
| 7 | Open a result, then press Back | Returns to the results without re-running the search |
| 8 | Install something small and portable | Confirmation lists what it will do; install completes |
| 9 | Run the installed application | It starts |
| 10 | Close RepoDeck, reopen it | The Installed page still lists what you installed |
| 11 | Check for updates, then Repair it | Both complete and say what they did |
| 12 | Remove the application, then uninstall RepoDeck | Both complete; `%LOCALAPPDATA%\RepoDeck` survives the uninstall |

Then reinstall RepoDeck and confirm step 10's application is still listed. That separation
between the program and its data is the property most worth proving on a machine that is not
the one it was designed on.

## If something fails

**RepoDeck writes a crash report** for any failure that stops it starting, at:

```
%LOCALAPPDATA%\RepoDeck\Logs\crash-<date>-<time>.log
```

On a failure before the window appears you should also get a message box naming that file.
If you get **neither a window nor a message box**, that itself is the finding, and the most
serious one possible - it means the process died before anything could report it. Say so.

The ordinary log is in the same folder as `repodeck-<date>.log`.

Neither file contains tokens or credentials. Both are safe to attach to an issue.

## What to report

For anything that fails, the useful report is:

1. Which numbered step
2. What you saw, in your own words
3. The crash report or log, attached
4. The output of `clean-machine-report.ps1`

Open it at https://github.com/Strict009/RepoDeck/issues

## The failures worth expecting

These are the ones a clean machine produces and this machine could not:

- **A missing native library.** RepoDeck ships its own runtime, but ships it as files; if
  security software quarantines one, it will not start. The crash report names this case.
- **A graphics stack that will not create a context.** Avalonia renders through SkiaSharp,
  and a VM with no GPU acceleration is a genuinely different environment. Worth testing both
  with and without hardware acceleration if your VM offers the choice.
- **SmartScreen being more obstructive than expected**, particularly on a machine with a
  managed security policy.
- **A user profile on a network drive or with redirected folders**, which changes where
  `%LOCALAPPDATA%` actually points.
