# Producing a RepoDeck alpha release

By default everything below happens on your own machine: it produces files in `dist/` and
stops. Uploading anything is opt-in, takes a switch, and even then produces a **draft** that
nobody can download until you press Publish yourself.

## The short version

```
pwsh build/package.ps1
```

That is the whole thing. It runs the tests, publishes a self-contained Windows build,
packages it two ways, checks the payload for anything that must not ship, and writes
SHA-256 checksums.

## What you get

For version `0.1.0-alpha`, in `dist/`:

| File | What it is |
|---|---|
| `RepoDeck-Portable-0.1.0-alpha-win-x64.zip` | Unzip and run. No installation. |
| `RepoDeck-Setup-0.1.0-alpha-win-x64.exe` | Per-user installer. No administrator access. |
| `RepoDeck-0.1.0-alpha-win-x64.sha256` | Checksums for both, in `sha256sum -c` format. |

The zip contains a single folder, `RepoDeck-0.1.0-alpha-win-x64/`, so extracting it does not
spray two hundred files into whatever directory you were in.

## Requirements

- **.NET 10 SDK** — for everything.
- **Inno Setup 6** — for the installer only. If it is missing, the script says so, tells you
  how to install it, and still produces the portable archive. A missing build tool should
  not cost you the artifact you could have had.

```
winget install --id JRSoftware.InnoSetup --source winget
```

Note that winget installs Inno Setup **per-user**, into
`%LOCALAPPDATA%\Programs\Inno Setup 6`. The packaging script looks there first.

## Changing the version

One file:

```xml
<!-- Directory.Build.props -->
<VersionPrefix>0.1.0</VersionPrefix>
<VersionSuffix>alpha</VersionSuffix>
```

Everything else derives from it — the assembly metadata, the Windows file version, the
artifact names, the installer's registration, and the version RepoDeck shows on its own
Settings page. The packaging script reads it rather than taking a version argument, so the
artifacts and the running application cannot disagree about what they are.

Removing `VersionSuffix` produces a build with no pre-release marker. The script prints a
warning when that happens, because a version without a suffix presents itself as a finished
release and RepoDeck is not one.

## Options

```
pwsh build/package.ps1 -SkipInstaller          # portable only
pwsh build/package.ps1 -KeepPublishDirectory   # leave artifacts/publish for inspection
pwsh build/package.ps1 -DraftRelease           # also create a draft GitHub release
```

## Putting a release on GitHub

Binaries do not belong in the git tree. Git keeps every version of every file forever, so
committing 92 MB per release puts that weight in the repository permanently — every future
clone pays for it, even after the files are deleted. They belong in a GitHub Release, stored
outside the history. That is also the only place RepoDeck can ever find itself: it reads
releases and their assets, not repository files.

```
pwsh build/package.ps1 -DraftRelease
```

This is **opt-in and never automatic**. Without the switch the script uploads nothing
anywhere. With it, it builds as normal and then creates a **draft** release: the release
exists on GitHub, visible only to people who can write to the repository, until somebody
presses **Publish** on the release page. That press is the last point at which a person
looks at it and decides.

Before uploading anything it checks that the GitHub CLI is installed and signed in, that
release notes exist at `docs/release-notes/<version>.md`, and that no release already exists
for the tag — an existing release is not something to overwrite silently, because it may
already be published and people may already have downloaded it.

The tag is created against the **exact commit the artifacts were built from**, not the tip
of the default branch, which is only the same thing by luck. If the working tree has
uncommitted changes the script says so loudly, because then the artifacts do not correspond
to any commit at all and the tag would describe something that was never built.

A version with a pre-release suffix is marked `--prerelease`. That matters beyond the label:
RepoDeck's own update checker skips pre-releases unless the installed version is itself one,
so a build marked this way behaves correctly toward its own users.

### Release notes

One file per version, at `docs/release-notes/<version>.md` — for example
`docs/release-notes/0.1.0-alpha.md`. The script refuses to draft a release without one.

