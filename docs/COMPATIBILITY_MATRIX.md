# Smart Input — Manual Compatibility Matrix (Phase 13B1)

This document defines how to manually record compatibility and responsiveness results **without storing user text**. Performance metrics in Advanced → Phase 13B1 are numeric-only and in-memory; this matrix is filled in by hand during QA.

## Privacy rules for manual recording

Record only:

- Application name (from this list, not window title text)
- Feature area (layout, autocorrect, snippet, prediction, hotkey, safe mode, secure field)
- Outcome enum: `works`, `partial`, `blocked`, `not_supported`, `not_tested`
- Optional numeric notes: latency feel (`fast` / `acceptable` / `slow`), retry count, Smart Input performance metric snapshot **after** reset + repro (Refresh in Advanced UI)
- Build/version identifier

Never record:

- Typed text, selected text, suggestions, passwords, chat messages, file names from user content, or full window titles

Example row:

| App | Feature | Outcome | Notes |
|-----|---------|---------|-------|
| Notepad | layout correction | works | p95 textReplacement &lt; 50 ms after 10 trials |

## Applications under test

| Application | Process hint | Primary input surface |
|-------------|--------------|------------------------|
| Notepad | `notepad.exe` | Plain text editor |
| Chrome | `chrome.exe` | Web inputs, address bar |
| Edge | `msedge.exe` | Web inputs, address bar |
| Firefox | `firefox.exe` | Web inputs, address bar |
| Telegram | `Telegram.exe` | Chat composer |
| Discord | `Discord.exe` | Chat composer |
| Word | `WINWORD.EXE` | Document body |
| Outlook | `OUTLOOK.EXE` | Mail compose |
| Cursor | `Cursor.exe` | Editor, chat, terminal |
| VS Code | `Code.exe` | Editor, terminal |
| Visual Studio | `devenv.exe` | Editor, designers |
| Windows Terminal | `WindowsTerminal.exe` | Shell buffer |
| PowerShell | `powershell.exe` | Console host |
| CMD | `cmd.exe` | Console host |
| Representative games | varies | In-game chat / search boxes only |

## Expected behavior by feature

### Layout correction (automatic)

- Runs after word boundaries when Protection and Automatic Layout are enabled.
- Wrong-layout tokens may be corrected via backspace + insert.
- Blocked in secure fields, excluded apps, emergency pause, and when policy returns `Blocked`.
- Safe Mode: automatic layout blocked; manual hotkeys may still work where policy allows.

### Autocorrection

- Runs at word boundaries when Autocorrect is enabled.
- Dictionary-based replacements only; no cloud lookup.
- Same safety gates as layout correction.

### Snippets

- Triggered by configured snippet prefixes at word boundaries.
- Expansion uses the same text replacement pipeline as corrections.

### Prediction overlay

- Shows next-word suggestion metadata locally; overlay position follows caret when supported.
- Disabled when prediction is off, policy blocks automation, or secure input is active.

### Tab acceptance

- Plain Tab (without modifiers) accepts the visible prediction when overlay is active and gate allows.
- Does not fire when focus is in secure fields or automation is blocked.

### Esc dismissal

- Plain Esc dismisses the prediction overlay until context changes (token/boundary shift).
- Does not dismiss unrelated UI in the target app when interception applies.

### Manual selected-text hotkeys

- Fix Layout / Fix Spelling / Fix Text operate on current selection in the foreground app.
- Require explicit hotkey; blocked in secure/unknown/excluded contexts per policy.
- Safe Mode allows manual operations where external text operations are permitted.

### Safe Mode

- Automatic correction and prediction automation blocked.
- Manual hotkeys and undo may remain available per policy.

### Secure fields

- Password fields and secure input contexts block automation.
- Metrics may show `replacementBlocked` increments; no text is logged.

## Manual test procedure

1. Reset performance metrics in Advanced → Phase 13B1.
2. Enable only the feature under test (e.g. Automatic Layout only).
3. In the target app, perform a fixed **non-sensitive** pattern (e.g. type a known layout-mismatch word, press space).
4. Repeat 5–10 times; note subjective responsiveness.
5. Refresh performance metrics; copy the numeric summary into your QA notes (not into shared logs with window titles).
6. Mark outcome in the matrix below.

## Compatibility matrix (fill during QA)

| Application | Layout | Autocorrect | Snippets | Prediction | Tab | Esc | Manual hotkeys | Safe Mode | Secure fields |
|-------------|--------|-------------|----------|------------|-----|-----|----------------|-----------|---------------|
| Notepad | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Chrome | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Edge | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Firefox | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Telegram | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Discord | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Word | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Outlook | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Cursor | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| VS Code | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Visual Studio | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Windows Terminal | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| PowerShell | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| CMD | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |
| Representative games | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested | not_tested |

## Interpreting performance metrics

Each duration metric reports `n` (sample count, max 256 rolling), `avg`, `max`, and `p95` in milliseconds. Useful thresholds for manual triage:

- `hookCallback` p95 should stay low (target &lt; 5 ms) to avoid input lag.
- `dispatchLatency` p95 indicates queueing delay before handlers run.
- `textReplacement` p95 reflects correction apply time in the target app.
- `overlayUpdate` p95 reflects prediction UI refresh cost.
- `hotkeyHandling` p95 reflects global hotkey coordinator work.

Counters:

- `replacementSuccess` / `replacementAborted` / `replacementFailed` / `replacementBlocked` / `replacementTimeout` / `replacementNotSupported`
- `uncertainEvents` — hook saw input but observation filter skipped classification
- `droppedEvents` — reserved for future bounded-queue drops (0 unless queue shedding is enabled)
- `queueDepth` / `queueHighWaterMark` — pending keyboard events awaiting dispatch
