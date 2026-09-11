using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Hotkeys;

namespace SmartInput.App.Services;

public interface IManualCorrectionHotkeyCoordinator
{
    IReadOnlyDictionary<string, GlobalHotkeyRegistrationResult?> RegistrationStatuses { get; }

    IReadOnlyDictionary<string, string> CurrentBindingDisplayNames { get; }

    event EventHandler? RegistrationChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<GlobalHotkeyRegistrationResult> ApplyBindingAsync(
        string hotkeyId,
        string hotkeyCombination,
        CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

public sealed class ManualCorrectionHotkeyCoordinator : IManualCorrectionHotkeyCoordinator, IAsyncDisposable
{
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly IManualSelectedTextCorrectionService _manualSelectedTextCorrectionService;
    private readonly ISettingsService _settingsService;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;
    private readonly ILogger<ManualCorrectionHotkeyCoordinator> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, GlobalHotkeyRegistrationResult?> _registrationStatuses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _currentBindingDisplayNames = new(StringComparer.Ordinal);

    private bool _initialized;
    private int _invocationGate;

    public ManualCorrectionHotkeyCoordinator(
        IGlobalHotkeyService globalHotkeyService,
        IManualSelectedTextCorrectionService manualSelectedTextCorrectionService,
        ISettingsService settingsService,
        IPerformanceMetricsRecorder performanceMetrics,
        ILogger<ManualCorrectionHotkeyCoordinator> logger)
    {
        _globalHotkeyService = globalHotkeyService;
        _manualSelectedTextCorrectionService = manualSelectedTextCorrectionService;
        _settingsService = settingsService;
        _performanceMetrics = performanceMetrics;
        _logger = logger;

        foreach (var definition in ManualCorrectionHotkeyCatalog.Definitions)
        {
            _registrationStatuses[definition.HotkeyId] = null;
            _currentBindingDisplayNames[definition.HotkeyId] = definition.DefaultHotkey;
        }
    }

    public IReadOnlyDictionary<string, GlobalHotkeyRegistrationResult?> RegistrationStatuses
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<string, GlobalHotkeyRegistrationResult?>(_registrationStatuses, StringComparer.Ordinal);
            }
        }
    }

    public IReadOnlyDictionary<string, string> CurrentBindingDisplayNames
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<string, string>(_currentBindingDisplayNames, StringComparer.Ordinal);
            }
        }
    }

    public event EventHandler? RegistrationChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _globalHotkeyService.HotkeyPressed += OnHotkeyPressed;
        _initialized = true;

        foreach (var definition in ManualCorrectionHotkeyCatalog.Definitions)
        {
            var combination = ManualCorrectionHotkeyCatalog.ResolveConfiguredHotkey(
                _settingsService.Current,
                definition);

            await ApplyBindingAsync(definition.HotkeyId, combination, persist: false, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task<GlobalHotkeyRegistrationResult> ApplyBindingAsync(
        string hotkeyId,
        string hotkeyCombination,
        CancellationToken cancellationToken = default)
    {
        return ApplyBindingAsync(hotkeyId, hotkeyCombination, persist: true, cancellationToken);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return;
        }

        _globalHotkeyService.HotkeyPressed -= OnHotkeyPressed;

        foreach (var definition in ManualCorrectionHotkeyCatalog.Definitions)
        {
            await _globalHotkeyService
                .UnregisterAsync(definition.HotkeyId, cancellationToken)
                .ConfigureAwait(false);

            SetStatus(definition.HotkeyId, null, definition.DefaultHotkey);
        }

        _initialized = false;
        RegistrationChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(ShutdownAsync());
    }

    private async Task<GlobalHotkeyRegistrationResult> ApplyBindingAsync(
        string hotkeyId,
        string hotkeyCombination,
        bool persist,
        CancellationToken cancellationToken)
    {
        var definition = ManualCorrectionHotkeyCatalog.TryGetDefinition(hotkeyId)
            ?? throw new ArgumentException($"Unknown manual correction hotkey id '{hotkeyId}'.", nameof(hotkeyId));

        var binding = HotkeyBindingParser.Parse(hotkeyCombination);
        lock (_sync)
        {
            _currentBindingDisplayNames[hotkeyId] = string.IsNullOrWhiteSpace(binding.DisplayName)
                ? hotkeyCombination.Trim()
                : binding.DisplayName;
        }

        if (binding.ValidationState == HotkeyValidationState.Empty)
        {
            await _globalHotkeyService
                .UnregisterAsync(hotkeyId, cancellationToken)
                .ConfigureAwait(false);

            if (persist)
            {
                await _settingsService
                    .UpdateAsync(
                        settings => ManualCorrectionHotkeyCatalog.SetConfiguredHotkey(settings, hotkeyId, string.Empty),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var unassigned = GlobalHotkeyRegistrationResult.Unassigned(hotkeyId);
            SetStatus(hotkeyId, unassigned, string.Empty);
            return unassigned;
        }

        if (!binding.IsValid)
        {
            await _globalHotkeyService
                .UnregisterAsync(hotkeyId, cancellationToken)
                .ConfigureAwait(false);

            var invalid = GlobalHotkeyRegistrationResult.Invalid(
                hotkeyId,
                binding.DisplayName,
                binding.ValidationMessage ?? "Hotkey binding is invalid.");

            SetStatus(hotkeyId, invalid, binding.DisplayName);
            return invalid;
        }

        if (persist)
        {
            await _settingsService
                .UpdateAsync(
                    settings => ManualCorrectionHotkeyCatalog.SetConfiguredHotkey(settings, hotkeyId, binding.DisplayName),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var result = await _globalHotkeyService
            .RegisterAsync(
                hotkeyId,
                (uint)binding.Modifiers,
                (uint)binding.VirtualKey,
                binding.DisplayName,
                cancellationToken)
            .ConfigureAwait(false);

        SetStatus(hotkeyId, result, result.DisplayName ?? binding.DisplayName);
        return result;
    }

    private void SetStatus(string hotkeyId, GlobalHotkeyRegistrationResult? result, string? displayName)
    {
        lock (_sync)
        {
            _registrationStatuses[hotkeyId] = result;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                _currentBindingDisplayNames[hotkeyId] = displayName;
            }
        }

        RegistrationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHotkeyPressed(object? sender, GlobalHotkeyPressedEventArgs args)
    {
        if (ManualCorrectionHotkeyCatalog.TryGetDefinition(args.HotkeyId) is null)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _invocationGate, 1, 0) != 0)
        {
            return;
        }

        _ = InvokeManualCorrectionAsync(args.HotkeyId);
    }

    private async Task InvokeManualCorrectionAsync(string hotkeyId)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var request = ManualCorrectionHotkeyCatalog.CreateRequest(hotkeyId);
            var result = await _manualSelectedTextCorrectionService
                .ExecuteAsync(request)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Manual correction hotkey {HotkeyId} completed with status={Status}.",
                hotkeyId,
                result.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual correction hotkey {HotkeyId} invocation failed.", hotkeyId);
        }
        finally
        {
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.HotkeyHandling,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            Interlocked.Exchange(ref _invocationGate, 0);
        }
    }
}
