# RepoDeck status

_Last updated: 2026-09-17_

## Current milestone

**Visual identity pass - retro digital.** Complete and verified. Stopping here for review
before Milestone 4.

RepoDeck now looks like a piece of software rather than a generic form. The identity is
drawn from the late-1990s media-player and file-sharing era - dark industrial chassis,
dense panels, one acid accent, segmented meters, small capitalised labels - interpreted
as a modern application rather than reproduced as a skin. No behaviour, security boundary
or wording of a verdict was changed to accommodate it.

## Completed in the visual identity pass

- **One token system.** `App.axaml` holds every colour, radius, spacing and metric as a
  themed resource. Dark is the intended appearance; a complete Light dictionary is kept
  so switching variant never produces an unusable window. A test fails the build if any
  view reintroduces a literal hex colour.
- **A rationed accent.** Acid lime (`#B3F03C`) means selection, active state, ready,
  compatible, progress, primary action and the RepoDeck mark - and nothing else.
- **Squared-off metrics.** Radii of 0-6px, not 12px web cards. Controls are 34px, large
  controls 44px.
- **Branded shell.** A 232px control strip: wordmark with an accent bar, an inset state
  panel, navigation with a leading accent border on the selected item, and a SYSTEM panel
  carrying a connection light, the GitHub allowance meter and the detected platform.
- **Status strip** along the bottom: activity light and word, current context, installed
  count, and any allowance warning with the exact numbers in a tooltip.
- **`SegmentedMeter`**, a custom-drawn bar meter used for the GitHub allowance and for
  determinate install progress. Where progress is genuinely unknown, an indeterminate bar
  is shown instead - the meter never invents a percentage.
- **Discover landing.** Nine category tiles and a row of plain-English prompts, so an
  empty search box is not the first thing a newcomer meets. Each tile runs an ordinary
  search; there is no curated catalogue and RepoDeck does not imply one.
- **Card grid.** `RepositoryCardView` as a real view: a 150px media band, a designed
  fallback (stable colour from the name, faint grid, initial), setup level as a chip with
  a status light, an ABANDONED marker, and a footer bar carrying the owner slug, language
  and the two actions. The grid reflows from three columns to two to one.
- **Details hero band**, carrying the best image found, the friendly title, setup level
  and the compatibility answer with a status light.
- **Button hierarchy** made explicit: one primary action per area, secondary for
  everything ordinary, `danger` for anything that removes files, link style for
  navigation away.
- **Accessibility kept.** Every status light sits beside a word, so no state is carried
  by colour alone; focus is visible on every interactive control; the meter is paired
  with a written allowance label.

## Verification performed

- `dotnet build` - clean, 0 errors, 0 warnings.
- `dotnet test` - **552 passed, 0 failed** (up from 507; 45 new tests).
- **The application was launched and driven**, not merely started: the Discover landing,
  a live "video editor" search returning thirty cards in a three-column grid, and a
  details page with a real README screenshot in the hero band were all captured on
  screen and inspected.
- Two defects were found by this work and fixed:
  - The allowance tooltip described a **future** reset with the past-tense formatter,
    producing "Resets 43 minutes ago" for something that had not happened yet. A
    forward-looking `Humanize.TimeUntil` was added and is tested across the range.
  - The status strip opened showing a hard-coded "Ready" rather than the page actually
    on screen. It is now initialised from the selected navigation item.
- The segmented meter's rule was extracted as a pure function and tested for clamping,
  negative values, over-range values, a zero maximum, zero segments and NaN.

## Known problems

1. **The identity is only enforced by convention plus one test.** The test catches
   literal hex colours in views; it does not catch a hard-coded radius or margin that
   should have been a token.
2. **Light theme is untested in practice.** The dictionary is complete and the
   application is usable in it, but every screenshot and every judgement in this pass was
   made against Dark.
3. **Card pictures are the social preview only.** A search result carries no README or
   file listing, so cards show GitHub's preview card rather than a real screenshot. Real
   screenshots appear when a project is opened. Fetching more per card would cost one API
   request each and is not worth the rate limit. GitHub's card is white, which sits
   awkwardly in a dark grid, but it carries real information and a blank tile does not.
4. **Image decoding has no automated test.** It needs Avalonia's render platform, which
   is unreliable under xunit without the dedicated Avalonia.Headless.XUnit integration.
   The loader's refusals and limits are tested deterministically; the decode itself is
   verified by running the application. Worth revisiting with the proper integration.
5. **Search is GitHub's search.** Typing "video editor" searches those words. There is no
   semantic or AI search, and RepoDeck does not claim any. The application-likelihood
   filter is the only prioritisation, and it is metadata-only.
6. **Setup level on cards is provisional** and capped at `Possible` confidence, because a
   search result carries no release information. The authoritative answer needs the
   details page.
7. **GIF screenshots are penalised** for size, so an animated demo may lose to a static
   image even when the animation is more useful.
8. **No disk cache for images.** They are cached in memory for the session only, so
   restarting refetches. Adequate, and worth revisiting if it becomes noticeable.
9. Earlier weaknesses remain: update checking is unimplemented, Favorites deferred, a UI
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
