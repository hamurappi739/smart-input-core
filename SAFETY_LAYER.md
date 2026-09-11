# Safety Layer (Phase 5)

Smart Input uses a conservative, fail-safe safety policy before any automation or external text operation.

## Policy States

| State | Automation | Manual external text ops |
| --- | --- | --- |
| **Allowed** | Yes | Yes |
| **SafeMode** | No | Yes (explicit user actions only) |
| **BlockedApplication** | No | No |
| **SecureInput** | No | No |
| **UnknownContext** | No | No |

Emergency pause overrides all states and blocks every operation.

## Precedence (highest first)

1. Emergency pause
2. Secure input active
3. Secure input unknown **or** active application unknown
4. Excluded application match
5. Safe Mode application/window class match
6. Allowed

## Default Safe Mode Applications

Process names (case-insensitive):

- Cursor
- VS Code (`Code`)
- Visual Studio (`devenv`)
- JetBrains IDEs (`idea64`, `rider64`, `webstorm64`, `pycharm64`, `clion64`, `goland64`, `phpstorm64`, `rubymine64`, `fleet`)
- Windows Terminal (`WindowsTerminal`, `wt`)
- PowerShell (`powershell`, `pwsh`)
- CMD / console hosts (`cmd`, `conhost`)

Identifiable game window classes:

- `UnityWndClass`
- `UnrealWindow`
- `SDL_app`

Game detection is intentionally limited. Many games cannot be identified reliably.

## Secure Input Detection (Windows)

Implementation: `GetGUIThreadInfo` with the `GUI_SECURE` flag on the foreground window thread.

**Limitations (not perfect):**

- Not all password fields set the secure flag consistently.
- Some applications use custom controls that bypass standard secure-input signaling.
- Remote desktop, elevated apps, and embedded web views may report incomplete context.
- When detection fails, the policy returns **UnknownContext** and blocks automation (fail-safe).

Smart Input does **not** log, store, or transmit field contents during secure-input checks.

## Manual Conversion Separation

- **Local test-box preview** (Advanced page) converts text in memory only and does not read foreground application content.
- **Selected-text conversion** and **replacement demo** always pass through the safety policy.
- Secure input **never** bypasses protection, even for manual actions.

## Emergency Pause

Toggle on the Home page. Persisted in `%AppData%/SmartInput/settings.json`. When active, all automation and external text operations are denied immediately.

## Excluded Applications

User-configurable process names on the Advanced page. Matching is case-insensitive exact match on the foreground process name (without `.exe`).
