using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Diagnostics;

internal static class TextReplacementOutcomeMapper
{
    internal static TextReplacementOutcomeKind Map(TextReplacementResult result)
    {
        return result.Status switch
        {
            TextReplacementStatus.Success => TextReplacementOutcomeKind.Success,
            TextReplacementStatus.AbortedByUserInput => TextReplacementOutcomeKind.Aborted,
            TextReplacementStatus.Blocked => TextReplacementOutcomeKind.Blocked,
            TextReplacementStatus.NotSupported => TextReplacementOutcomeKind.NotSupported,
            TextReplacementStatus.Failed when IsTimeout(result) => TextReplacementOutcomeKind.Timeout,
            TextReplacementStatus.Failed => TextReplacementOutcomeKind.Failed,
            _ => TextReplacementOutcomeKind.Failed,
        };
    }

    private static bool IsTimeout(TextReplacementResult result)
    {
        return result.FailureReason?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true;
    }
}
