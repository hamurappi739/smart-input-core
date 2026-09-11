using System.Buffers;
using System.Text;

namespace SmartInput.Infrastructure.RecoveredRules;

public enum RecoveredBoundaryCallbackBranch
{
    CallbackA,
    CallbackB,
}

public enum RecoveredBoundaryPreparationStatus
{
    EmptyInput,
    PreparedKnownWhitespace,
    InvalidUtf8,
}

/// <summary>
/// Audit-only reproduction of the statically confirmed part of Caramba's
/// boundary helper. It trims only the confirmed ASCII whitespace set and
/// reports which static descriptor would be selected. The two descriptor
/// streams and their formatter have now been statically resolved, so valid
/// input also exposes the exact final matcher bytes.
/// </summary>
public sealed class RecoveredBoundaryPreparationResult
{
    internal RecoveredBoundaryPreparationResult(
        RecoveredBoundaryPreparationStatus status,
        RecoveredBoundaryCallbackBranch branch,
        byte[] trimmedBytes,
        byte[]? finalMatcherBytes = null)
    {
        Status = status;
        CallbackBranch = branch;
        TrimmedBytes = trimmedBytes;
        FinalMatcherBytes = finalMatcherBytes;
    }

    public RecoveredBoundaryPreparationStatus Status { get; }
    public RecoveredBoundaryCallbackBranch CallbackBranch { get; }
    /// <summary>RVA of the selected static descriptor, not a function address.</summary>
    public int DescriptorRva => CallbackBranch == RecoveredBoundaryCallbackBranch.CallbackA
        ? 0x81D538
        : 0x81D53E;

    /// <summary>Whether the descriptor writer output was statically recovered.</summary>
    public bool FinalTransformKnown => FinalMatcherBytes is not null;
    public byte[] CopyTrimmedBytes() => TrimmedBytes.ToArray();
    public byte[]? CopyFinalMatcherBytes() => FinalMatcherBytes?.ToArray();
    public byte[] CopyDescriptorBytes() => RecoveredBoundaryPreparation.GetDescriptorBytes(DescriptorRva).ToArray();

    private byte[] TrimmedBytes { get; }
    private byte[]? FinalMatcherBytes { get; }
}

public static class RecoveredBoundaryPreparation
{
    internal static ReadOnlySpan<byte> GetDescriptorBytes(int descriptorRva)
        => descriptorRva switch
        {
            0x81D538 => [0x01, 0x02, 0xC0, 0x01, 0x03, 0x00],
            0x81D53E => [0x01, 0x02, 0xC0, 0x00],
            _ => throw new ArgumentOutOfRangeException(nameof(descriptorRva)),
        };
    public static RecoveredBoundaryPreparationResult Analyze(ReadOnlySpan<byte> source, int mode = 0)
    {
        if (source.IsEmpty)
        {
            return new RecoveredBoundaryPreparationResult(
                RecoveredBoundaryPreparationStatus.EmptyInput,
                RecoveredBoundaryCallbackBranch.CallbackB,
                []);
        }

        var codePoints = Decode(source);
        if (codePoints is null)
        {
            return new RecoveredBoundaryPreparationResult(
                RecoveredBoundaryPreparationStatus.InvalidUtf8,
                RecoveredBoundaryCallbackBranch.CallbackB,
                []);
        }

        var first = 0;
        var last = codePoints.Count;
        while (first < last && IsConfirmedTrimCodePoint(codePoints[first].Value))
        {
            first++;
        }

        while (last > first && IsConfirmedTrimCodePoint(codePoints[last - 1].Value))
        {
            last--;
        }

        var callback = mode == 0
            && codePoints.Count > 0
            && IsConfirmedBoundaryCodePoint(codePoints[^1].Value)
                ? RecoveredBoundaryCallbackBranch.CallbackA
                : RecoveredBoundaryCallbackBranch.CallbackB;

        var startByte = first < codePoints.Count ? codePoints[first].ByteOffset : source.Length;
        var endByte = last > 0 ? codePoints[last - 1].ByteOffset + codePoints[last - 1].ByteLength : startByte;
        var trimmed = source[startByte..endByte].ToArray();
        var finalMatcherBytes = BuildFinalMatcherBytes(callback, trimmed);
        return new RecoveredBoundaryPreparationResult(
            RecoveredBoundaryPreparationStatus.PreparedKnownWhitespace,
            callback,
            trimmed,
            finalMatcherBytes);
    }

    private static byte[] BuildFinalMatcherBytes(
        RecoveredBoundaryCallbackBranch branch,
        ReadOnlySpan<byte> trimmed)
    {
        var hasSuffix = branch == RecoveredBoundaryCallbackBranch.CallbackA;
        var result = new byte[checked(trimmed.Length + 1 + (hasSuffix ? 1 : 0))];
        result[0] = 0x02;
        trimmed.CopyTo(result.AsSpan(1));
        if (hasSuffix)
        {
            result[^1] = 0x03;
        }

        return result;
    }

    private static bool IsConfirmedTrimCodePoint(int value)
        => value is >= 0x09 and <= 0x0D or 0x20;

    private static bool IsConfirmedBoundaryCodePoint(int value)
        => value is 0x09 or 0x0A or 0x0D or 0x20 or 0x2D;

    private static List<CodePoint>? Decode(ReadOnlySpan<byte> source)
    {
        var result = new List<CodePoint>();
        var offset = 0;
        while (offset < source.Length)
        {
            var status = Rune.DecodeFromUtf8(source[offset..], out var rune, out var consumed);
            if (status != OperationStatus.Done || consumed <= 0)
            {
                return null;
            }

            result.Add(new CodePoint(rune.Value, offset, consumed));
            offset += consumed;
        }

        return result;
    }

    private readonly record struct CodePoint(int Value, int ByteOffset, int ByteLength);
}
