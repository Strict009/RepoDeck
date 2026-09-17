# RepoDeck status

_Last updated: 2026-09-16_

## Current milestone

**Milestone 1 - Discover and Understand.** Complete and verified.

Definition of done: RepoDeck launches on this PC, GitHub search works, real
repositories appear in a polished card interface, and selecting one shows useful
repository information. All four verified (see Verification below).

## Completed features

- Solution scaffold: `RepoDeck.sln`, Avalonia 12.1.2 on .NET 10, xunit test project.
- MVVM structure with CommunityToolkit.Mvvm and a convention-based `ViewLocator`.
- `GitHubClient` covering search, repository, README, languages and releases, with
  rate-limit parsing, cancellation, response caching and plain-English error messages.
- Discover page: search, sort, language / stars / updated filters, hide-archived,
  "probably applications" filter, paging, loading / empty / error / welcome states,
  cancellation.
- Repository cards with plain-English summary, facts, topics and an explained verdict.
- Repository details page: what it is, what it is for, installation status,
  compatibility status, factual signal panel, releases, technical metadata, README.
- `HeuristicRepositoryExplanationService` behind `IRepositoryExplanationService`.
- `ApplicationLikelihoodEvaluator` and `RepositorySignalBuilder`, both explainable.
- Dependency-free README markdown reader.
- Light and dark theming that follows the system.
- File logging with a 14-day trim; no tokens or secrets are ever logged.
- 107 unit tests, all offline.

## Verification performed

- `dotnet build` - clean, 0 warnings, 0 errors.
- `dotnet test` - 107 passed, 0 failed, ~54 ms.
- End-to-end run against the **live** GitHub API through the real ViewModels: a search
  for "video editor" returned 30 of 15k matches; opening the first result loaded 71
  README blocks, 5 releases, 6 languages and 5 signals with no errors. This was done
  with a temporary test that was removed afterwards, so the committed suite stays
  offline.
- Application launched on Windows 10 x64 and confirmed running with a clean startup log.

## Milestone 1 self-audit (2026-09-16)

Milestone 1 was audited against its own code rather than against this document. Findings
and fixes, committed separately as `fix: harden milestone 1 foundation`:

| # | Finding | Severity | Fix |
|---|---|---|---|
| 1 | A superseded search still ran its `catch`/`finally`, writing `IsBusy`, `ResultSummary` and `ErrorMessage` over the newer search's state. Results themselves were safe because the command cancels the previous execution, but the surrounding UI state was not. | Real | Generation counter; only the newest execution may write shared state. Regression test added. |
| 2 | `LoadMoreAsync` took no `CancellationToken`, so paging could not be cancelled and page 2 of an old query could append to a new result set. | Real | Takes a token, and is guarded by the same generation counter. |
| 3 | Opening a second repository left the first details page's four GitHub requests running against a page nobody was looking at. | Real | `MainWindowViewModel` cancels the active details load on navigation, back and section change. |
| 4 | `RateLimitChanged` is raised from whichever thread completed the HTTP request; handlers updated bound properties directly. This worked only because the service layer happened to capture the UI synchronisation context. | Latent | Service layer now uses `ConfigureAwait(false)` throughout, and handlers marshal through `IUiDispatcher`. |
| 5 | README regexes had no match timeout and no input cap, on content supplied by a stranger. | Real | 250 ms match timeout on every pattern, 512 KB input cap, graceful fallback on timeout. |
| 6 | The build command recorded in Milestone 1's report was wrong: .NET 10 generated `RepoDeck.slnx`, not `RepoDeck.sln`, so `dotnet build RepoDeck.sln` fails. | Documentation | Corrected here and in `README.md`. |

Checked and found already sound: no credentials, personal data or machine-specific paths
in tracked files; tokens never logged and never written to disk; README links reduced to
plain text so no repository-supplied URL is ever clickable; `SystemBrowser` refuses any
scheme other than http/https; `HttpClient` is a single long-lived instance with a pooled
connection lifetime; cancellation propagates through the client; supplementary detail
requests degrade individually rather than failing the page.

## Known problems

1. **The GUI was verified by launching it, not by inspecting every rendered control.**
   The window opens and runs cleanly, and every ViewModel behind it was exercised
   against live data, but individual XAML layouts have not been visually reviewed at
   multiple window sizes.
2. **Application-likelihood verdicts are conservative.** Genuine applications such as
   `auto-editor` currently read as "Unclear" because the evaluator only sees metadata
   and many projects have no topics. It is honest, but it under-classifies. Tuning
   belongs with Milestone 2, when file-level analysis provides better evidence.
3. **"Has releases" is not a search filter.** GitHub search cannot express it and
   checking per result would cost one API call per card. Deferred to Milestone 2, where
   release data is fetched anyway.
4. **Non-Latin descriptions are unverified in the UI.** A CJK description appeared in
   search results; it was mangled in console output, which is a console encoding matter,
   but glyph coverage in the bundled Inter font has not been checked on screen.
5. **Unauthenticated rate limits are tight** - 10 searches a minute. Setting
   `REPODECK_GITHUB_TOKEN` is strongly advised for real use.
6. **README rendering is plain text.** Links, tables and images are stripped. A proper
   markdown renderer is a candidate dependency for a later milestone.

## Next task

**Milestone 2 - Releases and compatibility detection.** No installing yet.

1. `RepositoryAnalyzerService`: file-level project-type detection (`*.csproj`, `*.sln`,
   `package.json`, `Cargo.toml`, `pyproject.toml`, `CMakeLists.txt`, Unity's `Assets/`
   and `ProjectSettings/`), returning a structured analysis with reasons.
2. Release asset analysis: recognise `.exe`, `.msi`, `.zip`, `win-x64`, `.AppImage`,
   `.deb`, `.rpm`, `.tar.gz`, `linux-x64`, and rank assets against the current machine.
   A source archive must never outrank a compatible compiled binary.
3. Replace the details page's `Requires inspection` compatibility text with real,
   evidence-backed findings ("Windows support likely because the latest release contains
   a win-x64 ZIP").
4. Version comparison for later update checks.
5. Favorites, with JSON persistence - the first use of the `Data` folder.
6. Tests: asset ranking, platform and architecture detection, project-type detection,
   version comparison.

Milestone 3 is download / extract / install manifests. Milestone 4 is the Run button.
Automatic source compilation stays out of scope until release-based installation is
reliable.

## Architecture decisions

Recorded in `ARCHITECTURE.md`. In short: discover, understand, inspect and execute are
separate from the start; abstractions exist only where substitution is real; detection
logic is pure and testable; uncertainty is modelled as a type rather than lost in a
string.
