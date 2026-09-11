using System.Text;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Services;

/// <summary>
/// Rolling buffer of recently typed printable characters for punctuation context.
/// Updated only on the live input path; not used for word-token correction.
/// </summary>
public sealed class RecentTextContextBuffer
{
    private const int DefaultMaxLength = 512;

    private readonly int _maxLength;
    private readonly StringBuilder _buffer = new();
    private readonly object _sync = new();
    private string? _lastCompletedToken;
    private long _version;

    public RecentTextContextBuffer(int maxLength = DefaultMaxLength)
    {
        _maxLength = maxLength;
    }

    public int Length
    {
        get
        {
            lock (_sync)
            {
                return _buffer.Length;
            }
        }
    }

    /// <summary>Last token retained when a boundary was delivered fail-open.</summary>
    public string? LastCompletedToken
    {
        get
        {
            lock (_sync)
            {
                return _lastCompletedToken;
            }
        }
    }

    /// <summary>
    /// Monotonically increasing mutation number. Consumers that launch an
    /// asynchronous text operation can use it to reject a stale target after
    /// genuine keyboard input has been processed.
    /// </summary>
    public long Version
    {
        get
        {
            lock (_sync)
            {
                return _version;
            }
        }
    }

    public void Apply(TokenInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_sync)
        {
            switch (input.Kind)
            {
                case TokenInputKind.Reset:
                case TokenInputKind.Uncertain:
                    ClearCore();
                    break;

                case TokenInputKind.Backspace:
                    _lastCompletedToken = null;
                    if (_buffer.Length > 0)
                    {
                        _buffer.Length--;
                        _version++;
                    }

                    break;

                case TokenInputKind.Character:
                    _lastCompletedToken = null;
                    AppendCharacterCore(input.Character);
                    break;

                case TokenInputKind.WordBoundary:
                    AppendSpaceFromBoundary(input.DeferredBoundary);
                    break;
            }
        }
    }

    private void AppendSpaceFromBoundary(DeferredBoundaryKey? boundary)
    {
        if (boundary is null)
        {
            return;
        }

        if (boundary.DeliveryKind == BoundaryDeliveryKind.UnicodeCharacter && boundary.Character == ' ')
        {
            AppendCharacterCore(' ');
            return;
        }

        if (boundary.VirtualKeyCode == VirtualKeys.Space)
        {
            AppendCharacterCore(' ');
        }
    }

    public string Snapshot()
    {
        lock (_sync)
        {
            return _buffer.ToString();
        }
    }

    public void Clear(bool preserveLastToken = false)
    {
        lock (_sync)
        {
            ClearCore(preserveLastToken);
        }
    }

    public void RememberCompletedToken(string token)
    {
        lock (_sync)
        {
            _lastCompletedToken = string.IsNullOrWhiteSpace(token) ? null : token;
            _version++;
        }
    }

    /// <summary>
    /// Reconciles the visible prefix after an early layout transaction. The
    /// operation is accepted only when the rolling context still ends with
    /// the exact source prefix, preventing a stale replacement from touching
    /// newer user input.
    /// </summary>
    public bool TryReplaceTrailing(string originalText, string replacementText)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(replacementText);

        lock (_sync)
        {
            if (string.IsNullOrEmpty(originalText)
                || !_buffer.ToString().EndsWith(originalText, StringComparison.Ordinal))
            {
                return false;
            }

            _buffer.Remove(_buffer.Length - originalText.Length, originalText.Length);
            foreach (var character in replacementText)
            {
                AppendCharacterCore(character);
            }
            _lastCompletedToken = null;
            return true;
        }
    }

    private void ClearCore(bool preserveLastToken = false)
    {
        if (!preserveLastToken)
        {
            _lastCompletedToken = null;
        }

        _buffer.Clear();
        _version++;
    }

    private void AppendCharacterCore(char character)
    {
        if (char.IsControl(character) && character is not '\t')
        {
            ClearCore();
            return;
        }

        if (_buffer.Length >= _maxLength)
        {
            _buffer.Remove(0, _buffer.Length - _maxLength + 1);
        }

        _buffer.Append(character);
        _version++;
    }
}
