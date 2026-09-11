namespace SmartInput.Core.Models;

public enum CorrectionUndoInvalidationReason
{
    SupersededByNewCorrection,
    GenuineUserInput,
    ApplicationContextChanged,
    PolicyChanged,
    EmergencyPause,
    Expired,
    UndoCompleted,
    UndoFailed,
    UndoBlocked,
}
