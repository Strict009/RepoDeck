# RepoDeck architecture

## The split that matters

Four concerns are kept separate from the start, because conflating them is what makes
this kind of application impossible to extend later:

| Concern | Where it lives | Milestone |
|---|---|---|
| **Discover** a repository | `Services/GitHub` | 1 (done) |
| **Understand** a repository | `Services/Explanation`, `Services/Readme` | 1 (done) |
| **Inspect** and plan an install | `Services/Analysis`, `Services/Install` | 2 (done) |
| **Execute** a plan | `Services/Install` | 3 (done) |
| **Present** it approachably | `Services/Media`, `FriendlyNaming`, `SetupDifficultyEvaluator` | 3.5 (done) |

Analysis never downloads. Planning never executes. Execution never decides. Each stage
consumes the previous stage's output as data, so a stage can be replaced without
rewriting its neighbours.

## Layers

```
Views (Avalonia XAML)      no logic, no formatting
  ViewModels               state, commands, all display formatting
    Services               GitHub access, explanation, analysis
      Models               plain data, no behaviour beyond derived values
        Infrastructure     paths, logging, caching, platform, process launching
```

## Decisions and why

**No DI container.** `Infrastructure/AppServices.cs` is a hand-written composition root.
At this size a container adds a dependency and indirection without removing work. All
construction happens in one file, so adopting a container later is a one-file change,
not a rewrite.

**One model layer, not DTO plus domain.** `GitHubRepository` mirrors GitHub's REST shape
and a snake_case naming policy removes the need for per-property attributes. Friendly
aliases (`Stars`, `LastActivity`, `OwnerLogin`) keep wire naming out of the ViewModels.
A second mapping layer would have doubled the model count and bought nothing yet.

**Interfaces only where substitution is real.** `IGitHubClient` (fakes in tests, and the
single seam to GitHub) and `IRepositoryExplanationService` (so an AI-backed explanation
can be added without anything else depending on AI). Everything else is concrete.
`ResponseCache`, `ReadmeParser`, `ApplicationLikelihoodEvaluator` and
`RepositorySignalBuilder` have no plausible second implementation, so they have no
interface.

**Detection logic is pure and static.** `GitHubSearchQueryBuilder`,
`ApplicationLikelihoodEvaluator`, `ReadmeParser`, `RepositorySignalBuilder` and
`Humanize` are pure functions over explicit inputs. That is what makes them testable
without the network, and it is why the test suite runs in well under a second.

**Formatting lives in ViewModels, not converters.** `RepositoryCardViewModel` exposes
`StarsText`, `UpdatedText` and so on as strings. The XAML binds plain strings, the
`Converters` folder stays empty, and the formatting is unit testable.

**Honesty is a type, not a convention.** `Confidence` (`Confirmed` / `Likely` /
`Unknown` / `RequiresInspection`) and `RepositorySignal` with `SignalTone` exist so that
uncertainty is carried through the code rather than being lost in a string. The
information panel has no aggregate score and no "safe" badge on purpose: popularity is
not safety, and RepoDeck says so in the panel itself.

**Every judgement carries reasons.** `ApplicationLikelihood.Reasons` and the signal list
are always populated alongside a verdict, so RepoDeck can answer "why did you conclude
that?" for anything it shows.

## The analysis pipeline (Milestone 2)

```
GitHub
  -> RepositoryAnalyzerService      one tree request + <=3 manifest reads
       ProjectStructureDetector     pure: file listing -> ecosystems, build system
       RefineWithManifests          pure: manifest contents -> frameworks, hints
       ApplicationClassifier        pure: weighted votes -> ApplicationType
       PlatformSupportAnalyzer      pure: -> platform support with confidence
  -> ReleaseAnalyzer                pure: -> ranked assets, one recommendation
       AssetNameParser              pure: file name -> platform, arch, package type
       CompatibilityAnalyzer        pure: asset + MachineProfile -> compatibility
  -> InstallPlanner                 -> InstallPlan
  -> [ the user reads the plan ]
  -> Milestone 3 installer          consumes the plan; does not re-decide anything
```

Only `RepositoryAnalyzerService` touches the network. Everything below it is a pure
function over explicit inputs, which is why the compatibility rules can be tested for a
Linux ARM64 machine from a Windows x64 development box.

### The InstallPlan boundary

`InstallPlan` is the seam between deciding and doing. It is typed, serialisable and
contains no secrets, so it can be logged, stored and shown to the user in full. The
installer that arrives in Milestone 3 consumes a finished plan and re-analyses nothing.
`CanProceed` is false whenever `BlockingIssues` is non-empty, and an analysis that did
not finish - a rate limit, an unreadable file listing - can never become a plan at all.

### Request budget

Opening a repository costs: repository, README, languages, releases, file listing, and
at most three manifest files. The file listing is a single git-tree request covering the
entire repository, so structural classification does not scale with repository size and
nothing is cloned. Manifests are read only when the structure pass said their contents
would change a conclusion. Search results are never deeply analysed - the Discover page
still uses metadata-only heuristics, because thirty cards cannot afford a request each.

### Rules that exist because they were got wrong

- **"darwin" contains "win", and "x86_64" contains "x86".** Compound spellings are
  normalised to canonical tokens before matching, and matching is on whole tokens, never
  loose substrings. macOS is checked before Windows.
