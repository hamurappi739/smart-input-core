using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

[Trait("Category", "MandatoryRegression")]
public class MandatoryRegressionSuiteTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly JointCorrectionDecisionService _joint;

    public MandatoryRegressionSuiteTests()
    {
        var converter = new KeyboardLayoutConverter();
        _joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon(converter)),
            converter);
    }

    [Fact]
    public void Catalog_ContainsAtLeast104Assertions()
    {
        Assert.True(
            MandatoryRegressionCatalog.TotalCount >= 104,
            $"catalog={MandatoryRegressionCatalog.TotalCount}");
    }

    [Fact]
    public void Catalog_AllMandatoryAssertionsPass()
    {
        var result = MandatoryRegressionRunner.EvaluateAll(_joint, _dictionary);

        Assert.True(
            result.Failed == 0 && result.Skipped == 0 && result.Passed >= 104,
            $"passed={result.Passed}; failed={result.Failed}; skipped={result.Skipped}; "
            + $"total={result.Total}; samples={string.Join(" | ", result.Failures.Take(15))}");
    }

    [Fact]
    public void Catalog_PrintMappingTable()
    {
        Console.WriteLine(MandatoryRegressionCatalog.FormatMappingTable());
        Console.WriteLine($"total={MandatoryRegressionCatalog.TotalCount}");
    }
}
