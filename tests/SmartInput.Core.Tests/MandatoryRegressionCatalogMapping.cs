namespace SmartInput.Core.Tests;

/// <summary>
/// Maps each of the 104 mandatory catalog assertions to OperationClusterRegressionTests coverage.
/// </summary>
internal static class MandatoryRegressionCatalogMapping
{
    private static readonly HashSet<string> MustCorrectInputs =
    [
        "миняй", "vbyzq", "vtyzq", "vtyz", "gtie", "gbie", "деелай", "машиина", "говноо", "меняяй",
        "дериись", "пениис", "будуут", "напесал", "исслидование", "посянить", "спецеально",
        "испровляет", "обстаят", "допалнение", "дууш", "превет", "helo", "teh", "adn",
        "recieve", "becuase", "thier", "ghbdtn", "руддщ", "мущ", "пзг",
    ];

    private static readonly HashSet<string> MustPreserveInputs =
    [
        "начала", "начало", "дает", "даёт", "даст", "дают", "дать", "жать", "дал", "дела",
        "дело", "видел", "видик", "нас", "нам", "меня", "сеня", "неизвестное", "неизвестно",
        "гавно", "говно", "душ", "пишу", "все", "всё", "еще", "ещё", "идет", "идёт", "ждет",
        "ждёт", "начали", "касса", "ванна", "группа", "класс", "суббота", "Россия", "Алла",
        "Анна", "тонна", "сумма", "комиссия", "профессия", "территория", "искусство",
        "рассказ", "программа", "hello", "letter", "coffee", "class", "address", "success",
        "necessary", "parallel", "application", "correct",
    ];

    private static readonly HashSet<string> MustNotApplyUnknownInputs =
    [
        "ufdyj", "leei", "plhfdcndeqnt",
    ];

    private static readonly HashSet<string> MustApplyCapitalizedInputs =
    [
        "Миняй", "Превет", "МИНЯЙ",
    ];

    private static readonly HashSet<string> MustNotLayoutLeakInputs =
    [
        "миняй", "гавно", "говно", "дууш", "душ", "меня",
    ];

    internal static bool IsCoveredByOperationCluster(MandatoryRegressionAssertion assertion)
        => assertion.Kind switch
        {
            MandatoryRegressionKind.MustApply
                => MustCorrectInputs.Contains(assertion.Input),
            MandatoryRegressionKind.MustApplyWithContext
                => string.Equals(assertion.Input, "nen", StringComparison.Ordinal),
            MandatoryRegressionKind.MustApplyCapitalized
                => MustApplyCapitalizedInputs.Contains(assertion.Input),
            MandatoryRegressionKind.MustPreserve
                => MustPreserveInputs.Contains(assertion.Input),
            MandatoryRegressionKind.MustNotLayoutLeak
                => MustNotLayoutLeakInputs.Contains(assertion.Input),
            MandatoryRegressionKind.MustNotApplyUnknown
                => MustNotApplyUnknownInputs.Contains(assertion.Input),
            _ => false,
        };
}
