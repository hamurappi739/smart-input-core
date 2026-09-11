using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Hotkeys;

namespace SmartInput.App.Services;

public interface IUndoHotkeyCoordinator
{
    GlobalHotkeyRegistrationResult? RegistrationStatus { get; }

    string CurrentBindingDisplayName { get; }

    event EventHandler? RegistrationChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<GlobalHotkeyRegistrationResult> ApplyBindingAsync(
        string hotkeyCombination,
        CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

public sealed class UndoHotkeyCoordinator : IUndoHotkeyCoordinator, IAsyncDisposable
{
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ICorrectionUndoService _correctionUndoService;
    private readonly ISettingsService _settingsService;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;
    private readonly ILogger<UndoHotkeyCoordinator> _logger;
    private readonly object _sync = new();

    private GlobalHotkeyRegistrationResult? _registrationStatus;
    private string _currentBindingDisplayName = HotkeyDefaults.UndoLastCorrectionHotkey;
    private bool _initialized;
    private int _undoInvocationGate;

    public UndoHotkeyCoordinator(
        IGlobalHotkeyService globalHotkeyService,
        ICorrectionUndoService correctionUndoService,
        ISettingsService settingsService,
        IPerformanceMetricsRecorder performanceMetrics,
        ILogger<UndoHotkeyCoordinator> logger)
    {
        _globalHotkeyService = globalHotkeyService;
        _correctionUndoService = correctionUndoService;
        _settingsService = settingsService;
        _performanceMetrics = performanceMetrics;
        _logger = logger;
    }

    public GlobalHotkeyRegistrationResult? RegistrationStatus
    {
        get
        {
            lock (_sync)
            {
                return _registrationStatus;
            }
        }
    }

    public string CurrentBindingDisplayName
    {
        get
        {
            lock (_sync)
            {
                return _currentBindingDisplayName;
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

        var combination = string.IsNullOrWhiteSpace(_settingsService.Current.UndoLastCorrectionHotkey)
            ? HotkeyDefaults.UndoLastCorrectionHotkey
            : _settingsService.Current.UndoLastCorrectionHotkey;

        await ApplyBindingAsync(combination, persist: false, cancellationToken).ConfigureAwait(false);
    }

    public Task<GlobalHotkeyRegistrationResult> ApplyBindingAsync(
        string hotkeyCombination,
        CancellationToken cancellationToken = default)
    {
        return ApplyBindingAsync(hotkeyCombination, persist: true, cancellationToken);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return;
        }

        _globalHotkeyService.HotkeyPressed -= OnHotkeyPressed;
        await _globalHotkeyService.UnregisterAsync(HotkeyDefaults.UndoLastCorrectionId, cancellationToken)
            .ConfigureAwait(false);

        lock (_sync)
        {
            _registrationStatus = null;
        }

        RegistrationChanged?.Invoke(this, EventArgs.Empty);
        _initialized = false;
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        await _globalHotkeyService.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<GlobalHotkeyRegistrationResult> ApplyBindingAsync(
        string hotkeyCombination,
        bool persist,
        CancellationToken cancellationToken)
    {
        var binding = HotkeyBindingParser.Parse(hotkeyCombination);
        lock (_sync)
        {
            _currentBindingDisplayName = string.IsNullOrWhiteSpace(binding.DisplayName)
                ? hotkeyCombination.Trim()
                : binding.DisplayName;
        }

        if (!binding.IsValid)
        {
            await _globalHotkeyService
                .UnregisterAsync(HotkeyDefaults.UndoLastCorrectionId, cancellationToken)
                .ConfigureAwait(false);

            var invalid = GlobalHotkeyRegistrationResult.Invalid(
                HotkeyDefaults.UndoLastCorrectionId,
                binding.DisplayName,
                binding.ValidationMessage ?? "Hotkey binding is invalid.");

            SetStatus(invalid);
            return invalid;
        }

        if (persist)
        {
            await _settingsService
                .UpdateAsync(settings => settings.UndoLastCorrectionHotkey = binding.DisplayName, cancellationToken)
                .ConfigureAwait(false);
        }

        var result = await _globalHotkeyService
            .RegisterAsync(
                HotkeyDefaults.UndoLastCorrectionId,
                (uint)binding.Modifiers,
                (uint)binding.VirtualKey,
                binding.DisplayName,
                cancellationToken)
            .ConfigureAwait(false);

        SetStatus(result);
        return result;
    }

    private void SetStatus(GlobalHotkeyRegistrationResult result)
    {
        lock (_sync)
        {
            _registrationStatus = result;
            if (!string.IsNullOrWhiteSpace(result.DisplayName))
            {
                _currentBindingDisplayName = result.DisplayName;
            }
        }

        RegistrationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHotkeyPressed(object? sender, GlobalHotkeyPressedEventArgs args)
    {
        if (!string.Equals(args.HotkeyId, HotkeyDefaults.UndoLastCorrectionId, StringComparison.Ordinal))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _undoInvocationGate, 1, 0) != 0)
        {
            return;
        }

        _ = InvokeUndoAsync();
    }

    private async Task InvokeUndoAsync()
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var result = await _correctionUndoService.TryUndoAsync().ConfigureAwait(false);
            _logger.LogInformation("Undo hotkey completed with outcome={Outcome}.", result.Outcome);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Undo hotkey invocation failed.");
        }
        finally
        {
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.HotkeyHandling,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            Interlocked.Exchange(ref _undoInvocationGate, 0);
        }
    }
}
