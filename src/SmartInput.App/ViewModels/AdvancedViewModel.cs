using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartInput.App.Services;
using SmartInput.Core.Diagnostics;
using SmartInput.Core.Engines;
using SmartInput.Core.Integration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.App.ViewModels;

public partial class AdvancedViewModel : ViewModelBase, IDisposable
{
    private const int MaxDisplayedEvents = 50;

    private readonly IInputDiagnosticCoordinator _diagnosticCoordinator;
    private readonly ISafeTextReplacementService _textReplacementService;
    private readonly ILayoutConversionService _layoutConversionService;
    private readonly IManualLayoutConversionService _manualLayoutConversionService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly ILiveLayoutCorrectionCoordinator _liveLayoutCorrectionCoordinator;
    private readonly ILivePredictionCoordinator _livePredictionCoordinator;
    private readonly ICorrectionUndoService _correctionUndoService;
    private readonly IManualSelectedTextCorrectionService _manualSelectedTextCorrectionService;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;
    private readonly ISettingsService _settingsService;
    private readonly IRustShadowAuditStatusReader _rustShadowAuditStatusReader;
    private readonly IResidentRuntimeStatusReader _residentRuntimeStatusReader;

    public AdvancedViewModel(
        IInputDiagnosticCoordinator diagnosticCoordinator,
        ISafeTextReplacementService textReplacementService,
        ILayoutConversionService layoutConversionService,
        IManualLayoutConversionService manualLayoutConversionService,
        IAutomationSafetyService automationSafetyService,
        ILiveLayoutCorrectionCoordinator liveLayoutCorrectionCoordinator,
        ILivePredictionCoordinator livePredictionCoordinator,
        ICorrectionUndoService correctionUndoService,
        IManualSelectedTextCorrectionService manualSelectedTextCorrectionService,
        IPerformanceMetricsRecorder performanceMetrics,
        ISettingsService settingsService,
        IRustShadowAuditStatusReader rustShadowAuditStatusReader,
        IResidentRuntimeStatusReader residentRuntimeStatusReader)
    {
        _diagnosticCoordinator = diagnosticCoordinator;
        _textReplacementService = textReplacementService;
        _layoutConversionService = layoutConversionService;
        _manualLayoutConversionService = manualLayoutConversionService;
        _automationSafetyService = automationSafetyService;
        _liveLayoutCorrectionCoordinator = liveLayoutCorrectionCoordinator;
        _livePredictionCoordinator = livePredictionCoordinator;
        _correctionUndoService = correctionUndoService;
        _manualSelectedTextCorrectionService = manualSelectedTextCorrectionService;
        _performanceMetrics = performanceMetrics;
        _settingsService = settingsService;
        _rustShadowAuditStatusReader = rustShadowAuditStatusReader;
        _residentRuntimeStatusReader = residentRuntimeStatusReader;
        _diagnosticCoordinator.DiagnosticReceived += OnDiagnosticReceived;
        _settingsService.SettingsChanged += OnSettingsChanged;

        IsMonitoring = _diagnosticCoordinator.IsMonitoring;
        LoadExistingEvents();
        RefreshSafetyStatus();
        RefreshLiveLayoutCorrectionStatus();
        _ = RefreshResidentRuntimeStatusAsync();
        RefreshLivePredictionStatus();
        RefreshUndoStatus();
        RefreshPerformanceMetrics();
        _ = RefreshRustShadowAuditStatusAsync();
        SyncPerformanceDebugLoggingFromSettings(_settingsService.Current);
    }

    public ObservableCollection<InputDiagnosticEntryViewModel> DiagnosticEvents { get; } = [];

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private string _monitoringStatusText = "Наблюдение неактивно";

    [ObservableProperty]
    private string _replacementDemoStatus = "Готово. Введите abc в целевом приложении, поставьте курсор после него и запустите демонстрацию.";

    [ObservableProperty]
    private bool _isReplacementDemoRunning;

    [ObservableProperty]
    private string _layoutTestInput = "ghbdtn";

    [ObservableProperty]
    private string _layoutTestOutput = string.Empty;

