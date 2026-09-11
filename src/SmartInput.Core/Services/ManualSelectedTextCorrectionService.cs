using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Services;

public interface IManualSelectedTextCorrectionService
{
    Task<ManualCorrectionResult> ExecuteAsync(
        ManualCorrectionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit selected-text corrections. Fix Layout uses the same
/// <see cref="ILayoutConversionService"/> as <see cref="ManualLayoutConversionService"/>.
/// </summary>
public sealed class ManualSelectedTextCorrectionService : IManualSelectedTextCorrectionService
{
    public const string PolicyBlockedReasonPrefix = "Manual correction blocked by safety policy: ";
    public const string SecureInputBlockedReason =
        "Manual correction is unavailable while secure input is active.";
    public const string ProtectionDisabledBlockedReason =
        "Manual correction is unavailable while Protection is disabled.";
    public const string LayoutDirectionRequiredReason =
        "A layout conversion direction is required for this action.";

    private readonly ILayoutConversionService _layoutConversionService;
    private readonly ISelectedTextService _selectedTextService;
    private readonly IAutocorrectionService _autocorrectionService;
    private readonly IAutocorrectDictionary _autocorrectDictionary;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly ISettingsService _settingsService;
    private readonly AutocorrectionOptions _autocorrectionOptions;

    public ManualSelectedTextCorrectionService(
        ILayoutConversionService layoutConversionService,
        ISelectedTextService selectedTextService,
        IAutocorrectionService autocorrectionService,
        IAutocorrectDictionary autocorrectDictionary,
        IAutomationSafetyService automationSafetyService,
        ISettingsService settingsService,
        AutocorrectionOptions? autocorrectionOptions = null)
    {
        _layoutConversionService = layoutConversionService;
        _selectedTextService = selectedTextService;
        _autocorrectionService = autocorrectionService;
        _autocorrectDictionary = autocorrectDictionary;
        _automationSafetyService = automationSafetyService;
        _settingsService = settingsService;
        _autocorrectionOptions = autocorrectionOptions ?? new AutocorrectionOptions();
    }

    public async Task<ManualCorrectionResult> ExecuteAsync(
        ManualCorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if ((request.Action is ManualCorrectionActionKind.FixLayout or ManualCorrectionActionKind.FixText)
            && request.LayoutDirection is null)
        {
            return ManualCorrectionResult.Failed(LayoutDirectionRequiredReason);
        }

        var blockedBeforeRead = await TryCreateBlockedResultAsync(cancellationToken).ConfigureAwait(false);
        if (blockedBeforeRead is not null)
        {
            return blockedBeforeRead;
        }

        string? selectedText;
        try
        {
            selectedText = await _selectedTextService
                .GetSelectedTextAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ManualCorrectionResult.Cancelled("Reading the selection was cancelled.");
        }
        catch (Exception)
        {
            return ManualCorrectionResult.Failed("Selected text could not be read from the foreground application.");
        }

        if (string.IsNullOrEmpty(selectedText))
        {
            return ManualCorrectionResult.NoSelection();
        }

        var workingText = selectedText;
        var layoutApplied = false;
        var tokensExamined = 0;
        var tokensChanged = 0;
        var spellingApplied = false;

        if (request.Action is ManualCorrectionActionKind.FixLayout or ManualCorrectionActionKind.FixText)
        {
            workingText = _layoutConversionService.Convert(workingText, request.LayoutDirection!.Value);
            layoutApplied = !string.Equals(workingText, selectedText, StringComparison.Ordinal);
        }

        if (request.Action is ManualCorrectionActionKind.FixSpelling or ManualCorrectionActionKind.FixText)
        {
            var spelling = ApplySpelling(workingText);
            workingText = spelling.Text;
            tokensExamined = spelling.TokensExamined;
            tokensChanged = spelling.TokensChanged;
            spellingApplied = tokensChanged > 0;
        }

        if (string.Equals(workingText, selectedText, StringComparison.Ordinal))
        {
            return ManualCorrectionResult.NoChange(
                selectedText.Length,
                tokensExamined,
                layoutApplied,
                spellingApplied);
        }

        var blockedBeforeReplace = await TryCreateBlockedResultAsync(cancellationToken).ConfigureAwait(false);
        if (blockedBeforeReplace is not null)
        {
            return blockedBeforeReplace;
        }

        bool replaced;
        try
        {
            replaced = await _selectedTextService
                .ReplaceSelectedTextAsync(workingText, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ManualCorrectionResult.Cancelled("Replacing the selection was cancelled.");
        }
        catch (Exception)
        {
            return ManualCorrectionResult.Failed("Selected text could not be replaced in the foreground application.");
        }

        if (!replaced)
        {
            return ManualCorrectionResult.Failed("Selected text could not be replaced in the foreground application.");
        }

        return ManualCorrectionResult.Success(
            workingText.Length,
            tokensExamined,
            tokensChanged,
            layoutApplied,
            spellingApplied);
    }

    private (string Text, int TokensExamined, int TokensChanged) ApplySpelling(string text)
    {
        var segments = SelectedTextSegmenter.Split(text);
        var output = new List<SelectedTextSegmenter.Segment>(segments.Count);
        var examined = 0;
        var changed = 0;

        foreach (var segment in segments)
        {
            if (!segment.IsCorrectableWord)
            {
                output.Add(segment);
                continue;
            }

            examined++;

            if (ProtectedTokenAnalyzer.IsProtected(segment.Text))
            {
                output.Add(segment);
                continue;
            }

            var language = TypingLanguageResolver.Resolve(segment.Text);
            if (language is null)
            {
                output.Add(segment);
                continue;
            }

            var detection = _autocorrectionService.Evaluate(
                segment.Text,
                language.Value,
                _autocorrectDictionary,
                _autocorrectionOptions);

            if (detection.Recommendation == AutocorrectionRecommendation.Candidate
                && !string.IsNullOrEmpty(detection.CandidateToken)
                && !string.Equals(detection.CandidateToken, segment.Text, StringComparison.Ordinal))
            {
                output.Add(segment with { Text = detection.CandidateToken });
                changed++;
            }
            else
            {
                output.Add(segment);
            }
        }

        return (SelectedTextSegmenter.Join(output), examined, changed);
    }

    private async Task<ManualCorrectionResult?> TryCreateBlockedResultAsync(CancellationToken cancellationToken)
    {
        if (!_settingsService.Current.IsEnabled)
        {
            return ManualCorrectionResult.Blocked(ProtectionDisabledBlockedReason);
        }

        var policy = await _automationSafetyService
            .EvaluateCurrentContextAsync(cancellationToken)
            .ConfigureAwait(false);

        if (policy.IsAllowed(AutomationOperationKind.ManualExternalTextOperation))
        {
            return null;
        }

        if (policy.State == AutomationPolicyState.SecureInput
            || (policy.Reason?.Contains("Secure input", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return ManualCorrectionResult.Blocked(SecureInputBlockedReason);
        }

        return ManualCorrectionResult.Blocked(
            PolicyBlockedReasonPrefix + (policy.Reason ?? policy.State.ToString()));
    }
}
