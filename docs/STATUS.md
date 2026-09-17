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

- `dotnet build RepoDeck.sln` - clean, 0 warnings, 0 errors.
- `dotnet test` - 107 passed, 0 failed, ~54 ms.
- End-to-end run against the **live** GitHub API through the real ViewModels: a search
  for "video editor" returned 30 of 15k matches; opening the first result loaded 71
  README blocks, 5 releases, 6 languages and 5 signals with no errors. This was done
  with a temporary test that was removed afterwards, so the committed suite stays
  offline.
- Application launched on Windows 10 x64 and confirmed running with a clean startup log.

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
