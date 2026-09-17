# RepoDeck

A desktop application that makes useful GitHub projects approachable to people who do
not write software.

**Discover -> Understand -> Inspect -> Install -> Run -> Update**

Milestone 1 delivers the first two steps: search GitHub, and understand what a
repository actually is in plain English.

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
