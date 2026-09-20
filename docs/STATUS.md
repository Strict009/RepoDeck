# RepoDeck status

_Last updated: 2026-09-19_

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

The check is one level deep and repair is not. See **Repair depth, measured** for exactly
where that boundary falls and what it costs.

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
- `dotnet test` - **1171 passed, 0 failed** (up from 798; 373 new tests).
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
11. **Damage detection sees the top level only; repair fixes everything.** These are two
    different depths and the gap between them is worth stating precisely. `Check` looks at
    four things: the recorded folder exists, is not empty, `ExecutablePath` is present, and
    every name in `OwnedEntries` still resolves. `OwnedEntries` is the **non-recursive**
    listing taken at install time, so for a top-level *directory* the check passes as soon
    as the directory itself exists, whatever has happened inside it. Delete
    `plugins\avcodec-61.dll` and RepoDeck reports a healthy installation; delete top-level
    `LICENSE.txt` and it reports "1 file(s) or folder(s) RepoDeck put there are missing".
    Both measured — see **Repair depth, measured** below. Repair itself has no such limit:
    it re-fetches and re-extracts the whole recorded release, so once anything triggers it,
    nested damage is repaired along with the rest. The shallowness is deliberate and
    documented on the class — RepoDeck cannot tell an application writing its own settings
    from a file going missing, and a check that called every ordinary write "damage" would
    teach people to ignore it. It is still a real limit: the most common kind of damage, a
    missing DLL somewhere inside, is exactly the kind it cannot see.
12. **An installed directory exists that no record claims.** `Apps\KytyPS5__KytyPS5` (57 MB)
    is present on the development machine. The log shows it was installed there and launched
    successfully, but it appears in neither `installed.json` nor the activity history — not
    as a failed install, not as anything. A backup taken later the same evening also lacks
    it, so it predates the 0.1.1 work. **Cause undetermined**, and not reproduced. Recorded
    here rather than explained. One thing it did establish: RepoDeck left it untouched
    through two application removals and a full uninstall, which is unplanned evidence that
    it only acts on what it claims.
13. **Upgrade while RepoDeck is running has not been tested.** Every installer run so far was
    made with the application closed.
14. Earlier weaknesses remain: badge rejection is a blocklist; Quick Look costs requests with
    no debounce; relevance works from a one-line description; collections are searches rather
    than curation; compact view cannot be sorted; the light theme is untested in practice;
    image decoding has no automated test and no disk cache.

## Physical-machine evidence

Two runs on hardware, covering different halves of the question.

### Install and first use — 0.1.0-alpha, second PC

RepoDeck 0.1.0-alpha was installed and used on a second physical Windows machine, from the
public installer on the GitHub release rather than a local build.

| Step | Result |
|---|---|
| Download the published installer | Worked |
| SmartScreen warning | Appeared, as expected for an unsigned build |
| Install | Completed |
| Launch | RepoDeck started |
| Search GitHub | Returned results |
| Find a specific project (KYTYPS5) | Found |
| Install that application | Completed |

That settles the question 0.1.0-alpha could not answer about itself: **a self-contained
build does start on a machine that is not the one it was built on**, and the installer works
end to end for a first-time user.

### Lifecycle — 0.1.1-alpha

The half that decides whether RepoDeck is worth keeping on a machine rather than merely
installable onto one. Run on the second physical PC, and also walked through on the machine
RepoDeck is built on.

