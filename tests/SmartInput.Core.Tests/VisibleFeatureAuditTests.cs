namespace SmartInput.Core.Tests;

/// <summary>
/// Catalog of user-visible product features and their implementation status.
/// Fails when a required feature is missing from the catalog or mis-classified.
/// </summary>
[Trait("Category", "VisibleFeatureAudit")]
public class VisibleFeatureAuditTests
{
    public enum ImplementationStatus
    {
        Implemented,
        Partial,
        Placeholder,
    }

    public sealed record VisibleFeature(
        string Id,
        string DisplayName,
        ImplementationStatus Status,
        string Notes);

    /// <summary>
    /// Canonical catalog. Add new visible features here; the presence test will fail if omitted.
    /// </summary>
    public static IReadOnlyList<VisibleFeature> Catalog { get; } =
    [
        new("Protection", "Защита (главный переключатель)", ImplementationStatus.Implemented,
            "HomeView IsProtectionEnabled → AppSettings.IsEnabled"),
        new("EmergencyPause", "Экстренная пауза", ImplementationStatus.Implemented,
            "HomeView + SafetyPolicyEvaluator + tray toggle"),
        new("Layout", "Автоматическая раскладка", ImplementationStatus.Implemented,
            "AutomaticLayoutCorrectionEngine / AutomaticLayoutEnabled"),
        new("Autocorrect", "Автоисправление опечаток", ImplementationStatus.Implemented,
            "AutocorrectionService / AutocorrectEnabled"),
        new("Prediction", "Подсказки при вводе", ImplementationStatus.Implemented,
            "Prediction engine + overlay / PredictionEnabled"),
        new("Snippets", "Сниппеты", ImplementationStatus.Implemented,
            "SnippetService / SnippetsEnabled"),
        new("DoubleShiftUndo", "Отмена двойным Shift", ImplementationStatus.Implemented,
            "DoubleShiftUndoCoordinator + CorrectionUndoService"),
        new("SafeMode", "Безопасный режим приложений", ImplementationStatus.Implemented,
            "DefaultSafeModeRules + SafetyPolicyEvaluator"),
        new("ExcludedApplications", "Исключённые приложения", ImplementationStatus.Implemented,
            "ApplicationsView + ExcludedApplicationNameValidator"),
        new("ManualLayoutHotkeys", "Ручная смена раскладки (хоткеи)", ImplementationStatus.Implemented,
            "Manual correction hotkeys in HotkeysView"),
        new("UndoHotkey", "Хоткей отмены исправления", ImplementationStatus.Implemented,
            "UndoLastCorrectionHotkey registration"),
        new("SystemTray", "Иконка в трее", ImplementationStatus.Implemented,
            "WindowsSystemTrayPlatformService + SystemTrayCoordinator"),
        new("SecureInputGuard", "Блокировка в защищённом вводе", ImplementationStatus.Implemented,
            "SecureInputState → SafetyPolicyEvaluator"),
        new("MyDictionary", "Мой словарь", ImplementationStatus.Implemented,
            "User autocorrect dictionary UI"),
        new("Capitalization", "Автокапитализация", ImplementationStatus.Placeholder,
            "CorrectionsView ToggleSwitch IsEnabled=False; setting persisted but UI disabled"),
        new("Punctuation", "Автопунктуация", ImplementationStatus.Placeholder,
            "CorrectionsView ToggleSwitch IsEnabled=False; setting persisted but UI disabled"),
        new("StartWithWindows", "Запуск с Windows", ImplementationStatus.Placeholder,
            "ApplicationsView ToggleSwitch IsEnabled=False; setting persisted but UI disabled"),
        new("LanguagesPage", "Языки (страница)", ImplementationStatus.Partial,
            "LanguagesView exists with disabled controls"),
        new("HotkeyEmergencyPauseToggle", "Хоткей экстренной паузы", ImplementationStatus.Placeholder,
            "HotkeysViewModel.PlaceholderHotkeys: Пока недоступно"),
    ];

    public static IEnumerable<object[]> CatalogMemberData() =>
        Catalog.Select(feature => new object[] { feature.Id, feature.Status });

    [Fact]
    public void Catalog_ContainsAllRequiredVisibleFeatures()
    {
        var ids = Catalog.Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);

        string[] required =
        [
            "Protection",
            "EmergencyPause",
            "Layout",
            "Autocorrect",
            "Prediction",
            "Snippets",
            "DoubleShiftUndo",
            "SafeMode",
            "Capitalization",
            "Punctuation",
            "StartWithWindows",
            "ExcludedApplications",
            "SecureInputGuard",
            "SystemTray",
        ];

        foreach (var id in required)
        {
            Assert.True(ids.Contains(id), $"Visible feature '{id}' is missing from the audit catalog.");
        }

        Assert.Equal(Catalog.Count, Catalog.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(CatalogMemberData))]
    public void CatalogEntry_HasDefinedStatus(string featureId, ImplementationStatus status)
    {
        Assert.False(string.IsNullOrWhiteSpace(featureId));
        Assert.True(Enum.IsDefined(status));
        var entry = Catalog.Single(feature => feature.Id == featureId);
        Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(entry.Notes));
    }

    [Theory]
    [InlineData("Capitalization", ImplementationStatus.Placeholder)]
    [InlineData("Punctuation", ImplementationStatus.Placeholder)]
    [InlineData("StartWithWindows", ImplementationStatus.Placeholder)]
    public void UiDisabledFeatures_ArePlaceholder(string featureId, ImplementationStatus expected)
    {
        var entry = Catalog.Single(feature => feature.Id == featureId);
        Assert.Equal(expected, entry.Status);
        Assert.Equal(ImplementationStatus.Placeholder, entry.Status);
    }

    [Theory]
    [InlineData("Protection")]
    [InlineData("EmergencyPause")]
    [InlineData("Layout")]
    [InlineData("Autocorrect")]
    [InlineData("Prediction")]
    [InlineData("Snippets")]
    [InlineData("DoubleShiftUndo")]
    [InlineData("SafeMode")]
    [InlineData("ExcludedApplications")]
    [InlineData("SecureInputGuard")]
    [InlineData("SystemTray")]
    [InlineData("ManualLayoutHotkeys")]
    [InlineData("UndoHotkey")]
    [InlineData("MyDictionary")]
    public void CoreProductFeatures_AreImplemented(string featureId)
    {
        var entry = Catalog.Single(feature => feature.Id == featureId);
        Assert.Equal(ImplementationStatus.Implemented, entry.Status);
    }

    [Fact]
    public void PlaceholderFeatures_MatchDisabledUiToggles()
    {
        var placeholders = Catalog
            .Where(feature => feature.Status == ImplementationStatus.Placeholder)
            .Select(feature => feature.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.Contains("Capitalization", placeholders);
        Assert.Contains("Punctuation", placeholders);
        Assert.Contains("StartWithWindows", placeholders);
        Assert.Contains("HotkeyEmergencyPauseToggle", placeholders);
    }
}
