# LocalPlanner

Windows-first desktop MVP for a private local-first calendar application.

## Current desktop scope

- `WPF` desktop app in `src/LocalPlanner.Desktop`
- Local event storage in `SQLite`
- Create, edit, list, and soft-delete events
- Event fields: `title`, `description`, `start/end`, `timezone`, `all-day`, `RRULE`
- Solution and CI pinned to `.NET 6`

## Project layout

```text
src/
  LocalPlanner.Desktop/
```

## Getting started

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.dotnet"
dotnet restore LocalPlanner.sln --packages .\.nuget\packages
dotnet build LocalPlanner.sln --configuration Debug --no-restore
```

## Local data

The desktop app stores its database under:

```text
%LOCALAPPDATA%\LocalPlanner\localplanner.db
```

## Current limitations

- No LAN sync yet
- No operation log writes yet
- No pairing/auth UI yet
- Recurrence is stored as raw `RRULE` text without advanced rule editing