- **The marker nearest the root identifies the project.** A `package.json` in a docs
  folder does not make a C++ project a Node project. Types carry the depth of the marker
  that proved them.
- **An unlabelled archive is not evidence of an application.** A header-only library
  shipping its headers in a ZIP is not a desktop program.
- **A source archive can never be recommended** while any compatible binary exists, and
  when only source exists RepoDeck says so rather than offering a download it cannot use.

### Confidence

`Confidence` runs Confirmed, Likely, Possible, Unknown, RequiresInspection, Unsupported.
Only a published build for a platform is ever Confirmed. Application type is capped
below Confirmed on purpose: what a project is *for* is always inferred from how it is
built, never stated by GitHub. `Unsupported` is a positive finding - a Windows-only
toolkit rules other platforms out - and is deliberately distinct from `Unknown`.

### Threading

The service layer uses `ConfigureAwait(false)` throughout, as library code should.
ViewModels marshal onto the UI thread through `IUiDispatcher`, so correctness does not
depend on the service layer happening to capture the UI synchronisation context.


## Executing a plan (Milestone 3)

```
InstallPlan  (produced in Milestone 2, decided nothing further here)
  -> InstallationService
       DownloadService      .part file -> verify size -> rename
       ExtractionService    every entry through ArchivePathGuard
       ExecutableLocator    confirm or correct the plan's prediction
       InstalledAppStore    manifest written as JSON
  -> LaunchService          only on the user pressing Run
```

The installer re-decides nothing. Which asset, which platform, which strategy, where it
goes - all of that was settled during analysis and travels in the plan. If the installer
ever needs to make a judgement, that judgement belongs upstream.

### Rules that are absolute

- **Nothing downloaded is ever executed.** The only launch is `LaunchService`, and only
  when the user presses Run on something already installed.
- **A Windows installer or Linux package is downloaded and left alone.** Running it needs
  elevation and is the user's decision. RepoDeck records where the file is and stops.
- **Elevation is never requested.** `Verb` is never set to `runas`, and the application
  manifest does not ask for administrator rights.
- **Nothing outside `Apps` is written or deleted.** Every path is checked against
  `AppPaths.Apps`, and the check is repeated at the point of deletion rather than trusted
  from earlier, because the cost of being wrong there is somebody's files.
- **A partial download never becomes an installation.** Bytes go to a `.part` file and are
  renamed only after the transfer finished and the byte count matched.
- **A failed install leaves nothing behind.** Any failure after the download rolls the
  installation folder back.

### Zip slip

`ArchivePathGuard` is a pure function, tested on its own, and every archive entry passes
through it before a byte is written. Traversal, absolute paths, drive-rooted paths, UNC
paths and degenerate names are refused; in tar archives, symbolic and hard links are
refused outright as another route out of the destination. Refused entries are recorded in
the extraction result rather than silently skipped, so an archive that tried to escape is
visible afterwards.

### Choosing what to run

The plan predicts executable names before anything is downloaded; `ExecutableLocator`
confirms or corrects that against what was actually extracted. The hard part is not
finding executables but rejecting the wrong ones - uninstallers, updaters, crash handlers
and bundled redistributables all look like executables and none of them is the program.
The locator is pure over a file list, so its ranking is tested without unpacking anything.

### The manifest

`ApplicationManifest` is RepoDeck's own bookkeeping, written as JSON under `Data`. The
Installed page reads only from it, so the library opens instantly, works offline and
costs nothing against the rate limit. The store writes through a temporary file and moves
it into place; a library file that has become unreadable is set aside for inspection and
RepoDeck carries on with an empty list rather than refusing to start.

### The confirmation gate

Pressing Install shows the plan and stops. Nothing is fetched until the user confirms.
That step is not a formality: it is the point at which RepoDeck stops being a browser and
starts writing to the machine.

## Making it approachable (Milestone 3.5)

RepoDeck's user wants useful software and does not need to know what a repository is. The
technical truth is never removed - it moves one level in.

### Pictures, without extra requests

```
RepositoryMediaService
  README markdown      (already fetched by the analyzer)
  file listing         (already fetched by the analyzer, carried on RepositoryAnalysis)
  social preview       (a predictable address; no API call at all)
    -> MediaRanker      pure: classify, reject, order
    -> ImageLoader      fetches only the winner
```

Discovery costs zero additional GitHub requests. A search result gets only the social
preview address, which is one predictable URL per repository and no API call; the full
discovery runs when a project is opened, from data already in hand.

Choosing a picture is almost entirely a rejection problem. A typical README opens with
eight build badges, a sponsor button and a licence shield. `MediaRanker` refuses badge
hosts, sponsorship buttons, workflow status images, SVGs and anything without an image
extension, then ranks screenshots above logos above the social preview. The social
preview is deliberately last: GitHub generates it automatically when the maintainer has
not uploaded one, so it is frequently text on a gradient. Its job is to be the fallback
that is never worse than an empty tile.

One keyword was removed after a test caught it: "example" matched every image in an
`examples/` folder and every URL on `example.com`.

### Untrusted images