| Step | Result |
|---|---|
| Launch installed application from RepoDeck | PASS — SharpEmu ran |
| Persistence after RepoDeck restart | PASS — still recognised, including "Last run just now" |
| Upgrade 0.1.0-alpha → 0.1.1-alpha | PASS |
| Windows registration after upgrade | PASS — one Start Menu entry, one uninstall registration, same GUID, no elevation, no unusual prompts |
| Version reporting | PASS — Settings shows 0.1.1-alpha |
| RepoDeck self-update check | PASS — reports up to date; does not offer the older public release and does not confuse prerelease ordering |
| Repair | PASS — after an executable was deliberately deleted, RepoDeck detected "Program missing, 2 things are wrong" and repaired it |
| Repair data preservation | PASS — application-owned `gui-settings.json` and `user/` retained their original timestamps |
| Remove | PASS — RepoDeck removed only the installation folder it owns |
| Removed-state persistence | PASS — searching again showed no installed marker |
| Diagnostic report privacy | PASS — mechanically checked for Windows username, token patterns, installed application names and literal profile paths; zero matches |
| Windows uninstall | PASS — program files, Start Menu entry and uninstall registration removed |
| User data after uninstall | `%LOCALAPPDATA%\RepoDeck` remained completely untouched |

**The 0.1.x gate is complete.** RepoDeck installs on a machine that is not its own, is
usable there, and removes itself without taking anything with it.

### Three defects the lifecycle run found

Found by doing it, not by reading the code.

1. **The count at the bottom of the window was decided once and never revisited.**
   `RefreshLibraryCount` had a single caller: the shell's constructor. It was right at
   startup and drifted from then on, in both directions. Removing the only installed
   application left the strip saying "1 installed" while the library file said `[]` and the
   application's own diagnostic report, generated seconds later, correctly said
   "Installed: 0 application(s)". `IInstalledAppStore` now announces changes and the shell
   recounts — raised outside the lock, and only after the write succeeded.
2. **Uninstalling RepoDeck asked the wrong question.** Windows asked whether to "completely
   remove RepoDeck and all of its components", then left 134.2 MB behind — 73 files,
   identical before and after — most of it the applications RepoDeck had installed, with
   nothing pointing at it. Keeping it is correct and was already deliberate; not saying so
   was not. The question now describes what it does, and the uninstaller ends by naming the
   folder it kept and why. It does not offer to delete it as well.
3. **The release notes had gone stale on privacy**, still telling people the diagnostic
   report contains the Windows user name after the audit had fixed exactly that.

Also cleared in the same pass: four `\x27` escapes left in comments by a shell-quoting
mishap, and a nullable warning in the one sweep that deletes directories inside `Apps` —
the guard was correct but the compiler could not prove it, so it is now an explicit `if`
rather than a condition buried in a ternary.

## Repair depth, measured

Prompted by a Wine observation — a file deleted from an installation and no repair offered —
and measured on **native Windows** to find out whether Wine had anything to do with it. It
does not.

`InstallationHealthChecker.Check` examines exactly four things:

1. `InstalledPath` parses and resolves inside `Apps` — otherwise `InconsistentRecord`, which
   is deliberately *not* repairable.
2. The directory exists.
3. The directory is not empty.
4. `ExecutablePath` exists, and every name in `OwnedEntries` resolves as a file *or* a
   directory.

`OwnedEntries` is `Directory.EnumerateFileSystemEntries(directory)` taken at install time —
**names only, one level deep, not recursive**. For a top-level directory the check therefore
passes the moment the directory itself exists, no matter what is missing inside it.

Measured against a real installation of `sharpemu/sharpemu`, whose `OwnedEntries` are
`LICENSE.txt, licenses, plugins, SharpEmu.exe`:

| Deleted | Detected? | What RepoDeck said |
|---|---|---|
| `plugins\avcodec-61.dll` (nested) | **No** | Nothing. No damage, no Repair button |
| `LICENSE.txt` (top level, not the program) | Yes | "1 file(s) or folder(s) RepoDeck put there are missing: LICENSE.txt" |
| `SharpEmu.exe` (the program) | Yes | "Program missing", counted among "2 things are wrong" |

The second row was taken with the nested DLL *still missing*, and RepoDeck reported **1**
missing thing, not 2. That is the boundary, unambiguously.

