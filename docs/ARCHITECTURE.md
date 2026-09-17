# RepoDeck architecture

## The split that matters

Four concerns are kept separate from the start, because conflating them is what makes
this kind of application impossible to extend later:

| Concern | Where it lives | Milestone |
|---|---|---|
| **Discover** a repository | `Services/GitHub` | 1 (done) |
| **Understand** a repository | `Services/Explanation`, `Services/Readme` | 1 (done) |
| **Inspect** and plan an install | `Services/Analysis`, `Services/Install` | 2 (done) |
| **Execute** a plan | not yet written | 3-4 |

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

## What Milestone 2 deliberately does not do

No downloading, no extraction, no installation, no launching, no source builds. RepoDeck
analyses and plans; Milestone 3 executes verified plans. The only process RepoDeck
starts is the system browser for an `http`/`https` URL, and the system file manager for
RepoDeck's own folder. Nothing from a repository is ever executed, and no command found
in a README is ever run.