    [ObservableProperty]
    private string _layoutConversionStatus =
        "Ручное преобразование выделения. Автоматическое исправление работает на границе слова, когда включены защита и автокоррекция раскладки.";

    [ObservableProperty]
    private bool _isLayoutConversionRunning;

    [ObservableProperty]
    private string _safetyPolicyStatus = "Политика безопасности ещё не проверена.";

    [ObservableProperty]
    private string _liveLayoutCorrectionStatus = "Автоматическое исправление раскладки неактивно.";

    [ObservableProperty]
    private string _residentRuntimeStatus = "Проверка резидентного процесса...";

    [ObservableProperty]
    private string _livePredictionStatus = "Подсказки при вводе неактивны.";

    [ObservableProperty]
    private string _undoStatus = "Отмена недоступна.";

    [ObservableProperty]
    private bool _isUndoRunning;

    [ObservableProperty]
    private string _manualCorrectionStatus =
        "Выделите текст в целевом приложении, затем выберите «Исправить раскладку», «Исправить орфографию» или «Исправить текст». Отображается только статус — текст никогда не записывается.";

    [ObservableProperty]
    private bool _isManualCorrectionRunning;

    [ObservableProperty]
    private string _performanceMetricsSummary =
        "Показатели производительности неактивны — включите защиту и используйте Smart Input, чтобы собирать длительности в памяти.";

    [ObservableProperty]
    private bool _isPerformanceDebugLoggingEnabled;

    [ObservableProperty]
    private string _rustShadowAuditStatus =
        "Ожидание безопасного статуса от фонового процесса…";

    partial void OnIsPerformanceDebugLoggingEnabledChanged(bool value)
    {
        if (_settingsService.Current.PerformanceDebugLoggingEnabled == value)
        {
            return;
        }

        _ = _settingsService.UpdateAsync(settings => settings.PerformanceDebugLoggingEnabled = value);
    }

    [RelayCommand]
    private void RefreshPerformanceMetrics()
    {
        PerformanceMetricsSummary = PerformanceMetricsFormatter.Format(_performanceMetrics.GetSnapshot());
    }

    [RelayCommand]
    private void ResetPerformanceMetrics()
    {
        _performanceMetrics.Reset();
        RefreshPerformanceMetrics();
    }

    [RelayCommand]
    private async Task RefreshRustShadowAuditStatusAsync()
    {
        var snapshot = await _rustShadowAuditStatusReader.TryReadAsync().ConfigureAwait(true);
        RustShadowAuditStatus = snapshot is null
            ? "Фоновый процесс сейчас не отвечает. Проверка Rust-кандидатов не меняет текст и может быть отключена."
            : FormatRustShadowAuditStatus(snapshot);
    }

    [RelayCommand]
    private async Task RefreshResidentRuntimeStatusAsync()
    {
        var snapshot = await _residentRuntimeStatusReader.TryReadAsync().ConfigureAwait(true);
        ResidentRuntimeStatus = snapshot is null
            ? "Резидентный процесс недоступен. Автоматическая замена вне окна настроек сейчас не работает."
            : FormatResidentRuntimeStatus(snapshot);
    }