**Repair has no such limit.** It re-fetches and re-extracts the whole recorded release
through the update transaction. Triggering repair on the missing `LICENSE.txt` restored the
nested `avcodec-61.dll` as well, without ever having noticed it was gone. The shortfall is
entirely in *noticing*, not in *fixing*.

The shallowness is intentional and is documented on the class: RepoDeck cannot distinguish
an application writing its own settings from a file going missing, and a check that called
every ordinary write "damage" would train people to ignore it. That reasoning is sound and
the behaviour is not a bug. It is still a real limitation, and the awkward part is that the
single most common form of real damage — one DLL missing from somewhere inside — is exactly
what it cannot see. Deepening it is 0.3's "stronger repair", and needs a way to tell
RepoDeck-placed files from application-written ones rather than simply recursing.

## Wine and Linux — observed, not supported

**Not a supported environment. Nothing here is a commitment, and none of it gated 0.1.1.**

Reported after the 0.1.1 release gate had already passed: the Windows x64 build installs and
runs under Wine on Linux. It searches GitHub, returns results, analyses projects and installs
applications. That is further than anyone designed for, and it is an accident rather than an
achievement.

Two limitations observed:

1. **RepoDeck believes it is on Windows x64.** It reports the environment the Windows API
   reports, which under Wine is Windows. It has no idea there is a Linux host underneath, so
   every compatibility judgement it makes is made as if it were running natively.
2. **A file deleted from a KYTYPS5 installation produced no repair recommendation.** This is
   **not Wine-specific** — see **Repair depth, measured** above. The same deletion behaves
   identically on native Windows, because a nested file is outside what the health check
   looks at. Wine is incidental; the observation is a real and previously unrecorded limit
   of the check.

### To investigate later, in this order

Nothing below is scheduled, and none of it is to be implemented before 0.2 ships.

- Reliable Wine detection that cannot break native Windows detection — the failure mode to
  avoid is a native machine misidentified as Wine, which is worse than not detecting Wine.
- The real Linux host OS and architecture, read from inside a Wine process.
- Wine version and prefix, where that is practical to obtain.
- Whether applications RepoDeck installs launch through the same Wine prefix RepoDeck is in.
- Install, update, repair and remove behaviour under Wine, run as a full lifecycle rather
  than sampled.
- Filesystem and path differences, particularly anything `ArchivePathGuard` depends on.
- Whether the compatibility analysis currently mistakes Wine for native Windows in a way
  that produces a wrong answer rather than merely an incomplete one.
- Whether native Linux releases should ever be offered to a Windows RepoDeck running under
  Wine — plausibly not, since it could not run them.
- Where the responsibility line sits between a native Linux RepoDeck and Windows
  RepoDeck-under-Wine.

### The design this is actually pointing at (post-0.2)

Worth writing down because it is a better idea than making the Windows binary
"Wine compatible". Three installation targets, understood separately:

```
Native Linux      Linux application   -> RepoDeck Linux
Windows via Wine  Windows application -> Wine prefix -> RepoDeck Linux
Native Windows    Windows application -> RepoDeck Windows
```

Which would let a native Linux RepoDeck say something genuinely useful about a project that
ships Windows binaries only:

    No native Linux release found.
    A Windows x64 release is available and may be usable through Wine.

    Native Linux:  no
    Wine:          experimental
    Windows:       publisher release available

That is the RepoDeck argument applied to a harder case. Rather than expecting somebody to
work out what `win-x64.zip`, `linux-x64.tar.gz`, an AppImage, a Flatpak, Wine and Proton
each mean for their machine, RepoDeck interprets the release page for them — and says
"experimental" where that is the honest word, instead of a yes or a no it cannot defend.

**Priority: native Linux support matters more than formal Wine support.** Wine getting this
far already is an encouraging accident, not a plan. Both sit behind 0.2.

## 0.1.1-alpha — released

A stabilisation release, held back until hardware agreed with it. Notes in
`docs/release-notes/0.1.1-alpha.md`.

### Failing loudly

