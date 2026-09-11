using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public sealed class HunspellWordFormProviderTests
{
    private readonly HunspellWordFormProvider _provider = new();

    [Theory]
    [InlineData("привет", TypingLanguage.Russian)]
    [InlineData("начала", TypingLanguage.Russian)]
    [InlineData("дать", TypingLanguage.Russian)]
    [InlineData("hello", TypingLanguage.English)]
    [InlineData("receive", TypingLanguage.English)]
    public void IsKnownWord_RecognizesBundledForms(string word, TypingLanguage language)
    {
        Assert.True(_provider.IsKnownWord(word, language));
    }

    [Theory]
    [InlineData("https://example.com", TypingLanguage.English)]
    [InlineData("C:\\work\\file.txt", TypingLanguage.English)]
    [InlineData("user_name", TypingLanguage.English)]
    [InlineData("привет", TypingLanguage.English)]
    [InlineData("hello", TypingLanguage.Russian)]
    public void IsKnownWord_RejectsProtectedAndWrongLanguage(string word, TypingLanguage language)
    {
        Assert.False(_provider.IsKnownWord(word, language));
    }

    [Fact]
    public void Suggest_ReturnsAReplacementFromTheBundledForms()
    {
        var suggestions = _provider.Suggest("превет", TypingLanguage.Russian);

        Assert.Contains(suggestions, suggestion =>
            string.Equals(suggestion, "привет", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Suggest_IsBoundedAndDoesNotReturnTheInput()
    {
        var suggestions = _provider.Suggest("teh", TypingLanguage.English, maxSuggestions: 3);

        Assert.InRange(suggestions.Count, 0, 3);
        Assert.DoesNotContain(suggestions, suggestion =>
            string.Equals(suggestion, "teh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DictionaryCatalog_RequiresBothAffixAndDictionaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "smartinput-hunspell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "en_US.dic"), "1\nhello\n");
            Assert.Empty(HunspellDictionaryCatalog.Discover(root));

            File.WriteAllText(Path.Combine(root, "en_US.aff"), "SET UTF-8\n");
            var discovered = HunspellDictionaryCatalog.Discover(root);

            Assert.True(discovered.TryGetValue(TypingLanguage.English, out var files));
            Assert.Equal(Path.Combine(root, "en_US.dic"), files.DictionaryPath);
            Assert.Equal(Path.Combine(root, "en_US.aff"), files.AffixPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Provider_LoadsAnExplicitHunspellPair()
    {
        var root = Path.Combine(Path.GetTempPath(), "smartinput-hunspell-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var dictionaryPath = Path.Combine(root, "en_US.dic");
            var affixPath = Path.Combine(root, "en_US.aff");
            File.WriteAllText(dictionaryPath, "2\nhello\nworld\n");
            File.WriteAllText(affixPath, "SET UTF-8\n");

            using var provider = new HunspellWordFormProvider(new Dictionary<TypingLanguage, HunspellDictionaryFiles>
            {
                [TypingLanguage.English] = new(dictionaryPath, affixPath),
            });

            Assert.True(provider.IsKnownWord("hello", TypingLanguage.English));
            Assert.Contains(provider.Suggest("helo", TypingLanguage.English), suggestion =>
                string.Equals(suggestion, "hello", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Provider_PrefersAValidPrecompiledIndex_WhenItIsAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "smartinput-hunspell-packed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var dictionaryPath = Path.Combine(root, "en_US.dic");
            var affixPath = Path.Combine(root, "en_US.aff");
            var packedPath = Path.Combine(root, "en_US.sidict");
            File.WriteAllText(dictionaryPath, "1\nsourcely\n");
            File.WriteAllText(affixPath, "SET UTF-8\n");
            using (var stream = File.Create(packedPath))
            {
                CompactWordIndex.Create(["hello", "world"]).WriteTo(stream);
            }

            using var provider = new HunspellWordFormProvider(new Dictionary<TypingLanguage, HunspellDictionaryFiles>
            {
                [TypingLanguage.English] = new(dictionaryPath, affixPath, packedPath),
            });

            Assert.True(provider.IsKnownWord("hello", TypingLanguage.English));
            Assert.False(provider.IsKnownWord("sourcely", TypingLanguage.English));
            Assert.Contains(provider.Suggest("helo", TypingLanguage.English), suggestion =>
                string.Equals(suggestion, "hello", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Adapter_ReturnsBoundedNearestCandidates()
    {
        var adapter = new HunspellExternalSpellCorrectionProvider(_provider);

        var result = adapter.FindCandidates("превет", TypingLanguage.Russian, maxEditDistance: 1);

        Assert.Contains(result, candidate =>
            string.Equals(candidate.Word, "привет", StringComparison.OrdinalIgnoreCase)
            && candidate.EditDistance == 1);
        Assert.All(result, candidate => Assert.Equal(1, candidate.EditDistance));
    }

    [Fact]
    public void Provider_FallsBackWhenOptionalPairIsMalformed()
    {
        var root = Path.Combine(Path.GetTempPath(), "smartinput-hunspell-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var dictionaryPath = Path.Combine(root, "ru_RU.dic");
            var affixPath = Path.Combine(root, "ru_RU.aff");
            File.WriteAllText(dictionaryPath, "not-a-dictionary");
            File.WriteAllText(affixPath, "not-a-valid-affix-file");

            var provider = new HunspellWordFormProvider(new Dictionary<TypingLanguage, HunspellDictionaryFiles>
            {
                [TypingLanguage.Russian] = new(dictionaryPath, affixPath),
            });

            Assert.True(provider.IsKnownWord("привет", TypingLanguage.Russian));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Provider_LoadsBundledFullDictionaries_AndRecognizesInflectedForms()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null
            && !File.Exists(Path.Combine(
                repository.FullName,
                "src",
                "SmartInput.App",
                "Dictionaries",
                "Hunspell",
                "ru_RU.dic")))
        {
            repository = repository.Parent;
        }

        Assert.NotNull(repository);
        var root = Path.Combine(
            repository!.FullName,
            "src",
            "SmartInput.App",
            "Dictionaries",
            "Hunspell");

        Assert.True(File.Exists(Path.Combine(root, "ru_RU.dic")));
        Assert.True(File.Exists(Path.Combine(root, "en_US.dic")));

        var catalog = HunspellDictionaryCatalog.Discover(root);
        Assert.NotNull(catalog[TypingLanguage.Russian].PackedIndexPath);
        Assert.NotNull(catalog[TypingLanguage.English].PackedIndexPath);

        var provider = new HunspellWordFormProvider(catalog);

        provider.WarmUp();

        Assert.True(provider.IsKnownWord("началу", TypingLanguage.Russian));
        Assert.True(provider.IsKnownWord("запуска", TypingLanguage.Russian));
        Assert.True(provider.IsKnownWord("running", TypingLanguage.English));
        Assert.Contains(provider.Suggest("превет", TypingLanguage.Russian), suggestion =>
            string.Equals(suggestion, "привет", StringComparison.OrdinalIgnoreCase));
    }
}