    [RelayCommand]
    private async Task FixLayoutEnglishToRussianAsync()
    {
        await RunManualCorrectionAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixLayout,
            LayoutDirection = LayoutConversionDirection.EnglishToRussian,
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task FixLayoutRussianToEnglishAsync()
    {
        await RunManualCorrectionAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixLayout,
            LayoutDirection = LayoutConversionDirection.RussianToEnglish,
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task FixSpellingAsync()
    {
        await RunManualCorrectionAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task FixTextEnglishToRussianAsync()
    {
        await RunManualCorrectionAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixText,
            LayoutDirection = LayoutConversionDirection.EnglishToRussian,
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task FixTextRussianToEnglishAsync()
    {
        await RunManualCorrectionAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixText,
            LayoutDirection = LayoutConversionDirection.RussianToEnglish,
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void RefreshUndoStatus()
    {
        var status = _correctionUndoService.Status;
        UndoStatus =
            $"доступно={status.IsAvailable}; тип={status.PendingKind?.ToString() ?? "нет"}; " +
            $"попыток={status.UndoAttempts}; успешно={status.UndoSucceeded}; " +
            $"заблокировано={status.UndoBlocked}; ошибок={status.UndoFailed}; " +
            $"аннулировано={status.UndoInvalidated}; отмен_для_обучения={status.LearningRejectionsRecorded}";
    }

    [RelayCommand]
    private async Task UndoLastCorrectionAsync()
    {
        if (IsUndoRunning)
        {
            return;
        }

        IsUndoRunning = true;
        UndoStatus = "Отмена последнего автоматического исправления...";

        try
        {
            var result = await _correctionUndoService.TryUndoAsync().ConfigureAwait(true);
            UndoStatus = FormatUndoStatus(result);
        }
        catch (Exception)
        {
            UndoStatus = "Непредвиденная ошибка отмены.";
        }
        finally
        {
            IsUndoRunning = false;
            RefreshUndoStatus();
        }
    }

    [RelayCommand]
    private void RefreshLivePredictionStatus()
    {
        var status = _livePredictionCoordinator.Status;
        LivePredictionStatus =
            $"есть_подсказка={status.HasSuggestion}; рекомендация={status.Recommendation}; " +
            $"уверенность={status.Confidence:F2}; слов_в_подсказке={status.SuggestionTokenCount}; " +
            $"символов_в_подсказке={status.SuggestionCharacterCount}; слов_в_контексте={status.ContextWordCount}; " +
            $"символов_в_контексте={status.ContextCharacterCount}; политика={status.LastPolicyState}; " +
            $"защита={status.IsProtectionEnabled}; подсказки={status.IsPredictionEnabled}; " +
            $"проверено={status.PredictionsEvaluated}; очищено={status.PredictionsCleared}; " +
            $"сбросов={status.BufferResets}; последнее_действие={status.LastAction}";
    }

    [RelayCommand]
    private void RefreshLiveLayoutCorrectionStatus()
    {
        var status = _liveLayoutCorrectionCoordinator.Status;
        LiveLayoutCorrectionStatus =
            $"завершено={status.TokensCompleted}; кандидатов={status.CandidatesDetected}; " +
            $"попыток={status.CorrectionsAttempted}; успешно={status.CorrectionsSucceeded}; " +
            $"заблокировано={status.CorrectionsBlocked}; сбросов={status.BufferResets}; " +
            $"подавлено={status.BoundariesSuppressed}; доставлено={status.BoundariesDelivered}; " +
            $"длина_буфера={status.CurrentTokenLength}; последнее_действие={status.LastAction}; " +
            $"политика={status.LastPolicyState}; защита={status.IsProtectionEnabled}; " +
            $"автораскладка={status.IsAutomaticLayoutEnabled}; автокоррекция={status.IsAutocorrectEnabled}; " +
            $"кандидатов_автокоррекции={status.AutocorrectCandidatesDetected}; " +
            $"попыток_автокоррекции={status.AutocorrectAttempts}; " +
            $"успешно_автокоррекции={status.AutocorrectSucceeded}; " +
            $"заблокировано_автокоррекции={status.AutocorrectBlocked}; " +
            $"шаблоны={status.IsSnippetsEnabled}; " +
            $"попыток_шаблонов={status.SnippetExpansionsAttempted}; " +
            $"успешно_шаблонов={status.SnippetExpansionsSucceeded}; " +
            $"заблокировано_шаблонов={status.SnippetExpansionsBlocked}; " +
            $"длина_буфера_шаблонов={status.CurrentSnippetTriggerLength}";
    }

    private static string FormatResidentRuntimeStatus(ResidentRuntimeStatusSnapshot snapshot)
    {
        var status = snapshot.CorrectionStatus;
        return $"Резидентный ввод: {(snapshot.IsMonitoring ? "активен" : "неактивен")}; " +
            $"завершено слов: {status.TokensCompleted}; кандидатов: {status.CandidatesDetected}; " +
            $"замен выполнено: {status.CorrectionsSucceeded}; пропущено: {status.CorrectionsBlocked}; " +
            $"автокоррекция кандидатов: {status.AutocorrectCandidatesDetected}; " +
            $"автокоррекция попыток: {status.AutocorrectAttempts}; " +
            $"автокоррекция выполнено: {status.AutocorrectSucceeded}; " +
            $"автокоррекция заблокировано: {status.AutocorrectBlocked}; " +
            $"пунктуация выполнено: {status.PunctuationSucceeded}; " +
            $"последнее действие: {status.LastAction}; политика: {status.LastPolicyState}; " +
            $"live hook={snapshot.LivePipeline.HookObserved}; " +
            $"resolved={snapshot.LivePipeline.CharacterResolved}; " +
            $"boundaries={snapshot.LivePipeline.BoundaryIntercepted}; " +
            $"decisions={snapshot.LivePipeline.DecisionEvaluated}; " +
            $"replacement_attempted={snapshot.LivePipeline.ReplacementAttempted}; " +
            $"replacement_result={snapshot.LivePipeline.ReplacementResult}; " +
            $"delivered={snapshot.LivePipeline.BoundaryDelivered}.";
    }

    [RelayCommand]
    private void RefreshSafetyStatus()
    {
        var policy = _automationSafetyService.EvaluateCurrentContext();
        SafetyPolicyStatus =
            $"Состояние: {policy.State}; автоматизация={(policy.AllowsAutomation ? "разрешена" : "заблокирована")}; " +
            $"ручные внешние операции={(policy.AllowsManualExternalTextOperations ? "разрешены" : "заблокированы")}" +
            (policy.Reason is null ? string.Empty : $"; причина={policy.Reason}");
    }

    [RelayCommand]
    private void ClearDiagnostics()
    {
        _diagnosticCoordinator.ClearRecentEvents();
        DiagnosticEvents.Clear();
    }

    [RelayCommand]
    private async Task RunAbcToXyzDemoAsync()
    {
        if (IsReplacementDemoRunning)
        {
            return;
        }

        IsReplacementDemoRunning = true;
        ReplacementDemoStatus = "Демонстрационная замена в активном приложении...";

        try
        {
            var result = await _textReplacementService.RunAbcToXyzDemoAsync().ConfigureAwait(true);
            ReplacementDemoStatus = FormatReplacementStatus(result);
        }
        catch (Exception)
        {
            ReplacementDemoStatus = "Непредвиденная ошибка демонстрационной замены.";
        }
        finally
        {
            IsReplacementDemoRunning = false;
        }
    }

    [RelayCommand]
    private void ConvertTestInputEnglishToRussian()
    {
        LayoutTestOutput = _layoutConversionService.Convert(
            LayoutTestInput,
            LayoutConversionDirection.EnglishToRussian);
        LayoutConversionStatus = "Тестовый ввод преобразован по соответствию физических клавиш English → Russian.";
    }

    [RelayCommand]
    private void ConvertTestInputRussianToEnglish()
    {
        LayoutTestOutput = _layoutConversionService.Convert(
            LayoutTestInput,
            LayoutConversionDirection.RussianToEnglish);
        LayoutConversionStatus = "Тестовый ввод преобразован по соответствию физических клавиш Russian → English.";
    }

    [RelayCommand]
    private async Task ConvertSelectionEnglishToRussianAsync()
    {
        await ConvertSelectionAsync(LayoutConversionDirection.EnglishToRussian).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ConvertSelectionRussianToEnglishAsync()
    {
        await ConvertSelectionAsync(LayoutConversionDirection.RussianToEnglish).ConfigureAwait(true);
    }

    public void Dispose()
    {
        _diagnosticCoordinator.DiagnosticReceived -= OnDiagnosticReceived;
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }

    private void OnSettingsChanged()
    {
        Dispatcher.UIThread.Post(() => SyncPerformanceDebugLoggingFromSettings(_settingsService.Current));
    }

    private void SyncPerformanceDebugLoggingFromSettings(AppSettings settings)
    {
        IsPerformanceDebugLoggingEnabled = settings.PerformanceDebugLoggingEnabled;
    }

    private async Task ConvertSelectionAsync(LayoutConversionDirection direction)
    {
        if (IsLayoutConversionRunning)
        {
            return;
        }

        IsLayoutConversionRunning = true;
        LayoutConversionStatus = "Чтение и преобразование выделенного текста в активном приложении...";

        try
        {
            var result = await _manualLayoutConversionService
                .ConvertSelectedTextAsync(direction)
                .ConfigureAwait(true);

            LayoutConversionStatus = FormatLayoutConversionStatus(result);
        }
        catch (Exception)
        {
            LayoutConversionStatus = "Непредвиденная ошибка ручного преобразования раскладки.";
        }
        finally
        {
            IsLayoutConversionRunning = false;
        }
    }

    private static string FormatLayoutConversionStatus(LayoutConversionResult result)
    {
        return result.Status switch
        {
            Core.Models.LayoutConversionStatus.Success =>
                $"Выделение успешно преобразовано ({result.CharacterCount} символов). Текст не записывается и не сохраняется.",
            Core.Models.LayoutConversionStatus.Blocked =>
                $"Преобразование заблокировано: {result.FailureReason}",
            Core.Models.LayoutConversionStatus.NoSelection =>
                "Нет доступного выделенного текста. Сначала выделите текст в целевом приложении. Нажатие этой кнопки переносит фокус в Smart Input, поэтому преобразование удобнее запускать горячей клавишей, когда фокус остаётся в целевом приложении.",
            Core.Models.LayoutConversionStatus.NotSupported =>
                $"Преобразование не поддерживается: {result.FailureReason}",
            _ => $"Ошибка преобразования: {result.FailureReason ?? "Неизвестная ошибка"}",
        };
    }

    private async Task RunManualCorrectionAsync(ManualCorrectionRequest request)
    {
        if (IsManualCorrectionRunning)
        {
            return;
        }

        IsManualCorrectionRunning = true;
        ManualCorrectionStatus = "Ручное исправление выделенного текста...";

        try
        {
            var result = await _manualSelectedTextCorrectionService
                .ExecuteAsync(request)
                .ConfigureAwait(true);
            ManualCorrectionStatus = FormatManualCorrectionStatus(request.Action, result);
        }
        catch (Exception)
        {
            ManualCorrectionStatus = "Непредвиденная ошибка ручного исправления.";
        }
        finally
        {
            IsManualCorrectionRunning = false;
        }
    }

    private static string FormatManualCorrectionStatus(
        ManualCorrectionActionKind action,
        ManualCorrectionResult result)
    {
        var actionLabel = action switch
        {
            ManualCorrectionActionKind.FixLayout => "Исправление раскладки",
            ManualCorrectionActionKind.FixSpelling => "Исправление орфографии",
            ManualCorrectionActionKind.FixText => "Исправление текста",
            _ => "Ручное исправление",
        };

        return result.Status switch
        {
            Core.Models.ManualCorrectionStatus.Success =>
                $"{actionLabel}: успешно. символов={result.CharacterCount}; проверено={result.TokensExamined}; изменено={result.TokensChanged}; раскладка={result.LayoutApplied}; орфография={result.SpellingApplied}. Текст не записывается.",
            Core.Models.ManualCorrectionStatus.NoChange =>
                $"{actionLabel}: изменений нет. проверено={result.TokensExamined}.",
            Core.Models.ManualCorrectionStatus.NoSelection =>
                $"{actionLabel}: нет доступного выделенного текста.",
            Core.Models.ManualCorrectionStatus.Blocked =>
                $"{actionLabel} заблокировано: {result.FailureReason}",
            Core.Models.ManualCorrectionStatus.Cancelled =>
                $"{actionLabel} отменено: {result.FailureReason}",
            _ => $"Ошибка «{actionLabel}»: {result.FailureReason ?? "Неизвестная ошибка"}",
        };
    }

    private static string FormatUndoStatus(CorrectionUndoResult result)
    {
        return result.Outcome switch
        {
            CorrectionUndoOutcome.Success =>
                $"Отмена для {result.Kind}: успешно. Исходный текст восстановлен. Текст слов не записывается.",
            CorrectionUndoOutcome.NotAvailable =>
                "Отмена недоступна. Сначала выполните успешное автоматическое исправление.",
            CorrectionUndoOutcome.Expired =>
                "Время для отмены истекло.",
            CorrectionUndoOutcome.Blocked =>
                $"Отмена заблокирована: {result.FailureReason}",
            CorrectionUndoOutcome.AbortedByUserInput =>
                "Отмена прервана: обнаружен одновременный ввод пользователя.",
            _ => $"Ошибка отмены: {result.FailureReason ?? "Неизвестная ошибка"}",
        };
    }

    private static string FormatReplacementStatus(TextReplacementResult result)
    {
        return result.Status switch
        {
            TextReplacementStatus.Success =>
                $"Демонстрация выполнена. Удалено символов: {result.DeletedCharacterCount}; вставлено: {result.InsertedCharacterCount}.",
            TextReplacementStatus.NotSupported =>
                $"Демонстрация не поддерживается: {result.FailureReason}",
            TextReplacementStatus.Blocked =>
                $"Демонстрация заблокирована: {result.FailureReason}",
            TextReplacementStatus.AbortedByUserInput =>
                $"Демонстрация прервана из-за одновременного ввода. До прерывания удалено: {result.DeletedCharacterCount}; вставлено: {result.InsertedCharacterCount}.",
            _ => $"Ошибка демонстрации: {result.FailureReason ?? "Неизвестная ошибка"}",
        };
    }

    private static string FormatRustShadowAuditStatus(RustShadowAuditSnapshot snapshot)
    {
        var status = snapshot.Status;
        var averageMicroseconds = status.Observed == 0
            ? 0
            : status.TotalEvaluationMicroseconds / status.Observed;
        return
            $"состояние={snapshot.ProviderState}; проверено={status.Observed}; недоступен={status.ProviderUnavailable}; " +
            $"ошибок_провайдера={status.NativeFailures}; оба_без_замены={status.BothKeep}; " +
            $"Rust_не_согласен_с_применением={status.RustKeepsOwnApplies}; " +
            $"Rust_предложил_при_CSharp_ожидании={status.RustAppliesOwnKeeps}; " +
            $"совпавших_замен={status.MatchingApplies}; различающихся_замен={status.DifferingApplies}; " +
            $"CSharp_раскладка={status.OwnLayoutApplies}; CSharp_орфография={status.OwnSpellingApplies}; " +
            $"CSharp_комбинированных={status.OwnCombinedApplies}; CSharp_ожидание={status.OwnWaits}; " +
            $"Rust_раскладка={status.RustLayoutReplacements}; Rust_орфография={status.RustSpellingReplacements}; " +
            $"Rust_защита={status.RustProtectedKeeps}; Rust_низкая_уверенность={status.RustLowConfidenceKeeps}; " +
            $"кандидатов_на_ручную_проверку={status.CandidatesForHumanReview}; " +
            $"средняя_задержка_мкс={averageMicroseconds}; максимум_мкс={status.MaxEvaluationMicroseconds}";
    }

    private void LoadExistingEvents()
    {
        foreach (var diagnosticEvent in _diagnosticCoordinator.GetRecentEvents())
        {
            DiagnosticEvents.Add(new InputDiagnosticEntryViewModel(diagnosticEvent));
        }

        UpdateMonitoringStatus();
    }

    private void OnDiagnosticReceived(object? sender, InputDiagnosticEvent diagnosticEvent)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DiagnosticEvents.Insert(0, new InputDiagnosticEntryViewModel(diagnosticEvent));

            while (DiagnosticEvents.Count > MaxDisplayedEvents)
            {
                DiagnosticEvents.RemoveAt(DiagnosticEvents.Count - 1);
            }

            IsMonitoring = diagnosticEvent.IsMonitoringActive;
            UpdateMonitoringStatus();
        });
    }

    private void UpdateMonitoringStatus()
    {
        IsMonitoring = _diagnosticCoordinator.IsMonitoring;
        MonitoringStatusText = IsMonitoring
            ? "Наблюдение активно — отслеживаются только агрегированные события клавиатуры."
            : "Наблюдение неактивно — включите защиту на главной странице.";
    }
}