`CrashReporter` runs before Avalonia and writes a readable report with a native message box
for failures that happen before there is a window. It recognises a missing native library,
an architecture mismatch, a refused folder and a full disk; anything else gets no invented
explanation.

### Reporting a problem

**Settings → Copy diagnostic report.** Version, OS, both architectures, runtime, paths,
whether the data folder is actually writable, token *presence*, allowance, and a count of
installed applications. A test sets a token in the environment and asserts it does not reach
the output, because the failure that matters here is a helpful diagnostic quietly publishing
somebody's credentials.

### RepoDeck finds itself

`SelfUpdateService` compares RepoDeck against its own published releases using the same
conservative comparison applied to everything else, then offers the release page. Verified
live against the real 0.1.0-alpha release: RepoDeck reported itself up to date, spending
exactly one request.

It deliberately does not self-install. The update transaction moves the live installation
aside and promotes a validated copy; a running process cannot have its own executable moved.
A second mechanism is real work, and a half-built one would mean the component responsible
for recovering from bad updates is itself the thing that breaks.

### Recovery paths exercised

Every file RepoDeck writes was fed empty, whitespace, truncated JSON, HTML, the wrong shape,
`null`, `[null,null]`, 200 nested brackets and raw bytes. Malformed GitHub responses, absurd
and pathological version tags, and abandoned staging and rollback directories were exercised
too.

**Two real defects found by doing it:**

1. **An asset with no download address was treated as installable**, so RepoDeck could offer
   an update it had nowhere to fetch. Assets without a URL are now excluded in
   `ReleaseAnalyzer`.
2. **`[null,null]` parsed into a list of nulls** in four stores - installed applications,
   favourites, activity history and recently viewed - which would have been handed out and
   dereferenced later. Null entries are now filtered on load.

### First run, reviewed

The content holds up: no repository, release, asset, architecture, token or checksum
vocabulary anywhere. One layout defect fixed - the welcome sat in the top third of the
window and left two thirds empty, reading as a page that had failed to load.

### The gate it was waiting on

Passed. Both halves are recorded under **Physical-machine evidence** above: install and
first use on a second physical PC for 0.1.0-alpha, and the full lifecycle — run, persist,
upgrade, self-update check, repair, remove, uninstall — for 0.1.1-alpha. Three defects came
out of the lifecycle run and are fixed in this release.

`docs/CLEAN-MACHINE-TEST.md` remains the script to re-run against future builds.

## Next task

**0.1.x — done.** Real machines were consulted and disagreed in three places; those are
fixed and shipped in 0.1.1-alpha. `docs/CLEAN-MACHINE-TEST.md` and
`build/clean-machine-report.ps1` stay as the script and the state capture for future builds.

What that leaves is the honest limit of the evidence: the lifecycle has been proven on two
machines, both of them this project's own. It has not been through a stranger's.

Startup failures now produce evidence rather than silence. `CrashReporter` is installed
before Avalonia and writes a readable report to the Logs folder, with a native message box
for a failure that happens before there is a window to put one in. It recognises the shapes
a clean machine actually produces - a missing native library, an architecture mismatch, a
refused folder, a full disk - and says nothing at all when the shape is unfamiliar, because
a confident wrong explanation sends somebody off to fix the wrong thing.

### 0.2 — Understand it

The theme: RepoDeck can answer "can I install this?" and should answer "what is this, why
would I want it, and what am I getting into?"

1. **Project Detail overhaul.** A real project page: identity and badges at the top, then
   tabs for Overview, Releases, Compatibility, Files and Activity. "RepoDeck found" and
   "Things to know" as separate lists, with the evidence behind a disclosure.
2. **Software search rather than repository search.** Score application likelihood, release
   availability, platform fit, recency, README evidence, binary availability and activity,
   then split results into Best matches and Other results. The claim stays "this appears to
   be the kind of application you searched for", never "this software is good".
3. **Install confidence.** High / Uncertain / Manual, each stating the evidence: which asset,
   from which release, and why RepoDeck believes it fits. Not a safety score, and
   deliberately not antivirus by vibes.
