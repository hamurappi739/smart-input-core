using System.Text;
using SmartInput.Core.Engines;

namespace SmartInput.Core.Tests;

public sealed class CompactWordIndexTests
{
    [Fact]
    public void Create_NormalizesDeduplicatesAndUsesPackedStorage()
    {
        var index = CompactWordIndex.Create(["Привет", "привет", "мир", "hello"]);

        Assert.Equal(3, index.EntryCount);
        Assert.True(index.Contains("ПРИВЕТ"));
        Assert.True(index.Contains("мир"));
        Assert.True(index.Contains("hello"));
        Assert.False(index.Contains("world"));

        var utf8Bytes = Encoding.UTF8.GetByteCount("привет")
            + Encoding.UTF8.GetByteCount("мир")
            + Encoding.UTF8.GetByteCount("hello");
        Assert.Equal(utf8Bytes + (sizeof(int) * 4), index.StorageBytes);
    }

    [Fact]
    public void BoundedDictionary_KeepsOneEditSuggestionBehavior()
    {
        var dictionary = new BoundedDictionary(["привет", "мир", "hello"]);

        var suggestions = dictionary.Suggest("превет", maxSuggestions: 8);

        Assert.Contains("привет", suggestions);
        Assert.True(dictionary.StorageBytes < 128);
    }

    [Fact]
    public void WriteToAndLoadFromFile_RoundTripsPackedIndex()
    {
        var path = Path.Combine(Path.GetTempPath(), "smartinput-packed-" + Guid.NewGuid().ToString("N") + ".sidict");
        try
        {
            var original = CompactWordIndex.Create(["привет", "машина", "hello"]);
            using (var stream = File.Create(path))
            {
                original.WriteTo(stream);
            }

            var loaded = CompactWordIndex.LoadFromFile(path);

            Assert.Equal(original.EntryCount, loaded.EntryCount);
            Assert.True(loaded.Contains("ПРИВЕТ"));
            Assert.True(loaded.Contains("машина"));
            Assert.True(loaded.Contains("hello"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_RejectsMalformedContainer()
    {
        var path = Path.Combine(Path.GetTempPath(), "smartinput-packed-bad-" + Guid.NewGuid().ToString("N") + ".sidict");
        try
        {
            File.WriteAllBytes(path, [0x00, 0x01, 0x02]);

            Assert.Throws<EndOfStreamException>(() => CompactWordIndex.LoadFromFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadMappedFromFile_ReadsValidatedPackWithoutChangingLookup()
    {
        var original = CompactWordIndex.Create(["hello", "привет", "мир"]);
        var path = Path.Combine(Path.GetTempPath(), $"smart-input-mapped-{Guid.NewGuid():N}.sidict");

        try
        {
            using (var stream = File.Create(path))
            {
                original.WriteTo(stream);
            }

            using var mapped = CompactWordIndex.LoadMappedFromFile(path);
            Assert.Equal(original.EntryCount, mapped.EntryCount);
            Assert.Equal(original.StorageBytes, mapped.StorageBytes);
            Assert.True(mapped.Contains("HELLO"));
            Assert.True(mapped.Contains("привет"));
            Assert.False(mapped.Contains("missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
