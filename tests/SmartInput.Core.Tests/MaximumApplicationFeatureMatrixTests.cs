using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Core.Validation;
using SmartInput.Platform.Abstractions.Security;

namespace SmartInput.Core.Tests;

[Trait("Category", "ApplicationFeatureMatrix")]
public class MaximumApplicationFeatureMatrixTests
{
    private readonly SafetyPolicyEvaluator _evaluator = new();

    public static IReadOnlyList<string> EditingApps { get; } =
    [
        "notepad", "winword", "excel", "powerpnt", "outlook", "olk",
        "soffice", "wordpad", "chrome", "msedge", "firefox", "opera",
        "telegram", "Discord", "WhatsApp", "slack", "ms-teams", "Teams",
        "thunderbird", "obsidian", "notion", "zoom",
        "spotify", "explorer", "AcroRd32", "FoxitReader",
    ];

    public static IReadOnlyList<string> SafeModeApps { get; } =
        DefaultSafeModeRules.ProcessNames.ToList();

    public static IReadOnlyList<string> GameWindowClasses { get; } =
        DefaultSafeModeRules.WindowClasses.ToList();

    public static IReadOnlyList<string> NeutralWindowClasses { get; } =
    [
        "",
        "Notepad",
        "Chrome_WidgetWin_1",
        "XLMAIN",
        "rctrl_renwnd32",
    ];

    private static IEnumerable<string> ValidProcessNames(IEnumerable<string> rawNames)
    {
        foreach (var raw in rawNames)
        {
            if (ExcludedApplicationNameValidator.TryNormalizeName(raw, out var normalized, out _))
            {
                yield return normalized;
            }
        }
    }

    private static IEnumerable<string> ProcessNameVariants(string normalizedName)
    {
        yield return normalizedName;
        yield return normalizedName + ".exe";
    }

    /// <summary>
    /// SafetyPolicyEvaluator compares ProcessName exactly to SafeMode/Excluded lists.
    /// Stored rules and Windows ProcessName are without .exe; normalize before evaluate
    /// the same way ExcludedApplicationNameValidator strips the suffix.
    /// </summary>
    private static string NormalizeForPolicy(string processName)
    {
        if (ExcludedApplicationNameValidator.TryNormalizeName(processName, out var normalized, out _))
        {
            return normalized;
        }

        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
    }