4. **Application identity.** Icons, screenshots, developer identity, categories.
5. **Release intelligence.** Better version detection and release comparison, building on
   the conservative comparison already in place.

### 0.3 — Manage it

A serious Library and Update Center: batch updates that remain individually inspectable,
stronger repair, detection of applications RepoDeck did not install, and a library summary
worth opening the application for - "3 updates available, 1 project has not published a
release in 3 years, 1 installation appears damaged".

### Then

Linux packaging, broader package types, and paste-a-GitHub-URL analysis: drop any repository
link into RepoDeck and have it say what the project is, whether it is an end-user
application at all, and which asset suits this machine.

**Native Linux first, Wine second.** The Windows build turns out to run under Wine already,
which is an accident worth investigating and not a supported environment — see **Wine and
Linux — observed, not supported**. The interesting destination is not a Wine-compatible
Windows binary but RepoDeck understanding three targets (native Linux, Windows-via-Wine,
native Windows) well enough to tell somebody that a project shipping only `win-x64.zip` has
no native Linux release, has a Windows one, and *might* work through Wine — with
"experimental" written where that is the honest word. Nothing in it is scheduled, and none
of it starts before 0.2 ships.

### Updating RepoDeck itself

Deliberately not scheduled yet, because it is harder than it looks and the existing update
transaction cannot be pointed at RepoDeck.

That transaction works by moving the live installation aside and promoting a validated copy
into its place. A running process cannot have its own executable moved on Windows, so
RepoDeck updating itself needs a different mechanism: a small separate updater that outlives
the exit, or a staged swap applied on next launch. The honest interim answer is to check,
tell the user a newer version exists, and send them to the release page - which is what the
installer's upgrade path already handles correctly.

The guiding test for all of it stays the same: **could somebody who does not know what a
GitHub repository is use RepoDeck to find, understand, install and maintain software from
GitHub?**

## Navigation and Discover Home (pre-release polish)

A focused pass before 0.1.0-alpha goes to anybody. No service or security architecture
changed; this is navigation and the Discover landing page.

### Back means something specific now

`NavigationHistory` replaced a single boolean and one hardcoded label reading "BACK TO
RESULTS" - which was a lie whenever somebody had arrived from Favorites, or from Discover
before searching for anything. The stack records the screen being left *and what to call
the way back to it*, worked out on the way out rather than on the way back, because by then
the screen may no longer be in the state that made the label true.

Pages are held rather than rebuilt, so returning to a search does not re-run it. The
results are already there and the GitHub allowance has already been spent on them once.
The history is capped at 20: somebody pressing Back that many times has long since stopped
meaning "the previous screen".

`Alt+Left` and the mouse back button both work, handled as tunnelling events so they fire
wherever the focus happens to be. Back that does nothing while the cursor is in the search
box is the kind of thing nobody reports and everybody notices.

Restored on the way back: the query, the results, the filters, Apps/All Projects, Card or
Compact, the Quick Look selection, and the scroll position. The last one lives on the view
model rather than in the view, because the shell rebuilds the view on every navigation and
the view model survives.

### Discover Home is not Discover Results

One view model, two states, treated as different places. Home is for somebody who does not
know what they want; results are for somebody who has asked a question and is narrowing the
answer. Sort, language, stars, last-updated and the view toggle are result controls and no
longer appear on Home - offering them before there is anything to filter asks a beginner to
operate machinery with nothing in it.

Choosing Discover in the sidebar returns to Home, clearing the search and its results but
keeping the filters and browse mode. Those are preferences about browsing rather than part
of one particular search.

### The page itself

Categories are larger and carry the same drawn kind marks the cards use. Five of the nine
had been `ProjectKind.Utility`, which drew the same gear five times and made the row read
as unfinished; they are spread across the marks that fit.

