using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Core.Validation;
using SmartInput.Infrastructure.Persistence;

namespace SmartInput.Core.Tests;

public class SnippetServiceTests
{
    [Fact]
    public async Task AddEditDelete_PersistenceRoundTrip()
    {
        var filePath = CreateTempFilePath();
        var persistence = CreateJsonPersistence(filePath);
        var service = new SnippetService(persistence);

        var add = await service.AddAsync(CreateSnippet("addr", "123 Main St"));
        Assert.True(add.IsValid);

        var updated = await service.UpdateAsync(new SnippetDefinition
        {
            Id = add.Normalized!.Id,
            Trigger = "addr",
            Replacement = "456 Oak Ave",
            IsEnabled = true,
        });
        Assert.True(updated.IsValid);

        var reloaded = new SnippetService(persistence);
        await reloaded.LoadAsync();
        Assert.Single(reloaded.Snippets);
        Assert.Equal("456 Oak Ave", reloaded.Snippets[0].Replacement);

        await reloaded.DeleteAsync(add.Normalized.Id);
        var empty = new SnippetService(persistence);
        await empty.LoadAsync();
        Assert.Empty(empty.Snippets);

        File.Delete(filePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("ta\tb")]
    public void Validate_RejectsInvalidTriggers(string? trigger)
    {
        var result = SnippetValidator.Validate(
            CreateSnippet(trigger ?? string.Empty, "value"),
            []);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Add_RejectsEmptyAndDuplicateTriggers()
    {
        var service = CreateInMemoryService();

        var empty = await service.AddAsync(CreateSnippet(" ", "value"));
        Assert.False(empty.IsValid);

        var first = await service.AddAsync(CreateSnippet("sig", "Best regards"));
        Assert.True(first.IsValid);

        var duplicate = await service.AddAsync(CreateSnippet("SIG", "Other"));
        Assert.False(duplicate.IsValid);
        Assert.Contains(duplicate.Errors, error => error.Contains("уже существует", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FindMatch_CaseSensitiveMatching()
    {
        var service = CreateInMemoryService();
        await service.AddAsync(CreateSnippet("Sig", "Sensitive", caseSensitive: true));

        var match = service.FindMatch("Sig");
        var miss = service.FindMatch("sig");

        Assert.True(match.IsMatch);
        Assert.Equal("Sensitive", match.Replacement);
        Assert.False(miss.IsMatch);
    }

    [Fact]
    public async Task FindMatch_CaseInsensitiveMatching()
    {
        var service = CreateInMemoryService();
        await service.AddAsync(CreateSnippet("sig", "Hello", caseSensitive: false));

        Assert.True(service.FindMatch("SIG").IsMatch);
        Assert.True(service.FindMatch("sig").IsMatch);
        Assert.Equal("Hello", service.FindMatch("Sig").Replacement);
    }

    [Fact]
    public async Task FindMatch_LanguageFiltering()
    {
        var service = CreateInMemoryService();
        await service.AddAsync(new SnippetDefinition
        {
            Trigger = "addr",
            Replacement = "EN",
            Language = TypingLanguage.English,
            IsEnabled = true,
        });

        Assert.True(service.FindMatch("addr", new SnippetMatchContext { Language = TypingLanguage.English }).IsMatch);
        Assert.False(service.FindMatch("addr", new SnippetMatchContext { Language = TypingLanguage.Russian }).IsMatch);
        Assert.False(service.FindMatch("addr", new SnippetMatchContext()).IsMatch);
    }

    [Fact]
    public async Task FindMatch_EnabledAndDisabledApplicationFilters()
    {
        var service = CreateInMemoryService();
        await service.AddAsync(new SnippetDefinition
        {
            Trigger = "onlyNote",
            Replacement = "notepad only",
            IsEnabled = true,
            EnabledApplications = ["notepad"],
        });
        await service.AddAsync(new SnippetDefinition
        {
            Trigger = "noCursor",
            Replacement = "blocked in cursor",
            IsEnabled = true,
            DisabledApplications = ["Cursor"],
        });

        Assert.True(service.FindMatch("onlyNote", new SnippetMatchContext { ApplicationProcessName = "notepad" }).IsMatch);
        Assert.False(service.FindMatch("onlyNote", new SnippetMatchContext { ApplicationProcessName = "word" }).IsMatch);
        Assert.False(service.FindMatch("onlyNote", new SnippetMatchContext()).IsMatch);

        Assert.False(service.FindMatch("noCursor", new SnippetMatchContext { ApplicationProcessName = "cursor" }).IsMatch);
        Assert.True(service.FindMatch("noCursor", new SnippetMatchContext { ApplicationProcessName = "notepad" }).IsMatch);
    }

    [Fact]
    public async Task FindMatch_DisabledSnippet_DoesNotMatch()
    {
        var service = CreateInMemoryService();
        var added = await service.AddAsync(CreateSnippet("sig", "Hello"));
        await service.SetEnabledAsync(added.Normalized!.Id, false);

        Assert.False(service.FindMatch("sig").IsMatch);
    }

    [Fact]
    public async Task FindMatch_DeterministicPrecedence_PrefersMoreSpecific()
    {
        var service = CreateInMemoryService();

        await service.AddAsync(new SnippetDefinition
        {
            Trigger = "sig",
            Replacement = "generic",
            IsEnabled = true,
        });
        await service.AddAsync(new SnippetDefinition
        {
            Trigger = "sig",
            Replacement = "english-specific",
            Language = TypingLanguage.English,
            IsEnabled = true,
        });
        await service.AddAsync(new SnippetDefinition
        {
            Trigger = "mail",
            Replacement = "any-app",
            IsEnabled = true,
        });
        await service.AddAsync(new SnippetDefinition
        {
            Trigger = "mail",
            Replacement = "notepad-only",
            IsEnabled = true,
            EnabledApplications = ["notepad"],
        });
        await service.AddAsync(new SnippetDefinition
        {
            Trigger = "Case",
            Replacement = "insensitive",
            CaseSensitive = false,
            IsEnabled = true,
        });
        await service.AddAsync(new SnippetDefinition
        {
            Trigger = "Case",
            Replacement = "sensitive",
            CaseSensitive = true,
            IsEnabled = true,
        });

        var languageMatch = service.FindMatch("sig", new SnippetMatchContext { Language = TypingLanguage.English });
        Assert.Equal("english-specific", languageMatch.Replacement);

        var appMatch = service.FindMatch("mail", new SnippetMatchContext { ApplicationProcessName = "notepad" });
        Assert.Equal("notepad-only", appMatch.Replacement);

        var caseMatch = service.FindMatch("Case");
        Assert.Equal("sensitive", caseMatch.Replacement);
    }

    [Fact]
    public async Task FindMatch_NoRecursiveExpansion()
    {
        var service = CreateInMemoryService();
        await service.AddAsync(CreateSnippet("a", "b"));
        await service.AddAsync(CreateSnippet("b", "c"));

        var match = service.FindMatch("a");
        Assert.True(match.IsMatch);
        Assert.Equal("b", match.Replacement);
        Assert.NotEqual("c", match.Replacement);
    }

    [Fact]
    public async Task Add_AllowsUnicodeAndMultilineReplacement()
    {
        var service = CreateInMemoryService();
        var replacement = "Привет,\n世界\r\nLine3";
        var result = await service.AddAsync(CreateSnippet("greet", replacement));

        Assert.True(result.IsValid);
        Assert.Equal(replacement, service.FindMatch("greet").Replacement);
    }

    [Fact]
    public async Task Load_MalformedPersistence_ReturnsEmpty()
    {
        var filePath = CreateTempFilePath();
        await File.WriteAllTextAsync(filePath, "{ not valid json");
        var persistence = CreateJsonPersistence(filePath);
        var service = new SnippetService(persistence);

        await service.LoadAsync();

        Assert.Empty(service.Snippets);
        File.Delete(filePath);
    }

    [Fact]
    public async Task Load_SkipsInvalidEntriesInDocument()
    {
        var filePath = CreateTempFilePath();
        await File.WriteAllTextAsync(filePath, """
            {
              "snippets": [
                { "id": "11111111-1111-1111-1111-111111111111", "trigger": "", "replacement": "x", "isEnabled": true },
                { "id": "22222222-2222-2222-2222-222222222222", "trigger": "ok", "replacement": "value", "isEnabled": true }
              ]
            }
            """);

        var service = new SnippetService(CreateJsonPersistence(filePath));
        await service.LoadAsync();

        Assert.Single(service.Snippets);
        Assert.Equal("ok", service.Snippets[0].Trigger);
        File.Delete(filePath);
    }

    private static SnippetService CreateInMemoryService()
    {
        return new SnippetService(new InMemorySnippetPersistence());
    }

    private static JsonSnippetPersistence CreateJsonPersistence(string path)
    {
        return new JsonSnippetPersistence(
            path,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonSnippetPersistence>.Instance);
    }

    private static string CreateTempFilePath()
    {
        return Path.Combine(Path.GetTempPath(), $"smartinput-snippets-{Guid.NewGuid():N}.json");
    }

    private static SnippetDefinition CreateSnippet(
        string trigger,
        string replacement,
        bool caseSensitive = false,
        TypingLanguage? language = null)
    {
        return new SnippetDefinition
        {
            Trigger = trigger,
            Replacement = replacement,
            CaseSensitive = caseSensitive,
            Language = language,
            IsEnabled = true,
        };
    }

    private sealed class InMemorySnippetPersistence : ISnippetPersistence
    {
        private IReadOnlyList<SnippetDefinition> _snippets = [];

        public Task<IReadOnlyList<SnippetDefinition>> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_snippets);
        }

        public Task SaveAsync(IReadOnlyList<SnippetDefinition> snippets, CancellationToken cancellationToken = default)
        {
            _snippets = snippets.ToList();
            return Task.CompletedTask;
        }
    }
}