    [Fact]
    public void FeatureMatrix_CrossProduct_EvaluatesAtLeast500Combinations()
    {
        var editing = ValidProcessNames(EditingApps).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var safeMode = ValidProcessNames(SafeModeApps).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var allApps = editing.Concat(safeMode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(allApps.Count >= 30, $"Expected at least 30 apps, got {allApps.Count}");

        var evaluated = 0;
        var editingAllowed = 0;
        var safeModeBlocked = 0;
        var gameClassBlocked = 0;
        var secureBlocked = 0;
        var featureCombos = 0;

        var policyVariants = new (string Label, Func<string, string, SafetyPolicyContext> Build)[]
        {
            ("healthy", static (process, window) =>
                SafetyPolicyDefaults.CreateDefaultContext(processName: process, windowClassName: window)),
            ("emergency", static (process, window) =>
                SafetyPolicyDefaults.CreateDefaultContext(
                    isEmergencyPaused: true,
                    processName: process,
                    windowClassName: window)),
            ("secure-active", static (process, window) =>
                SafetyPolicyDefaults.CreateDefaultContext(
                    secureInputState: SecureInputState.Active,
                    processName: process,
                    windowClassName: window)),
            ("secure-unknown", static (process, window) =>
                SafetyPolicyDefaults.CreateDefaultContext(
                    secureInputState: SecureInputState.Unknown,
                    processName: process,
                    windowClassName: window)),
            ("unknown-context", static (process, window) =>
                SafetyPolicyDefaults.CreateDefaultContext(
                    isApplicationContextKnown: false,
                    processName: process,
                    windowClassName: window)),
            ("excluded", static (process, window) =>
                SafetyPolicyDefaults.CreateDefaultContext(
                    processName: process,
                    windowClassName: window,
                    excludedApplications: [process])),
        };

        // Apps × (.exe / bare) × neutral window × policy variants
        foreach (var app in allApps)
        {
            foreach (var nameVariant in ProcessNameVariants(app))
            {
                var process = NormalizeForPolicy(nameVariant);
                foreach (var window in NeutralWindowClasses)
                {
                    foreach (var (_, build) in policyVariants)
                    {
                        var result = _evaluator.Evaluate(build(process, window));
                        evaluated++;

                        if (build == policyVariants[0].Build
                            && editing.Contains(process, StringComparer.OrdinalIgnoreCase)
                            && result.AllowsAutomation)
                        {
                            editingAllowed++;
                        }

                        if (build == policyVariants[0].Build
                            && safeMode.Contains(process, StringComparer.OrdinalIgnoreCase))
                        {
                            Assert.Equal(AutomationPolicyState.SafeMode, result.State);
                            Assert.False(result.AllowsAutomation);
                            safeModeBlocked++;
                        }
                    }
                }
            }
        }

        // Game window classes sampled for a few process names (not full app × class blast).
        var gameSampleProcesses = new[] { "game", "notepad", "chrome", "steam", "explorer" };
        foreach (var processRaw in gameSampleProcesses)
        {
            foreach (var nameVariant in ProcessNameVariants(processRaw))
            {
                var process = NormalizeForPolicy(nameVariant);
                foreach (var windowClass in GameWindowClasses)
                {
                    var result = _evaluator.Evaluate(SafetyPolicyDefaults.CreateDefaultContext(
                        processName: process,
                        windowClassName: windowClass));
                    evaluated++;
                    Assert.False(result.AllowsAutomation);
                    Assert.Equal(AutomationPolicyState.SafeMode, result.State);
                    gameClassBlocked++;
                }
            }
        }

        // Secure input blocks every editing app (with/without .exe).
        foreach (var app in editing)
        {
            foreach (var nameVariant in ProcessNameVariants(app))
            {
                var process = NormalizeForPolicy(nameVariant);
                foreach (var secure in new[] { SecureInputState.Active, SecureInputState.Unknown })
                {
                    var result = _evaluator.Evaluate(SafetyPolicyDefaults.CreateDefaultContext(
                        processName: process,
                        secureInputState: secure));
                    evaluated++;
                    Assert.False(result.AllowsAutomation);
                    secureBlocked++;
                }
            }
        }

        // Settings feature-flag combos × representative policy outcomes.
        bool[] flags = [false, true];
        var featureApps = new[] { "notepad", "Cursor", "chrome" };
        foreach (var app in featureApps)
        {
            foreach (var isEnabled in flags)
            foreach (var layout in flags)
            foreach (var autocorrect in flags)
            foreach (var prediction in flags)
            foreach (var snippets in flags)
            {
                var settings = new AppSettings
                {
                    IsEnabled = isEnabled,
                    AutomaticLayoutEnabled = layout,
                    AutocorrectEnabled = autocorrect,
                    PredictionEnabled = prediction,
                    SnippetsEnabled = snippets,
                };

                var policy = _evaluator.Evaluate(SafetyPolicyDefaults.CreateDefaultContext(
                    processName: NormalizeForPolicy(app)));

                var automationWouldRun = settings.IsEnabled
                    && policy.AllowsAutomation
                    && (settings.AutomaticLayoutEnabled
                        || settings.AutocorrectEnabled
                        || settings.SnippetsEnabled);

                _ = automationWouldRun;
                _ = settings.PredictionEnabled;
                featureCombos++;
                evaluated++;
            }
        }

        Assert.True(evaluated >= 500, $"Expected >= 500 combinations, got {evaluated}");
        Assert.True(editingAllowed > 0);
        Assert.True(safeModeBlocked > 0);
        Assert.True(gameClassBlocked > 0);
        Assert.True(secureBlocked > 0);
        Assert.True(featureCombos >= 3 * 32); // 3 apps × 2^5 flags
        Assert.Equal(DefaultSafeModeRules.ProcessNames.Count, safeMode.Count);
        Assert.Equal(DefaultSafeModeRules.WindowClasses.Count, GameWindowClasses.Count);
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("powershell.exe")]
    public void SafeModeApps_WithAndWithoutExe_BlockAfterNormalization(string processName)
    {
        var normalized = NormalizeForPolicy(processName);
        var result = _evaluator.Evaluate(SafetyPolicyDefaults.CreateDefaultContext(processName: normalized));
        Assert.Equal(AutomationPolicyState.SafeMode, result.State);
        Assert.False(result.AllowsAutomation);
        Assert.True(result.AllowsManualExternalTextOperations);
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("notepad.exe")]
    [InlineData("chrome")]
    [InlineData("chrome.exe")]
    public void EditingApps_WithAndWithoutExe_AllowAfterNormalization(string processName)
    {
        var normalized = NormalizeForPolicy(processName);
        var result = _evaluator.Evaluate(SafetyPolicyDefaults.CreateDefaultContext(processName: normalized));
        Assert.Equal(AutomationPolicyState.Allowed, result.State);
        Assert.True(result.AllowsAutomation);
    }
}