They are written for the person downloading it, not for a changelog generator. For an
unsigned alpha that means saying plainly that SmartScreen will warn, why, and what to do
about it; where the program goes and where its data goes; and what has not been tested.

## Verifying what you built

```
cd dist
sha256sum -c RepoDeck-0.1.0-alpha-win-x64.sha256
```

## What the script refuses to package

The payload is checked before anything is archived, and packaging fails rather than
producing a bad artifact quietly. It rejects debug symbols, source files, project files,
test assemblies, xunit, coverlet, `*.token`, `secrets.json`, `appsettings.Local.json`, and
any absolute path from the build machine appearing inside the shipped `.deps.json` or
`.runtimeconfig.json`.

The symbol check earns its place: the first Release publish was 224 MB, of which 101 MB was
`libSkiaSharp.pdb` and `libHarfBuzzSharp.pdb`, arriving as package content from SkiaSharp
and HarfBuzzSharp. They are excluded in `RepoDeck.csproj`, and the packaging check is there
so that a future dependency reintroducing them fails the build instead of doubling the
download.

## Where things go

**The program**, when installed: `%LOCALAPPDATA%\Programs\RepoDeck`

**Everything RepoDeck owns**, always: `%LOCALAPPDATA%\RepoDeck`

```
%LOCALAPPDATA%\RepoDeck\
    Apps\          applications RepoDeck installed for you
    Cache\         cached GitHub responses
    Downloads\     things it fetched but would not install
    Logs\          diagnostic log files
    Data\          manifests, favourites, preferences, activity history
```

These are deliberately separate. **Uninstalling RepoDeck does not touch the second one.**
Applications RepoDeck installed on your behalf stay where they are, and reinstalling finds
them again — verified by uninstalling and reinstalling with three application directories
in place.

The portable build uses exactly the same location. A portable copy and an installed copy on
the same machine share one library rather than quietly keeping two.

## Why Inno Setup

RepoDeck tells people, on the confirmation panel before it installs anything, that it will
not ask for administrator access. An installer that demanded elevation would contradict that
on the first screen a new user sees.

Inno Setup makes a genuine per-user install a single directive (`PrivilegesRequired=lowest`)
into `%LOCALAPPDATA%\Programs\RepoDeck`, with Start Menu and optional desktop shortcuts,
uninstall registration, and in-place upgrades keyed on a fixed `AppId`. MSI can be made to
install per-user but that is precisely where MSI is weakest. MSIX needs a signing
certificate the user has to trust before sideloading, which is a lot of friction for an
alpha. Squirrel and Velopack want to own the update mechanism, and RepoDeck has a
deliberate one of its own.

The cost is that `ISCC.exe` is not a NuGet package, so a build machine needs it installed.
That is why its absence is a warning rather than a failure.

## What has been tested

On the development machine, against these artifacts:

1. Release build — clean, 0 warnings, 0 errors
2. Portable build — extracted to a fresh directory and run
3. Installer — run through its wizard, and silently
4. First launch — from publish output, from the portable archive, and from the installed copy
5. Persistent data path — all three see the same `%LOCALAPPDATA%\RepoDeck` and the same library
6. Uninstall — program directory, both shortcuts and the registration removed; user data intact
7. Reinstall — after a full uninstall, finds its data again
8. Upgrade — 0.1.0-alpha to 0.1.1-alpha in place, leaving one registration entry, not two

**Not yet verified on a clean machine.** Everything above happened on a computer that has
the .NET SDK installed. The build is self-contained and does not reference the shared
runtime, but "does not need .NET installed" has not been demonstrated anywhere it was
genuinely absent. That is the one claim still owed a test.

## Not done yet, deliberately

- **No code signing.** Windows SmartScreen will warn about both artifacts, and it is right
  to: they are unsigned binaries from an unknown publisher.
- **Nothing is published unless asked.** `-DraftRelease` creates a draft; publishing it is
  a deliberate press on GitHub. There is no auto-update feed.
- **win-x64 only.** The application runs on Linux; there is no Linux packaging yet.
