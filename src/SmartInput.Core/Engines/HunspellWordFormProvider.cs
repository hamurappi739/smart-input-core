using System.Collections.Concurrent;
using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Text;
using WeCantSpell.Hunspell;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Local word-form validation and suggestions backed by Hunspell.
/// The default constructor builds a safe development dictionary from the
/// bundled lexicon. Full .dic/.aff files can be supplied later without
/// changing the service contract.
/// </summary>
public interface IHunspellWordFormProvider
{
    /// <summary>
    /// Loads the local dictionaries before keyboard-hook processing starts.
    /// This keeps the first real keystroke free from dictionary I/O.
    /// </summary>
    void WarmUp();

    bool IsKnownWord(string token, TypingLanguage language);

    IReadOnlyList<string> Suggest(
        string token,
        TypingLanguage language,
        int maxSuggestions = 8);

    /// <summary>
    /// Returns the Hunspell lemma for an inflected form when one is available.
    /// Implementations that do not expose lemmas may keep the default answer.
    /// </summary>
    bool TryGetRootWord(string token, TypingLanguage language, out string root)
    {
        root = string.Empty;
        return false;
    }
}

public sealed class HunspellWordFormProvider : IHunspellWordFormProvider, IDisposable
{
    private readonly ConcurrentDictionary<TypingLanguage, Lazy<BoundedDictionary>> _dictionaries = new();
    private readonly IReadOnlyDictionary<TypingLanguage, HunspellDictionaryFiles>? _files;

    public HunspellWordFormProvider()
    {
        _files = HunspellDictionaryCatalog.Discover();
    }

    public HunspellWordFormProvider(
        IReadOnlyDictionary<TypingLanguage, HunspellDictionaryFiles> dictionaryFiles)
    {
        ArgumentNullException.ThrowIfNull(dictionaryFiles);
        _files = dictionaryFiles;
    }

    public void WarmUp()
    {
        _ = GetDictionary(TypingLanguage.English);
        _ = GetDictionary(TypingLanguage.Russian);
    }

    public void Dispose()
    {
        foreach (var lazyDictionary in _dictionaries.Values)
        {
            if (lazyDictionary.IsValueCreated)
            {
                lazyDictionary.Value.Dispose();
            }
        }

        _dictionaries.Clear();
    }

    public bool IsKnownWord(string token, TypingLanguage language)
    {
        if (!CanQuery(token, language))
        {
            return false;
        }

        return GetDictionary(language).Contains(
            AutocorrectDictionaryNormalizer.NormalizeLookupKey(token));
    }

    public IReadOnlyList<string> Suggest(
        string token,
        TypingLanguage language,
        int maxSuggestions = 8)
    {
        if (!CanQuery(token, language) || maxSuggestions <= 0)
        {
            return [];
        }

        var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(token);
        return GetDictionary(language).Suggest(normalized, maxSuggestions);
    }

    public bool TryGetRootWord(string token, TypingLanguage language, out string root)
    {
        root = string.Empty;
        if (!CanQuery(token, language))
        {
            return false;
        }

        return GetDictionary(language).TryGetRootWord(
            AutocorrectDictionaryNormalizer.NormalizeLookupKey(token),
            out root);
    }

