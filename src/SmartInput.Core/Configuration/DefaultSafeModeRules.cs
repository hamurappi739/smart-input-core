using System.Collections.Frozen;

namespace SmartInput.Core.Configuration;

public static class DefaultSafeModeRules
{
    public static IReadOnlyList<string> ProcessNames { get; } =
    [
        // Code editors and IDEs are intentionally not blocked by default.
        // Their code/identifier tokens remain protected by the token guards;
        // users can still add a particular editor to ExcludedApplications.
        "WindowsTerminal",
        "wt",
        "powershell",
        "pwsh",
        "cmd",
        "conhost",
        "mintty",
        "putty",
        "WindowsSandbox",
        "mstsc",
    ];

    public static IReadOnlyList<string> WindowClasses { get; } =
    [
        "UnityWndClass",
        "UnrealWindow",
        "SDL_app",
        "Valve001",
    ];

    public static FrozenSet<string> ProcessNameSet { get; } =
        ProcessNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static FrozenSet<string> WindowClassSet { get; } =
        WindowClasses.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
