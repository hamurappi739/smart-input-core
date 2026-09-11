using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;
using SmartInput.Platform.Windows.Input;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsTextReplacementService : ITextReplacementService
{
    private readonly TextReplacementSessionCoordinator _sessionCoordinator;
    private readonly ILogger<WindowsTextReplacementService> _logger;
    private readonly SemaphoreSlim _executionLock = new(1, 1);

    public WindowsTextReplacementService(
        TextReplacementSessionCoordinator sessionCoordinator,
        ILogger<WindowsTextReplacementService> logger)
    {
        _sessionCoordinator = sessionCoordinator;
        _logger = logger;
    }

    public async Task<TextReplacementResult> ReplaceRecentTextAsync(
        TextReplacementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _executionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        var deletedCount = 0;
        var insertedCount = 0;

        try
        {
            using var session = _sessionCoordinator.BeginReplacement(cancellationToken);

            session.ThrowIfCancellationRequested();

            // Keep the replacement contiguous. Sending one INPUT at a time
            // with scheduler delays lets the user's next keystrokes land
            // between Backspace and Unicode insertion, producing interleaved
            // text in fast applications such as Telegram.
            var inputs = BuildReplacementInputs(request.OriginalText, request.ReplacementText);
            var sent = Win32Input.SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<Win32Input.Input>());
            if (sent != inputs.Length)
            {
                throw new InvalidOperationException("SendInput sent only part of the replacement batch.");
            }

            deletedCount = request.OriginalText.Length;
            insertedCount = request.ReplacementText.Length;

            _logger.LogInformation(
                "Text replacement completed. deleted={DeletedCount} inserted={InsertedCount}",
                deletedCount,
                insertedCount);

            return TextReplacementResult.Success(deletedCount, insertedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Text replacement aborted by concurrent user input. deleted={DeletedCount} inserted={InsertedCount}",
                deletedCount,
                insertedCount);

            return TextReplacementResult.AbortedByUserInput(deletedCount, insertedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text replacement failed.");
            return TextReplacementResult.Failed("Text replacement could not be completed.");
        }
        finally
        {
            _executionLock.Release();
        }
    }

    private static void SendVirtualKey(ushort virtualKey)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(virtualKey, scanCode: 0, flags: 0),
            CreateKeyboardInput(virtualKey, scanCode: 0, flags: Win32Input.KeyeventfKeyUp),
        };

        if (Win32Input.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Input.Input>()) != inputs.Length)
        {
            throw new InvalidOperationException("SendInput failed for virtual key injection.");
        }
    }

    private static void SendUnicodeCharacter(char character)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(virtualKey: 0, scanCode: character, flags: Win32Input.KeyeventfUnicode),
            CreateKeyboardInput(virtualKey: 0, scanCode: character, flags: Win32Input.KeyeventfUnicode | Win32Input.KeyeventfKeyUp),
        };

        if (Win32Input.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Input.Input>()) != inputs.Length)
        {
            throw new InvalidOperationException("SendInput failed for unicode injection.");
        }
    }

    internal static Win32Input.Input[] BuildReplacementInputs(
        string originalText,
        string replacementText)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(replacementText);

        var inputs = new Win32Input.Input[checked((originalText.Length + replacementText.Length) * 2)];
        var offset = 0;
        foreach (var _ in originalText)
        {
            inputs[offset++] = CreateKeyboardInput(Win32Input.VkBack, scanCode: 0, flags: 0);
            inputs[offset++] = CreateKeyboardInput(Win32Input.VkBack, scanCode: 0, flags: Win32Input.KeyeventfKeyUp);
        }

        foreach (var character in replacementText)
        {
            inputs[offset++] = CreateKeyboardInput(virtualKey: 0, scanCode: character, flags: Win32Input.KeyeventfUnicode);
            inputs[offset++] = CreateKeyboardInput(virtualKey: 0, scanCode: character, flags: Win32Input.KeyeventfUnicode | Win32Input.KeyeventfKeyUp);
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
}