Addresses come from READMEs written by strangers and point at hosts RepoDeck knows
nothing about. `ImageLoader` therefore takes https only, caps redirects, checks the
content type, enforces a byte ceiling *while streaming* rather than trusting
Content-Length, decodes inside a try, and downscales on load. A failure is never an error
the user sees: the card shows its fallback tile instead.

Card pictures load after the results appear and are cancelled when a new search starts,
so abandoning a search does not keep paying for images nobody is looking at.

### Setup difficulty

`SetupLevel` is Easy, SomeSetup, Advanced, DeveloperFocused or Unknown, and is
deliberately blind to popularity. A wildly popular project can be Advanced; an obscure one
can be Easy. Conflating difficulty with quality would be the same mistake as treating
stars as a safety signal, and there is a test that holds two identical projects - one with
three stars, one with three hundred thousand - to the same verdict.

There are two entry points because the callers can afford different evidence. A search
result gets metadata only and is capped at `Possible` confidence; an opened project has
been analysed properly and can reach `Likely`.

### Terminology

The interface says App, Version, Download and "Will it work on this PC?". It says
Repository, release tag, asset, architecture and install strategy inside the GitHub
information and Technical details sections, where someone looking for them will find them
and nobody else has to.

### Progressive disclosure

The details page answers four questions before any GitHub vocabulary appears: what is
this, what can I do with it, will it work on this PC, can RepoDeck install it. Then
pictures. Then the installation plan and the factual signals panel. Everything else -
RepoDeck's analysis, every download in the release, GitHub metadata, the README - sits
behind collapsed disclosures. Nothing was deleted to achieve this.

## The design system

RepoDeck has a deliberate visual identity, drawn from the late-1990s media-player and
file-sharing era: a dark industrial chassis, dense panels, small capitalised labels,
segmented meters and a single acid accent. It is an interpretation, not a skin - nothing
here reproduces a particular product's artwork, and none of it changes what the
application does or says.

### Tokens, and nothing else

`App.axaml` holds the entire palette as themed colours, each exposed as a brush, plus
radii, spacing and control metrics. Views reference brushes and metrics; they never
reference a colour. This is enforced: a test fails the build if any view under
`Views/` reintroduces a literal hex value, because a design system that anything can
bypass stops being one within a month.

Both theme variants are complete. Dark is the intended appearance and what every visual
judgement was made against; Light exists so that switching variant never produces an
unusable window.

### The accent is rationed

Acid lime means exactly seven things: selection, active state, ready, compatible,
progress, the primary action in an area, and the RepoDeck mark. It means nothing else. A
window where every border is green communicates nothing at all, and RepoDeck's accent has
to stay legible because it is also how the interface says "this will work on your
computer".

Caution amber, danger red and info blue carry the meanings their names suggest and are
used no more freely.

### State is never colour alone

Every status light in the application sits beside a word. The shell's light is next to
READY or WORKING, the connection light next to the service name, the allowance meter next
to ALLOWANCE OK, ALLOWANCE LOW or ALLOWANCE USED UP, and the compatibility light next to
the sentence stating the verdict. Someone who cannot distinguish lime from amber loses
decoration and no information.

### `SegmentedMeter`

A custom-drawn `Control` rather than a hundred nested borders, so it is cheap enough to
sit anywhere. Its rule - how many bars light for a value - is a pure static function,
tested for clamping, negative input, over-range input, a zero maximum, zero segments and
NaN, without standing up a windowing system.

It is only used where progress is actually known. Where progress is indeterminate the
interface shows an indeterminate bar instead. A meter displaying an invented percentage
would be lying, and lying about progress is the specific thing RepoDeck exists not to do.

### The Discover landing

An empty search box is a poor thing to greet someone with, so the landing offers nine
category tiles and a row of plain-English prompts. Each one runs an ordinary GitHub
search with a query deliberately more specific than its label - searching the word
"Games" alone returns engines and tutorials. There is no curated catalogue behind the
tiles and the interface does not imply one.

### Cards

`RepositoryCardView` is a real view resolved by the `ViewLocator`, not an inline
template, so the card can be laid out properly and reused. It carries a fixed-height
media band, the friendly name, the purpose in plain English, the setup level as a chip
with a status light, and a footer bar holding the owner slug, the language and the two
actions. The grid is a `WrapPanel`, so it reflows from three columns to two to one.

Where no image is available the fallback is designed rather than absent: a stable colour
derived from the project's full name, a faint grid, and the project's initial. The same
project looks the same on every visit, and a page of results with no screenshots still
looks deliberate rather than broken.

### Button hierarchy

One primary action per area. Secondary for everything ordinary. `danger` for anything
that removes files, which is the only styling cue the uninstall and forget flows get in
addition to their explicit confirmation step. Link styling for navigation away from the
application.

## Discover as an application browser

RepoDeck's search results describe programs, not repositories. The distinction drives the
whole page: what leads a card, what a card offers to do, and what vocabulary is allowed
outside the technical sections.

### The order a card is read in

Name, picture, purpose, platform, setup effort, what RepoDeck can do about it, the action.
Owner slug, language and star count come last and small. They are facts about a
repository, and a card is about a program; nothing is removed, it moves to the details
page.

### Three columns, then two, then one