    private BoundedDictionary GetDictionary(TypingLanguage language)
    {
        return _dictionaries
            .GetOrAdd(language, static (selectedLanguage, state) =>
                new Lazy<BoundedDictionary>(
                    () => BuildDictionary(selectedLanguage, state),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                _files)
            .Value;
    }

    private static BoundedDictionary BuildDictionary(
        TypingLanguage language,
        IReadOnlyDictionary<TypingLanguage, HunspellDictionaryFiles>? files)
    {
        if (files is not null
            && files.TryGetValue(language, out var dictionaryFiles))
        {
            if (LooksLikeHunspellPair(dictionaryFiles))
            {
                try
                {
                    var compactIndex = !string.IsNullOrWhiteSpace(dictionaryFiles.PackedIndexPath)
                        && File.Exists(dictionaryFiles.PackedIndexPath)
                        ? CompactWordIndex.LoadMappedFromFile(dictionaryFiles.PackedIndexPath)
                        : CompactWordIndex.Create(
                            EnumerateDictionaryWords(dictionaryFiles.DictionaryPath)
                                .Concat(SafeCommonForms));

                    // Keep the compact index memory-mapped and defer the
                    // heavyweight Hunspell graph. Exact lookups and ordinary
                    // one-edit corrections do not need the graph; it is
                    // created only for a genuinely morphological/ambiguous
                    // query. This keeps the resident idle process compact
                    // without removing runtime morphology from the contract.
                    return new BoundedDictionary(
                        compactIndex,
                        () => WordList.CreateFromFiles(
                            dictionaryFiles.DictionaryPath,
                            dictionaryFiles.AffixPath));
                }
                catch (Exception)
                {
                    // A bad optional pack must never prevent SmartInput from
                    // starting. Fall back to the bundled lexicon; no input
                    // text is included in diagnostics.
                }
            }
        }

        var words = StarterAutocorrectLexicon.Entries
            .Where(entry => entry.Key.Language == language)
            .Select(entry => entry.Key.Word)
            .Concat(SafeCommonForms);
        return new BoundedDictionary(words);
    }

    private static BoundedDictionary CreateBoundedDictionary(string dictionaryPath)
    {
        return new BoundedDictionary(EnumerateDictionaryWords(dictionaryPath).Concat(SafeCommonForms));
    }

    private static IEnumerable<string> EnumerateDictionaryWords(string dictionaryPath)
    {
        foreach (var line in File.ReadLines(dictionaryPath))
        {
            var value = line.Trim();
            if (value.Length == 0 || value[0] == '#')
            {
                continue;
            }

            // The first line is the entry count. Dictionary flags follow a
            // slash and are not part of the lookup word.
            if (int.TryParse(value, out _))
            {
                continue;
            }

            var flagSeparator = value.IndexOf('/');
            if (flagSeparator >= 0)
            {
                value = value[..flagSeparator];
            }

            if (value.Length > 0)
            {
                yield return value;
            }
        }
    }

    // A .dic file contains stems plus affix flags. We keep only a small set
    // of high-value forms for exact boundary protection rather than expanding
    // every rule combination into a large resident graph.
    private static readonly string[] SafeCommonForms =
    [
        "начала", "началу", "началом", "начале", "начали",
        "запуска", "запуску", "запуском", "команду", "решения",
        "самого", "странно", "машина", "жизнь", "пишу",
    ];

    private static bool LooksLikeHunspellPair(HunspellDictionaryFiles files)
    {
        try
        {
            if (!File.Exists(files.DictionaryPath) || !File.Exists(files.AffixPath))
            {
                return false;
            }

            var dictionaryHeader = File.ReadLines(files.DictionaryPath).FirstOrDefault();
            var affixHeader = File.ReadLines(files.AffixPath).FirstOrDefault();
            return int.TryParse(dictionaryHeader, out var entryCount)
                && entryCount > 0
                && !string.IsNullOrWhiteSpace(affixHeader)
                && affixHeader.StartsWith("SET ", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }


    private static bool CanQuery(string token, TypingLanguage language)
    {
        if (string.IsNullOrWhiteSpace(token) || ProtectedTokenAnalyzer.IsProtected(token))
        {
            return false;
        }

        return language switch
        {
            TypingLanguage.English => TokenScriptAnalyzer.Classify(token) == TokenScript.Latin,
            TypingLanguage.Russian => TokenScriptAnalyzer.Classify(token) == TokenScript.Cyrillic,
            _ => false,
        };
    }
}

/// <summary>
/// Memory-bounded dictionary backend. It stores the normalized inventory in a
/// single sorted UTF-8 blob plus an offset table: no resident <see cref="string"/>
/// instance or HashSet bucket is retained for every word. One-edit neighbors
/// are still generated on demand, avoiding the multi-gigabyte Hunspell graph.
/// </summary>
internal sealed class BoundedDictionary : IDisposable
{
    private const string EnglishAlphabet = "abcdefghijklmnopqrstuvwxyz";
    private const string RussianAlphabet = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
    private readonly ICompactWordIndex _index;
    private readonly Lazy<WordList?>? _hunspellFactory;
    private readonly object _hunspellSync = new();
    private readonly ConcurrentDictionary<string, bool> _knownWordCache = new(StringComparer.Ordinal);
    private const int MaximumKnownWordCacheEntries = 8_192;

    public BoundedDictionary(IEnumerable<string> words)
    {
        _index = CompactWordIndex.Create(words);
    }

    public BoundedDictionary(CompactWordIndex index, WordList? hunspell = null)
        : this(
            index,
            hunspell is null
                ? null
                : () => hunspell)
    {
    }

    internal BoundedDictionary(
        ICompactWordIndex index,
        Func<WordList?>? hunspellFactory)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _hunspellFactory = hunspellFactory is null
            ? null
            : new Lazy<WordList?>(
                hunspellFactory,
                LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal int EntryCount => _index.EntryCount;

    internal int StorageBytes => _index.StorageBytes;

    public bool Contains(string word)
    {
        var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (_knownWordCache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var result = _index.Contains(normalized);
        if (!result && _hunspellFactory is not null)
        {
            result = IsHunspellBackedKnownWord(normalized);
        }
        if (_knownWordCache.Count < MaximumKnownWordCacheEntries)
        {
            _knownWordCache.TryAdd(normalized, result);
        }

        return result;
    }

    public IReadOnlyList<string> Suggest(string token, int maxSuggestions)
    {
        if (maxSuggestions <= 0 || token.Length == 0)
        {
            return [];
        }

        var alphabet = token.Any(static c => c >= 'а' && c <= 'я' || c is 'ё' or 'А' or 'Я')
            ? RussianAlphabet
            : EnglishAlphabet;
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The compact index is the normal hot path. Generate bounded one-edit
        // candidates first; this avoids constructing Hunspell for the common
        // typo case and therefore avoids a large resident allocation.
        AddDeletions(token, candidates);
        AddTranspositions(token, candidates);
        AddSubstitutions(token, alphabet, candidates);
        AddInsertions(token, alphabet, candidates);

        if (candidates.Count == 0 && _hunspellFactory is not null)
        {
            try
            {
                var queryOptions = new QueryOptions
                {
                    MaxSuggestions = Math.Clamp(maxSuggestions * 4, 8, 64),
                    MaxCharDistance = 2,
                    MaxRoots = 256,
                    MaxWords = 256,
                    MaxGuess = 256,
                };

                var hunspell = _hunspellFactory.Value;
                if (hunspell is null)
                {
                    return candidates
                        .Take(maxSuggestions)
                        .ToArray();
                }

                lock (_hunspellSync)
                {
                    foreach (var suggestion in hunspell.Suggest(token, queryOptions))
                    {
                        if (!string.IsNullOrWhiteSpace(suggestion))
                        {
                            candidates.Add(suggestion);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // The compact index remains a safe fallback if an optional
                // Hunspell query fails for a malformed or unsupported token.
            }
        }

        return candidates
            .Take(maxSuggestions)
            .ToArray();
    }

    internal bool TryGetRootWord(string word, out string root)
    {
        root = string.Empty;
        var hunspell = _hunspellFactory?.Value;
        if (hunspell is null)
        {
            return false;
        }

        try
        {
            lock (_hunspellSync)
            {
                var details = hunspell.CheckDetails(word);
                if (!details.Correct || string.IsNullOrWhiteSpace(details.Root))
                {
                    return false;
                }

                root = details.Root;
                return !string.Equals(
                    AutocorrectDictionaryNormalizer.NormalizeLookupKey(root),
                    AutocorrectDictionaryNormalizer.NormalizeLookupKey(word),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsHunspellBackedKnownWord(string word)
    {
        var hunspell = _hunspellFactory?.Value;
        if (hunspell is null || string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        try
        {
            lock (_hunspellSync)
            {
                var details = hunspell.CheckDetails(word);
                if (!details.Correct)
                {
                    // Some source .dic packs contain malformed or obsolete
                    // entries. The real Hunspell check is authoritative, even
                    // when an old compact index contains that entry.
                    return false;
                }

                if (_index.Contains(word))
                {
                    return true;
                }

                // A root absent from the compact inventory is admitted only
                // when Hunspell proved it is an affix-generated form. This
                // keeps the compact index's protection semantics while adding
                // true runtime morphology.
                return !string.IsNullOrWhiteSpace(details.Root)
                    && !string.Equals(
                        AutocorrectDictionaryNormalizer.NormalizeLookupKey(details.Root),
                        AutocorrectDictionaryNormalizer.NormalizeLookupKey(word),
                        StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void AddDeletions(string token, ISet<string> candidates)
    {
        for (var index = 0; index < token.Length; index++)
        {
            AddIfKnown(token.Remove(index, 1), candidates);
        }
    }

    private void AddTranspositions(string token, ISet<string> candidates)
    {
        var buffer = token.ToCharArray();
        for (var index = 0; index < buffer.Length - 1; index++)
        {
            (buffer[index], buffer[index + 1]) = (buffer[index + 1], buffer[index]);
            AddIfKnown(new string(buffer), candidates);
            (buffer[index], buffer[index + 1]) = (buffer[index + 1], buffer[index]);
        }
    }

    private void AddSubstitutions(string token, string alphabet, ISet<string> candidates)
    {
        var buffer = token.ToCharArray();
        for (var index = 0; index < buffer.Length; index++)
        {
            var original = buffer[index];
            foreach (var replacement in alphabet)
            {
                if (replacement == original)
                {
                    continue;
                }

                buffer[index] = replacement;
                AddIfKnown(new string(buffer), candidates);
            }

            buffer[index] = original;
        }
    }

    private void AddInsertions(string token, string alphabet, ISet<string> candidates)
    {
        for (var index = 0; index <= token.Length; index++)
        {
            foreach (var insertion in alphabet)
            {
                AddIfKnown(token.Insert(index, insertion.ToString()), candidates);
            }
        }
    }

    private void AddIfKnown(string candidate, ISet<string> candidates)
    {
        if (_index.Contains(candidate))
        {
            candidates.Add(candidate);
        }
    }

    public void Dispose()
    {
        _knownWordCache.Clear();
        _index.Dispose();
    }
}

internal interface ICompactWordIndex : IDisposable
{
    int EntryCount { get; }

    int StorageBytes { get; }

    bool Contains(string word);
}

/// <summary>
/// Compact exact-word index. Input words are normalized, deduplicated and
/// sorted once during loading. At rest only UTF-8 bytes and word boundaries
/// remain; lookup encodes the short query into a stack buffer and binary-searches
/// the byte ranges without allocating a query string or a per-word object.
/// </summary>
internal sealed class CompactWordIndex : ICompactWordIndex
{
    private const uint FormatMagic = 0x58444953; // "SIDX" little-endian
    private const int FormatVersion = 1;
    private const int MaximumEntryCount = 2_000_000;
    private const int MaximumBlobBytes = 128 * 1024 * 1024;
    private const int StackQueryLimit = 256;
    private readonly byte[] _wordBytes;
    private readonly int[] _offsets;

    private CompactWordIndex(byte[] wordBytes, int[] offsets)
    {
        _wordBytes = wordBytes;
        _offsets = offsets;
    }

    public int EntryCount => _offsets.Length - 1;

    public int StorageBytes => _wordBytes.Length + (_offsets.Length * sizeof(int));

    public static CompactWordIndex Create(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        // The temporary string array exists only while a pack is read. It is
        // released after construction; the long-lived representation below is
        // binary and does not retain those string objects.
        var ordered = words
            .Where(static word => !string.IsNullOrWhiteSpace(word))
            .Select(AutocorrectDictionaryNormalizer.NormalizeLookupKey)
            .Where(static word => word.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static word => word, StringComparer.Ordinal)
            .ToArray();

        var offsets = new int[ordered.Length + 1];
        var totalBytes = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            offsets[index] = totalBytes;
            totalBytes = checked(totalBytes + Encoding.UTF8.GetByteCount(ordered[index]));
        }

        offsets[^1] = totalBytes;
        var blob = GC.AllocateUninitializedArray<byte>(totalBytes);
        var writeOffset = 0;
        foreach (var word in ordered)
        {
            writeOffset += Encoding.UTF8.GetBytes(word, blob.AsSpan(writeOffset));
        }

        return new CompactWordIndex(blob, offsets);
    }

    /// <summary>
    /// Loads a precompiled local dictionary. The container is intentionally
    /// simple and validated before use so an optional corrupted pack falls
    /// back to parsing the source Hunspell dictionary.
    /// </summary>
    public static CompactWordIndex LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        if (reader.ReadUInt32() != FormatMagic || reader.ReadInt32() != FormatVersion)
        {
            throw new FormatException("Unsupported compact dictionary format.");
        }

        var entryCount = reader.ReadInt32();
        var blobLength = reader.ReadInt32();
        if (entryCount < 0 || entryCount > MaximumEntryCount
            || blobLength < 0 || blobLength > MaximumBlobBytes)
        {
            throw new FormatException("Compact dictionary header is outside allowed bounds.");
        }

        var offsets = new int[checked(entryCount + 1)];
        for (var index = 0; index < offsets.Length; index++)
        {
            offsets[index] = reader.ReadInt32();
        }

        ValidateOffsets(offsets, blobLength);
        var blob = reader.ReadBytes(blobLength);
        if (blob.Length != blobLength || stream.Position != stream.Length)
        {
            throw new FormatException("Compact dictionary payload is truncated or has trailing data.");
        }

        return new CompactWordIndex(blob, offsets);
    }

    public static ICompactWordIndex LoadMappedFromFile(string path)
    {
        return MappedCompactWordIndex.LoadFromFile(path);
    }

    internal void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatMagic);
        writer.Write(FormatVersion);
        writer.Write(EntryCount);
        writer.Write(_wordBytes.Length);
        foreach (var offset in _offsets)
        {
            writer.Write(offset);
        }

        writer.Write(_wordBytes);
    }

    public bool Contains(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || EntryCount == 0)
        {
            return false;
        }

        var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
        var requiredBytes = Encoding.UTF8.GetByteCount(normalized);
        byte[]? rented = null;
        try
        {
            Span<byte> query = requiredBytes <= StackQueryLimit
                ? stackalloc byte[StackQueryLimit]
                : (rented = ArrayPool<byte>.Shared.Rent(requiredBytes));
            query = query[..requiredBytes];
            Encoding.UTF8.GetBytes(normalized, query);

            var low = 0;
            var high = EntryCount - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var start = _offsets[middle];
                var candidate = _wordBytes.AsSpan(start, _offsets[middle + 1] - start);
                var comparison = query.SequenceCompareTo(candidate);
                if (comparison == 0)
                {
                    return true;
                }

                if (comparison < 0)
                {
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static void ValidateOffsets(IReadOnlyList<int> offsets, int blobLength)
    {
        if (offsets.Count == 0 || offsets[0] != 0 || offsets[^1] != blobLength)
        {
            throw new FormatException("Compact dictionary offsets are invalid.");
        }

        var previous = 0;
        foreach (var offset in offsets)
        {
            if (offset < previous || offset > blobLength)
            {
                throw new FormatException("Compact dictionary offsets are not monotonic.");
            }

            previous = offset;
        }
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// File-backed variant of <see cref="CompactWordIndex"/>. The SIDICT header
/// and offset table are validated in place; words are compared directly from
/// a read-only mapping instead of copying the blob and offset array into the
/// managed heap. Pack generation still uses CompactWordIndex.WriteTo.
/// </summary>
internal sealed class MappedCompactWordIndex : ICompactWordIndex
{
    private const uint FormatMagic = 0x58444953;
    private const int FormatVersion = 1;
    private const int MaximumEntryCount = 2_000_000;
    private const int MaximumBlobBytes = 128 * 1024 * 1024;
    private const int HeaderBytes = sizeof(uint) + (sizeof(int) * 3);
    private const int StackQueryLimit = 256;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly long _offsetTableStart;
    private readonly long _blobStart;
    private int _disposed;

    private MappedCompactWordIndex(
        MemoryMappedFile file,
        MemoryMappedViewAccessor view,
        int entryCount,
        long offsetTableStart,
        long blobStart)
    {
        _file = file;
        _view = view;
        EntryCount = entryCount;
        _offsetTableStart = offsetTableStart;
        _blobStart = blobStart;
    }

    public int EntryCount { get; }

    public int StorageBytes => checked((int)(_view.Capacity - HeaderBytes));

    public static MappedCompactWordIndex LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists || fileInfo.Length < HeaderBytes
            || fileInfo.Length > HeaderBytes + MaximumBlobBytes
                + ((long)MaximumEntryCount + 1) * sizeof(int))
        {
            throw new FormatException("Mapped compact dictionary file is outside allowed bounds.");
        }

        var file = MemoryMappedFile.CreateFromFile(
            path,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            access: MemoryMappedFileAccess.Read);
        var view = file.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);
        try
        {
            if (view.ReadUInt32(0) != FormatMagic
                || view.ReadInt32(sizeof(uint)) != FormatVersion)
            {
                throw new FormatException("Unsupported compact dictionary format.");
            }

            var entryCount = view.ReadInt32(sizeof(uint) + sizeof(int));
            var blobLength = view.ReadInt32(sizeof(uint) + (sizeof(int) * 2));
            if (entryCount < 0 || entryCount > MaximumEntryCount
                || blobLength < 0 || blobLength > MaximumBlobBytes)
            {
                throw new FormatException("Compact dictionary header is outside allowed bounds.");
            }

            var offsetTableStart = (long)HeaderBytes;
            var blobStart = checked(offsetTableStart + ((long)entryCount + 1) * sizeof(int));
            var expectedLength = checked(blobStart + blobLength);
            if (expectedLength != fileInfo.Length)
            {
                throw new FormatException("Compact dictionary payload length is invalid.");
            }

            var previous = 0;
            for (var index = 0; index <= entryCount; index++)
            {
                var offset = view.ReadInt32(offsetTableStart + ((long)index * sizeof(int)));
                if (offset < previous || offset > blobLength)
                {
                    throw new FormatException("Compact dictionary offsets are not monotonic.");
                }

                previous = offset;
            }

            return new MappedCompactWordIndex(
                file,
                view,
                entryCount,
                offsetTableStart,
                blobStart);
        }
        catch
        {
            view.Dispose();
            file.Dispose();
            throw;
        }
    }

    public bool Contains(string word)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (string.IsNullOrWhiteSpace(word) || EntryCount == 0)
        {
            return false;
        }

        var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
        var requiredBytes = Encoding.UTF8.GetByteCount(normalized);
        byte[]? rented = null;
        try
        {
            Span<byte> query = requiredBytes <= StackQueryLimit
                ? stackalloc byte[StackQueryLimit]
                : (rented = ArrayPool<byte>.Shared.Rent(requiredBytes));
            query = query[..requiredBytes];
            Encoding.UTF8.GetBytes(normalized, query);

            var low = 0;
            var high = EntryCount - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var start = ReadOffset(middle);
                var end = ReadOffset(middle + 1);
                var comparison = Compare(query, end - start, _blobStart + start);
                if (comparison == 0)
                {
                    return true;
                }

                if (comparison < 0)
                {
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private int ReadOffset(int index)
    {
        return _view.ReadInt32(_offsetTableStart + ((long)index * sizeof(int)));
    }

    private int Compare(ReadOnlySpan<byte> query, int candidateLength, long candidateStart)
    {
        var sharedLength = Math.Min(query.Length, candidateLength);
        for (var index = 0; index < sharedLength; index++)
        {
            var comparison = query[index].CompareTo(_view.ReadByte(candidateStart + index));
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return query.Length.CompareTo(candidateLength);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _view.Dispose();
        _file.Dispose();
    }
}

public sealed record HunspellDictionaryFiles(
    string DictionaryPath,
    string AffixPath,
    string? PackedIndexPath = null);

/// <summary>
/// Adapts Hunspell suggestions to the bounded candidate contract used by the
/// external evaluator. Hunspell does not expose corpus frequencies, so the
/// evaluator requires a unique nearest result before it can apply anything.
/// </summary>
public sealed class HunspellExternalSpellCorrectionProvider : IExternalSpellCorrectionProvider
{
    private readonly IHunspellWordFormProvider _wordForms;

    public HunspellExternalSpellCorrectionProvider(IHunspellWordFormProvider wordForms)
    {
        _wordForms = wordForms ?? throw new ArgumentNullException(nameof(wordForms));
    }

    public IReadOnlyList<ExternalSpellCandidate> FindCandidates(
        string token,
        TypingLanguage language,
        int maxEditDistance = 1)
    {
        if (string.IsNullOrWhiteSpace(token) || maxEditDistance < 0)
        {
            return [];
        }

        var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(token);
        return _wordForms
            .Suggest(normalized, language, maxSuggestions: 16)
            .Select(candidate => (Word: candidate, Distance: EditDistance(normalized, candidate)))
            .Where(item => item.Distance > 0 && item.Distance <= maxEditDistance)
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Word, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ExternalSpellCandidate(item.Word, item.Distance, 0))
            .ToArray();
    }

    private static int EditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1);
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
