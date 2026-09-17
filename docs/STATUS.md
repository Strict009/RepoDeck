# RepoDeck status

_Last updated: 2026-09-17_

## Current milestone

**Discover UX refinement - an application browser, not a repository browser.** Complete
and verified. Stopping here for review before Milestone 4.

The visual direction from the previous pass is unchanged. This milestone changed what the
results say and offer, not how they are styled.

## Completed in the Discover UX pass

- **Three-column grid.** `ResponsiveCardsPanel` caps the column count at three and picks
  it from the width available, falling to two and then one. Cards gained roughly a third
  of their width, which is what makes the media and the description worth having.
- **Media that shows the project.** GitHub's generated preview card now ranks below every
  genuine image. The card grid asks for the project's own artwork and falls back to its
  designed tile, because at card size the generated card is unreadable text presented as
  though it were a screenshot. Quick Look, which has room to show it legibly, still uses
  it when there is nothing else.
- **App-store buttons rejected** alongside build badges and sponsor buttons. A "Get it on
  F-Droid" banner was being adopted as a project's screenshot and filling the card.
- **Card hierarchy reordered**: name, picture, purpose, platform, setup effort, what
  RepoDeck can do about it, action. Owner slug and language demoted to one line of small
  print; stars moved off the card entirely.
- **Five installability states** - Ready to install, Needs setup, Developer focused, Not
  compatible, Unknown - each carrying its evidence, and each carrying the sentence saying
  it is not a safety judgement. A metadata answer never exceeds `Possible` confidence and
  never claims "Ready to install".
- **INSTALL on the card** once a plan exists and can proceed. It opens the plan and its
  confirmation step; nothing is downloaded until the user confirms.
- **`SOURCE` replaces `GitHub`** and is styled as a fourth, quiet level below secondary.
  DETAILS and INSTALL are the dominant actions on a card.
- **Card and Compact views**, remembered between runs in `preferences.json`. Compact fits
  about eleven results where Card fits four.
- **Quick Look**, a side panel that answers the same four questions as the details page
  without leaving the search. Docks beside the results where there is room and overlays
  them where there is not.
- **Terminology**: "projects found" rather than "matching repositories"; "Nothing found"
  rather than "No repositories matched". GitHub's vocabulary stays under Technical Details.
- **Restrained interaction feedback**: status LEDs beside every verdict, a segmented
  Card/Compact switch that is inset and lit on the active half, inset wells for panels the
  interface reads from, hover and pressed states on every control level. No scanlines, no
  glow, no CRT effects.

## Four defects found and fixed by this work

1. **Raw HTML shown to the user as a description.** The tag stripper was bounded at 200
   characters and a real HelloGitHub badge tag runs past 230, so the whole tag - URL,
   inline styles and all - was presented as the answer to "what is this?". Seen on screen,
   not inferred.
2. **Markdown table markup in plain-English summaries.** A donation table's header and its
   row of dashes were being joined onto the surrounding prose.
3. **Store badges adopted as screenshots**, as above.
4. **The card's action did nothing when the card was already selected**, because it
   assigned an unchanged selection.

## Verification performed

- `dotnet build` - clean, 0 errors, 0 warnings.
- `dotnet test` - **654 passed, 0 failed** (up from 552; 102 new tests).
- **The application was launched and driven at three widths** - 1600px, 1120px and 820px -
  and the grid was confirmed at three, two and one column. Compact view, the Card/Compact
  switch, keyboard navigation through the results and the panel following the selection
  were all exercised on screen.
- **The install route was followed end to end** on a real project: the card showed INSTALL
  once the plan existed, pressing it opened the details page at the installation plan, and
  the plan showed the asset name, its size, the destination inside RepoDeck's own folder,
  the sentence "RepoDeck will not run what it downloads", and **Yes, install it / Not
  now**. The download itself was deliberately not started.
- **The Quick Look staleness guard is tested against a case that genuinely needs it**:
  analysis already handed off, which a cancellation token cannot stop. The test was
  confirmed to fail with the guard removed and to pass with it restored.

## Known problems

1. **Quick Look costs GitHub requests.** Selecting a result spends a README request, a
   releases request and a file listing. Arrowing quickly down a list of thirty results
   will exhaust an unauthenticated allowance. The requests are cancelled when the
   selection moves on and the cache absorbs revisits, but there is no debounce yet, and
   that is the first thing to add if it becomes a nuisance.
2. **The card grid still shows designed tiles for most results.** Real artwork appears
   only for projects Quick Look has looked at. This is the honest position - a search
   result carries no README - but a first page of results is still mostly coloured tiles.
3. **Compact view has no column headers and cannot be sorted** by its own columns. It is a
   denser list, not a table.
4. **The identity is enforced by convention plus one test.** That test catches literal hex
   colours in views; it does not catch a hard-coded radius or margin.
5. **Light theme is complete but untested in practice.** Every judgement was made in Dark.
6. **Image decoding has no automated test.** It needs Avalonia's render platform, which is
   unreliable under xunit without the dedicated integration. The loader's refusals and
   limits are tested deterministically; decoding is verified by running the application.
7. **Search is GitHub's search.** Typing "video editor" searches those words. There is no
   semantic or AI search and RepoDeck does not claim any.
8. **GIF screenshots are penalised** for size, so an animated demo may lose to a static
   image even when the animation is more useful.
9. **No disk cache for images**; they are cached in memory for the session only.
10. Earlier weaknesses remain: update checking is unimplemented, Favorites deferred, a UI
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

### Visual identity pass - retro digital

A deliberate look drawn from the late-1990s media-player and file-sharing era: dark
industrial chassis, dense panels, small capitalised labels, segmented meters, one acid
lime accent. Every colour, radius and metric became a themed token in `App.axaml`, and a
test fails the build if a literal hex value reappears in a view. The shell gained a
branded control strip, a status strip and a custom-drawn `SegmentedMeter` used only where
progress is actually known. Every status light sits beside a word, so no state is carried
by colour alone.

### Milestone 3.5 - visual discovery and plain English

RepoDeck became usable by someone who does not know what a repository, a release asset or
an architecture is. `RepositoryMediaService` finds screenshots, logos and preview images
from material the analyzer already fetched, so pictures cost **no additional API
requests**. `MediaRanker` refuses badge hosts, sponsorship buttons, workflow status
images and SVGs. `ImageLoader` is https-only with capped redirects, a content-type check
and a byte ceiling enforced while streaming; a failed image is never a user-visible
error. Cards lead with a picture, a friendly name and a plain-English purpose;
`SetupDifficultyEvaluator` states how much work a project will be and is deliberately
blind to popularity; the details page answers four questions before using any GitHub
vocabulary.

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
