# RepoDeck status

_Last updated: 2026-09-18_

## Current milestone

**Milestone 4 - Library and lifecycle management.** Complete, with the gaps listed under
Known problems.

Software installed through RepoDeck now stays understandable afterwards: you can see
whether it is current, read what an update would change, take it with rollback protection,
put a damaged installation back, or remove it - without knowing what a GitHub release is.

## Completed

### Reading a version

`ReleaseVersion` parses the tag spellings RepoDeck can be sure about - an optional `v`, two
to four dotted numbers, an optional suffix - and refuses the rest. Two comparisons, used
for different things:

- `CompareTo` is a total order for sorting. It always answers.
- `CompareOrNull` answers **only when RepoDeck can defend the answer**, and returns null
  otherwise. Every user-facing decision goes through this one.

The distinction exists because semantic versioning says any suffix marks a pre-release, so
`1.0.0` outranks `1.0.0-anything`. Real projects do not read the specification. Suffixes
that genuinely mean unfinished - `alpha`, `beta`, `rc`, `preview`, `nightly` and nine more -
keep the semver rule, and pre-release labels are ordered the way the specification says,
numerically where the identifier is a number, so `beta.10` beats `beta.9`. Any other suffix
is treated as the project's own business, carrying no ordering information at all.

### Checking for updates

`UpdateChecker.Evaluate` is pure and takes the release list, the machine and the time, so
every awkward shape of release list is testable without a network. It is conservative in
five specific ways:

- Stable releases only, unless the installed version is itself a pre-release.
- Drafts are never considered.
- Both tags must parse, or the answer is `Unknown` with the reason.
- A newer release with no asset for this machine becomes `ManualUpdateRequired` - a link,
  not a button that cannot work.
- Releases RepoDeck cannot show are newer are not candidates. When the only thing on offer
  is one of those, the answer is `Unknown` and it says which two tags it could not order.

Where several releases are genuinely newer, the highest numbers win. Where the numbers tie
and only the suffix differs, the **publication date** decides, because that is evidence
rather than a guess about naming.

Nothing is written. An answer about a remote release goes stale, and a stale answer stored
in the manifest as fact would be worse than no answer.

### Updating, transactionally

`UpdateService` runs one sequence and never writes into a live installation:

    download -> verify -> assemble in staging -> validate the staged copy
              -> move the live copy aside -> promote -> validate in place -> remove backup

The old directory is *moved* to `Apps/.rollback/<guid>`, not deleted, and stays there until
the promoted copy has been validated where it now lives. Any failure after the move puts it
back. `UpdateResult` distinguishes "failed and your previous version is back" from "failed
and the installation is damaged", because those are different sentences for the user.

A staged copy that turns out to contain nothing runnable fails **before** the live copy is
touched at all.

### Repair

Repair builds an `UpdatePlan` targeting the release **already recorded** and runs the
identical transaction. It inherits every protection including rollback, and it cannot
quietly become an upgrade. It is deliberately not surgery: RepoDeck does not work out which
file is missing and fetch that one, because reasoning about a half-broken directory is
reasoning in the situation where reasoning is least reliable.

`InstallationHealthChecker` reports five problems - missing directory, missing executable,
empty directory, inconsistent record, missing owned entries - and the panel names them
rather than only counting them. An inconsistent *record* is not repairable by fetching
files, so RepoDeck says so instead of offering a button that cannot help.

### Not interrupting a running program

`RunningApplicationDetector` matches by executable name and then by `MainModule.FileName`
inside the installation root. It is asked **before anything is downloaded**, so somebody
with the program open is told to close it rather than finding out after waiting for 57 MB.
Nothing is ever force-killed. Failure to determine the answer counts as "not running",
because the transaction is already safe against being wrong.

### Uninstall hardening

`ApplicationManifest` is data on disk and is therefore treated as untrusted. Before removing
anything, RepoDeck re-derives containment and refuses a path that is outside the managed
root, is the managed root, or is one of the reserved `.staging` / `.rollback` folders - even
if the file has been edited by hand. A refused uninstall **keeps the record**, so a manifest
RepoDeck would not act on never disappears silently.

