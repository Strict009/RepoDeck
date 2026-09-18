# RepoDeck status

_Last updated: 2026-09-17_

## Current milestone

**Discover and Quick Look polish.** Complete and verified. Stopping here for review before
Milestone 4.

The established visual style is unchanged. This pass reordered what Quick Look says,
made the picture strip work, gave projects without pictures something honest to show, and
added two local reasoning layers that cost nothing.

## Completed in the polish pass

### Quick Look hierarchy

- The top of the panel is now the six things somebody deciding needs: name, one sentence
  of purpose, works on this PC, setup level, download size, and the Install or Run button.
- Everything RepoDeck reasoned from moved behind one disclosure, **Why does RepoDeck think
  this?**, grouped by the question it answers: what kind of project this is, will it work
  on this PC, how much setup, can RepoDeck install it, what is in the release, and what
  RepoDeck could not work out.
- **Nothing was removed.** Filenames, architectures, release contents and the analyzer's
  own uncertainties are all still there. They moved; they did not shrink.

### Picture strip

- Thumbnails are selectable and promote themselves to the hero image.
- Selection never refetches: each tile owns its bitmap for the life of the panel, so
  promoting one swaps a reference.
- The strip is a `ListBox` bound to the hero rather than a row of buttons, so arrow keys
  move between pictures and focus is visible without a pointer. The selected thumbnail is
  outlined rather than tinted, because at 74px a tint is invisible.
- A hero with no second picture shows no strip: one picture is not a gallery.

### Artwork for projects with no pictures

- `KindArtwork` draws a plain geometric mark for the kind of thing a project appears to
  be - a window, a prompt, a controller, a level meter, a film frame, a cog, angle
  brackets, stacked plates - over the same faint grid as before.
- It is RepoDeck's own artwork and is captioned with the kind. **Nothing invents a
  screenshot, a logo or a brand**: the marks are generic and identical for every project
  of a kind. An unknown kind still falls back to the initial, because guessing a picture
  would be worse than admitting there isn't one.

### Classification

- `ProjectKindClassifier` answers with one of nine kinds from a search result alone.
  Evidence is scored rather than matched first-wins, tags outweigh words, and a near-tie
  says so instead of picking a winner quietly.
- Reading material is given no kind at all rather than being called a program.
- The analyzer refines it once it has looked inside, except that finding "a desktop
  application" confirms rather than replaces a subject like audio or video.

### Relevance

- `RelevanceScorer` answers one question: how likely is this result to be what the user
  meant? It is not a quality, trust, safety or popularity rating, and **stars are
  deliberately not an input** - there is a test asserting an obscure project and a famous
  one with identical text score the same.
- Signals: name match strength, tag match, project kind, installability, archived, and
  whether it looks like reading material. All free.
- **A thirty-result search still makes exactly one request.** A test asserts zero
  repository, README, release and tree requests during ranking.
- Adjustments are bounded around a baseline of 50, so results shift but are never buried.
  The score is never displayed - a number beside a project would read as a verdict.

### Apps and Everything

- Replaces the "Programs only" checkbox, which read as a filter that throws things away.
- Apps orders by relevance and sets aside only the clearest cases: a library RepoDeck is
  `Likely` or `Confirmed` about, or a score of 30 or below. It says how many and how to
  get them back.
- **A result RepoDeck merely could not classify is shown in both modes.** Uncertainty is
  not a verdict.
- Everything leaves GitHub's own order alone. The choice is remembered.

## Defects found and fixed by this work

1. **A Play Store badge was being used as a project's card artwork and hero image.** Three
   separate misses, each found by looking at the running application:
   - `art/google_play_badge.png` - the word list was written with hyphens and the file used
     underscores. Separators are now normalised before matching.
   - `developer.android.com/images/brand/en_generic_rgb_wo_60.png` - Google's Play badge on
     a vendor domain, under a filename that says nothing about stores, in a folder called
     `/brand/`, so it was classified as the project's own **logo**. Now matched by host and
     by the vendor filenames.
2. **Substring matching classified a command-line tool as an emulator**, because "from"
   contains "rom". Single words are now matched as whole words.
3. **The side panel overlaid the results at widths where it could have docked.** The
   threshold now guarantees a docked panel leaves room for a whole card column.

## Verification performed

- `dotnet build` - clean, 0 errors, 0 warnings.
- `dotnet test` - **746 passed, 0 failed** (up from 654; 92 new tests).
- Quick Look staleness and cancellation are now covered by 19 tests, including: a
  superseded look that cannot write into a panel that has moved on, cancellation leaving no
  pictures behind, a cancelled look never reported as a failure, eight rapid selections
  leaving the last showing, and an abandoned look not writing its verdict into the wrong
  card.
- **Driven on screen at 1600px, 1400px, 1150px and 840px.** Three, two and one column
  confirmed; the panel docks at 1150 and overlays at 840; Card and Compact both exercised;
  the reasoning disclosure opened and its contents read.

## Known problems

1. **Badge rejection is a blocklist.** A novel store or vendor badge host will get through.
   The failure is visible rather than silent - a card shows somebody else's logo, which is
   obvious on sight - but it is not solved in general.
2. **Quick Look costs GitHub requests.** Selecting a result spends a README request, a
   releases request and a file listing. Arrowing quickly down thirty results will exhaust
   an unauthenticated allowance. Requests are cancelled when the selection moves on and the
   cache absorbs revisits, but there is no debounce yet.
3. **Relevance works from a one-line description and a handful of tags.** It is wrong
   sometimes. The interface is built so that being wrong is cheap: ordering shifts, nothing
   uncertain is hidden, and every verdict can be opened and read.
4. **The card grid still shows drawn artwork for most results.** Real screenshots appear
   only for projects Quick Look has looked at.
5. **Compact view has no column headers and cannot be sorted.** It is a denser list.
6. **Light theme is complete but untested in practice.** Every judgement was made in Dark.
7. **Image decoding has no automated test.** It needs Avalonia's render platform, which is
   unreliable under xunit. The loader's refusals and limits are tested deterministically.
8. **Search is GitHub's search.** There is no semantic or AI search and RepoDeck does not
   claim any; the relevance layer reorders what GitHub returned and nothing more.
9. **No disk cache for images**; they are cached in memory for the session only.
10. Earlier weaknesses remain: update checking is unimplemented, Favorites deferred.

## Next task

**Milestone 4 - Lifecycle and updates.** Not started.

1. Version comparison across the tag spellings real projects use.
2. Check update per application, and update-all, reusing the existing plan pipeline.
3. Verify downloads against a publisher-provided checksum asset when one is published.
4. A real Downloads history, beyond the current preserved-assets list.
5. Favorites, or a decision to drop them.

Source builds remain out of scope.

## Earlier milestones

### Discover UX pass - an application browser

Results became cards about programs rather than rows about repositories. A three-column
responsive grid, a card hierarchy leading with the name and a picture, five installability
states each carrying evidence, INSTALL on the card once a plan exists, Card and Compact
views, and the Quick Look side panel. GitHub's generated preview card was demoted below
every genuine image. Four defects were found by running it: raw HTML shown as a
description, markdown table markup in summaries, store badges adopted as screenshots, and
a card action that did nothing when the card was already selected.

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
