# RepoDeck

A desktop application that makes useful GitHub projects approachable to people who do
not write software.

**Discover -> Understand -> Inspect -> Install -> Run -> Update**

Search GitHub, understand in plain English what a project actually is, see whether it
will run on your computer, and install and launch the ones RepoDeck can handle. Anything
it cannot install confidently is downloaded and handed to you with the reason stated -
nothing is ever run on your behalf without you asking.

Update checking is the next milestone and is not implemented yet.

## Look and feel

A dark, dense interface in the spirit of late-1990s media players and desktop utilities:
squared-off panels, small capitalised labels, segmented meters and one acid-lime accent
that is used sparingly enough to still mean something. Every status light sits beside a
word, so no state is carried by colour alone.

Results are cards about programs rather than rows about repositories: a picture, a name,
what it is for, whether it runs on your computer, and what RepoDeck can do about it.
Selecting one opens a side panel with the full answer without losing your place in the
search. A denser Compact view is a click away for anyone who prefers a list.

Show APPS to put programs RepoDeck has evidence you can run at the top, or EVERYTHING for
raw GitHub discovery. Projects with no pictures of their own get artwork RepoDeck draws
for the kind of thing they appear to be - never an invented screenshot or logo.

## Requirements

- .NET 10 SDK
- Windows x64 or Linux x64

## Build and run

```
dotnet build
dotnet run --project src/RepoDeck/RepoDeck.csproj
dotnet test
```

## GitHub rate limits

Without a token, GitHub allows 60 requests an hour and 10 searches a minute, which is
easy to exhaust. To raise the limits, set an environment variable before starting
RepoDeck:

```
setx REPODECK_GITHUB_TOKEN your_token_here
```

A token with no scopes is enough for searching public repositories. RepoDeck never
writes the token to disk and never records it in the log.

## Where RepoDeck keeps its files

`%LOCALAPPDATA%\RepoDeck` on Windows, `~/.local/share/RepoDeck` on Linux. RepoDeck only
ever writes inside that folder.

See `docs/ARCHITECTURE.md` and `docs/STATUS.md`.
