namespace SmartInput.Platform.Abstractions.Input;

public interface ITextReplacementSessionNotifier
{
    bool IsReplacementActive { get; }

    void NotifyKeyboardEventDuringReplacement(KeyEventType eventType, KeyboardHookMetadata metadata);
}