`ResponsiveCardsPanel` picks its column count from the width it is given and caps it at
three. A `WrapPanel` full of fixed-width cards packed four across on a wide monitor, and
four narrow cards have room for a thumbnail and half a sentence each. The rule is a pure
static, tested at every width, and the panel only does arithmetic on the result.

### Card and Compact

Card is the default and will stay the default: someone who does not know what they want is
helped far more by a picture and a sentence than by a dense list. Compact is one row per
project for the opposite person, who already knows the landscape and wants forty results
on screen rather than nine. The same facts in the same order, with the picture reduced to
a tile. The choice is remembered in `preferences.json` under the Data folder; nothing in
that file affects what RepoDeck installs, so an unreadable one silently falls back to the
defaults.

### Installability

`InstallabilityState` is one of five answers - Ready to install, Needs setup, Developer
focused, Not compatible, Unknown - and it is a **capability statement, never a safety
statement**. It says what RepoDeck can do with a project. It says nothing whatever about
whether the software is trustworthy, no part of the interface may present it as though it
did, and every surface that shows a state also carries the sentence saying so.

`InstallabilityEvaluator` has two entry points because the grid and Quick Look know
different amounts. `FromMetadata` answers from a search result and is capped at
`Possible`; it can call a library developer-focused, and otherwise answers Unknown,
because claiming "Ready to install" without having looked at a single release would be
guessing about the one thing a user most wants to rely on. `FromPlan` answers once a plan
exists and is the only one that may permit a direct install action.

A system installer is `NeedsSetup` rather than `ReadyToInstall`. RepoDeck fetches those
and hands them over; that is not installing, by RepoDeck's own definition, and a button
implying otherwise would be a lie.

Every state other than Unknown carries its evidence. A verdict nobody can interrogate is
worth less than no verdict.

### INSTALL on a card does not install

A card offers INSTALL only when a plan exists and can proceed, and pressing it opens the
details page with that plan on screen and its confirmation step ready. Nothing is
downloaded until the user confirms what the plan says. Milestone 3's gate has exactly one
implementation and this is not a second one - the label names where the button takes you.

### Quick Look

Selecting a result fills a side panel with the same analysis the details page runs:
best media, the plain-English explanation, setup effort, whether it runs on this machine,
whether RepoDeck can install it and why, download size, a screenshot strip, and the
technical facts folded away. Comparing six candidates becomes six clicks instead of six
round trips through a full page.

It is guarded by a generation counter **as well as** a cancellation token. The token stops
work that stops to look at it; analysis already handed off finishes regardless, and its
continuation then runs against a panel describing something else entirely. The counter is
what stops those answers landing. There is a test that fails without it.

Where the window is wide enough the panel docks beside the results; below
`PanelDisplayConverter.MinimumDockedContentWidth` it covers them instead. Docking it on a
narrow window left the results about 270px wide, which is not a result. This drives a
two-column grid directly rather than using a `SplitView`, whose overlay mode closes its own
pane on any click in the content area - which fought row selection, so clicking a result
to open the panel closed it in the same gesture.

### Media: the project's own pictures first

GitHub generates a preview card for every repository, and what it contains is the name and
description set in small type on a flat background. It ranks below every genuine image,
and the card grid asks for `PrimaryArtwork` - a screenshot, a logo, a documentation image -
falling back to its own designed tile rather than showing unreadable text as though the
user were expected to read it. Quick Look, which is wide enough to show it legibly, asks
for `Primary` and will use the generated card when there is nothing else.

Store buttons are rejected alongside build badges. "Get it on F-Droid" is a picture of
somebody else's logo, and it was being adopted as a project's screenshot.

A search result carries no README and no file listing, so cards start with their designed
tile. When Quick Look reads the README it often finds a real screenshot, and hands it back
to the card - so the grid improves as someone explores it rather than staying generic.

### What Quick Look shows first

The top of the panel is what somebody deciding actually needs: the name, one sentence
saying what it is for, whether it works on this PC, how much setup it needs, what the
download costs, and the button. Nothing else.

Everything RepoDeck reasoned from - which download it chose, what architecture that is
built for, what the release contained, what it could not establish - sits behind one
disclosure headed **Why does RepoDeck think this?**, grouped by the question it answers.

Nothing was removed to make room. The evidence used to be strewn across the panel as
bullet lists under each answer, which pushed the five things a newcomer needs below the
fold. It moved; it did not shrink.

### The picture strip

Every image worth showing becomes a tile in `Gallery`. The first is the hero and the rest
are the strip, and selecting one promotes it. Each tile owns its bitmap for the life of
the panel, so promoting a thumbnail swaps a reference - it never fetches anything again.
The image loader also caches in memory, so even a genuinely new request would not hit the
network twice, but the point is that selection costs nothing at all.

The strip is a `ListBox` with `SelectedItem` bound to the hero rather than a row of
buttons, which gives arrow-key movement and a visible focus ring for free. The selected
thumbnail is outlined rather than tinted: at 74 pixels a background tint is invisible.

One picture is a hero, not a gallery - the strip only appears when there are at least two.

### Artwork for projects with no pictures

