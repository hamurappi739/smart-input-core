using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public sealed class EarlyLayoutTests
{
    private static readonly Lazy<EarlyLayoutModel> Model = new(() => EarlyLayoutModel.Train(
        StarterAutocorrectLexicon.Entries.Select(x => (x.Key.Language, x.Key.Word, x.Value * x.Value))));
    private static EarlyLayoutSession Session()
    {
        var session = new EarlyLayoutSession(Model.Value, new EarlyLayoutOptions { MinimumMargin = 1.5 });
        session.SetContext(42, TypingLanguage.English, allowed: true);
        return session;
    }
    private static EarlyLayoutProposal Proposal(EarlyLayoutSession session)
    {
        EarlyLayoutProposal? result = null;
        foreach (var c in "ghbd") result = session.Append(c);
        return Assert.IsType<EarlyLayoutProposal>(result);
    }

    [Theory]
    [InlineData("ghbd", TypingLanguage.English)]
    [InlineData("акщт", TypingLanguage.Russian)]
    public void StrongPrefixProducesPhysicalCandidate(string token, TypingLanguage language)
    {
        var result = Model.Value.Evaluate(token, language, new EarlyLayoutOptions { MinimumMargin = 1.5 });
        Assert.Equal(EarlyLayoutVerdict.Candidate, result.Verdict);
        Assert.True(result.Margin >= 1.5);
        Assert.NotEqual(language, result.TargetLanguage);
    }

    [Theory]
    [InlineData("gh")]
    [InlineData("ghb")]
    [InlineData("frontend")]
    [InlineData("hello")]
    [InlineData("BMW")]
    [InlineData("AAA")]
    [InlineData("GPT")]
    [InlineData("QA")]
    [InlineData("userName")]
    [InlineData("ghbd_name")]
    [InlineData("ghbd@example.test")]
    [InlineData("https://ghbd.test")]
    [InlineData("C:\\ghbd")]
    public void PreservesOrdinaryAndProtectedInput(string token)
        => Assert.NotEqual(EarlyLayoutVerdict.Candidate, Model.Value.Evaluate(token, TypingLanguage.English).Verdict);

    [Theory]
    [InlineData("прив")]
    [InlineData("здрав")]
    [InlineData("йф")]
    [InlineData("ЙФ")]
    [InlineData("пзе")]
    [InlineData("больш")]
    public void PreservesCorrectRussianPrefixesAndDefersAbbreviations(string token)
        => Assert.NotEqual(EarlyLayoutVerdict.Candidate, Model.Value.Evaluate(token, TypingLanguage.Russian).Verdict);

    [Fact]
    public void SecureContextAlwaysBlocks()
        => Assert.Equal(EarlyLayoutVerdict.Protected,
            Model.Value.Evaluate("ghbd", TypingLanguage.English, protectedContext: true).Verdict);

    [Fact]
    public void NumberingWordsDoesNotResolveSharedPrefixes()
    {
        var model = EarlyLayoutModel.Train(new[] {
            (TypingLanguage.English, "ghbdcustom", 1.0),
            (TypingLanguage.Russian, "привет", 1.0),
            (TypingLanguage.Russian, "привести", 1.0),
        });
        Assert.Equal(EarlyLayoutVerdict.Wait,
            model.Evaluate("ghbd", TypingLanguage.English, new EarlyLayoutOptions { MinimumMargin = 0 }).Verdict);
    }

    [Fact]
    public void NewCharacterInvalidatesProposal()
    {
        var session = Session();
        var proposal = Proposal(session);
        session.Append('t');
        Assert.False(session.TryReserve(proposal, 42, true));
    }

    [Fact]
    public void BackspaceInvalidatesProposal()
    {
        var session = Session();
        var proposal = Proposal(session);
        session.Backspace();
        Assert.False(session.TryReserve(proposal, 42, true));
    }

    [Fact]
    public void FocusChangeInvalidatesProposal()
    {
        var session = Session();
        var proposal = Proposal(session);
        session.SetContext(43, TypingLanguage.English, true);
        Assert.False(session.TryReserve(proposal, 43, true));
        Assert.False(session.TryReserve(proposal, 42, true));
    }

    [Fact]
    public void PolicyChangeInvalidatesProposal()
    {
        var session = Session();
        var proposal = Proposal(session);
        session.SetContext(42, TypingLanguage.English, false);
        Assert.False(session.TryReserve(proposal, 42, true));
    }

    [Fact]
    public void MustOwnInputBarrierBeforeReservation()
    {
        var session = Session();
        var proposal = Proposal(session);
        Assert.False(session.TryReserve(proposal, 42, false));
        Assert.True(session.TryReserve(proposal, 42, true));
        Assert.Throws<InvalidOperationException>(() => session.Append('t'));
        Assert.Throws<InvalidOperationException>(() => session.Backspace());
        Assert.False(session.TryReserve(proposal, 42, true));
    }

    [Fact]
    public void SuccessDoesNotDoubleApplyAndPreservesContinuation()
    {
        var session = Session();
        var proposal = Proposal(session);
        Assert.Equal("прив", proposal.Replacement);
        Assert.True(session.TryReserve(proposal, 42, true));
        Assert.True(session.Complete(proposal, true, true));
        Assert.False(session.Complete(proposal, true, true));
        session.SetContext(42, TypingLanguage.Russian, true);
        Assert.Null(session.Append('е'));
        Assert.Null(session.Append('т'));
        Assert.Equal(6, session.Status.Length);
        Assert.True(session.Status.Switched);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FailedTransactionDoesNotRetry(bool textApplied, bool acknowledged)
    {
        var session = Session();
        var proposal = Proposal(session);
        Assert.True(session.TryReserve(proposal, 42, true));
        Assert.False(session.Complete(proposal, textApplied, acknowledged));
        Assert.Null(session.Append('t'));
        Assert.True(session.Status.Switched);
    }

    [Fact]
    public void IdentifierSuffixDoesNotBecomeFreshWord()
    {
        var session = Session();
        foreach (var c in "id_ghbd") Assert.Null(session.Append(c));
        session.Reset();
        Assert.NotNull(Proposal(session));
    }

    [Fact]
    public void BoundedTokenDiscardsUntilBoundary()
    {
        var session = Session();
        for (var i = 0; i < 256; i++) Assert.Null(session.Append('a'));
        foreach (var c in "ghbd") Assert.Null(session.Append(c));
        Assert.True(session.Status.Length <= 128);
    }

    [Fact]
    public void ModelRoundTripMatchesEveryPrefix()
    {
        using var stream = new MemoryStream();
        Model.Value.Save(stream);
        stream.Position = 0;
        var loaded = EarlyLayoutModel.Load(stream);
        foreach (var word in new[] { "ghbdtn", "frontend", "hello", "ghbdcustom" })
        for (var i = 1; i <= word.Length; i++)
            Assert.Equal(Model.Value.Evaluate(word[..i], TypingLanguage.English), loaded.Evaluate(word[..i], TypingLanguage.English));
        Assert.True(loaded.TableBytes < 4 * 1024 * 1024);
    }

    [Fact]
    public void InvalidModelHeaderIsRejected()
        => Assert.Throws<InvalidDataException>(() => EarlyLayoutModel.Load(new MemoryStream(new byte[8])));

    [Fact]
    public void TruncatedModelIsRejected()
        => Assert.Throws<EndOfStreamException>(() => EarlyLayoutModel.Load(new MemoryStream(new byte[2])));

    [Fact]
    public void MaliciousPrefixCountIsRejectedBeforeAllocation()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write(0x314C4953);
        writer.Write(int.MaxValue);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => EarlyLayoutModel.Load(stream));
    }

    [Fact]
    public void ModelRejectsTrailingData()
    {
        using var stream = new MemoryStream();
        Model.Value.Save(stream);
        stream.WriteByte(1);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => EarlyLayoutModel.Load(stream));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void InvalidThresholdsCannotEnableAggressiveSwitching(double margin)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Model.Value.Evaluate("ghbd", TypingLanguage.English,
            new EarlyLayoutOptions { MinimumMargin = margin }));

    [Fact]
    public void ThreeCharactersRequireExplicitOptionAndEightContinuations()
    {
        var words = new[] { "привет", "приветы", "привету", "приветом", "привете", "приветов", "приветам", "приветах" };
        var training = words.Select(word => (TypingLanguage.Russian, word, 1.0))
            .Append((TypingLanguage.English, "hello", 1.0)).ToArray();
        var model = EarlyLayoutModel.Train(training);
        var options = new EarlyLayoutOptions { MinimumLength = 3, MinimumMargin = 0 };
        Assert.Equal(EarlyLayoutVerdict.Wait, model.Evaluate("ghb", TypingLanguage.English).Verdict);
        Assert.Equal(EarlyLayoutVerdict.Candidate, model.Evaluate("ghb", TypingLanguage.English, options).Verdict);
        var insufficient = EarlyLayoutModel.Train(training.Skip(1));
        Assert.Equal(EarlyLayoutVerdict.Wait, insufficient.Evaluate("ghb", TypingLanguage.English, options).Verdict);
    }

    [Fact]
    public void ReservationDoesNotSurviveFocusChange()
    {
        var session = Session();
        var proposal = Proposal(session);
        Assert.True(session.TryReserve(proposal, 42, true));
        session.SetContext(43, TypingLanguage.English, true);
        Assert.False(session.Complete(proposal, true, true));
        Assert.Equal(0, session.Status.Length);
        Assert.False(session.Status.Reserved);
    }

    [Fact]
    public void SameValuesInUnissuedProposalCannotReserve()
    {
        var session = Session();
        var proposal = Proposal(session);
        var copy = new EarlyLayoutProposal
        {
            Version = proposal.Version, Context = proposal.Context,
            Original = proposal.Original, Replacement = proposal.Replacement,
            TargetLanguage = proposal.TargetLanguage,
        };
        Assert.False(session.TryReserve(copy, 42, true));
        Assert.True(session.TryReserve(proposal, 42, true));
    }

    [Fact]
    public void MissingForegroundContextCannotProduceProposal()
    {
        var session = Session();
        session.SetContext(0, TypingLanguage.English, true);
        foreach (var character in "ghbd") Assert.Null(session.Append(character));
    }
}
