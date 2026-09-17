# RepoDeck status

_Last updated: 2026-09-16_

## Current milestone

**Milestone 2 - Repository intelligence and install planning.** Complete and verified.

RepoDeck can now answer: what is this repository, can it run on this computer, and how
would RepoDeck install it? It produces a full installation plan and stops there.
**Nothing is downloaded, extracted or executed.**

## Completed in Milestone 2

- `RepositoryAnalyzerService` producing a typed `RepositoryAnalysis` with evidence,
  warnings and explicit unknowns.
- `GetTreeAsync`: the whole file listing in one request, so classification does not
  scale with repository size and nothing is cloned.
- `ProjectStructureDetector` for .NET, Node, Rust, Python, Java, C/C++, Go, Unity,
  Godot, Docker and script-only repositories, ignoring vendored dependencies.
- A manifest pass reading at most three project files, resolving Avalonia, WPF, Windows
  Forms, Electron, Tauri, Qt/Tk/wx, and library versus application.
- `ApplicationClassifier`: weighted evidence, never repository names, confidence capped
  below Confirmed.
- `AssetNameParser`, `CompatibilityAnalyzer`, `ReleaseAnalyzer`: platform, architecture
  and package-type detection with deterministic, explainable ranking.
- `MachineProfile` passed into the analyzers, including emulation rules, so nothing is
  hard-coded to the development machine.
- `InstallPlan` and `InstallPlanner`: typed, serialisable, inspectable, never executed.
- Details page: RepoDeck Analysis panel, compatibility evidence, recommended download
  with reasons, installation plan and Installation Preview, disabled Install button.
- Analysis progress reporting; the UI stays responsive.
- 274 unit tests, all offline.

## Verification performed

- `dotnet build` - clean, 0 errors, 0 warnings.
- `dotnet test` - 274 passed, 0 failed.
- Six real repositories analysed against live GitHub (see below).
- Application launched on Windows 10 x64; clean startup log.
- Confirmed by inspection that the only `Process.Start` calls in the codebase are the
  guarded browser opener (http/https only) and the folder opener (RepoDeck's own
  directories). There is no download, extraction or execution code anywhere.

## Real repositories tested

| Repository | RepoDeck's conclusion | Correct? |
|---|---|---|
| ShareX/ShareX | .NET / Avalonia / Windows Forms, desktop application (Likely), recommends the x64 setup executable, Windows installer, warns about administrator rights | Yes |
| sharkdp/bat | Rust, command-line tool (Likely), recommends the x86_64 pc-windows-msvc ZIP, portable archive, expects `bat.exe` | Yes |
| mltframework/shotcut | C/C++, desktop application (Possible), recommends the win64 executable, standalone executable | Yes |
| AvaloniaUI/Avalonia | .NET / Avalonia, desktop application (Likely), no downloads, source build required | Type wrong - it is a framework. Plan is correct. |
| nlohmann/json | C/C++, not determined, no compatible download, cannot install | Honest. Ideally "library". |
| FFmpeg/FFmpeg | C/C++, not determined, no releases, source build required | Honest |

Two genuine bugs were found this way and fixed as general rules, not special cases:
project types were ranked by detection order rather than proximity to the repository
root, and any archive counted as evidence of a runnable program.

## Milestone 1 self-audit (2026-09-16)

Audited against the code, not against this document. Committed as
`fix: harden milestone 1 foundation`.

| # | Finding | Severity | Fix |
|---|---|---|---|
| 1 | A superseded search still ran its `catch`/`finally`, writing `IsBusy`, `ResultSummary` and `ErrorMessage` over the newer search's state. Results were safe because the command cancels the previous execution; the surrounding UI state was not. | Real | Generation counter; only the newest execution may write shared state. Regression test added. |
| 2 | `LoadMoreAsync` took no `CancellationToken`, so paging could not be cancelled and page 2 of an old query could append to a new result set. | Real | Takes a token, guarded by the same counter. |
| 3 | Opening a second repository left the first details page's four GitHub requests running. | Real | Cancelled on navigation, back and section change. |
| 4 | `RateLimitChanged` is raised on whichever thread finished the request; handlers updated bound properties directly. This worked only because the service layer happened to capture the UI context. | Latent | `ConfigureAwait(false)` throughout the service layer; handlers marshal via `IUiDispatcher`. |
| 5 | README regexes had no match timeout and no input cap, on content from a stranger. | Real | 250 ms match timeout per pattern, 512 KB input cap, graceful fallback. |
| 6 | Milestone 1's reported build command was wrong: .NET 10 generated `RepoDeck.slnx`, so `dotnet build RepoDeck.sln` fails. | Documentation | Corrected; the command is plain `dotnet build`. |

Checked and already sound: no credentials, personal data or machine-specific paths in
tracked files; tokens never logged or persisted; README links reduced to plain text so
no repository-supplied URL is clickable; `SystemBrowser` refuses any non-http(s) scheme;
one long-lived `HttpClient`; cancellation propagates; supplementary detail requests
degrade individually rather than failing the page.

## Known problems

1. **A UI framework's own repository reads as a desktop application.** AvaloniaUI/Avalonia
   references Avalonia packages, so it is classified as a desktop application rather than
   a framework. The install plan is still correct (source build required, nothing to
   download). Fixing it properly means weighing NuGet packaging evidence across several
   project files rather than only the one nearest the root; deliberately not overfitted.
2. **Header-only and library repositories often land on "not determined"** rather than
   "library". Honest, but less useful than it could be.
3. **The Discover page still uses Milestone 1 metadata heuristics** for its verdict
   badges. Deep analysis costs roughly six requests, so it stays on the details page.
4. **Only the shallowest manifest of each kind is read**, at most three in total. A
   repository whose real nature is described in a deeper project file may be misjudged.
5. **Unauthenticated rate limits are tight.** A details page costs about six requests, so
   roughly ten repositories an hour without a token. Set `REPODECK_GITHUB_TOKEN`.
6. **Favorites were not implemented.** Milestone 2 explicitly deprioritised them below
   the analysis work, and the analysis work filled the milestone.
7. **The GUI was verified by launching it and by testing every ViewModel behind it**, not
   by visually reviewing each rendered panel at several window sizes.
8. **README rendering is still plain text.** Links, tables and images are stripped.

## Next task

**Milestone 3 - Download, extract, install, register.** Executing verified plans.

1. `DownloadService`: progress, cancellation, temporary files, failure cleanup and a
   verified byte count. A partial download must never masquerade as an installation.
2. `ExtractionService` for ZIP and tar.gz into RepoDeck's managed `Apps` folder, with
   path traversal ("zip slip") refused outright.
3. Executable identification after extraction, confirming or correcting the plan's
   predicted candidates.
4. `ApplicationManifest` written as JSON in `Data`, recording repository, release tag,
   installed path, executable, platform, architecture and dates.
5. The Installed library page, with Run, Open folder, View repository, Check update and
   Uninstall.
6. Uninstall confined strictly to RepoDeck's own directory.
7. Enable the Install button only for plans where `CanProceed` is true, and show the
   plan for confirmation before anything is fetched.

Windows installers and Linux packages stay out of scope for automatic execution:
RepoDeck should download them and hand the decision to the user. Source builds remain
out of scope until release-based installation is reliable.

## Architecture decisions

Recorded in `ARCHITECTURE.md`. In short: discover, understand, inspect and execute stay
separate; analysis never downloads and planning never executes; detection logic is pure
and testable against machines other than this one; uncertainty is a type rather than a
string; and RepoDeck never renders a "safe" verdict.