Four collections lead as large cards - Portable apps, No installation needed, Small &
useful, Weird & useful - with the rest as chips. **Popular on GitHub is deliberately not
promoted.** Giving it a large card would turn a star count owned by GitHub into a
recommendation owned by RepoDeck.

"OR DESCRIBE WHAT YOU WANT" became "NOT SURE WHAT TO SEARCH FOR?". The explanatory box
became three steps - SEARCH, UNDERSTAND, INSTALL - with a control statement underneath that
is true of the installer as it actually behaves: RepoDeck does not silently run scripts or
installers, and the third step says it handles *the supported installation*, not every one.

`EVERYTHING` became `ALL PROJECTS`, with an explanation on each half of the switch rather
than one for the pair.

### Recently viewed

`RecentlyViewed`: owner, name, a trimmed one-line description and a timestamp. Twelve
entries, deduped, local, clearable, and nothing leaves the machine. No accounts, no
synchronisation. The shelf does not appear at all when it is empty, because an empty
"Recently viewed" heading on a first run is a promise of content that is not there.

Reopening an entry asks GitHub for the real repository rather than inventing a
half-populated record and showing it as fact.

### Defect found by driving it

**Clicking Discover in the sidebar did nothing when already inside Discover.** The
`SelectedItem` binding only raises on change, and somebody on a details page reached from
Discover still has Discover selected - so the click left them looking at the page they were
trying to leave. Handled on the tap instead, so a destination is a way out from anywhere,
including from inside itself.

### Tested

Driven on screen: Discover to search to details and back with everything restored; `Alt+Left`;
the contextual label reading "Back to results"; sidebar Discover returning to Home; the
Recently viewed shelf appearing only after something had been opened; Home and Results
inspected separately; narrow (960px) reflowing to two columns without clipping.

1025 tests, 0 failed.

### Not covered

- **Installed has no details page**, so "Installed to details and back" could not be
  exercised. Its rows expand in place and its Source button opens a browser. Favorites does
  navigate to details and was the path tested instead.
- **The mouse back button was not pressed**, only written. This machine did not have a mouse
  with side buttons to hand.
- **Scroll restoration was not measured precisely** - the results were restored and the page
  was not at the top, but no exact offset was compared.

## Distribution (0.1.0-alpha)

RepoDeck can now be built into something installable. Deliberately pre-1.0 and deliberately
suffixed: it installs other people's software onto somebody's machine, and calling it 1.0
before anyone but its author has run it would be a claim it has not earned.

### Versioning

`Directory.Build.props` holds the only copy of the version. The assembly metadata, the
Windows file version, the artifact names, the installer's registration and the string
RepoDeck shows on its own Settings page all derive from it, and the packaging script reads
it rather than taking an argument - so the artifacts and the running application cannot
disagree about what they are.

The four-part numeric forms are derived rather than repeated, because three places to edit
is two chances to forget.

### Artifacts

One command, `pwsh build/package.ps1`, produces into `dist/`:

| File | Size |
|---|---|
| `RepoDeck-Portable-0.1.0-alpha-win-x64.zip` | 52.4 MB |
| `RepoDeck-Setup-0.1.0-alpha-win-x64.exe` | 39.9 MB |
| `RepoDeck-0.1.0-alpha-win-x64.sha256` | checksums, `sha256sum -c` format |

A test asserts the version still carries a pre-release suffix, so dropping it takes a
deliberate act with a failing test in the way rather than an absent-minded edit.

Self-contained win-x64, 222 files, 123 MB unpacked. Not trimmed and not single-file:
trimming an Avalonia application removes types the XAML loader finds by name at runtime and
the failure is a blank window rather than a build error, and single-file would unpack to a
temporary directory on first run, which is one more thing to go wrong on the machine
RepoDeck is trying to prove itself on. ReadyToRun is on; it costs 15 MB and buys startup.

### Installer

Inno Setup 6, per-user, no elevation, into `%LOCALAPPDATA%\Programs\RepoDeck`.

