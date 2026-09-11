using System.Reflection;

namespace SmartInput.Core.Dictionaries;

/// <summary>
/// Compact, read-only membership index for bundled Russian word forms.
/// It is deliberately used as a conservative guard only: a positive result
/// can stop an automatic mutation but can never trigger one.
/// </summary>
internal static class RussianWordFormBloomFilter
{
    private const string ResourceName = "SmartInput.Core.Dictionaries.Lexicons.ru_RU_forms.bloom";
    private const int ExpectedHashCount = 10;
    private const int HeaderLength = 16;
    private static readonly Lazy<Index> LazyIndex = new(LoadIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static bool MightContain(string word)
    {
        if (!IsRussianWord(word))
        {
            return false;
        }

        return LazyIndex.Value.MightContain(word);
    }

    private static Index LoadIndex()
    {
        using var stream = typeof(RussianWordFormBloomFilter).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Russian word-form index was not found: {ResourceName}");
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != 'S' || magic[1] != 'I' || magic[2] != 'B' || magic[3] != 'F')
        {
            throw new InvalidOperationException("The embedded Russian word-form index has an invalid header.");
        }

        var bitCount = reader.ReadInt32();
        var hashCount = reader.ReadInt32();
        _ = reader.ReadInt32(); // Indexed word-form count; kept for format validation/provenance.
        if (bitCount <= 0 || hashCount != ExpectedHashCount)
        {
            throw new InvalidOperationException("The embedded Russian word-form index has invalid dimensions.");
        }

        var expectedByteCount = checked((bitCount + 7) / 8);
        var bits = reader.ReadBytes(expectedByteCount);
        if (bits.Length != expectedByteCount || stream.Position != stream.Length)
        {
            throw new InvalidOperationException("The embedded Russian word-form index is incomplete.");
        }

        return new Index(bitCount, hashCount, bits);
    }

    private static bool IsRussianWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        foreach (var character in word)
        {
            if (character is not (>= 'а' and <= 'я') and not 'ё' and not (>= 'А' and <= 'Я') and not 'Ё')
            {
                return false;
            }
        }

        return true;
    }

    private sealed class Index(int bitCount, int hashCount, byte[] bits)
    {
        public bool MightContain(string word)
        {
            unchecked
            {
                uint first = 2166136261;
                uint second = 0x9E3779B9;
                foreach (var character in word)
                {
                    var normalized = char.ToLowerInvariant(character);
                    first = (first ^ normalized) * 16777619;
                    second = (second ^ normalized) * 2246822519;
                }

                if (second == 0)
                {
                    second = 0x27D4EB2D;
                }

                for (var index = 0; index < hashCount; index++)
                {
                    var bit = (int)((first + ((uint)index * second)) % bitCount);
                    if ((bits[bit >> 3] & (1 << (bit & 7))) == 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
