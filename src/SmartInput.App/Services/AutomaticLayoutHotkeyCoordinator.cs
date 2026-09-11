using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Hotkeys;

namespace SmartInput.App.Services;

/// <summary>
/// Owns the single global shortcut that enables or disables automatic layout
/// correction. It intentionally does not change Protection, manual correction,
/// or the user's ordinary Windows layout shortcut.
/// </summary>
public interface IAutomaticLayoutHotkeyCoordinator
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

public sealed class AutomaticLayoutHotkeyCoordinator : IAutomaticLayoutHotkeyCoordinator, IAsyncDisposable
{
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ISettingsService _settingsService;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;
    private readonly ILogger<AutomaticLayoutHotkeyCoordinator> _logger;
    private readonly object _sync = new();

    private GlobalHotkeyRegistrationResult? _registrationStatus;
    private string _currentBindingDisplayName = HotkeyDefaults.ToggleAutomaticLayoutHotkey;
    private bool _initialized;
    private int _toggleGate;

    public AutomaticLayoutHotkeyCoordinator(
        IGlobalHotkeyService globalHotkeyService,
        ISettingsService settingsService,
        IPerformanceMetricsRecorder performanceMetrics,
        ILogger<AutomaticLayoutHotkeyCoordinator> logger)
    {
        _globalHotkeyService = globalHotkeyService;
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

        var configured = _settingsService.Current.ToggleAutomaticLayoutHotkey;
        var binding = string.IsNullOrWhiteSpace(configured)
            ? HotkeyDefaults.ToggleAutomaticLayoutHotkey
            : configured;

        await ApplyBindingAsync(binding, persist: false, cancellationToken).ConfigureAwait(false);
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
        await _globalHotkeyService.UnregisterAsync(HotkeyDefaults.ToggleAutomaticLayoutId, cancellationToken)
            .ConfigureAwait(false);

        lock (_sync)
        {
            _registrationStatus = null;
        }

        _initialized = false;
        RegistrationChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(ShutdownAsync());
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

        if (binding.ValidationState == HotkeyValidationState.Empty)
        {
            await _globalHotkeyService.UnregisterAsync(HotkeyDefaults.ToggleAutomaticLayoutId, cancellationToken)
                .ConfigureAwait(false);

            if (persist)
            {
                await _settingsService.UpdateAsync(
                    settings => settings.ToggleAutomaticLayoutHotkey = string.Empty,
                    cancellationToken).ConfigureAwait(false);
            }

            var unassigned = GlobalHotkeyRegistrationResult.Unassigned(HotkeyDefaults.ToggleAutomaticLayoutId);
            SetStatus(unassigned);
            return unassigned;
        }

        if (!binding.IsValid)
        {
            await _globalHotkeyService.UnregisterAsync(HotkeyDefaults.ToggleAutomaticLayoutId, cancellationToken)
                .ConfigureAwait(false);

            var invalid = GlobalHotkeyRegistrationResult.Invalid(
                HotkeyDefaults.ToggleAutomaticLayoutId,
                binding.DisplayName,
                binding.ValidationMessage ?? "Hotkey binding is invalid.");
            SetStatus(invalid);
            return invalid;
        }

        if (persist)
        {
            await _settingsService.UpdateAsync(
                settings => settings.ToggleAutomaticLayoutHotkey = binding.DisplayName,
                cancellationToken).ConfigureAwait(false);
        }

        var result = await _globalHotkeyService.RegisterAsync(
            HotkeyDefaults.ToggleAutomaticLayoutId,
            (uint)binding.Modifiers,
            (uint)binding.VirtualKey,
            binding.DisplayName,
            cancellationToken).ConfigureAwait(false);

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
        if (!string.Equals(args.HotkeyId, HotkeyDefaults.ToggleAutomaticLayoutId, StringComparison.Ordinal)
            || Interlocked.CompareExchange(ref _toggleGate, 1, 0) != 0)
        {
            return;
        }

        _ = ToggleAutomaticLayoutAsync();
    }

    private async Task ToggleAutomaticLayoutAsync()
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await _settingsService.UpdateAsync(
                settings => settings.AutomaticLayoutEnabled = !settings.AutomaticLayoutEnabled)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Automatic layout correction toggled. enabled={Enabled}",
                _settingsService.Current.AutomaticLayoutEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic layout hotkey invocation failed.");
        }
        finally
        {
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.HotkeyHandling,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            Interlocked.Exchange(ref _toggleGate, 0);
        }
    }
}