### The library page

Each row answers, in order: what is it, what version have I got, is there a newer one, is it
still intact, and what can I do about any of that. Release notes are shown as plain,
read-only text and never rendered as markup, so nothing in them can become a link or an
image.

Update-all checks **sequentially, not in parallel**, to protect the rate limit.

### Downloads, Favorites and activity

- **Downloads** lists what RepoDeck fetched but would not install, in three sections. It has
  no Run button and never will: the page exists *because* RepoDeck declined to run something.
- **Favorites** are independent of what is installed. A favourite can be installed,
  uninstalled, or never installed, and removing an application does not lose the bookmark.
  The star is a filled or hollow shape, not a colour.
- **What RepoDeck has done** on the Settings page: the lifecycle history in plain English,
  capped at 400 entries, with no paths, stack traces or anything from a token. Clearing it
  affects nothing that is installed.

### Security boundary - unchanged

Milestone 4 adds no execution of installers, no source builds, no scripts, no elevation, no
PATH changes, no package-manager calls and no automatic pre-release installation. Downloaded
content remains untrusted. The update confirmation says this on screen before anything runs.

## Defects found and fixed by this work

Seven of these were found by running the application, not by reading it.

1. **Uninstall reported success while leaving 69 MB on disk.** The recursive delete was
   trusted rather than checked: it returned without throwing, nothing was removed, and
   RepoDeck went on to drop the record and write "Removed it." into the history. The user
   was left with files that nothing was tracking any more - the exact outcome the
   refused-path rules exist to prevent, arrived at from the other direction. The delete is
   now verified, retried briefly for the Windows pending-delete case, and a failure keeps
   the record and reports failure. Found by removing a real installation; the suite had
   proved RepoDeck would not delete the wrong thing and had never proved that it deletes
   the right thing.
2. **RepoDeck offered `v0.0.3` as an update to `v0.0.3-release.4`** - a downgrade, with the
   word "update" on the button. Correct semantic versioning, wrong answer: `release.4` is
   the project's build number. Fixed by recognising only real pre-release words and refusing
   to order anything else; `CompareOrNull` and fifteen tests now hold the line.
3. **The DOWNLOAD size was blank in the update panel.** It was bound to the plan, which is
   not built until the user asks to update - so the panel asking them to decide showed an
   empty field. The size is now recorded on the check result, where it was already known.
4. **Offered the older of two equal-numbered releases.** Semver ranks an alphanumeric
   identifier above a numeric one, so `release.3-patch.1` beat `release.4`. The publication
   date now settles ties.
5. **"Everything is up to date" printed above a row saying "Can't determine."** The summary
   counted updates and ignored everything else. It now says what the rows support.
6. **The star looked identical whether saved or not.** An explicit `TextBlock` inside the
   button picked up the global text style, so the button's foreground never reached it, and
   the state depended on a colour that was not being applied anyway. It is now a filled or
   hollow star.
7. **A repair recorded that it had updated.** Repair runs the update transaction on purpose,
   and was inheriting the transaction's account of itself: the history read "Updated to
   v0.0.3-release.4" next to "Repaired". Update-shaped events are now suppressed for a
   repair.
8. **The repair panel said "2 things are wrong" without saying what.** The explanations
   existed and were never displayed.
9. **The activity history was recorded all milestone and shown nowhere.**
10. **Repair always failed on this machine.** `MachineProfile` was not reaching the executable
   locator, so `Platform = Unknown` rejected `.exe`. Found by a test before it shipped;
   `Locate` now falls back to the machine profile.
11. **A rollback test could pass without restoring anything.** If `Directory.Move` failed,
    the backup was never taken and the assertion was vacuous. Rewritten around a service that
    locks a file *in staging*, so the backup is definitely taken first; both new tests were
    confirmed to fail when the restore branch is disabled.

## Verification performed

