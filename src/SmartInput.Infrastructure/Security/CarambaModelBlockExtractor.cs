namespace SmartInput.Infrastructure.Security;

public sealed record CarambaDawgBlocks(byte[] BlockA, byte[] BlockB);

/// <summary>
/// Extracts the two DAWG blocks from the confirmed field paths of the
/// recovered model payload. Field numbers are format facts; language identity
/// is intentionally not inferred from A/B ordering.
/// </summary>
public static class CarambaModelBlockExtractor
{
    private const int TopLevelFieldA = 2;
    private const int TopLevelFieldB = 5;

    public static CarambaDawgBlocks Extract(ReadOnlySpan<byte> modelPayload)
    {
        var fieldA = ReadFirstLengthDelimitedField(modelPayload, TopLevelFieldA);
        var fieldB = ReadFirstLengthDelimitedField(modelPayload, TopLevelFieldB);
        var blockA = ReadFirstLengthDelimitedField(fieldA, nestedFieldNumber: 2);
        var blockB = ReadFirstLengthDelimitedField(fieldB, nestedFieldNumber: 1);

        // Structural validation prevents a malformed nested protobuf value
        // from being handed to the DAWG reader as an apparently valid block.
        DawgBlockReader.Parse(blockA);
        DawgBlockReader.Parse(blockB);
        return new CarambaDawgBlocks(blockA, blockB);
    }

    private static byte[] ReadFirstLengthDelimitedField(ReadOnlySpan<byte> message, int nestedFieldNumber)
    {
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset);
            var fieldNumber = checked((int)(key >> 3));
            var wireType = (int)(key & 7);
            if (fieldNumber <= 0)
            {
                throw new FormatException("Model protobuf contains an invalid field number.");
            }

            if (wireType == 2)
            {
                var length = checked((int)ReadVarint(message, ref offset));
                if (length < 0 || length > message.Length - offset)
                {
                    throw new FormatException("Model protobuf length-delimited field is truncated.");
                }

                var value = message.Slice(offset, length).ToArray();
                offset += length;
                if (fieldNumber == nestedFieldNumber)
                {
                    return value;
                }

                continue;
            }

            SkipField(message, ref offset, wireType);
        }

        throw new FormatException($"Model protobuf field {nestedFieldNumber} was not found.");
    }

    private static void SkipField(ReadOnlySpan<byte> message, ref int offset, int wireType)
    {
        switch (wireType)
        {
            case 0:
                _ = ReadVarint(message, ref offset);
                return;
            case 1:
                EnsureRemaining(message, offset, 8);
                offset += 8;
                return;
            case 5:
                EnsureRemaining(message, offset, 4);
                offset += 4;
                return;
            default:
                throw new FormatException("Unsupported model protobuf wire type.");
        }
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> message, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            EnsureRemaining(message, offset, 1);
            var current = message[offset++];
            if (shift == 63 && current > 1)
            {
                throw new FormatException("Model protobuf varint overflows 64 bits.");
            }

            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new FormatException("Model protobuf varint is too long.");
    }

    private static void EnsureRemaining(ReadOnlySpan<byte> message, int offset, int count)
    {
        if (offset < 0 || count < 0 || count > message.Length - offset)
        {
            throw new FormatException("Model protobuf field is truncated.");
        }
    }
}
