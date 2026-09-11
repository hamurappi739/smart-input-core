using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Windows.Native;
using SmartInput.Platform.Windows.Overlay;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsCorrectionNotificationOverlayService
    : ICorrectionNotificationOverlayService, IAsyncDisposable
{
    internal const int OverlayMessageBase = 0x8020;
    internal const int OverlayMessageShow = OverlayMessageBase;
    internal const int OverlayMessageHide = OverlayMessageBase + 1;
    internal const int OverlayMessageShutdown = OverlayMessageBase + 2;

    internal static readonly int WindowExtendedStyles = PredictionOverlayWindowStyles.ExtendedStyles;

    private readonly ILogger<WindowsCorrectionNotificationOverlayService> _logger;
    private readonly ConcurrentQueue<OverlayWorkItem> _workItems = new();
    private TaskCompletionSource _threadReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _lifecycleSync = new();

    private Thread? _uiThread;
    private uint _uiThreadId;
    private volatile bool _disposed;
    private volatile bool _isRunning;
    private volatile CorrectionNotificationStatus _status =
        new(CorrectionNotificationVisibility.Hidden, false);

    public WindowsCorrectionNotificationOverlayService(
        ILogger<WindowsCorrectionNotificationOverlayService> logger)
    {
        _logger = logger;
    }

    public CorrectionNotificationStatus Status => _status;

    public async Task ShowAsync(
        CorrectionNotificationContent content,
        PredictionOverlayPlacement placement,
        CancellationToken cancellationToken = default)
    {
        await EnqueueAsync(OverlayWorkKind.Show, content, placement, cancellationToken).ConfigureAwait(false);
    }

    public async Task HideAsync(CancellationToken cancellationToken = default)
    {
        await EnqueueAsync(OverlayWorkKind.Hide, null, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await EnqueueAsync(OverlayWorkKind.Shutdown, null, null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
    }

    private async Task EnqueueAsync(
        OverlayWorkKind kind,
        CorrectionNotificationContent? content,
        PredictionOverlayPlacement? placement,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureStarted();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _workItems.Enqueue(new OverlayWorkItem
        {
            Kind = kind,
            Content = content,
            Placement = placement,
            Completion = completion,
        });

        WakeUiThread();

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            completion);

        await completion.Task.ConfigureAwait(false);
    }

    private void EnsureStarted()
    {
        lock (_lifecycleSync)
        {
            if (_isRunning)
            {
                return;
            }

            _threadReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiThread = new Thread(RunUiThread)
            {
                IsBackground = true,
                Name = "SmartInput.CorrectionNotification",
            };

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            _isRunning = true;
        }

        _threadReady.Task.GetAwaiter().GetResult();
    }

    private void WakeUiThread()
    {
        if (_uiThreadId != 0)
        {
            Win32Overlay.PostThreadMessage(_uiThreadId, OverlayMessageShow, 0, 0);
        }
    }

    private void RunUiThread()
    {
        _uiThreadId = GetCurrentThreadId();

        nint windowHandle = 0;
        OverlayRenderState? renderState = null;

        try
        {
            windowHandle = OverlayWindowFactory.EnsureWindowCreated();
            renderState = new OverlayRenderState(windowHandle);
            _status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Hidden, true);
            _threadReady.TrySetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize correction notification overlay window.");
            _threadReady.TrySetException(ex);
            return;
        }

        while (!_disposed)
        {
            DrainWorkQueue(renderState);

            if (!Win32Overlay.GetMessage(out var message, 0, 0, 0))
            {
                break;
            }

            if (message.Message >= OverlayMessageBase && message.Message <= OverlayMessageShutdown)
            {
                DrainWorkQueue(renderState);
                continue;
            }

            Win32Overlay.TranslateMessage(ref message);
            Win32Overlay.DispatchMessage(ref message);
        }

        if (windowHandle != 0)
        {
            Win32Overlay.DestroyWindow(windowHandle);
        }

        _status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Hidden, false);
    }

    private void DrainWorkQueue(OverlayRenderState renderState)
    {
        while (_workItems.TryDequeue(out var workItem))
        {
            try
            {
                switch (workItem.Kind)
                {
                    case OverlayWorkKind.Show:
                        renderState.Show(workItem.Content!, workItem.Placement!);
                        _status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Visible, true);
                        break;
                    case OverlayWorkKind.Hide:
                        renderState.Hide();
                        _status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Hidden, true);
                        break;
                    case OverlayWorkKind.Shutdown:
                        _disposed = true;
                        renderState.Hide();
                        _status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Hidden, false);
                        break;
                }

                workItem.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Correction notification overlay work item failed.");
                workItem.Completion.TrySetException(ex);
            }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private sealed class OverlayWorkItem
    {
        public required OverlayWorkKind Kind { get; init; }

        public CorrectionNotificationContent? Content { get; init; }

        public PredictionOverlayPlacement? Placement { get; init; }

        public required TaskCompletionSource Completion { get; init; }
    }

    private enum OverlayWorkKind
    {
        Show,
        Hide,
        Shutdown,
    }

    private sealed class OverlayRenderState
    {
        private const int HorizontalPadding = 12;
        private const int VerticalPadding = 6;

        private readonly nint _windowHandle;
        private readonly Font _font;
        private readonly object _renderSync = new();

        public OverlayRenderState(nint windowHandle)
        {
            _windowHandle = windowHandle;
            _font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
        }

        public void Show(CorrectionNotificationContent content, PredictionOverlayPlacement placement)
        {
            Render(content, placement);
        }

        public void Hide()
        {
            lock (_renderSync)
            {
                Win32Overlay.SetWindowPos(
                    _windowHandle,
                    0,
                    0,
                    0,
                    0,
                    0,
                    (uint)(Win32Overlay.SwpHideWindow | Win32Overlay.SwpNoActivate | Win32Overlay.SwpNoMove | Win32Overlay.SwpNoSize));
            }
        }

        private void Render(CorrectionNotificationContent content, PredictionOverlayPlacement placement)
        {
            lock (_renderSync)
            {
                using var measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
                using var measureGraphics = Graphics.FromImage(measureBitmap);
                measureGraphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                var textSize = measureGraphics.MeasureString(
                    content.Message,
                    _font,
                    int.MaxValue,
                    StringFormat.GenericTypographic);

                var width = Math.Max(1, (int)Math.Ceiling(textSize.Width) + HorizontalPadding * 2);
                var height = Math.Max(1, (int)Math.Ceiling(textSize.Height) + VerticalPadding * 2);

                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                graphics.Clear(Color.Transparent);

                using var backgroundBrush = new SolidBrush(Color.FromArgb(220, 40, 40, 40));
                using var path = CreateRoundedRect(0, 0, width, height, 6);
                graphics.FillPath(backgroundBrush, path);

                using var textBrush = new SolidBrush(Color.FromArgb(245, 245, 245));
                graphics.DrawString(
                    content.Message,
                    _font,
                    textBrush,
                    HorizontalPadding,
                    VerticalPadding,
                    StringFormat.GenericTypographic);

                ApplyLayeredBitmap(width, height, placement.X, placement.Y, bitmap);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRect(
            int x,
            int y,
            int width,
            int height,
            int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ApplyLayeredBitmap(int width, int height, int x, int y, Bitmap bitmap)
        {
            var screenDc = Win32Overlay.GetDC(0);
            var memoryDc = Win32Overlay.CreateCompatibleDC(screenDc);
            var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            nint previousBitmap = 0;

            try
            {
                previousBitmap = Win32Overlay.SelectObject(memoryDc, hBitmap);

                var size = new Win32Overlay.Size { Cx = width, Cy = height };
                var destination = new Win32Overlay.Point { X = x, Y = y };
                var source = new Win32Overlay.Point { X = 0, Y = 0 };
                var blend = new Win32Overlay.BlendFunction
                {
                    BlendOp = Win32Overlay.AcSrcOver,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = Win32Overlay.AcSrcAlpha,
                };

                Win32Overlay.UpdateLayeredWindow(
                    _windowHandle,
                    screenDc,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    Win32Overlay.UlwAlpha);

                Win32Overlay.SetWindowPos(
                    _windowHandle,
                    Win32Overlay.HwndTopmost,
                    x,
                    y,
                    width,
                    height,
                    (uint)(Win32Overlay.SwpNoActivate | Win32Overlay.SwpShowWindow));
            }
            finally
            {
                if (previousBitmap != 0)
                {
                    Win32Overlay.SelectObject(memoryDc, previousBitmap);
                }

                Win32Overlay.DeleteObject(hBitmap);
                Win32Overlay.DeleteDC(memoryDc);
                Win32Overlay.ReleaseDC(0, screenDc);
            }
        }
    }

    private static class OverlayWindowFactory
    {
        private static readonly WndProcDelegate WndProcImpl = WindowProc;
        private static readonly nint WndProcPointer = Marshal.GetFunctionPointerForDelegate(WndProcImpl);
        private static bool _classRegistered;
        private static readonly object ClassSync = new();

        private const string OverlayWindowClassName = "SmartInputCorrectionNotification";

        internal static nint EnsureWindowCreated()
        {
            EnsureClassRegistered();

            var windowHandle = Win32Overlay.CreateWindowEx(
                WindowExtendedStyles,
                OverlayWindowClassName,
                string.Empty,
                Win32Overlay.WsPopup,
                0,
                0,
                1,
                1,
                Win32Overlay.HwndMessage,
                0,
                0,
                0);

            if (windowHandle == 0)
            {
                throw new InvalidOperationException("Failed to create correction notification overlay window.");
            }

            return windowHandle;
        }

        private static void EnsureClassRegistered()
        {
            lock (ClassSync)
            {
                if (_classRegistered)
                {
                    return;
                }

                var windowClass = new Win32Overlay.WndClassEx
                {
                    CbSize = Marshal.SizeOf<Win32Overlay.WndClassEx>(),
                    LpszClassName = OverlayWindowClassName,
                    LpfnWndProc = WndProcPointer,
                    HInstance = 0,
                };

                if (Win32Overlay.RegisterClassEx(ref windowClass) == 0)
                {
                    throw new InvalidOperationException(
                        "Failed to register correction notification overlay window class.");
                }

                _classRegistered = true;
            }
        }

        private static nint WindowProc(nint hWnd, uint message, nint wParam, nint lParam)
        {
            if (message == Win32Overlay.WmDestroy)
            {
                return 0;
            }

            return Win32Overlay.DefWindowProc(hWnd, message, wParam, lParam);
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate nint WndProcDelegate(nint hWnd, uint message, nint wParam, nint lParam);
    }
}