The choice follows from something RepoDeck already says. The install confirmation tells
people it will not ask for administrator access; an installer that demanded elevation would
contradict that on the first screen a new user sees. `PrivilegesRequiredOverridesAllowed` is
deliberately unset for the same reason - with it, Inno opens by offering "Install for all
users (requires administrative privileges)", which puts an elevation prompt in front of an
application whose whole argument is that it never needs one.

MSI can be made to install per-user, but that is precisely where MSI is weakest. MSIX needs
a signing certificate the user must trust before sideloading. Squirrel and Velopack want to
own the update mechanism, and RepoDeck has a deliberate one of its own.

`ISCC.exe` is not a NuGet package, so its absence is a warning rather than a failure: the
script says how to install it and still produces the portable archive.

### Where things live

The program installs to `%LOCALAPPDATA%\Programs\RepoDeck`. Everything RepoDeck owns stays
in `%LOCALAPPDATA%\RepoDeck`, and **uninstalling does not touch it** - applications RepoDeck
installed on somebody's behalf are not its to delete. The portable build uses the same
location, so a portable copy and an installed copy share one library rather than quietly
keeping two.

### What the audit found

Most of it was already right. `AppPaths` resolved `%LOCALAPPDATA%` and XDG correctly, every
store derived from it, there were no config files, no absolute paths, no `BaseDirectory` or
`GetCurrentDirectory` use, the theme and fonts were embedded resources, and the developer
tooling was already excluded outside Debug.

Six gaps, all fixed: no version metadata at all; no application icon; `app.manifest`
hard-coding `1.0.0.0` and so claiming a stability RepoDeck has not reached; no publish
configuration; no way for a tester to see which build they were running; and `dist/` not
ignored.

### The payload check

The first Release publish was 224 MB, of which **101 MB was `libSkiaSharp.pdb` and
`libHarfBuzzSharp.pdb`** - native debug symbols arriving as package content. `DebugType=none`
does not touch those. They are excluded in the project file, and the packaging script now
refuses to build if any `.pdb` reappears, along with sources, project files, test
assemblies, xunit, coverlet, `*.token`, `secrets.json`, `appsettings.Local.json`, or any
absolute build-machine path inside the shipped `.deps.json` or `.runtimeconfig.json`.

### Tested

All eight, on the development machine:

| # | Test | Result |
|---|---|---|
| 1 | Release build | Clean, 0 warnings, 0 errors |
| 2 | Portable build | Extracted to a fresh directory and run |
| 3 | Installer | Driven through its wizard, and run silently |
| 4 | First launch | From publish output, portable archive and installed copy |
| 5 | Persistent data path | All three share `%LOCALAPPDATA%\RepoDeck` and one library |
| 6 | Uninstall | Program directory, both shortcuts and registration gone; data intact |
| 7 | Reinstall | Found its three application directories again |
| 8 | Upgrade | 0.1.0-alpha to 0.1.1-alpha in place, one registration entry not two |

Two defects were found and fixed while testing: the installer offered an "Install for all
users" elevation path, and the checksum file was written with CRLF, which makes every line
of a `sha256sum -c` check fail.

### Still owed

1. ~~**Clean-machine verification.**~~ Settled: 0.1.0-alpha was installed from the published
   installer onto a second physical Windows PC with no development tooling, and 0.1.1-alpha
   went through the whole lifecycle there. See **Physical-machine evidence**.
2. **No code signing.** SmartScreen will warn about both artifacts, correctly: they are
   unsigned binaries from an unknown publisher.
3. ~~**Nothing is published.**~~ Settled at 0.1.1-alpha: both artifacts and a SHA-256 file
   are attached to a public GitHub release, and RepoDeck's self-update check reads it. There
   is still no update *feed* beyond the releases API.
4. **win-x64 only.** The application runs on Linux; there is no Linux packaging.
5. **Upgrade while running was not tested.** Both installer runs were made with RepoDeck
   closed.

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
