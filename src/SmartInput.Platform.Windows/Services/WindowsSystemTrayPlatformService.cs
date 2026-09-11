using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartInput.Platform.Abstractions.Tray;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsSystemTrayPlatformService : ISystemTrayPlatformService, IDisposable
{
    private const uint WmTrayIcon = Win32Tray.WmUser + 10;
    private const uint TrayMessageProcessWork = Win32Tray.WmUser + 11;

    private const int MenuToggleProtection = 1001;
    private const int MenuToggleAutomaticLayout = 1002;
    private const int MenuToggleAutocorrect = 1003;
    private const int MenuTogglePrediction = 1004;
    private const int MenuToggleSnippets = 1005;
    private const int MenuToggleEmergencyPause = 1006;
    private const int MenuOpenSettings = 1007;
    private const int MenuExit = 1008;

    private static readonly string WindowClassName = "SmartInputTrayMessageWindow";

    private readonly ILogger<WindowsSystemTrayPlatformService> _logger;
    private readonly ConcurrentQueue<TrayWorkItem> _workItems = new();
    private readonly object _lifecycleSync = new();

    private Thread? _messageThread;
    private uint _messageThreadId;
    private ManualResetEventSlim? _threadReady;
    private volatile bool _isRunning;
    private volatile bool _disposed;
    private volatile bool _isVisible;

    private nint _windowHandle;
    private WndProcDelegate? _windowProc;
    private Icon? _icon;
    private nint _iconHandle;
    private SystemTrayMenuState _menuState = new();
    private string _tooltip = "SmartInput";

    public WindowsSystemTrayPlatformService(ILogger<WindowsSystemTrayPlatformService> logger)
    {
        _logger = logger;
    }

    public event Action<SystemTrayMenuAction>? MenuActionRequested;

    public Task<bool> TryShowAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromResult(false);
        }

        if (_isVisible)
        {
            return Task.FromResult(true);
        }

        return EnqueueBoolWorkAsync(TrayWorkKind.Show, cancellationToken);
    }

    public Task HideAsync(CancellationToken cancellationToken = default)
    {
        if (!_isVisible)
        {
            return Task.CompletedTask;
        }

        return EnqueueVoidWorkAsync(TrayWorkKind.Hide, cancellationToken);
    }

    public Task UpdateMenuStateAsync(SystemTrayMenuState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workItems.Enqueue(new TrayWorkItem
        {
            Kind = TrayWorkKind.UpdateMenuState,
            MenuState = state,
            Completion = completion,
        });

        WakeMessageThread();
        return completion.Task;
    }

    public Task SetTooltipAsync(string tooltip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workItems.Enqueue(new TrayWorkItem
        {
            Kind = TrayWorkKind.SetTooltip,
            Tooltip = tooltip,
            Completion = completion,
        });

        WakeMessageThread();
        return completion.Task;
    }

    public Task ShowNotificationAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);

        EnsureStarted();

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workItems.Enqueue(new TrayWorkItem
        {
            Kind = TrayWorkKind.ShowNotification,
            NotificationTitle = title,
            NotificationMessage = message,
            Completion = completion,
        });

        WakeMessageThread();
        return completion.Task;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return Task.CompletedTask;
        }

        return EnqueueVoidWorkAsync(TrayWorkKind.Shutdown, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ShutdownAsync().GetAwaiter().GetResult();
    }

    private Task<bool> EnqueueBoolWorkAsync(TrayWorkKind kind, CancellationToken cancellationToken)
    {
        EnsureStarted();

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workItems.Enqueue(new TrayWorkItem
        {
            Kind = kind,
            Completion = completion,
        });

        WakeMessageThread();

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(static state =>
            {
                ((TaskCompletionSource<bool>)state!).TrySetCanceled();
            }, completion);
        }

        return completion.Task;
    }

    private Task EnqueueVoidWorkAsync(TrayWorkKind kind, CancellationToken cancellationToken)
    {
        EnsureStarted();

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workItems.Enqueue(new TrayWorkItem
        {
            Kind = kind,
            Completion = completion,
        });

        WakeMessageThread();

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(static state =>
            {
                ((TaskCompletionSource<bool>)state!).TrySetCanceled();
            }, completion);
        }

        return completion.Task;
    }

    private void EnsureStarted()
    {
        if (_isRunning)
        {
            return;
        }

        lock (_lifecycleSync)
        {
            if (_isRunning)
            {
                return;
            }

            _threadReady = new ManualResetEventSlim(false);
            _messageThread = new Thread(MessageThreadMain)
            {
                IsBackground = true,
                Name = "SmartInputTrayThread",
            };
            _messageThread.SetApartmentState(ApartmentState.STA);
            _messageThread.Start();

            if (!_threadReady.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("System tray message thread failed to start.");
            }

            _isRunning = true;
        }
    }

    private void WakeMessageThread()
    {
        if (_messageThreadId != 0)
        {
            Win32Tray.PostThreadMessage(_messageThreadId, TrayMessageProcessWork, 0, 0);
        }
    }

    private void MessageThreadMain()
    {
        _messageThreadId = Win32Tray.GetCurrentThreadId();

        try
        {
            if (!CreateMessageWindow())
            {
                _threadReady?.Set();
                return;
            }

            _icon = TrayIconFactory.CreateDefaultIcon();
            _iconHandle = _icon.Handle;

            _threadReady?.Set();

            while (Win32Tray.GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Message == TrayMessageProcessWork)
                {
                    ProcessWorkItems();
                    continue;
                }

                Win32Tray.TranslateMessage(ref message);
                Win32Tray.DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System tray message thread failed.");
            _threadReady?.Set();
        }
        finally
        {
            RemoveTrayIconOnThread();
            if (_windowHandle != 0)
            {
                Win32Tray.DestroyWindow(_windowHandle);
                _windowHandle = 0;
            }

            _icon?.Dispose();
            _icon = null;
            _iconHandle = 0;
        }
    }

    private bool CreateMessageWindow()
    {
        _windowProc = WindowProc;

        var windowClass = new Win32Tray.WindowClass
        {
            LpszClassName = WindowClassName,
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
        };

        Win32Tray.RegisterClassW(ref windowClass);

        _windowHandle = Win32Tray.CreateWindowExW(
            0,
            WindowClassName,
            "SmartInputTrayHost",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);

        return _windowHandle != 0;
    }

    private nint WindowProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmTrayIcon)
        {
            if ((uint)lParam == Win32Tray.WmRButtonUp)
            {
                ShowContextMenu();
            }

            return 0;
        }

        if (message == Win32Tray.WmCommand)
        {
            HandleMenuCommand((int)wParam);
            return 0;
        }

        if (message == Win32Tray.WmDestroy)
        {
            Win32Tray.PostThreadMessage(_messageThreadId, Win32Tray.WmQuit, 0, 0);
            return 0;
        }

        return Win32Tray.DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void ProcessWorkItems()
    {
        while (_workItems.TryDequeue(out var workItem))
        {
            try
            {
                switch (workItem.Kind)
                {
                    case TrayWorkKind.Show:
                        workItem.Completion?.TrySetResult(AddTrayIconOnThread());
                        break;
                    case TrayWorkKind.Hide:
                        RemoveTrayIconOnThread();
                        workItem.Completion?.TrySetResult(true);
                        break;
                    case TrayWorkKind.UpdateMenuState:
                        _menuState = workItem.MenuState ?? new SystemTrayMenuState();
                        workItem.Completion?.TrySetResult(true);
                        break;
                    case TrayWorkKind.SetTooltip:
                        _tooltip = workItem.Tooltip ?? "SmartInput";
                        ModifyTrayIconOnThread();
                        workItem.Completion?.TrySetResult(true);
                        break;
                    case TrayWorkKind.ShowNotification:
                        ShowNotificationOnThread(
                            workItem.NotificationTitle ?? "Smart Input",
                            workItem.NotificationMessage ?? string.Empty);
                        workItem.Completion?.TrySetResult(true);
                        break;
                    case TrayWorkKind.Shutdown:
                        RemoveTrayIconOnThread();
                        if (_windowHandle != 0)
                        {
                            Win32Tray.DestroyWindow(_windowHandle);
                            _windowHandle = 0;
                        }

                        Win32Tray.PostThreadMessage(_messageThreadId, Win32Tray.WmQuit, 0, 0);
                        _isRunning = false;
                        workItem.Completion?.TrySetResult(true);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process tray work item {Kind}.", workItem.Kind);
                workItem.Completion?.TrySetResult(false);
            }
        }
    }

    private bool AddTrayIconOnThread()
    {
        if (_windowHandle == 0 || _iconHandle == 0)
        {
            return false;
        }

        if (_isVisible)
        {
            return true;
        }

        var data = CreateNotifyIconData();
        var success = Win32Tray.ShellNotifyIcon(Win32Tray.NimAdd, ref data);
        _isVisible = success;

        if (!success)
        {
            _logger.LogWarning("Shell_NotifyIcon(NIM_ADD) failed.");
        }

        return success;
    }

    private void ModifyTrayIconOnThread()
    {
        if (!_isVisible || _windowHandle == 0)
        {
            return;
        }

        var data = CreateNotifyIconData();
        Win32Tray.ShellNotifyIcon(Win32Tray.NimModify, ref data);
    }

    private void ShowNotificationOnThread(string title, string message)
    {
        if (!_isVisible || _windowHandle == 0)
        {
            return;
        }

        var data = CreateNotifyIconData();
        data.UFlags = Win32Tray.NifInfo;
        data.SzInfo = TruncateBalloonText(message, 255);
        data.SzInfoTitle = TruncateBalloonText(title, 63);
        data.DwInfoFlags = Win32Tray.NiifInfo;
        Win32Tray.ShellNotifyIcon(Win32Tray.NimModify, ref data);
    }

    private void RemoveTrayIconOnThread()
    {
        if (!_isVisible || _windowHandle == 0)
        {
            return;
        }

        var data = CreateNotifyIconData();
        Win32Tray.ShellNotifyIcon(Win32Tray.NimDelete, ref data);
        _isVisible = false;
    }

    private Win32Tray.NotifyIconData CreateNotifyIconData()
    {
        return new Win32Tray.NotifyIconData
        {
            CbSize = Marshal.SizeOf<Win32Tray.NotifyIconData>(),
            HWnd = _windowHandle,
            UId = Win32Tray.IdIcon,
            UFlags = Win32Tray.NifMessage | Win32Tray.NifIcon | Win32Tray.NifTip,
            UCallbackMessage = WmTrayIcon,
            HIcon = _iconHandle,
            SzTip = TruncateTooltip(_tooltip),
        };
    }

    private static string TruncateTooltip(string tooltip)
    {
        return tooltip.Length <= 127 ? tooltip : tooltip[..127];
    }

    private static string TruncateBalloonText(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private void ShowContextMenu()
    {
        var menu = Win32Tray.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            AppendToggle(menu, MenuToggleProtection, "Включить защиту", _menuState.IsProtectionEnabled);
            AppendToggle(menu, MenuToggleAutomaticLayout, "Включить автокоррекцию раскладки", _menuState.IsAutomaticLayoutEnabled);
            AppendToggle(menu, MenuToggleAutocorrect, "Включить автокоррекцию", _menuState.IsAutocorrectEnabled);
            AppendToggle(menu, MenuTogglePrediction, "Включить подсказки", _menuState.IsPredictionEnabled);
            AppendToggle(menu, MenuToggleSnippets, "Включить шаблоны текста", _menuState.IsSnippetsEnabled);
            Win32Tray.AppendMenu(menu, Win32Tray.MfSeparator, 0, string.Empty);
            AppendToggle(menu, MenuToggleEmergencyPause, "Экстренная пауза", _menuState.IsEmergencyPaused);
            Win32Tray.AppendMenu(menu, Win32Tray.MfSeparator, 0, string.Empty);
            Win32Tray.AppendMenu(menu, Win32Tray.MfString, MenuOpenSettings, "Открыть настройки");
            Win32Tray.AppendMenu(menu, Win32Tray.MfString, MenuExit, "Выйти");

            Win32Tray.GetCursorPos(out var cursor);
            Win32Tray.SetForegroundWindow(_windowHandle);
            var commandId = Win32Tray.TrackPopupMenu(
                menu,
                Win32Tray.TpmBottomAlign | Win32Tray.TpmLeftAlign | Win32Tray.TpmReturnCmd,
                cursor.X,
                cursor.Y,
                0,
                _windowHandle,
                0);

            // TPM_RETURNCMD returns the selected command instead of sending WM_COMMAND.
            // Dispatch it explicitly so every tray-menu item performs its action.
            if (commandId != 0)
            {
                HandleMenuCommand((int)commandId);
            }
        }
        finally
        {
            Win32Tray.DestroyMenu(menu);
        }
    }

    private static void AppendToggle(nint menu, int id, string label, bool isChecked)
    {
        var flags = Win32Tray.MfString;
        if (isChecked)
        {
            flags |= Win32Tray.MfChecked;
        }

        Win32Tray.AppendMenu(menu, flags, id, label);
    }

    private void HandleMenuCommand(int commandId)
    {
        var action = commandId switch
        {
            MenuToggleProtection => SystemTrayMenuAction.ToggleProtection,
            MenuToggleAutomaticLayout => SystemTrayMenuAction.ToggleAutomaticLayout,
            MenuToggleAutocorrect => SystemTrayMenuAction.ToggleAutocorrect,
            MenuTogglePrediction => SystemTrayMenuAction.TogglePrediction,
            MenuToggleSnippets => SystemTrayMenuAction.ToggleSnippets,
            MenuToggleEmergencyPause => SystemTrayMenuAction.ToggleEmergencyPause,
            MenuOpenSettings => SystemTrayMenuAction.OpenSettings,
            MenuExit => SystemTrayMenuAction.Exit,
            _ => (SystemTrayMenuAction?)null,
        };

        if (action is null)
        {
            return;
        }

        var handler = MenuActionRequested;
        if (handler is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => handler(action.Value));
    }

    private delegate nint WndProcDelegate(nint hWnd, uint message, nint wParam, nint lParam);

    private enum TrayWorkKind
    {
        Show,
        Hide,
        UpdateMenuState,
        SetTooltip,
        ShowNotification,
        Shutdown,
    }

    private sealed class TrayWorkItem
    {
        public TrayWorkKind Kind { get; init; }

        public SystemTrayMenuState? MenuState { get; init; }

        public string? Tooltip { get; init; }

        public string? NotificationTitle { get; init; }

        public string? NotificationMessage { get; init; }

        public TaskCompletionSource<bool>? Completion { get; init; }
    }
}
