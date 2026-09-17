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

## What Milestone 3 deliberately does not do

No source builds, and no running of installers. RepoDeck installs precompiled release
assets into its own folder and launches them on request; a Windows installer or a Linux
package is fetched and handed to the user, because running one needs elevation and is
their decision. No command found in a README is ever run, no PATH is modified, no runtime
is installed, and no system setting is touched.

Update checking is not implemented. The Installed page says so rather than offering a
button that appears to work.
