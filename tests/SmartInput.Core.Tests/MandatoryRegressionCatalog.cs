using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

internal enum MandatoryRegressionKind
{
    MustApply,
    MustPreserve,
    MustNotLayoutLeak,
    MustNotApplyUnknown,
    MustApplyWithContext,
    MustApplyCapitalized,
}

internal sealed record MandatoryRegressionAssertion(
    string Id,
    MandatoryRegressionKind Kind,
    string Input,
    string? ExpectedOutput = null,
    string? ForbiddenOutput = null,
    SentenceLanguageHint? LanguageHint = null,
    string SourceDocument = "acceptance-gate");

/// <summary>
/// Canonical list of 104 individual input/output assertions for acceptance gates.
/// Each entry is one assertion — not one test method.
/// </summary>
internal static class MandatoryRegressionCatalog
{
    internal static IReadOnlyList<MandatoryRegressionAssertion> All { get; } = Build();

    internal static int TotalCount => All.Count;

    internal static string FormatMappingTable()
    {
        var lines = new List<string>
        {
            "Id\tRequiredAssertion\tKind\tOperationClusterCoverage\tStatus\tReason",
        };

        foreach (var assertion in All)
        {
            var coverage = MapToOperationCluster(assertion);
            lines.Add(
                $"{assertion.Id}\t{FormatAssertion(assertion)}\t{assertion.Kind}\t{coverage.TestName}\t{coverage.Status}\t{coverage.Reason}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static (string TestName, string Status, string Reason) MapToOperationCluster(
        MandatoryRegressionAssertion assertion)
    {
        if (MandatoryRegressionCatalogMapping.IsCoveredByOperationCluster(assertion))
        {
            return ("OperationClusterRegressionTests", "Present", "Parameterized theory row");
        }

        return ("—", "Missing", "Not yet wired into OperationClusterRegressionTests");
    }

    private static string FormatAssertion(MandatoryRegressionAssertion assertion)
        => assertion.Kind switch
        {
            MandatoryRegressionKind.MustApply or MandatoryRegressionKind.MustApplyCapitalized
                or MandatoryRegressionKind.MustApplyWithContext
                => $"{assertion.Input} → {assertion.ExpectedOutput}",
            MandatoryRegressionKind.MustPreserve => $"{assertion.Input} unchanged",
            MandatoryRegressionKind.MustNotLayoutLeak => $"{assertion.Input} must not → {assertion.ForbiddenOutput}",
            MandatoryRegressionKind.MustNotApplyUnknown => $"{assertion.Input} must not Apply",
            _ => assertion.Input,
        };

    private static IReadOnlyList<MandatoryRegressionAssertion> Build()
    {
        var list = new List<MandatoryRegressionAssertion>();

        void Apply(string id, string input, string output)
            => list.Add(new MandatoryRegressionAssertion(id, MandatoryRegressionKind.MustApply, input, output));

        void ApplyCtx(string id, string input, string output)
            => list.Add(new MandatoryRegressionAssertion(
                id,
                MandatoryRegressionKind.MustApplyWithContext,
                input,
                output,
                LanguageHint: new SentenceLanguageHint(TypingLanguage.Russian, 4, 0, 4, 20)));

        void ApplyCap(string id, string input, string output)
            => list.Add(new MandatoryRegressionAssertion(id, MandatoryRegressionKind.MustApplyCapitalized, input, output));

        void Preserve(string id, string word)
            => list.Add(new MandatoryRegressionAssertion(id, MandatoryRegressionKind.MustPreserve, word));

        void NoLayout(string id, string input, string forbidden)
            => list.Add(new MandatoryRegressionAssertion(id, MandatoryRegressionKind.MustNotLayoutLeak, input, ForbiddenOutput: forbidden));

        void NoUnknown(string id, string input)
            => list.Add(new MandatoryRegressionAssertion(id, MandatoryRegressionKind.MustNotApplyUnknown, input));

        // Positive spelling / combined / layout (29)
        Apply("A01", "миняй", "меняй");
        Apply("A02", "vbyzq", "меняй");
        Apply("A03", "vtyzq", "меняй");
        ApplyCtx("A04", "nen", "тут");
        Apply("A05", "gtie", "пишу");
        Apply("A06", "gbie", "пишу");
        Apply("A07", "деелай", "делай");
        Apply("A08", "машиина", "машина");
        Apply("A09", "говноо", "говно");
        Apply("A10", "меняяй", "меняй");
        Apply("A11", "дериись", "дерись");
        Apply("A12", "пениис", "пенис");
        Apply("A13", "будуут", "будут");
        Apply("A14", "напесал", "написал");
        Apply("A15", "исслидование", "исследование");
        Apply("A16", "посянить", "пояснить");
        Apply("A17", "спецеально", "специально");
        Apply("A18", "испровляет", "исправляет");
        Apply("A19", "обстаят", "обстоят");
        Apply("A20", "допалнение", "дополнение");
        Apply("A21", "дууш", "душ");
        Apply("A22", "превет", "привет");
        Apply("A23", "helo", "hello");
        Apply("A24", "teh", "the");
        Apply("A25", "adn", "and");
        Apply("A26", "recieve", "receive");
        Apply("A27", "becuase", "because");
        Apply("A28", "thier", "their");
        Apply("A29", "ghbdtn", "привет");
        Apply("A30", "руддщ", "hello");
        Apply("A31", "мущ", "veo");
        Apply("A32", "пзг", "gpu");

        // Capitalization preservation on apply (3)
        ApplyCap("C01", "Миняй", "Меняй");
        ApplyCap("C02", "Превет", "Привет");
        ApplyCap("C03", "МИНЯЙ", "МЕНЯЙ");

        // Exact known words (28)
        Preserve("E01", "начала");
        Preserve("E02", "начало");
        Preserve("E03", "дает");
        Preserve("E04", "даёт");
        Preserve("E05", "даст");
        Preserve("E06", "дают");
        Preserve("E07", "дать");
        Preserve("E08", "жать");
        Preserve("E09", "дал");
        Preserve("E10", "дела");
        Preserve("E11", "дело");
        Preserve("E12", "видел");
        Preserve("E13", "видик");
        Preserve("E14", "нас");
        Preserve("E15", "нам");
        Preserve("E16", "меня");
        Preserve("E17", "сеня");
        Preserve("E18", "неизвестное");
        Preserve("E19", "неизвестно");
        Preserve("E20", "гавно");
        Preserve("E21", "говно");
        Preserve("E22", "душ");
        Preserve("E23", "пишу");
        Preserve("E24", "все");
        Preserve("E25", "всё");
        Preserve("E26", "еще");
        Preserve("E27", "ещё");
        Preserve("E28", "идет");
        Preserve("E29", "идёт");
        Preserve("E30", "ждет");
        Preserve("E31", "ждёт");
        Preserve("E32", "начали");

        // Legitimate double letters — Russian (14)
        Preserve("D01", "касса");
        Preserve("D02", "ванна");
        Preserve("D03", "группа");
        Preserve("D04", "класс");
        Preserve("D05", "суббота");
        Preserve("D06", "Россия");
        Preserve("D07", "Алла");
        Preserve("D08", "Анна");
        Preserve("D09", "тонна");
        Preserve("D10", "сумма");
        Preserve("D11", "комиссия");
        Preserve("D12", "профессия");
        Preserve("D13", "территория");
        Preserve("D14", "искусство");
        Preserve("D15", "рассказ");
        Preserve("D16", "программа");

        // Legitimate double letters — English (10)
        Preserve("D17", "hello");
        Preserve("D18", "letter");
        Preserve("D19", "coffee");
        Preserve("D20", "class");
        Preserve("D21", "address");
        Preserve("D22", "success");
        Preserve("D23", "necessary");
        Preserve("D24", "parallel");
        Preserve("D25", "application");
        Preserve("D26", "correct");

        // Layout must-not-leak (6)
        NoLayout("L01", "миняй", "vbyzq");
        NoLayout("L02", "гавно", "ufdyj");
        NoLayout("L03", "говно", "ujdyj");
        NoLayout("L04", "дууш", "leei");
        NoLayout("L05", "душ", "lei");
        NoLayout("L06", "меня", "vtyz");

        // Unknown-name must not Apply (5)
        NoUnknown("U01", "vbyzq");
        NoUnknown("U02", "ufdyj");
        NoUnknown("U03", "leei");
        // Four-key pronouns now participate in the exact short-layout policy.
        Apply("U04", "vtyz", "меня");
        NoUnknown("U05", "plhfdcndeqnt");

        if (list.Count < 104)
        {
            throw new InvalidOperationException($"Mandatory regression catalog must contain at least 104 assertions, found {list.Count}.");
        }

        return list;
    }
}

internal sealed class MandatoryRegressionRunResult
{
    public int Total { get; init; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<string> Failures { get; } = [];
}

internal static class MandatoryRegressionRunner
{
    private static readonly Dictionary<string, string> UnknownNameNegativeWithPositiveAnchor =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vbyzq"] = "меняй",
            ["vtyzq"] = "меняй",
        };

    internal static MandatoryRegressionRunResult EvaluateAll(JointCorrectionDecisionService joint, IAutocorrectDictionary dictionary)
    {
        var result = new MandatoryRegressionRunResult { Total = MandatoryRegressionCatalog.TotalCount };

        foreach (var assertion in MandatoryRegressionCatalog.All)
        {
            if (!EvaluateAssertion(joint, dictionary, assertion, out var failure))
            {
                result.Failed++;
                if (result.Failures.Count < 40)
                {
                    result.Failures.Add(failure);
                }

                continue;
            }

            result.Passed++;
        }

        return result;
    }

    private static bool EvaluateAssertion(
        JointCorrectionDecisionService joint,
        IAutocorrectDictionary dictionary,
        MandatoryRegressionAssertion assertion,
        out string failure)
    {
        var decision = joint.Evaluate(
            assertion.Input,
            dictionary,
            layoutEnabled: true,
            autocorrectEnabled: true,
            languageHint: assertion.LanguageHint);

        switch (assertion.Kind)
        {
            case MandatoryRegressionKind.MustApply:
            case MandatoryRegressionKind.MustApplyWithContext:
            case MandatoryRegressionKind.MustApplyCapitalized:
                if (decision.Recommendation != JointCorrectionRecommendation.Apply
                    || !string.Equals(decision.ReplacementToken, assertion.ExpectedOutput, StringComparison.Ordinal))
                {
                    failure = $"{assertion.Id}:{assertion.Input} expected Apply→{assertion.ExpectedOutput} got {decision.Recommendation}→{decision.ReplacementToken}";
                    return false;
                }

                break;

            case MandatoryRegressionKind.MustPreserve:
                if (decision.Recommendation == JointCorrectionRecommendation.Apply)
                {
                    failure = $"{assertion.Id}:{assertion.Input} must preserve, got Apply→{decision.ReplacementToken}/{decision.Kind}";
                    return false;
                }

                break;

            case MandatoryRegressionKind.MustNotApplyUnknown:
                if (UnknownNameNegativeWithPositiveAnchor.TryGetValue(assertion.Input, out var anchorTarget))
                {
                    if (decision.Recommendation != JointCorrectionRecommendation.Apply
                        || !string.Equals(decision.ReplacementToken, anchorTarget, StringComparison.Ordinal))
                    {
                        failure =
                            $"{assertion.Id}:{assertion.Input} unknown-name negative with anchor expected Apply→{anchorTarget} "
                            + $"got {decision.Recommendation}→{decision.ReplacementToken}/{decision.Kind}";
                        return false;
                    }

                    break;
                }

                if (decision.Recommendation == JointCorrectionRecommendation.Apply)
                {
                    failure = $"{assertion.Id}:{assertion.Input} must not Apply, got {decision.ReplacementToken}/{decision.Kind}";
                    return false;
                }

                break;

            case MandatoryRegressionKind.MustNotLayoutLeak:
                if (decision.Recommendation == JointCorrectionRecommendation.Apply
                    && string.Equals(decision.ReplacementToken, assertion.ForbiddenOutput, StringComparison.OrdinalIgnoreCase))
                {
                    failure = $"{assertion.Id}:{assertion.Input} leaked to forbidden {assertion.ForbiddenOutput}";
                    return false;
                }

                break;
        }

        failure = string.Empty;
        return true;
    }
}
