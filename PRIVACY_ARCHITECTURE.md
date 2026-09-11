# Privacy Architecture

Smart Input is designed as a local-first typing assistant. Privacy is a core architectural constraint, not an optional feature.

## Principles

1. **No server transmission of typed text.** User keystrokes and corrections never leave the device.
2. **No cloud dependency.** The product must work fully offline.
3. **No keylogging history.** The app does not persist a chronological log of everything the user types.
4. **Safe logging.** Debug and diagnostic logs must never include typed text, selected text, or clipboard contents.
5. **Secure field bypass.** Password fields and secure input contexts are always excluded from monitoring and correction.
6. **Local learning only.** Dictionary entries, undo-learning data, snippets, and profiles remain on the user's machine.

## Implementation Guidelines

- Platform input hooks (future phases) feed ephemeral buffers to engines; buffers are not written to disk as history.
- Persistence stores only explicit user configuration and opted-in learning artifacts.
- Logging uses structured metadata (feature name, error code, timing) — never raw user content.
- Network access is not required for core functionality and should remain absent unless explicitly added for non-typing features (e.g., optional updates) with separate user consent.

## Phase 0 Status

Phase 0 installs no keyboard hooks and captures no typed text. Settings are stored locally in `%AppData%/SmartInput/settings.json`.
