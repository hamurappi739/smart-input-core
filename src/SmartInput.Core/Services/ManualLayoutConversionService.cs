using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Services;

public interface IManualLayoutConversionService
{
    Task<LayoutConversionResult> ConvertSelectedTextAsync(
        LayoutConversionDirection direction,
        CancellationToken cancellationToken = default);
}

public sealed class ManualLayoutConversionService : IManualLayoutConversionService
{
    public const string SecureInputBlockedReason =
        "Преобразование раскладки недоступно во время защищённого ввода.";

    public const string PolicyBlockedReasonPrefix = "Преобразование раскладки заблокировано политикой безопасности: ";

    private readonly ILayoutConversionService _layoutConversionService;
    private readonly ISelectedTextService _selectedTextService;
    private readonly IAutomationSafetyService _automationSafetyService;

    public ManualLayoutConversionService(
        ILayoutConversionService layoutConversionService,
        ISelectedTextService selectedTextService,
        IAutomationSafetyService automationSafetyService)
    {
        _layoutConversionService = layoutConversionService;
        _selectedTextService = selectedTextService;
        _automationSafetyService = automationSafetyService;
    }

    public async Task<LayoutConversionResult> ConvertSelectedTextAsync(
        LayoutConversionDirection direction,
        CancellationToken cancellationToken = default)
    {
        if (!await _automationSafetyService
                .IsOperationAllowedAsync(AutomationOperationKind.ManualExternalTextOperation, cancellationToken)
                .ConfigureAwait(false))
        {
            var reason = _automationSafetyService.GetBlockedReason(AutomationOperationKind.ManualExternalTextOperation)
                ?? "Ручное преобразование раскладки не разрешено в текущем контексте.";

            if (reason.Contains("Secure input", StringComparison.OrdinalIgnoreCase))
            {
                return LayoutConversionResult.Blocked(SecureInputBlockedReason);
            }

            return LayoutConversionResult.Blocked(PolicyBlockedReasonPrefix + reason);
        }

        var selectedText = await _selectedTextService.GetSelectedTextAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(selectedText))
        {
            return LayoutConversionResult.NoSelection();
        }

        var converted = _layoutConversionService.Convert(selectedText, direction);
        var replaced = await _selectedTextService
            .ReplaceSelectedTextAsync(converted, cancellationToken)
            .ConfigureAwait(false);

        if (!replaced)
        {
            return LayoutConversionResult.Failed("Не удалось заменить выделенный текст в активном приложении.");
        }

        return LayoutConversionResult.Success(converted.Length);
    }
}
