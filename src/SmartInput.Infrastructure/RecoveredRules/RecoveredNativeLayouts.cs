using System.Buffers.Binary;

namespace SmartInput.Infrastructure.RecoveredRules;

/// <summary>Confirmed native output layout; all fields remain structural.</summary>
public static class RecoveredTransformOutputLayout
{
    public const int Size = 24;
    public const int CapacityOffset = 0x00;
    public const int PointerOffset = 0x08;
    public const int LengthOffset = 0x10;
}

/// <summary>
/// Reads only the confirmed table-index slot of a native 32-byte match record.
/// The returned value is opaque and is not interpreted as a word or rule ID.
/// </summary>
public static class RecoveredMatchRecordLayout
{
    public const int Size = 32;
    public const int TableIndexOffset = 0x18;

    public static bool TryReadTableIndex(ReadOnlySpan<byte> record, out uint tableIndex)
    {
        tableIndex = 0;
        if (record.Length < Size)
        {
            return false;
        }

        tableIndex = BinaryPrimitives.ReadUInt32LittleEndian(record[TableIndexOffset..]);
        return true;
    }
}
