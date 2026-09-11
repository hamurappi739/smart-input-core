using System.Runtime.InteropServices;
using SmartInput.Platform.Abstractions.Input;
using Microsoft.Extensions.Logging;
using SmartInput.Platform.Abstractions.Text;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsSelectedTextService : ISelectedTextService
{
    private const int ClipboardRetryDelayMilliseconds = 30;
    private const int MaxClipboardFormatBytes = 64 * 1024 * 1024;

    private readonly ILogger<WindowsSelectedTextService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WindowsSelectedTextService(ILogger<WindowsSelectedTextService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        ClipboardSnapshot? backup = null;
        var clipboardChangedByCopy = false;
        var clipboardWasStable = false;
        var restoreSucceeded = true;
        string? selectedText = null;

        try
        {
            if (!ClipboardNative.TryCaptureSnapshot(out backup))
            {
                return null;
            }

            var sequenceBeforeCopy = Win32Clipboard.GetClipboardSequenceNumber();
            WindowsKeyboardShortcutSender.SendChord(
                WindowsKeyboardShortcutSender.VkControl,
                WindowsKeyboardShortcutSender.VkC);

            await Task.Delay(ClipboardRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);

            var sequenceAfterCopy = Win32Clipboard.GetClipboardSequenceNumber();
            clipboardChangedByCopy = sequenceAfterCopy != sequenceBeforeCopy;
            if (clipboardChangedByCopy)
            {
                selectedText = ClipboardNative.ReadUnicodeText();
                clipboardWasStable = Win32Clipboard.GetClipboardSequenceNumber() == sequenceAfterCopy;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Selected text read failed.");
            return null;
        }
        finally
        {
            if (clipboardChangedByCopy && clipboardWasStable && backup is not null)
            {
                restoreSucceeded = backup.TryRestore();
            }

            _gate.Release();
        }

        return clipboardChangedByCopy
            && clipboardWasStable
            && restoreSucceeded
            && !string.IsNullOrEmpty(selectedText)
            ? selectedText
            : null;
    }

    public async Task<bool> ReplaceSelectedTextAsync(
        string replacementText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacementText);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Replace the active selection directly.  Clipboard paste is not
            // used: it can race with the user's clipboard and can destroy
            // non-text formats.  One Backspace removes the selection, then
            // the replacement is sent as one contiguous injected batch.
            var inputs = BuildSelectedReplacementInputs(replacementText);
            var sent = Win32Input.SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<Win32Input.Input>());
            if (sent != inputs.Length)
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Selected text replacement failed.");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static Win32Input.Input[] BuildSelectedReplacementInputs(string replacementText)
    {
        ArgumentNullException.ThrowIfNull(replacementText);

        var inputs = new Win32Input.Input[checked((replacementText.Length + 1) * 2)];
        var offset = 0;
        inputs[offset++] = CreateKeyboardInput(Win32Input.VkBack, scanCode: 0, flags: 0);
        inputs[offset++] = CreateKeyboardInput(
            Win32Input.VkBack,
            scanCode: 0,
            flags: Win32Input.KeyeventfKeyUp);

        foreach (var character in replacementText)
        {
            inputs[offset++] = CreateKeyboardInput(
                virtualKey: 0,
                scanCode: character,
                flags: Win32Input.KeyeventfUnicode);
            inputs[offset++] = CreateKeyboardInput(
                virtualKey: 0,
                scanCode: character,
                flags: Win32Input.KeyeventfUnicode | Win32Input.KeyeventfKeyUp);
        }

        return inputs;
    }

    private static Win32Input.Input CreateKeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        return new Win32Input.Input
        {
            Type = Win32Input.InputKeyboard,
            Data = new Win32Input.InputUnion
            {
                Keyboard = new Win32Input.KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                    ExtraInfo = SmartInputInjectionMarkers.SmartInputExtraInfo,
                },
            },
        };
    }

    private sealed class ClipboardSnapshot(IReadOnlyList<ClipboardFormatData> formats)
    {
        private readonly IReadOnlyList<ClipboardFormatData> _formats = formats;

        internal bool TryRestore()
        {
            if (!Win32Clipboard.OpenClipboard(0))
            {
                return false;
            }

            try
            {
                if (!Win32Clipboard.EmptyClipboard())
                {
                    return false;
                }

                foreach (var item in _formats)
                {
                    var handle = Win32Clipboard.GlobalAlloc(
                        Win32Clipboard.GmemMoveable,
                        (nuint)item.Data.Length);
                    if (handle == 0)
                    {
                        return false;
                    }

                    var pointer = Win32Clipboard.GlobalLock(handle);
                    if (pointer == 0)
                    {
                        Win32Clipboard.GlobalFree(handle);
                        return false;
                    }

                    try
                    {
                        Marshal.Copy(item.Data, 0, pointer, item.Data.Length);
                    }
                    finally
                    {
                        Win32Clipboard.GlobalUnlock(handle);
                    }

                    if (Win32Clipboard.SetClipboardData(item.Format, handle) == 0)
                    {
                        Win32Clipboard.GlobalFree(handle);
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                Win32Clipboard.CloseClipboard();
            }
        }
    }

    private readonly record struct ClipboardFormatData(uint Format, byte[] Data);

    private static class ClipboardNative
    {
        internal static bool TryCaptureSnapshot(out ClipboardSnapshot? snapshot)
        {
            snapshot = null;

            if (!Win32Clipboard.OpenClipboard(0))
            {
                return false;
            }

            try
            {
                var formats = new List<ClipboardFormatData>();
                var format = 0u;
                while ((format = Win32Clipboard.EnumClipboardFormats(format)) != 0)
                {
                    var handle = Win32Clipboard.GetClipboardData(format);
                    if (handle == 0)
                    {
                        return false;
                    }

                    var size = Win32Clipboard.GlobalSize(handle).ToUInt64();
                    if (size == 0 || size > MaxClipboardFormatBytes)
                    {
                        // Some clipboard formats are not movable global
                        // memory (for example CF_BITMAP).  Refuse the whole
                        // operation rather than losing an unsupported format.
                        return false;
                    }

                    var pointer = Win32Clipboard.GlobalLock(handle);
                    if (pointer == 0)
                    {
                        return false;
                    }

                    try
                    {
                        var data = new byte[checked((int)size)];
                        Marshal.Copy(pointer, data, 0, data.Length);
                        formats.Add(new ClipboardFormatData(format, data));
                    }
                    finally
                    {
                        Win32Clipboard.GlobalUnlock(handle);
                    }
                }

                snapshot = new ClipboardSnapshot(formats);
                return true;
            }
            finally
            {
                Win32Clipboard.CloseClipboard();
            }
        }

        internal static string? ReadUnicodeText()
        {
            if (!Win32Clipboard.OpenClipboard(0))
            {
                return null;
            }

            try
            {
                var handle = Win32Clipboard.GetClipboardData(Win32Clipboard.CfUnicodeText);
                if (handle == 0)
                {
                    return null;
                }

                var pointer = Win32Clipboard.GlobalLock(handle);
                if (pointer == 0)
                {
                    return null;
                }

                try
                {
                    return Marshal.PtrToStringUni(pointer);
                }
                finally
                {
                    Win32Clipboard.GlobalUnlock(handle);
                }
            }
            finally
            {
                Win32Clipboard.CloseClipboard();
            }
        }
    }
}