A coloured square with a letter in it says only "this is a thing", and a page of them says
"RepoDeck found nothing". `KindArtwork` draws a plain geometric mark for the kind of thing
the project appears to be: a window for a desktop application, a prompt for a command-line
tool, a controller for a game, a level meter for audio, a film frame for video, a cog for
a utility, angle brackets for a developer tool, stacked plates for a library.

It is RepoDeck's own artwork and must never be mistaken for the project's. Nothing here
invents a screenshot, a logo or a brand: the marks are generic shapes, identical for every
project of a kind, and a chip captions them with the kind rather than presenting them as
the project's own. Where the kind is unknown it falls back to the initial, because
guessing a picture would be worse than admitting there isn't one.

### Rejecting other people's badges

Store and platform buttons are rejected alongside build badges and sponsor buttons, and
this took three passes to get right against real READMEs:

- `f-droid.org/badge/get-it-on.png` - matched by host.
- `art/google_play_badge.png` - missed at first, because the word list was written with
  hyphens and the file used underscores. Separators are now normalised before matching.
- `developer.android.com/images/brand/en_generic_rgb_wo_60.png` - missed twice. It is
  Google's Play badge, served from a vendor domain, under a filename that says nothing
  about stores, in a folder called `/brand/` - so it was classified as the *project's own
  logo* and filled the card. Now matched by host and by the vendor filenames, which do not
  change.

This is a blocklist, and a novel badge host will get through. The failure is visible
rather than silent: a card shows somebody else's logo, which is obvious on sight.

### Vocabulary

"projects found", not "matching repositories". "Nothing found", not "No repositories
matched". APPS and EVERYTHING as a browsing mode, not a "Programs only" checkbox - a
checkbox reads as a filter that throws things away, and this is a choice about what kind
of browsing is happening.

GitHub's own words - repository, release asset, tag, fork - live under Technical Details,
where anyone who wants them can find them and nobody else has to read them.

## Classification and relevance

Two local, deterministic layers sit between GitHub's search results and the grid. Neither
of them costs a request, and neither is a judgement about quality, trust or safety.

### What kind of thing is this?

`ProjectKindClassifier` answers with one of nine kinds - desktop application, command-line
tool, game or emulator, audio, video, utility, developer tool, library, or unknown - from
a search result's name, description, tags and language.

