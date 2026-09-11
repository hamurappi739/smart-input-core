# Smart Input Architecture

## Overview

Smart Input is a privacy-first desktop typing assistant. Phase 0 establishes a layered solution that separates UI, domain logic, persistence, and operating-system integration.

## Project Responsibilities

| Project | Responsibility |
| --- | --- |
| `SmartInput.App` | Avalonia UI, view models, navigation, DI composition root for the desktop app |
| `SmartInput.Core` | Domain models, settings services, future typing engines and orchestration |
| `SmartInput.Platform.Abstractions` | OS-neutral contracts for input monitoring, text replacement, tray, hotkeys, etc. |
| `SmartInput.Platform.Windows` | Windows-specific implementations of platform contracts |
| `SmartInput.Infrastructure` | Local persistence (JSON settings now; stores for dictionary, profiles, snippets, learning) |
| `SmartInput.Core.Tests` | Unit tests for core domain logic |

## Dependency Direction

```
SmartInput.App
  -> SmartInput.Core
  -> SmartInput.Infrastructure
  -> SmartInput.Platform.Abstractions
  -> SmartInput.Platform.Windows

SmartInput.Infrastructure
  -> SmartInput.Core

SmartInput.Platform.Windows
  -> SmartInput.Platform.Abstractions

SmartInput.Core
  -> (no UI or platform dependencies)

SmartInput.Platform.Abstractions
  -> (contracts only)
```

**Critical rule:** `SmartInput.Core` must never reference `SmartInput.Platform.Windows`, Avalonia, or Win32 APIs. Core logic stays portable and testable.

## Platform Abstraction Strategy

Operating-system capabilities are exposed through small interfaces in `SmartInput.Platform.Abstractions`:

- `IInputMonitor` — keyboard event observation (not implemented in Phase 0)
- `IActiveApplicationService` — foreground application detection
- `ISecureInputDetector` — password/secure field bypass detection
- `ITextReplacementService` — replace recently typed text
- `ISelectedTextService` — read/replace selected text
- `IGlobalHotkeyService` — global shortcuts
- `ISystemTrayPlatformService` — tray icon and menu

The app registers platform services through DI extension methods:

- `AddSmartInputWindowsPlatform()` in Phase 0
- Future: `AddSmartInputMacPlatform()`, `AddSmartInputLinuxPlatform()`

The shell (`SmartInput.App`) selects the platform registration at startup. Core and UI depend only on abstractions.

## Where Future Typing Engines Will Live

| Area | Location |
| --- | --- |
| Layout detection/conversion | `SmartInput.Core/Engines` |
| Autocorrect and capitalization | `SmartInput.Core/Engines` |
| Local prediction | `SmartInput.Core/Engines` |
| Pipeline orchestration | `SmartInput.Core/Engines/IProcessingPipeline` |
| Safety/bypass policy | `SmartInput.Core/Services` |
| Per-app profile resolution | `SmartInput.Core/Services` |

Engines consume platform abstractions for input context and text replacement. They never call Win32 directly.

## Where Windows-Specific APIs Will Live

All low-level Windows code belongs in `SmartInput.Platform.Windows`:

- keyboard hooks (`SetWindowsHookEx`, etc.)
- input injection (`SendInput`, UI Automation)
- foreground window/process APIs
- secure input detection
- tray icon implementation

Phase 0 contains stub implementations only.

## Persistence

`SmartInput.Infrastructure` owns local storage:

- `JsonSettingsPersistence` — `%AppData%/SmartInput/settings.json`
- Placeholder in-memory stores for dictionary, profiles, snippets, and learning data

SQLite or another structured store can be introduced later without changing Core contracts.

## Adding macOS and Linux Later

1. Create `SmartInput.Platform.macOS` and/or `SmartInput.Platform.Linux`.
2. Implement the same interfaces from `SmartInput.Platform.Abstractions`.
3. Register the appropriate platform extension from `SmartInput.App` based on runtime OS.
4. Keep `SmartInput.Core` unchanged.

No rewrite of the application core is required because domain logic never depended on Windows types.

## MVVM and DI

- Views and view models live in `SmartInput.App`.
- View models depend on Core services (`ISettingsService`) and eventually platform abstractions via Core orchestrators.
- `Microsoft.Extensions.DependencyInjection` composes services at startup in `Program.cs`.
