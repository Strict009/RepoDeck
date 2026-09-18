# RepoDeck status

_Last updated: 2026-09-17_

## Current milestone

**Finishing the experience.** Complete and verified, with one gap noted below. Stopping
here for review before Milestone 4.

Onboarding, real browsing, a Why? beside every verdict, a confirmation worth the name, and
an installation you can watch happen.

## Completed

### First-run onboarding

- A welcome takes the whole window on a first run: what RepoDeck can do, what it will
  never do, and the one thing it has to admit - that it cannot tell anyone whether
  software is safe, and nothing it shows is a recommendation.
- The four "will never" promises are the Milestone 3 boundaries in the second person, and
  a test holds them to it. If the installer stops keeping one, the welcome becomes a lie
  on the first screen and the suite says so.
- Then one question - Apps or Everything - with a sensible default already chosen.
- Skipping is allowed, gives the same defaults, and the welcome does not come back.

### Categories and collections

- Nine categories: Utilities, Media, Games & Emulation, Graphics, File Tools, Networking,
  Privacy, Productivity, Developer Tools.
- Six collections: Popular on GitHub, Recently updated, Portable apps, No installation
  needed, Small & useful, and Weird & useful.
- All of them are searches; collections also set the sort and filters, because "recently
  updated" is a question about ordering rather than subject.
- Each description says what it actually asks GitHub for. "Popular on GitHub" says
  outright that it is GitHub's own number and not a recommendation, and a test asserts no
  collection describes itself as best, top, trusted or safe.

### Why? everywhere

- A **WHY?** button sits beside every verdict in Quick Look. Pressing it opens the
  reasoning at the group that answers that question, highlighted.
- The questions the buttons point at are constants shared with the code that builds the
  groups, so a button can never point at a question nothing answers.
- Every group leads with the verdict itself, so a WHY? that opened an empty panel is
  impossible even when the analysis found nothing else to say.

### The confirmation

- The name, version, exact file and size, and whose project it is.
- **What RepoDeck will do**, numbered and in order, built from the plan.
- **What RepoDeck will not do**, ruled out by name: run an installer or script, ask for
  administrator access, change a Windows setting, or start the program until asked.
- The full destination path.
- Tests assert nothing in the "will" list describes running anything, and nothing anywhere
  in it claims the software is safe.

### Installing, as four stages

- DOWNLOAD, VERIFY, EXTRACT, REGISTER shown together from the start, each with a status
  light, a segmented meter and a word.
- Only Download has a real percentage. The others report happening and then done, rather
  than animating through a number RepoDeck does not have.
- Reaching a later stage marks earlier ones done; a failure marks nothing done that was
  not done.

### Smaller things

- Compact rows use the drawn kind mark rather than a letter. At 40px a waveform or a
  window reads as a silhouette where a letter says nothing.
- `Humanize.FileSizePrecise` for live byte counters, so a download does not sit on "18 MB"
  and then jump.

## Defects found and fixed by this work

1. **The analysis line read "Preparing installation plan..." above a finished plan.**
   `Progress<T>` marshals through the synchronisation context, so a report could be
   delivered after the analysis had finished and the last one won. An existing test
   asserted the status was cleared and passed, because in tests there is no synchronisation
   context to delay the report. Guarded with a flag.
2. **The "Will it work on this PC?" evidence group could be missing**, which would have
   made that WHY? button open nothing. Every group a button points at now leads with the
   verdict itself.

## Verification performed

- `dotnet build` - clean, 0 errors, 0 warnings.
- `dotnet test` - **798 passed, 0 failed** (up from 746; 52 new tests).
- **Driven on screen**: the first-run welcome and its second step, finishing into the
  landing page with all nine categories and six collections, the WHY? button opening and
  highlighting the compatibility evidence, and the full confirmation panel for a real
  project including both lists and the destination path.
- The preferences file was deleted first so the first run was genuinely a first run.

## Known problems

1. **The four-stage install display has not been photographed against a live download.**
   Its logic is covered by eleven tests - stage ordering, completion, failure, the byte
   counter, and that no stage invents a percentage - and the view compiles against it with
   compiled bindings, but I have not watched it run. Doing so meant either downloading
   114 MB of somebody else's software onto this machine unasked, or removing an
   application the user already had installed, and neither seemed mine to do. It is the
   one thing in this milestone I cannot say I have seen working.
2. **RepoDeck cannot yet find itself.** Searching for "RepoDeck" and having it correctly
   report "works on this PC / ready to install" needs a public GitHub release to exist
   first. Worth doing as an end-to-end test of the whole product the moment there is one.
3. **Badge rejection is a blocklist.** A novel store or vendor badge host will get through.
4. **Quick Look costs GitHub requests.** Selecting a result spends a README request, a
   releases request and a file listing. Arrowing quickly down thirty results will exhaust
   an unauthenticated allowance. There is no debounce yet.
5. **Relevance works from a one-line description and a handful of tags.** It is wrong
   sometimes. Being wrong is cheap: ordering shifts, nothing uncertain is hidden, and every
   verdict can be opened and read.
6. **Collections are searches, not curation.** "Weird & useful" will return some rubbish.
   That is inherent to doing this without a hand-maintained catalogue, and the descriptions
   say so.
7. **Compact view has no column headers and cannot be sorted.**
8. **Light theme is complete but untested in practice.** Every judgement was made in Dark.
9. **Image decoding has no automated test.** It needs Avalonia's render platform, which is
   unreliable under xunit.
10. **No disk cache for images**; they are cached in memory for the session only.
11. Earlier weaknesses remain: update checking is unimplemented, Favorites deferred.

## Next task

**Milestone 4 - Lifecycle and updates.** Not started.

1. Version comparison across the tag spellings real projects use.
2. Check update per application, and update-all, reusing the existing plan pipeline.
3. Verify downloads against a publisher-provided checksum asset when one is published.
4. A real Downloads history, beyond the current preserved-assets list.
5. Favorites, or a decision to drop them.

Source builds remain out of scope.

## Earlier milestones

### Discover and Quick Look polish

Quick Look was reordered so the top is the six things somebody deciding needs, with all
the evidence moved behind one disclosure grouped by question. Thumbnails became selectable
and promote themselves to the hero without refetching. `KindArtwork` replaced letter tiles
with drawn marks for nine kinds of project. `ProjectKindClassifier` and `RelevanceScorer`
added two local, deterministic, request-free reasoning layers, and a test asserts a
thirty-result search still makes exactly one request. Three defects were found by running
it: a Play Store badge used as card artwork (twice), "from" matching "rom", and the panel
overlaying where it could have docked.

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
