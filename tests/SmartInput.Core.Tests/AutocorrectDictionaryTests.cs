using Microsoft.Extensions.Logging;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Infrastructure.Persistence;

namespace SmartInput.Core.Tests;

public class AutocorrectDictionaryTests
{
    [Fact]
    public void BuiltInLexicon_ContainsEnglishAndRussianWords()
    {
        var dictionary = CreateDictionary();

        Assert.True(dictionary.Contains("hello", TypingLanguage.English));
        Assert.True(dictionary.Contains("привет", TypingLanguage.Russian));
        Assert.False(dictionary.Contains("hello", TypingLanguage.Russian));
    }

    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        var dictionary = CreateDictionary();

        Assert.True(dictionary.Contains("WORLD", TypingLanguage.English));
        Assert.True(dictionary.Contains("world", TypingLanguage.English));
        Assert.Equal(
            dictionary.GetFrequency("world", TypingLanguage.English),
            dictionary.GetFrequency("WORLD", TypingLanguage.English));
    }

    [Fact]
    public async Task UserDictionary_AddRemove_UpdatesCompositeLookup()
    {
        var store = CreateStore();
        var dictionary = new CompositeAutocorrectDictionary(store);

        await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "customword",
            Language = TypingLanguage.English,
            Frequency = 0.95,
        });

        Assert.True(dictionary.Contains("customword", TypingLanguage.English));
        Assert.Equal(0.95, dictionary.GetFrequency("customword", TypingLanguage.English));

        await store.RemoveAsync("customword", TypingLanguage.English);

        Assert.False(dictionary.Contains("customword", TypingLanguage.English));
    }

    [Fact]
    public async Task UserDictionary_PreservesDisplayCasingInStore()
    {
        var store = CreateStore();

        await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "CustomWord",
            Language = TypingLanguage.English,
        });

        Assert.Equal("CustomWord", store.Entries[0].Word);
    }

    [Fact]
    public async Task UserDictionary_DefaultFrequency_IsUsedWhenMissing()
    {
        var store = CreateStore();
        var dictionary = new CompositeAutocorrectDictionary(store);

        await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "customword",
            Language = TypingLanguage.English,
        });

        Assert.Equal(
            AutocorrectDictionaryNormalizer.DefaultUserEntryFrequency,
            dictionary.GetFrequency("customword", TypingLanguage.English));
    }

    [Fact]
    public async Task UserDictionary_UserEntryOverridesBuiltInFrequency()
    {
        var store = CreateStore();
        var dictionary = new CompositeAutocorrectDictionary(store);

        await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "hello",
            Language = TypingLanguage.English,
            Frequency = 0.42,
        });

        Assert.Equal(0.42, dictionary.GetFrequency("hello", TypingLanguage.English));
    }

    [Fact]
    public async Task NeverAutocorrect_UserEntry_IsFlagged()
    {
        var store = CreateStore();
        var dictionary = new CompositeAutocorrectDictionary(store);

        await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "mybrand",
            Language = TypingLanguage.English,
            NeverAutocorrect = true,
        });

        Assert.True(dictionary.Contains("mybrand", TypingLanguage.English));
        Assert.True(dictionary.IsNeverAutocorrect("mybrand", TypingLanguage.English));
    }

    [Fact]
    public async Task NeverAutocorrect_UserEntry_BlocksAutocorrectionEngine()
    {
        var store = CreateStore();
        var dictionary = new CompositeAutocorrectDictionary(store);
        var service = new AutocorrectionService();

        await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "mybrand",
            Language = TypingLanguage.English,
            NeverAutocorrect = true,
        });

        var result = service.Evaluate("mybrand", TypingLanguage.English, dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Theory]
    [InlineData("Cursor")]
    [InlineData("GitHub")]
    [InlineData("myVariableName")]
    public void ProtectedTechnicalTokens_AreBlocked(string token)
    {
        var dictionary = CreateDictionary();

        Assert.False(dictionary.Contains(token, TypingLanguage.English));
        Assert.True(dictionary.IsNeverAutocorrect(token, TypingLanguage.English));
    }

    [Fact]
    public async Task DuplicateEntries_AreMergedDeterministically()
    {
        var store = CreateStore();

        await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "brand",
            Language = TypingLanguage.English,
            Frequency = 0.40,
        });
        await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "Brand",
            Language = TypingLanguage.English,
            Frequency = 0.90,
        });

        Assert.Single(store.Entries);
        Assert.Equal(0.90, store.Entries[0].Frequency);
        Assert.Equal("Brand", store.Entries[0].Word);
    }

    [Fact]
    public async Task InvalidEntries_AreRejectedOrSkipped()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "   ",
            Language = TypingLanguage.English,
        }));

        var persistence = new FakeUserAutocorrectDictionaryPersistence(
        [
            new UserAutocorrectDictionaryEntry
            {
                Word = "valid",
                Language = TypingLanguage.English,
            },
            new UserAutocorrectDictionaryEntry
            {
                Word = " ",
                Language = TypingLanguage.English,
            },
        ]);

        var reloadingStore = new UserAutocorrectDictionaryStore(persistence);
        await reloadingStore.LoadAsync();

        Assert.Single(reloadingStore.Entries);
        Assert.Equal("valid", reloadingStore.Entries[0].Word);
    }

    [Fact]
    public async Task InvalidFrequencyAndLanguage_AreRejectedOrSkipped()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "invalid-frequency",
            Language = TypingLanguage.English,
            Frequency = double.NaN,
        }));

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "invalid-language",
            Language = (TypingLanguage)99,
        }));

        var persistence = new FakeUserAutocorrectDictionaryPersistence(
        [
            new UserAutocorrectDictionaryEntry
            {
                Word = "valid",
                Language = TypingLanguage.English,
                Frequency = 0.5,
            },
            new UserAutocorrectDictionaryEntry
            {
                Word = "invalid-frequency",
                Language = TypingLanguage.English,
                Frequency = double.PositiveInfinity,
            },
            new UserAutocorrectDictionaryEntry
            {
                Word = "invalid-language",
                Language = (TypingLanguage)99,
            },
        ]);

        var reloadingStore = new UserAutocorrectDictionaryStore(persistence);
        await reloadingStore.LoadAsync();

        Assert.Single(reloadingStore.Entries);
        Assert.Equal("valid", reloadingStore.Entries[0].Word);
    }

    [Fact]
    public async Task LoadingTooManyEntries_UsesBoundedDeterministicSnapshot()
    {
        var entries = Enumerable.Range(0, AutocorrectDictionaryNormalizer.MaxUserDictionaryEntries + 32)
            .Select(index => new UserAutocorrectDictionaryEntry
            {
                Word = $"word-{index}",
                Language = TypingLanguage.English,
            })
            .ToList();
        var store = new UserAutocorrectDictionaryStore(
            new FakeUserAutocorrectDictionaryPersistence(entries));

        await store.LoadAsync();

        Assert.Equal(AutocorrectDictionaryNormalizer.MaxUserDictionaryEntries, store.Entries.Count);
        Assert.Equal("word-0", store.Entries[0].Word);
        Assert.Equal("word-999", store.Entries[^1].Word);
    }

    [Fact]
    public async Task Persistence_RoundTrip_PreservesEntries()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"smartinput-dict-{Guid.NewGuid():N}.json");
        var persistence = new JsonUserAutocorrectDictionaryPersistence(
            tempPath,
            new TestLogger<JsonUserAutocorrectDictionaryPersistence>());
        var store = new UserAutocorrectDictionaryStore(persistence);

        try
        {
            await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
            {
                Word = "MyBrand",
                Language = TypingLanguage.English,
                Frequency = 0.88,
                NeverAutocorrect = true,
            });
            await store.SaveAsync();

            var reloadedStore = new UserAutocorrectDictionaryStore(persistence);
            await reloadedStore.LoadAsync();

            Assert.Single(reloadedStore.Entries);
            Assert.Equal("MyBrand", reloadedStore.Entries[0].Word);
            Assert.Equal(TypingLanguage.English, reloadedStore.Entries[0].Language);
            Assert.Equal(0.88, reloadedStore.Entries[0].Frequency);
            Assert.True(reloadedStore.Entries[0].NeverAutocorrect);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task Persistence_MalformedFile_FailsOpenWithoutThrowing()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"smartinput-dict-malformed-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(tempPath, "{ malformed local state");
            var persistence = new JsonUserAutocorrectDictionaryPersistence(
                tempPath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonUserAutocorrectDictionaryPersistence>.Instance);

            var entries = await persistence.LoadAsync();

            Assert.Empty(entries);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task Persistence_AtomicSave_LeavesValidDestinationAndNoTemporaryFiles()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"smartinput-dict-atomic-{Guid.NewGuid():N}.json");

        try
        {
            var persistence = new JsonUserAutocorrectDictionaryPersistence(
                tempPath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonUserAutocorrectDictionaryPersistence>.Instance);

            await persistence.SaveAsync(
            [
                new UserAutocorrectDictionaryEntry
                {
                    Word = "localword",
                    Language = TypingLanguage.English,
                },
            ]);

            var entries = await persistence.LoadAsync();

            Assert.Single(entries);
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(tempPath)!,
                $"{Path.GetFileName(tempPath)}.*.tmp"));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task Persistence_AtomicSave_CanReplaceWhileAnotherReaderHoldsTheFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"smartinput-dict-reader-{Guid.NewGuid():N}.json");

        try
        {
            var persistence = new JsonUserAutocorrectDictionaryPersistence(
                tempPath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonUserAutocorrectDictionaryPersistence>.Instance);
            await persistence.SaveAsync(
            [
                new UserAutocorrectDictionaryEntry
                {
                    Word = "before",
                    Language = TypingLanguage.English,
                },
            ]);

            await using var reader = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            await persistence.SaveAsync(
            [
                new UserAutocorrectDictionaryEntry
                {
                    Word = "after",
                    Language = TypingLanguage.English,
                },
            ]);

            var entries = await persistence.LoadAsync();

            Assert.Single(entries);
            Assert.Equal("after", entries[0].Word);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task Reload_IsDeterministic()
    {
        var persistence = new FakeUserAutocorrectDictionaryPersistence(
        [
            new UserAutocorrectDictionaryEntry
            {
                Word = "alpha",
                Language = TypingLanguage.English,
                Frequency = 0.5,
            },
            new UserAutocorrectDictionaryEntry
            {
                Word = "beta",
                Language = TypingLanguage.Russian,
                Frequency = 0.6,
            },
        ]);

        var store = new UserAutocorrectDictionaryStore(persistence);
        await store.LoadAsync();
        var dictionary = new CompositeAutocorrectDictionary(store);

        var first = dictionary.GetFrequency("alpha", TypingLanguage.English);
        await store.LoadAsync();
        var second = dictionary.GetFrequency("alpha", TypingLanguage.English);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task PersistenceLogger_DoesNotIncludeTokenText()
    {
        var logger = new TestLogger<JsonUserAutocorrectDictionaryPersistence>();
        var persistence = new JsonUserAutocorrectDictionaryPersistence(
            Path.Combine(Path.GetTempPath(), $"smartinput-dict-log-{Guid.NewGuid():N}.json"),
            logger);

        await persistence.SaveAsync(
        [
            new UserAutocorrectDictionaryEntry
            {
                Word = "secret-token",
                Language = TypingLanguage.English,
            },
        ]);

        Assert.All(logger.Messages, message => Assert.DoesNotContain("secret-token", message));
    }

    [Fact]
    public async Task UserDictionary_IsUserDictionaryEntry_ReturnsTrue()
    {
        var store = CreateStore();
        var dictionary = new CompositeAutocorrectDictionary(store);

        await store.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
        {
            Word = "customword",
            Language = TypingLanguage.English,
        });

        Assert.True(dictionary.IsUserDictionaryEntry("customword", TypingLanguage.English));
        Assert.False(dictionary.IsUserDictionaryEntry("hello", TypingLanguage.English));
    }

    [Fact]
    public void CompositeDictionary_WorksWithAutocorrectionEngine()
    {
        var dictionary = CreateDictionary();
        var service = new AutocorrectionService();

        var result = service.Evaluate("превет", TypingLanguage.Russian, dictionary);

        Assert.Equal("привет", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void StarterLexicon_IncludesTheBundledFrequencyLexicons()
    {
        Assert.InRange(StarterAutocorrectLexicon.EnglishWordCount, 40_000, 50_000);
        Assert.InRange(StarterAutocorrectLexicon.RussianWordCount, 40_000, 50_000);
    }

    [Fact]
    public void StarterLexicon_RecognizesCommonWordsFromBothBundledFrequencyLexicons()
    {
        var dictionary = CreateDictionary();

        Assert.True(dictionary.Contains("computer", TypingLanguage.English));
        Assert.True(dictionary.Contains("собака", TypingLanguage.Russian));
    }

    [Fact]
    public void CompositeDictionary_UsesMorphologicalProviderForRareWordForms()
    {
        var provider = new FakeHunspellWordFormProvider(
            (TypingLanguage.Russian, "редкослову"),
            (TypingLanguage.Russian, "запуска"));
        var dictionary = new CompositeAutocorrectDictionary(CreateStore(), provider);

        Assert.True(dictionary.Contains("редкослову", TypingLanguage.Russian));
        Assert.True(dictionary.Contains("запуска", TypingLanguage.Russian));
        Assert.Equal(0.68, dictionary.GetFrequency("редкослову", TypingLanguage.Russian));
    }

    [Fact]
    public void CompositeDictionary_MorphologyProtectsValidOriginalFromSpelling()
    {
        var provider = new FakeHunspellWordFormProvider((TypingLanguage.Russian, "начала"));
        var dictionary = new CompositeAutocorrectDictionary(CreateStore(), provider);
        var result = new AutocorrectionService().Evaluate(
            "начала",
            TypingLanguage.Russian,
            dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
    }

    private static CompositeAutocorrectDictionary CreateDictionary()
    {
        return new CompositeAutocorrectDictionary(CreateStore());
    }

    private static UserAutocorrectDictionaryStore CreateStore()
    {
        return new UserAutocorrectDictionaryStore(new FakeUserAutocorrectDictionaryPersistence([]));
    }

    private sealed class FakeUserAutocorrectDictionaryPersistence(IReadOnlyList<UserAutocorrectDictionaryEntry> entries)
        : IUserAutocorrectDictionaryPersistence
    {
        public IReadOnlyList<UserAutocorrectDictionaryEntry>? LastSaved { get; private set; }

        public Task<IReadOnlyList<UserAutocorrectDictionaryEntry>> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entries);
        }

        public Task SaveAsync(
            IReadOnlyList<UserAutocorrectDictionaryEntry> saveEntries,
            CancellationToken cancellationToken = default)
        {
            LastSaved = saveEntries.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHunspellWordFormProvider(
        params (TypingLanguage Language, string Word)[] words)
        : IHunspellWordFormProvider
    {
        private readonly HashSet<(TypingLanguage Language, string Word)> _words =
            words.ToHashSet();

        public void WarmUp()
        {
        }

        public bool IsKnownWord(string token, TypingLanguage language)
            => _words.Contains((language, token.ToLowerInvariant()));

        public IReadOnlyList<string> Suggest(
            string token,
            TypingLanguage language,
            int maxSuggestions = 8)
            => [];
    }

    private sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
