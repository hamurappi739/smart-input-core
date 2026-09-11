# Text Replacement Compatibility (Phase 2)

Smart Input Phase 2 uses `SendInput` with virtual Backspace events followed by Unicode character injection. This approach is intentionally narrow and demo-scoped.

## Supported scenarios

- Standard single-line text fields with a text caret (Notepad, Word, many browser inputs)
- Replacement of text immediately **before the caret** (suffix at cursor position)
- Explicit demo trigger from the Advanced page (`abc` → `xyz`)

## Known limitations

### Caret and selection

- The demo assumes the original text sits directly before the caret. If the caret is in the middle of a line or the suffix does not match, Backspace deletes the wrong characters.
- Existing text selections are not preserved. Backspace operates on the caret, not the selection.
- Multi-caret or rich-text editors may behave unpredictably.

### Application classes

- **Terminals/consoles** (Win32, ConPTY, Windows Terminal): often ignore or reinterpret Unicode injection.
- **Remote desktop / VM guests**: injected input may be filtered or delayed.
- **Elevated applications**: injection from a non-elevated Smart Input process may be blocked by UIPI.
- **Games and DirectInput apps**: frequently bypass standard text input paths.
- **Password / secure fields**: not detected in Phase 2; avoid running the demo in credential fields.

### Input method and layout

- Unicode injection inserts literal characters and does not simulate physical key presses for the active keyboard layout.
- IME/composition windows may consume or alter injected characters.

### Performance and timing

- Small delays are inserted between injected events to reduce dropped keystrokes. Very slow or overloaded targets may still miss events.
- Only one replacement operation runs at a time (serialized by a platform lock).

### Privacy

- Replacement text is never written to disk.
- Logs contain counts only (`deleted`, `inserted`), never original or replacement strings.

## Concurrent user input policy (Phase 2 hardening)

- Genuine user keyboard events are **never suppressed** during replacement.
- Only injected events (`LLKHF_INJECTED` or Smart Input `dwExtraInfo` marker) are ignored by monitoring.
- If a genuine **KeyDown** arrives while replacement is active, the replacement is **aborted immediately**:
  - further `SendInput` calls are not issued;
  - partial delete/insert counts are returned with status `AbortedByUserInput`;
  - the user's keystroke continues to the target application normally.
- KeyUp-only events do not abort replacement.

## Secure input

- Replacement is blocked when `ISecureInputDetector` reports secure input active.
- Full password-field detection is not implemented until Phase 5; the guard is wired and ready.
