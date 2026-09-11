using SmartInput.Core.Engines;
using SmartInput.Core.Integration;

namespace SmartInput.Core.Tests;

public sealed class PortableCorrectionEngineTests
{
    [Fact]
    public void Evaluate_LayoutCandidate_ReturnsTransportSafeDecision()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var converter = new KeyboardLayoutConverter();
        var decision = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            new AutocorrectionService(),
            converter);
        var engine = new PortableCorrectionEngine(decision, dictionary);

        var response = engine.Evaluate(new PortableCorrectionRequest
        {
            Token = "ghbdtn",
            LayoutEnabled = true,
            AutocorrectEnabled = false,
        });

        Assert.Equal("apply", response.Decision);
        Assert.Equal("привет", response.ReplacementToken);
        Assert.Equal("layout", response.Kind);
        Assert.True(response.CanApply);
    }

    [Fact]
    public void Evaluate_ProtectedToken_DoesNotProduceApply()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var converter = new KeyboardLayoutConverter();
        var decision = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            new AutocorrectionService(),
            converter);
        var engine = new PortableCorrectionEngine(decision, dictionary);

        var response = engine.Evaluate(new PortableCorrectionRequest
        {
            Token = "https://example.com/ghbdtn",
        });

        Assert.NotEqual("apply", response.Decision);
        Assert.False(response.CanApply);
        Assert.Null(response.ReplacementToken);
    }
}
