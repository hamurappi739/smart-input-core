# Smart Input Roadmap

## Phase 0 — Architecture **(current)**

Solution structure, MVVM shell, configuration models, platform abstraction contracts, Windows stub implementations, JSON settings persistence, documentation, build and test pipeline.

## Phase 1 — Windows Input Prototype

Low-level keyboard observation prototype on Windows behind `IInputMonitor`. No text modification.

## Phase 2 — Safe Text Replacement

Controlled text replacement through `ITextReplacementService` with safety guards.

## Phase 3 — RU/EN Layout Converter

Manual and programmatic Russian/English layout conversion engine in Core.

## Phase 4 — Automatic Layout Detection

Detect wrong-layout typing and suggest or apply corrections.

## Phase 5 — Safety Layer

Secure field bypass, IDE/terminal/game safe mode, per-application profiles.

## Phase 6 — GUI Expansion

Full settings UI for languages, corrections, profiles, privacy, and advanced options.

## Phase 7 — Autocorrect

Local typo and spelling correction engine.

## Phase 8 — Undo + Learning

Learn from user undo actions; persist learning data locally.

## Phase 9 — Snippets

Text expansions and snippet management.

## Phase 10 — Manual Selected Text Correction

Correct user-selected text via hotkey or command.

## Phase 11 — Local Prediction

On-device next-word prediction without cloud dependency.

## Phase 12 — Ghost Text UI

Inline prediction ghost text presentation.

## Phase 13 — Optimization and Compatibility Testing

Performance tuning and broad application compatibility validation.