- `dotnet build` - clean, 0 errors, 0 warnings.
- `dotnet test` - **988 passed, 0 failed** (up from 798; 190 new tests).
- Every new guard was confirmed to **fail without its fix**, not merely to pass with it.

**Driven on screen, against real GitHub and a real installation:**

- Update check - the false positive, then the corrected `Unknown`, then a genuine update.
- A **real update transaction**: 57 MB fetched, staged, promoted and validated; the manifest
  advanced, `installedAt` preserved, `updatedAt` set, `.staging` and `.rollback` left empty.
- **Repair, twice**, from a deliberately damaged installation - executable and a directory
  deleted. Both restored the installation byte-for-byte at the same version.
- **Uninstall and reinstall of a real application**, which is how the delete defect above
  was found. The second time, with the fix in place, the folder was genuinely gone.
- The library, the update confirmation, the repair confirmation, Downloads, Favorites
  (empty, then populated, then persisted across a restart), and the activity list.

The rolled-back version number used to make an update genuinely available was a controlled
edit to the manifest, noted here so the result is not mistaken for an unprompted upgrade.

## Known problems

1. **The update progress display was not photographed.** The transaction completed in about
   three seconds, so DOWNLOAD/VERIFY/PREPARE/REPLACE never stayed on screen long enough to
   capture. Its logic is tested; I have not watched it.
2. **Downloads was only seen empty.** Producing a populated page live means downloading
   something RepoDeck refuses to install. The three sections are covered by tests.
3. **The uninstall *refusal* paths were not exercised live.** An ordinary uninstall was,
   and found a real defect. The traversal and reserved-folder refusals are still only
   covered by tests that hand-edit a manifest.
4. **The silent-delete condition could not be reproduced in a test.** A locked file throws,
   which the code already handled; what happened live was a recursive delete returning
   successfully having removed nothing. The verification step exists because that was
   observed, not because it could be written down as a test.
5. **Rollback has not been forced live.** It is covered by tests that fail when the restore
   branch is disabled, but I did not sabotage a real update mid-promotion.
6. **`Unknown` is a dead end.** When RepoDeck cannot order two tags it explains why and
   stops. It does not offer "install this anyway", which would be the honest escape hatch for
   somebody who knows their project's naming better than RepoDeck does.
7. **Pre-release suffix recognition is a word list.** A project using an unusual word for a
   beta gets `Unknown` rather than an offer. That is the safe direction to be wrong in, but
   it is still a list.
8. **Update checking costs one request per application**, sequentially. A large library on an
   unauthenticated allowance will be slow, and there is no scheduling or background check.
9. **No checksum verification against a publisher-provided checksum asset.** RepoDeck records
   the hash of what it downloaded; it does not compare it to a hash the project published.
10. **Transfers are in memory only**, so the Downloads page starts empty each run. This is
   deliberate - an "active download" cannot survive the process performing it - but it means
   a failed download is forgotten on restart.
11. Earlier weaknesses remain: badge rejection is a blocklist; Quick Look costs requests with
    no debounce; relevance works from a one-line description; collections are searches rather
    than curation; compact view cannot be sorted; the light theme is untested in practice;
    image decoding has no automated test and no disk cache.

## Next task

Not started, and deliberately outside Milestone 4's boundary:

1. Checksum verification against a published checksum asset.
2. A background or scheduled update check, rather than only on request.
3. RepoDeck finding itself - still needs a public release to exist.
4. An explicit "install this anyway" for the `Unknown` case.

Source builds and installer execution remain out of scope.

## Earlier milestones

### Finishing the experience

Onboarding took the whole window on a first run and stated the four things RepoDeck will
never do, with a test holding the installer to them. Nine categories and six collections,
all of them searches, each describing what it actually asks GitHub for. A **WHY?** button
beside every verdict, pointing at constants shared with the code that builds the reasoning.
A confirmation listing what RepoDeck will do, what it will not do, and the destination path.
Installing shown as four stages, only one of which has a real percentage.

Two defects were found by running it: a stale progress report overwriting a finished plan,
and a WHY? button that could point at an evidence group that did not exist.

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