Evidence is scored rather than matched first-wins, because a project called
"video-player" tagged `library` is a library that handles video, and the order its words
happen to appear in should not decide that. Tags weigh more than words, since tags are
chosen deliberately and a description is prose. A near-tie says so ("it could also be a
library") rather than picking a winner and keeping quiet.

Single words are matched as whole words. Substring matching found "rom" inside "from" and
classified a command-line search tool as an emulator.

Reading material - anything tagged `awesome`, `curated`, `tutorial`, `roadmap` and the
like - is given no kind at all. Calling a list of video tools a video program would be
worse than admitting RepoDeck does not know.

The analyzer refines this once it has looked inside, and its answer wins where it has
one - except that finding "a desktop application" *confirms* rather than replaces a
subject like audio or video. "Desktop application" is a shape; "audio" is what the program
is for, and the second is more useful to somebody browsing.

### How likely is this to be what you meant?

`RelevanceScorer` answers one question and no others: given what the user typed, how
likely is this result to be the thing they were looking for?

It is **not** a quality rating, **not** a trust or safety rating, and **not** a popularity
rating. Stars are deliberately not an input, and there is a test that asserts an obscure
project and a famous one with the same name and description score identically. A wildly
popular library is still the wrong answer for somebody who typed "music player", and
treating popularity as relevance is how a search stops surfacing the small useful thing
that does exactly what was asked.

The signals are: how much of the query the name matches (exact, every word, some), whether
the tags match it, what kind of thing the project is, whether RepoDeck has a plan to
install it here, whether it is archived, and whether it looks like reading material.

Everything it reads is already in the search response or computed locally from it, so
ranking thirty results costs nothing. A test asserts that a thirty-result search makes
exactly one request and no repository, README, release or tree requests at all: the
signals must never grow into an N+1 fetch.

The baseline is 50 and adjustments are bounded, so a result can be pushed around but never
buried. GitHub's own ordering encodes text relevance RepoDeck cannot see, and overriding
it wholesale would be arrogant.

Every adjustment records a reason. The score itself is never displayed - a person seeing
"87" beside a project would read it as a verdict on the software, which is exactly what it
is not.

### Apps and Everything

`Apps` orders by relevance and sets aside only the clearest cases: a library RepoDeck is
`Likely` or `Confirmed` about, or something scoring 30 or below. `Everything` leaves
GitHub's order alone and sets nothing aside.

A result RepoDeck merely could not classify is shown in both. "I could not tell what this
is" is not the same as "this is not for you", and a mode the user did not explicitly pick
must not quietly decide it is. When Apps does set something aside it says how many and how
to get them back.

This replaced a "Programs only" checkbox, which read as a filter that throws things away
rather than a choice about what kind of browsing is happening. The choice is remembered.

### The limits of this

The classifier and the scorer both work from a one-line description and a handful of tags.
They are wrong sometimes, and the interface is built so that being wrong is cheap: the
ordering shifts, nothing is hidden that RepoDeck is not sure about, and every verdict can
be opened up and read.

## Finishing the experience

### The front door

A first run shows a welcome instead of the interface. Two steps: what RepoDeck does and
what it will never do, then one question with a sensible default already chosen.

The "will never" half carries the same weight as the "can" half on purpose. The audience
for this application has been trained by twenty years of download sites to expect a
program that fetches software to also install four other things, and saying so plainly at
the front door is worth more than any amount of reassurance later. The four promises are
the Milestone 3 boundaries restated in the second person, and a test holds them to it: if
the installer ever stops keeping one, the welcome becomes a lie on the first screen.

Alongside them sits the one thing that has to be admitted in the same breath - that
RepoDeck cannot tell anybody whether software is safe, and nothing it shows is a
recommendation. A welcome is exactly the wrong place to imply otherwise.

Skipping is allowed and is not punished: the defaults are the ones the second step offers,
and the welcome does not come back. The flag lives with the interface preferences, because
losing it means seeing a welcome screen again rather than anything that matters.

### Why?

Every verdict RepoDeck makes has a **WHY?** beside it. Pressing it opens the reasoning at
the group that answers that particular question, highlighted, rather than dumping six
groups on somebody who wanted one answer.

This is the whole point of keeping evidence rather than conclusions. A beginner can ask why
about the one thing that puzzled them - "why does it think this works on my PC?" - and be
told that the release contains `SharpEmu-win-x64.zip`, that `win` matches Windows, that
`x64` matches their processor, and that the archive contains a runnable Windows
application. They learn the vocabulary by reading answers rather than by having to know it
first.

The questions the buttons point at are constants on the view model, shared with the code
that builds the groups, so a button can never point at a question nothing answers. Every
group leads with the verdict itself, so a WHY? that opened an empty panel - worse than no
button at all - is impossible even when the analysis found nothing else to say.

### The confirmation

The last moment before anything is written is where RepoDeck's argument either holds or
does not. A button saying "Install" beside a size is what every other installer offers.
What is offered here is:

- the name, the version, the exact file and its size, and whose project it is;
- **what RepoDeck will do**, numbered, in order;
- **what RepoDeck will not do**, ruled out by name;
- the full path it will write to.

Both lists are built from the plan rather than from a constant, so they cannot drift away
from what the installer actually does. Tests assert that nothing in the "will" list
describes running anything, and that nothing anywhere in it claims the software is safe.

### Installing, as four stages

A single bar reading "Installing..." tells somebody nothing about what is happening to
their computer. RepoDeck shows all four stages at once from the moment installing begins -
DOWNLOAD, VERIFY, EXTRACT, REGISTER - each with a status light, a segmented meter and a
word. That is the argument for trusting this made visible while it happens, rather than
claimed in a paragraph beforehand.

Only Download has a real percentage. The other three report that they are happening and
then that they are done: a meter animating through a number RepoDeck does not have would
be inventing the one thing progress is for. Reaching a later stage marks the earlier ones
done, because a report from a later stage is proof the earlier ones finished and a stage
left quietly dark would look like something had gone wrong. A failure or a cancellation
marks nothing done that was not done.

The byte counter uses `Humanize.FileSizePrecise` rather than `FileSize`, because the
latter drops decimals above ten and a download would sit on "18 MB" for several seconds
before jumping - which reads as a stall rather than as progress.

### Categories and collections

Nine categories by subject, and six collections that slice the search a different way:
Popular on GitHub, Recently updated, Portable apps, No installation needed, Small &
useful, and Weird & useful.

All of them are searches. Nothing is hand-picked by anybody, and each description says
what it actually asks GitHub for - "Popular on GitHub" says "the most starred, GitHub's
own number, not a recommendation", because the label alone could easily be read as
"RepoDeck recommends", which it is not and must never become. A test asserts that no
collection describes itself with words like "best", "top", "trusted" or "safe".

## Lifecycle and updates (Milestone 4)

Milestone 3 ended when something was installed. Milestone 4 is about what happens
afterwards, and it reuses the decide/do split rather than inventing a second one: an
`UpdatePlan` and a `RepairPlan` are written down and shown before anything is executed,
exactly as an `InstallPlan` is.

### Two comparisons, on purpose

`ReleaseVersion` exposes both `CompareTo` and `CompareOrNull`, and the difference is the
whole point.

`CompareTo` is a total order. It exists so a list of releases can be sorted, and it always
produces an answer. `CompareOrNull` returns `int?`, and null means **RepoDeck cannot defend
an answer**. Every decision the user sees goes through `CompareOrNull`; sorting goes through
`CompareTo`; the two are never swapped.

This came out of a live defect. Semantic versioning says any suffix marks a pre-release, so
`1.0.0` outranks `1.0.0-anything` - which made RepoDeck offer `v0.0.3` as an update to
somebody running `v0.0.3-release.4`. That is a downgrade with the word "update" on the
button, and it is the single worst thing an update checker can do.

The resolution is to be honest about which suffixes carry meaning. A short list of words -
`alpha`, `beta`, `rc`, `pre`, `preview`, `dev`, `nightly`, `snapshot`, `canary`, `insider`,
`experimental`, `test`, `unstable`, `early` - really do mean unfinished, and for those the
semver rule applies, including its identifier-by-identifier ordering so that `beta.10` beats
`beta.9` rather than losing to it alphabetically. Any other suffix is the project's own
business. Two versions whose numbers are equal and whose suffixes differ in an unrecognised
way cannot be ordered, and RepoDeck says so.

The failure mode this creates - `Unknown` where a human could have answered - is the one
worth having. Being slow to notice an update is an annoyance; offering an older version as
a newer one destroys the reason to use RepoDeck at all.

### Choosing which release to offer

When several releases are genuinely newer, the highest numeric version wins. When the
numbers tie and only the suffix differs, the **publication date** decides.

That rule replaced `MaxBy` over the total order, which had picked `v0.0.3-release.3-patch.1`
over `v0.0.3-release.4` because semver ranks an alphanumeric identifier above a numeric one.
The date is evidence about what the project shipped last. The suffix is a naming convention
RepoDeck has no standing to interpret.

### The update transaction

    download -> verify -> assemble in staging -> validate the staged copy
              -> move the live copy aside -> promote -> validate in place -> remove backup

A live installation is never written into. The new version is fully assembled and validated
in `Apps/.staging/<guid>` before the existing one is touched at all, so a bad download, a
corrupt archive or an archive containing nothing runnable fails while the working copy is
still working.

Only then is the live directory **moved** - not deleted - to `Apps/.rollback/<guid>`, and it
stays there until the promoted copy has been validated in its final location. Any failure
after the move restores it.

`UpdateResult` therefore has to distinguish three outcomes rather than two: it succeeded, it
failed and the previous version is back, or it failed and the installation is damaged. Those
are three different sentences for the user, and collapsing them into "it didn't work" would
lose the one piece of information that tells them whether they still have working software.

`CleanAbandonedRollbacks` runs at startup alongside `CleanAbandonedStaging`, for the case
where the process died mid-transaction.

### Repair is the update transaction

`RepairAsync` builds an `UpdatePlan` whose target is **the release already recorded in the
manifest**, and runs the ordinary update path.

This is a deliberate choice against the obvious alternative of fetching only the missing
files. Repair happens when a directory is in an unknown state, which is exactly the
situation in which incremental reasoning is least trustworthy - working out *which* file is
missing requires trusting the record that has already proved unreliable. Reinstalling the
recorded release through the transaction that already has rollback is both simpler and
safer.

Because the target is the recorded release, a repair can never quietly become an upgrade.

One thing repair must *not* inherit is the transaction's account of itself. `UpdatePlan`
carries an `IsRepair` flag, and the update-shaped history events are suppressed when it is
set, because a history reading "Updated to v0.0.3" after somebody pressed REPAIR describes
something that did not happen.

### Health is narrow

`InstallationHealthChecker` reports five problems and nothing else: a missing directory, a
missing executable, an empty directory, an inconsistent record, missing owned entries. A
health check that cried wolf would be worse than none, so it only reports things it can
observe directly.

`IsRepairable` excludes `InconsistentRecord`, because a record that disagrees with itself is
not fixed by fetching files - it is fixed by removing the record, which is the user's
decision rather than RepoDeck's.

### Asking before downloading

`RunningApplicationDetector` is consulted **before** the download starts, not before the
swap. Somebody with the program open should be told to close it immediately, not after
waiting for 57 MB.

It matches on executable name and then confirms via `MainModule.FileName` inside the
installation root, so an unrelated program with the same name elsewhere is not mistaken for
this one. Failure to determine an answer counts as "not running": the transaction is already
safe against being wrong, and a detector that blocked on uncertainty would block constantly.

Nothing is ever force-killed. RepoDeck asks.

### The manifest is untrusted input

An `ApplicationManifest` is JSON on disk, which means it can be edited, corrupted or
replaced. Uninstall therefore re-derives containment at the point of use rather than
trusting the recorded path: `ArchivePathGuard.IsInside` against the managed root, plus
explicit refusal of the root itself and of the reserved `.staging` and `.rollback` folders.

A refused uninstall **keeps the record**. Dropping it would be the worse failure - a
manifest RepoDeck will not act on would vanish silently, leaving files on disk that nothing
knows about.

The same rule applies when the deletion itself does not work, and that case had to be
learned the hard way. A live uninstall reported success, dropped the record and wrote
"Removed it." into the history while 69 MB was still sitting on disk: the recursive delete
had returned without throwing and removed nothing. On Windows that can happen when another
process holds a handle to something inside - a scanner reading a freshly written
executable, a second copy of RepoDeck, a file browser with the folder open.

So the delete is verified rather than assumed. RepoDeck checks the directory is actually
gone, retries briefly for the pending case, and if files survive it keeps the record,
records nothing in the history and reports failure. Keeping the record is also what makes
a second attempt possible at all.

The suite had proved RepoDeck would not delete the wrong thing and had never once proved
that it deletes the right thing. It does now.

### Release notes are remote text

Update release notes are shown in a `SelectableTextBlock` as plain text, truncated, never
rendered as markup. Nothing in them can become a link, an image or a request.

### Transfers are in memory, history is on disk

These are different things and are stored differently.

`TransferRegistry` tracks what is being fetched right now. It is in memory only and
deliberately so: an "active download" cannot survive the process performing it, and
persisting it would produce records that lie after a crash. What outlives a run is the
manifest the download produced, or nothing.

`LifecycleHistory` is what RepoDeck has done, and that does belong on disk. It is a
user-facing record rather than a log - the log file already exists and is the right place
for stack traces, request URLs and byte counts. Consequently it holds no tokens, no
authentication headers, no paths beyond the managed root and no exception detail; a failure
records that it failed and the sentence the user was already shown. Capped at 400 entries
and trimmed on write.

### Favorites are not installations

A favourite is a bookmark. It can be installed, uninstalled, or never installed, and
removing an application does not remove the bookmark. Keeping them in a separate store is
what makes that true by construction rather than by care.

### Update-all is sequential

Checking runs one application at a time. Parallel checks would be faster and would exhaust
an unauthenticated rate allowance on a library of any size.

## Navigation

### A stack, not a flag

`NavigationHistory` holds where the user has been. Each entry records the page view model,
what the status strip calls it, and **what a child screen should call the way back to it**.

That last field is recorded when leaving rather than computed when returning, and the
distinction matters. Discover is two places depending on what is on it - a page of results
somebody wants to get back to, or the home page they started from - and by the time Back is
pressed the screen may no longer be in the state that made the label true.

Pages are held, not rebuilt. Returning to a search must not re-run it: the results are
already there, the GitHub allowance has been spent on them once, and repeating the request
would be slower and would sometimes fail. This is why the section view models are
long-lived and only detail pages are constructed per visit.

The stack is bounded at 20. A session that opens two hundred projects should not keep two
hundred view models alive, and somebody pressing Back that many times has long since
stopped meaning "the previous screen".

### A destination is a way out from anywhere

Binding the sidebar to `SelectedItem` alone is not enough, and this was a real defect rather
than a theoretical one. Somebody on a details page reached from Discover still has Discover
selected, so clicking Discover changed no selection, raised no event, and left them looking
at the page they were trying to leave.

The tap is handled instead. Choosing a destination clears the history and returns that
section to its root - which for Discover means its home state, not whatever was last on
screen inside it.

### State that belongs to the view model, not the view

The shell swaps the whole page on navigation, so the view is destroyed and rebuilt. Anything
that must survive a round trip therefore lives on the view model: the results, the query,
the filters, the browse mode, the Quick Look selection, and the scroll position.

The scroll offset is the least obvious of these. It is a number on `DiscoverViewModel`,
written when the view detaches and restored when it attaches, deferred to `Loaded` priority
because a `ScrollViewer` clamps an offset to the extent it currently knows about - which, at
attach time, is nothing.

### Home and Results are one view model and two places

`ShowHome` and `ShowResultControls` are separate states deliberately. Home is for somebody
who does not know what they want; results are for somebody narrowing an answer. Sort,
language, stars, last-updated and the view toggle only appear once there is something to
apply them to, because offering them earlier asks a beginner to operate machinery with
nothing in it.

Sharing a view model keeps the search pipeline in one place. Splitting the *states* keeps
the page honest about what it is for.

### Recently viewed is a convenience, not a profile

`RecentlyViewed` stores an owner, a name, a trimmed one-line description and a timestamp -
enough to draw a card without asking GitHub anything, and nothing more. Twelve entries,
deduplicated, capped, local, clearable. No accounts and no synchronisation.

Reopening an entry fetches the real repository rather than reconstructing a half-populated
one from what was stored. A card built from four fields and presented as the project itself
would be RepoDeck asserting something it does not know.

The shelf is absent rather than empty when there is no history, because an empty heading is
a promise of content that is not there.

## Caching and rate limits

`ResponseCache` is an in-memory TTL cache keyed by request URI: 5 minutes for searches,
15 for repository metadata, 30 for README, languages and releases. Unauthenticated
GitHub allows 60 core requests an hour, so revisiting a page must not cost a round trip.
`RateLimitStatus` is parsed from the response headers on every call and surfaced in the
status bar, and an exhausted limit becomes a plain-English message with a reset time.

## Authentication

`GitHubTokenProvider` reads `REPODECK_GITHUB_TOKEN` and nothing else. No token is
written to disk, none is committed, and none is logged. The provider takes its reader as
a delegate, so an OS credential store or OAuth device flow slots in behind it later.

## Threading

Every network call is `async` and takes a `CancellationToken`. The Discover search is a
`[RelayCommand(IncludeCancelCommand = true)]`, which is what backs the Cancel button.
The UI thread is never blocked.

`Progress<T>` marshals through the synchronisation context, which means a report can
arrive after the work that produced it has finished. Anywhere that matters, a flag or a
generation counter guards the final state, because the last report to arrive is not
necessarily the most recent one.

## What RepoDeck deliberately does not do

No source builds, and no running of installers. RepoDeck installs precompiled release
assets into its own folder and launches them on request; a Windows installer or a Linux
package is fetched and handed to the user, because running one needs elevation and is
their decision. No command found in a README is ever run, no PATH is modified, no runtime
is installed, and no system setting is touched.

Milestone 4 changed none of that. It adds no execution of installers, no scripts, no
elevation, no package-manager calls and no automatic pre-release installation. Downloaded
content remains untrusted, and the update confirmation says so on screen before anything
runs.

The Downloads page will never gain a Run button. It exists precisely because RepoDeck
declined to run something.
