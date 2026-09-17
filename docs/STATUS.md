# RepoDeck status

_Last updated: 2026-09-17_

## Current milestone

**Milestone 3.5 - Visual discovery and accessibility.** Complete and verified.

RepoDeck is now built for someone who wants useful software and does not need to know what
a repository, a release asset or an architecture is. The technical truth is all still
here; it sits one level in rather than in the way.

## Completed in Milestone 3.5

- `RepositoryMediaService`: finds screenshots, logos and preview images from the README,
  the repository file listing and GitHub's social preview card - **at no additional API
  cost**, because the first two are already fetched by the analyzer and the third is a
  predictable address.
- `MediaRanker`: pure classification and ranking. Refuses badge hosts, sponsorship
  buttons, workflow status images, SVGs and anything without an image extension; ranks
  screenshots above logos above the social preview.
- `ImageLoader`: https only, capped redirects, content-type check, byte ceiling enforced
  while streaming, decode inside a try, downscale on load, in-memory cache. A failed image
  is never a user-visible error.
- Visual Discover cards: picture, friendly name, plain-English purpose, setup level,
  platform hint. Owner slug, language and update date demoted to small print; stars and
  licence moved to Details.
- Attractive fallback tiles: a stable colour derived from the project name plus its
  initial, so a result with no imagery still looks deliberate.
- Lazy image loading, cancelled when a new search replaces the results.
- `SetupDifficultyEvaluator`: Easy, SomeSetup, Advanced, DeveloperFocused, Unknown - with
  reasons, and deliberately blind to popularity.
- `FriendlyNaming`: "obs-studio" reads as "Obs Studio"; "ShareX" is left alone; known
  abbreviations are not mangled into "Ui" and "Cli".
- Details page restructured to answer four questions before any GitHub vocabulary:
  what is this, what can I do with it, will it work on this PC, can RepoDeck install it.
  Then pictures. Then the plan. Everything technical behind collapsed disclosures.
- Search prompts phrased by purpose: video editor, send files, music player, screen
  recorder, retro games, duplicate files.
- 506 tests.

## Verification performed

- `dotnet build` - clean, 0 errors, 0 warnings.
- `dotnet test` - 506 passed, 0 failed.
- **Live media discovery** against ShareX, shotcut, bat and localsend: real screenshots
  found for three of the four, the social preview used as fallback for the fourth, and
  **no badge survived ranking in any of them**.
- Every chosen image fetched successfully over the network with a correct image content
  type and verified image magic numbers.
- Image decoding verified for real under Avalonia's headless platform: the three live
  images decode and downscale. (A plain test process has no render platform, which is why
  `Avalonia.Headless` was added as a test-only dependency - without it, decoding cannot be
  exercised at all.)
- **Live setup classification**: ShareX "Needs some setup" (installer), bat "Ready to use"
  (portable build), nlohmann/json and FFmpeg "For advanced users" (would need compiling).
- Application launched on Windows 10 x64, clean startup log.

## Known problems

1. **Card pictures are the social preview only.** A search result carries no README or
   file listing, so cards show GitHub's preview card rather than a real screenshot. Real
   screenshots appear when a project is opened. Fetching more per card would cost one API
   request each and is not worth the rate limit.
2. **The exact pixel dimensions reported under the headless test platform are not
   meaningful** - it reports the requested width rather than doing real raster work. The
   decode itself is genuinely exercised; aspect ratio is handled by `UniformToFill` at
   render time.
3. **Search is GitHub's search.** Typing "video editor" searches those words. There is no
   semantic or AI search, and RepoDeck does not claim any. The application-likelihood
   filter is the only prioritisation, and it is metadata-only.
4. **Setup level on cards is provisional** and capped at `Possible` confidence, because a
   search result carries no release information. The authoritative answer needs the
   details page.
5. **GIF screenshots are penalised** for size, so an animated demo may lose to a static
   image even when the animation is more useful.
6. **No disk cache for images.** They are cached in memory for the session only, so
   restarting refetches. Adequate, and worth revisiting if it becomes noticeable.
7. Earlier weaknesses remain: update checking is unimplemented, Favorites deferred, a UI
   framework's own repository still reads as a desktop application.

## Next task

**Milestone 4 - Lifecycle and updates.** Not started.

1. Version comparison across the tag spellings real projects use.
2. Check update per application, and update-all, reusing the existing plan pipeline.
3. Verify downloads against a publisher-provided checksum asset when one is published.
4. A real Downloads history, beyond the current preserved-assets list.
5. Favorites, or a decision to drop them.

Source builds remain out of scope.

## Earlier milestones

### Milestone 3 - install, register, run

Transactional installation through `Apps/.staging/<guid>`: extract, validate, identify the
program, then promote. If it never promotes and registers a manifest, it is not installed.
A failed reinstall leaves the working version in place.

`Installed` means RepoDeck established something runnable. An archive with nothing runnable
inside is recorded as `Downloaded` with the reason stated; several plausible executables
produce `AwaitingExecutableChoice` with Run disabled until the user settles it. Windows
installers and Linux packages are fetched and handed over, never run.

The Run safety audit found one real defect: launches used `UseShellExecute = true`, which
is shell interpretation. Fixed. Elevation is never requested and no arguments are ever
passed.

### Milestone 1 self-audit (2026-09-16)

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

### Milestone 2 live-testing findings

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
