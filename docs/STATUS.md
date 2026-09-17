# RepoDeck status

_Last updated: 2026-09-17_

## Current milestone

**Milestone 3 - Download, extract, install, register, run.** Complete and verified.

RepoDeck now carries out the plans it produced in Milestone 2. It downloads a release
asset, verifies it, extracts it into its own folder, identifies the program inside,
records a manifest, and launches it when the user presses Run.

**Nothing downloaded is ever executed by RepoDeck.** The only launch is the user pressing
Run on something already installed. Windows installers and Linux packages are downloaded
and handed over, never run. Elevation is never requested.

## Completed in Milestone 3

- `DownloadService`: streams to a `.part` file, verifies the byte count against what the
  server promised, and only then renames. Cancellation and every failure path delete the
  partial file. SHA-256 computed in passing and recorded in the manifest.
- `ExtractionService` for ZIP and tar.gz, with `ArchivePathGuard` refusing traversal,
  absolute, drive-rooted, UNC and degenerate entry paths, and refusing symbolic and hard
  links in tar archives. Refused entries are reported, not silently skipped. Expanded
  size and entry count are capped.
- `ExecutableLocator`: confirms or corrects the plan's predicted executable against what
  was actually extracted, and rejects uninstallers, updaters, crash handlers and bundled
  redistributables.
- `InstallationService`: orchestrates the sequence, refuses any plan where `CanProceed`
  is false, and rolls the installation folder back on any failure.
- `InstalledAppStore`: manifests as JSON, written through a temporary file and moved into
  place; an unreadable library is set aside rather than deleted.
- `LaunchService`: re-confirms at launch that the executable is the recorded one, still
  exists, and is inside RepoDeck's `Apps` folder. Never elevates.
- Details page install flow: Install shows the plan and stops; a confirmation step
  precedes any download; live progress with a working Cancel; then Run, Open folder and
  Uninstall. A registered application whose program has gone offers Repair.
- Installed page: every installation with version, install date, last run, size, status,
  and Run / Open folder / View repository / Uninstall.
- 371 unit tests, all offline.

## Verification performed

- `dotnet build` - clean, 0 errors, 0 warnings.
- `dotnet test` - 371 passed, 0 failed.
- **A real install against live GitHub**: `sharkdp/bat` v0.26.1.
  - Plan selected `bat-v0.26.1-x86_64-pc-windows-msvc.zip` (3.4 MB), portable archive.
  - Downloaded with real progress reporting, extracted to 10 files.
  - Executable correctly identified as `bat.exe` inside the archive's nested versioned
    folder - the common case the locator exists to handle.
  - SHA-256 recorded; every extracted file confirmed inside RepoDeck's `Apps` folder.
  - Uninstall removed the folder completely.
  - **The downloaded binary was not executed.** The check asserted the launcher considered
    it runnable and deliberately stopped there.
  - Run in a temporary root and cleaned up, so nothing was left on the machine.
- Application launched on Windows 10 x64; clean startup log.
- Confirmed by inspection that there are exactly three `Process.Start` calls in the
  codebase: the browser opener (http/https only), the folder opener (RepoDeck's own
  directories), and `LaunchService`. No `runas`, no elevation request anywhere.

## Known problems

1. **Update checking is not implemented.** The Installed page states this rather than
   offering a button that does nothing. It needs version comparison, which is the first
   task of the next milestone.
2. **The Downloads page is still a placeholder.** Download progress appears on the details
   page during an install, which covers the need, but there is no history of past
   downloads. Deliberately deprioritised below the install pipeline.
3. **Favorites are still not implemented.** Deferred twice now; it should either be built
   or dropped from the plan.
4. **Only the first-level archive layout is understood.** An archive that nests the
   program two or more folders deep still resolves, but the ranking penalty for depth is
   a heuristic and could pick wrongly in an unusual layout.
5. **`.7z`, `.tar.xz` and `.dmg` are recognised by the analyzer but cannot be extracted.**
   Only ZIP and tar.gz are implemented; a plan naming another format is refused at the
   point of extraction with a readable message.
6. **No integrity check against a publisher-provided checksum.** RepoDeck records the
   SHA-256 it computed, but does not compare it to a `.sha256` asset when one exists.
   That is a genuine gap and worth closing early in the next milestone.
7. **Uninstall does not ask for confirmation.** It only ever deletes inside RepoDeck's own
   folder, but a confirmation step would still be better.
8. **The GUI was verified by launching it and by testing every ViewModel behind it**, not
   by visually reviewing each rendered panel at several window sizes.
9. **Earlier classification weaknesses remain**: a UI framework's own repository still
   reads as a desktop application, and library repositories often land on "not
   determined".

## Next task

**Milestone 4 - Updates and trust.**

1. Version comparison, handling the tag spellings real projects use (`v1.2.3`, `1.2.3`,
   `release-1.2.3`, date stamps), with tests over real examples.
2. Check update per application, and an update-all across the library, reusing the
   existing analysis and plan pipeline rather than a second path.
3. Verify downloads against a publisher-provided checksum asset when one is published,
   and say plainly when none is.
4. Confirmation before uninstall.
5. A real Downloads page with history and cleanup of the Downloads folder.
6. Favorites, or a decision to drop them.
7. `.7z` and `.tar.xz` extraction, if repositories worth installing actually use them.

Source builds remain out of scope. Release-based installation should be boring and
reliable before dependency managers enter the picture.

## Milestone 1 self-audit (2026-09-16)

Audited against the code, not against this document. Committed as
`fix: harden milestone 1 foundation`.

| # | Finding | Severity | Fix |
|---|---|---|---|
| 1 | A superseded search still ran its `catch`/`finally`, writing `IsBusy`, `ResultSummary` and `ErrorMessage` over the newer search's state. | Real | Generation counter; only the newest execution may write shared state. |
| 2 | `LoadMoreAsync` took no `CancellationToken`, so paging could not be cancelled and page 2 of an old query could append to a new result set. | Real | Takes a token, guarded by the same counter. |
| 3 | Opening a second repository left the first details page's four GitHub requests running. | Real | Cancelled on navigation, back and section change. |
| 4 | `RateLimitChanged` is raised on whichever thread finished the request; handlers updated bound properties directly. | Latent | `ConfigureAwait(false)` in the service layer; handlers marshal via `IUiDispatcher`. |
| 5 | README regexes had no match timeout and no input cap, on content from a stranger. | Real | 250 ms match timeout per pattern, 512 KB input cap. |
| 6 | Milestone 1's reported build command was wrong: .NET 10 generated `RepoDeck.slnx`. | Documentation | Corrected; the command is plain `dotnet build`. |

## Milestone 2 live-testing findings

Running real repositories through the analyzer found two genuine bugs, fixed as general
rules rather than special cases: project types were ranked by detection order rather than
proximity to the repository root (so `bat` reported as ".NET" and `shotcut` as
"Node.js"), and any archive counted as evidence of a runnable program (so a header-only
C++ library was called a desktop application).

## Architecture decisions

Recorded in `ARCHITECTURE.md`. In short: discover, understand, inspect and execute stay
separate; the installer re-decides nothing; every write and delete is confined to
RepoDeck's own folder and re-checked at the point of use; uncertainty is a type rather
than a string; and RepoDeck never renders a "safe" verdict.
