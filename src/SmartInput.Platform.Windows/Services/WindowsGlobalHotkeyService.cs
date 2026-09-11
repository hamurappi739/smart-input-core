using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartInput.Platform.Abstractions.Hotkeys;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private readonly ILogger<WindowsGlobalHotkeyService> _logger;
    private readonly ConcurrentDictionary<string, RegisteredHotkey> _registrations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, string> _idToHotkeyId = new();
    private readonly ConcurrentQueue<HotkeyWorkItem> _workItems = new();
    private readonly object _lifecycleSync = new();

    private Thread? _messageThread;
    private uint _messageThreadId;
    private ManualResetEventSlim? _threadReady;
    private Exception? _threadStartupFailure;
    private volatile bool _isRunning;
    private volatile bool _disposed;
    private int _nextHotkeyId = 1;

    public WindowsGlobalHotkeyService(ILogger<WindowsGlobalHotkeyService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<GlobalHotkeyPressedEventArgs>? HotkeyPressed;

    public async Task<GlobalHotkeyRegistrationResult> RegisterAsync(
        string hotkeyId,
        uint modifiers,
        uint virtualKey,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hotkeyId);
        ArgumentNullException.ThrowIfNull(displayName);

        EnsureStarted();

        var completion = new TaskCompletionSource<GlobalHotkeyRegistrationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _workItems.Enqueue(new HotkeyWorkItem
        {
            Kind = HotkeyWorkKind.Register,
            HotkeyId = hotkeyId,
            Modifiers = modifiers | Win32Hotkey.ModNoRepeat,
            VirtualKey = virtualKey,
            DisplayName = displayName,
            Completion = completion,
        });

        WakeMessageThread();

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<GlobalHotkeyRegistrationResult>)state!).TrySetCanceled(),
            completion);

        return await completion.Task.ConfigureAwait(false);
    }

    public async Task UnregisterAsync(string hotkeyId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hotkeyId);

        if (!_isRunning)
        {
            return;
        }

        var completion = new TaskCompletionSource<GlobalHotkeyRegistrationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _workItems.Enqueue(new HotkeyWorkItem
        {
            Kind = HotkeyWorkKind.Unregister,
            HotkeyId = hotkeyId,
            Completion = completion,
        });

        WakeMessageThread();

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<GlobalHotkeyRegistrationResult>)state!).TrySetCanceled(),
            completion);

        await completion.Task.ConfigureAwait(false);
    }

    public async Task UnregisterAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        var completion = new TaskCompletionSource<GlobalHotkeyRegistrationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _workItems.Enqueue(new HotkeyWorkItem
        {
            Kind = HotkeyWorkKind.UnregisterAll,
            Completion = completion,
        });

        WakeMessageThread();

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<GlobalHotkeyRegistrationResult>)state!).TrySetCanceled(),
            completion);

        await completion.Task.ConfigureAwait(false);
    }

    public GlobalHotkeyRegistrationResult? GetRegistration(string hotkeyId)
    {
        return _registrations.TryGetValue(hotkeyId, out var registration)
            ? registration.Result
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_isRunning)
            {
                await UnregisterAllAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unregister hotkeys during shutdown.");
        }

        lock (_lifecycleSync)
        {
            if (_messageThreadId != 0)
            {
                Win32Hotkey.PostThreadMessage(_messageThreadId, Win32Hotkey.WmQuit, 0, 0);
            }

            _messageThread?.Join(TimeSpan.FromSeconds(3));
            _messageThread = null;
            _isRunning = false;
            _threadReady?.Dispose();
            _threadReady = null;
        }
    }

    private void EnsureStarted()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WindowsGlobalHotkeyService));
            }

            if (_isRunning)
            {
                return;
            }

            _threadReady = new ManualResetEventSlim(false);
            _threadStartupFailure = null;
            _messageThread = new Thread(MessageThreadMain)
            {
                IsBackground = true,
                Name = "SmartInput-GlobalHotkeys",
            };
            _messageThread.SetApartmentState(ApartmentState.STA);
            _messageThread.Start();

            if (!_threadReady.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("Global hotkey message thread failed to start.");
            }

            if (_threadStartupFailure is not null)
            {
                throw new InvalidOperationException("Global hotkey message thread failed to initialize.", _threadStartupFailure);
            }

            _isRunning = true;
        }
    }

    private void WakeMessageThread()
    {
        if (_messageThreadId != 0)
        {
            if (!Win32Hotkey.PostThreadMessage(_messageThreadId, Win32Hotkey.WmRegisterHotkey, 0, 0))
            {
                _logger.LogWarning(
                    "Could not notify the global-hotkey message thread (Win32 error {ErrorCode}).",
                    Marshal.GetLastWin32Error());
            }
        }
    }

    private void MessageThreadMain()
    {
        try
        {
            _messageThreadId = Win32Hotkey.GetCurrentThreadId();

            // Force Windows to create this thread's message queue before reporting it ready.
            // PostThreadMessage fails when called before that queue exists, which previously
            // made initial hotkey registration nondeterministic.
            Win32Hotkey.PeekMessage(out _, 0, 0, 0, Win32Hotkey.PmNoRemove);
            _threadReady?.Set();

            while (Win32Hotkey.GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Message == Win32Hotkey.WmHotkey)
                {
                    HandleHotkeyPressed((int)message.WParam);
                    continue;
                }

                if (message.Message is Win32Hotkey.WmRegisterHotkey or Win32Hotkey.WmUnregisterHotkey)
                {
                    ProcessWorkItems();
                    continue;
                }

                Win32Hotkey.TranslateMessage(ref message);
                Win32Hotkey.DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            _threadStartupFailure ??= ex;
            _logger.LogError(ex, "Global-hotkey message thread stopped unexpectedly.");
            _threadReady?.Set();
        }
        finally
        {
            UnregisterAllOnThread();
            _messageThreadId = 0;
            _isRunning = false;
        }
    }

    private void ProcessWorkItems()
    {
        while (_workItems.TryDequeue(out var workItem))
        {
            try
            {
                switch (workItem.Kind)
                {
                    case HotkeyWorkKind.Register:
                        workItem.Completion?.TrySetResult(RegisterOnThread(workItem));
                        break;
                    case HotkeyWorkKind.Unregister:
                        UnregisterOnThread(workItem.HotkeyId!);
                        workItem.Completion?.TrySetResult(
                            GlobalHotkeyRegistrationResult.Registered(workItem.HotkeyId!, string.Empty));
                        break;
                    case HotkeyWorkKind.UnregisterAll:
                        UnregisterAllOnThread();
                        workItem.Completion?.TrySetResult(
                            GlobalHotkeyRegistrationResult.Registered(string.Empty, string.Empty));
                        break;
                }
            }
            catch (Exception ex)
            {
                workItem.Completion?.TrySetException(ex);
            }
        }
    }

    private GlobalHotkeyRegistrationResult RegisterOnThread(HotkeyWorkItem workItem)
    {
        UnregisterOnThread(workItem.HotkeyId!);

        var id = _nextHotkeyId++;
        if (!Win32Hotkey.RegisterHotKey(0, id, workItem.Modifiers, workItem.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == Win32Hotkey.ErrorHotkeyAlreadyRegistered)
            {
                var conflict = GlobalHotkeyRegistrationResult.Conflict(
                    workItem.HotkeyId!,
                    workItem.DisplayName!,
                    $"Hotkey '{workItem.DisplayName}' is already registered by another application.");
                _registrations[workItem.HotkeyId!] = new RegisteredHotkey(id, conflict, RegisteredNative: false);
                return conflict;
            }

            var failed = GlobalHotkeyRegistrationResult.Failed(
                workItem.HotkeyId!,
                workItem.DisplayName,
                $"Failed to register hotkey '{workItem.DisplayName}' (Win32 error {error}).");
            _registrations[workItem.HotkeyId!] = new RegisteredHotkey(id, failed, RegisteredNative: false);
            return failed;
        }

        var success = GlobalHotkeyRegistrationResult.Registered(workItem.HotkeyId!, workItem.DisplayName!);
        _registrations[workItem.HotkeyId!] = new RegisteredHotkey(id, success, RegisteredNative: true);
        _idToHotkeyId[id] = workItem.HotkeyId!;
        _logger.LogInformation("Registered global hotkey id={HotkeyId}.", workItem.HotkeyId);
        return success;
    }

    private void UnregisterOnThread(string hotkeyId)
    {
        if (!_registrations.TryRemove(hotkeyId, out var existing))
        {
            return;
        }

        if (existing.RegisteredNative)
        {
            Win32Hotkey.UnregisterHotKey(0, existing.NativeId);
            _idToHotkeyId.TryRemove(existing.NativeId, out _);
            _logger.LogInformation("Unregistered global hotkey id={HotkeyId}.", hotkeyId);
        }
    }

    private void UnregisterAllOnThread()
    {
        foreach (var hotkeyId in _registrations.Keys.ToArray())
        {
            UnregisterOnThread(hotkeyId);
        }
    }

    private void HandleHotkeyPressed(int nativeId)
    {
        if (!_idToHotkeyId.TryGetValue(nativeId, out var hotkeyId))
        {
            return;
        }

        try
        {
            HotkeyPressed?.Invoke(this, new GlobalHotkeyPressedEventArgs { HotkeyId = hotkeyId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Global hotkey handler failed for id={HotkeyId}.", hotkeyId);
        }
    }

    private enum HotkeyWorkKind
    {
        Register,
        Unregister,
        UnregisterAll,
    }

    private sealed class HotkeyWorkItem
    {
        public HotkeyWorkKind Kind { get; init; }

        public string? HotkeyId { get; init; }

        public uint Modifiers { get; init; }

        public uint VirtualKey { get; init; }

        public string? DisplayName { get; init; }

        public TaskCompletionSource<GlobalHotkeyRegistrationResult>? Completion { get; init; }
    }

    private sealed record RegisteredHotkey(
        int NativeId,
        GlobalHotkeyRegistrationResult Result,
        bool RegisteredNative);
}
