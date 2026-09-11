using System.Text;

const uint FormatMagic = 0x58444953; // "SIDX" little-endian
const int FormatVersion = 1;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: SmartInput.DictionaryCompiler <input.dic> <output.sidict> [extra-word ...]");
    return 2;
}

var inputPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine("Input dictionary was not found.");
    return 3;
}

var words = EnumerateDictionaryWords(inputPath)
    .Concat(args.Skip(2))
    .Where(static word => !string.IsNullOrWhiteSpace(word))
    .Select(static word => word.Trim().ToLowerInvariant())
    .Distinct(StringComparer.Ordinal)
    .OrderBy(static word => word, StringComparer.Ordinal)
    .ToArray();

var offsets = new int[words.Length + 1];
var blobLength = 0;
for (var index = 0; index < words.Length; index++)
{
    offsets[index] = blobLength;
    blobLength = checked(blobLength + Encoding.UTF8.GetByteCount(words[index]));
}

offsets[^1] = blobLength;
var destinationDirectory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrWhiteSpace(destinationDirectory))
{
    Directory.CreateDirectory(destinationDirectory);
}

using (var stream = File.Create(outputPath))
using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
{
    writer.Write(FormatMagic);
    writer.Write(FormatVersion);
    writer.Write(words.Length);
    writer.Write(blobLength);
    foreach (var offset in offsets)
    {
        writer.Write(offset);
    }

    foreach (var word in words)
    {
        writer.Write(Encoding.UTF8.GetBytes(word));
    }
}

Console.WriteLine($"Packed {words.Length} entries into {new FileInfo(outputPath).Length} bytes.");
return 0;

static IEnumerable<string> EnumerateDictionaryWords(string dictionaryPath)
{
    foreach (var line in File.ReadLines(dictionaryPath))
    {
        var value = line.Trim();
        if (value.Length == 0 || value[0] == '#' || int.TryParse(value, out _))
        {
            continue;
        }

        var flagSeparator = value.IndexOf('/');
        yield return flagSeparator >= 0 ? value[..flagSeparator] : value;
    }
}
