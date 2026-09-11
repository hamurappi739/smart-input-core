using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public interface ICorrectionFeedbackNotifier
{
    void NotifySuccessfulCorrection(CorrectionKind kind);

    void NotifyCorrectionUndone();

    void NotifyUserInput();

    void NotifyContextInvalidated();
}

public sealed class NullCorrectionFeedbackNotifier : ICorrectionFeedbackNotifier
{
    public static NullCorrectionFeedbackNotifier Instance { get; } = new();

    public void NotifySuccessfulCorrection(CorrectionKind kind)
    {
    }

    public void NotifyCorrectionUndone()
    {
    }

    public void NotifyUserInput()
    {
    }

    public void NotifyContextInvalidated()
    {
    }
}

public static class CorrectionNotificationMessages
{
    public const string Layout =
        "Раскладка исправлена · дважды Shift — отменить";

    public const string Autocorrect =
        "Опечатка исправлена · дважды Shift — отменить";

    public const string Snippet =
        "Шаблон вставлен · дважды Shift — отменить";

    public const string Punctuation =
        "Пробел перед знаком препинания убран · дважды Shift — отменить";

    public const string Combined =
        "Опечатка исправлена · дважды Shift — отменить";

    public static string ForKind(CorrectionKind kind)
    {
        return kind switch
        {
            CorrectionKind.Layout => Layout,
            CorrectionKind.Autocorrect => Autocorrect,
            CorrectionKind.Combined => Combined,
            CorrectionKind.Snippet => Snippet,
            CorrectionKind.Punctuation => Punctuation,
            _ => Layout,
        };
    }
}
