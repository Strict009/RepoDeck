# RepoDeck architecture

## The split that matters

Four concerns are kept separate from the start, because conflating them is what makes
this kind of application impossible to extend later:

| Concern | Where it lives | Milestone |
|---|---|---|
| **Discover** a repository | `Services/GitHub` | 1 |
| **Understand** a repository | `Services/Explanation`, `Services/Readme` | 1 |
| **Inspect** and plan an install | `Services/Analysis` | 2-3 |
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

## What Milestone 1 deliberately does not do

No downloading, no extraction, no installation, no launching, no source builds. The
details page states `Requires inspection` for compatibility rather than guessing, and
says in as many words that RepoDeck does not yet examine release files. The only process
RepoDeck starts is the system browser, and only for an `http`/`https` URL.
