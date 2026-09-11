# Smart Input

Offline Windows text-input engine for RU↔EN layout correction, conservative
spelling correction, keyboard-boundary handling, user snippets and safe undo.

## Scope

- `src/SmartInput.Core` — layout mapping, token decisions, spelling and safety gates.
- `src/SmartInput.Platform.Windows` — Windows keyboard, boundary and replacement adapters.
- `src/SmartInput.ResidentHost` — resident input host.
- `src/SmartInput.App` — Avalonia settings application.
- `tests` — Core, Windows-adapter and application tests.
- `tools` — dictionary, audit, privacy and release helpers.

The Rust scorer is integrated as an audit/shadow provider. The current live
replacement owner remains the C# Core pipeline.

## Build and test

```powershell
dotnet build SmartInput.sln -c Release
dotnet test SmartInput.sln -c Release --no-restore
```

The repository intentionally excludes build output, crash dumps, local runtime
data, generated release bundles, and historical review snapshots. User text is
not intended for diagnostic logging; audit tools emit aggregate results and
privacy-safe identifiers.

## License and dictionaries

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the notices next to
the bundled Hunspell dictionaries before redistribution.
