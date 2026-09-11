using Microsoft.Extensions.DependencyInjection;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Hotkeys;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Security;
using SmartInput.Platform.Abstractions.Text;
using SmartInput.Platform.Abstractions.Tray;
using SmartInput.Platform.Windows.Input;
using SmartInput.Platform.Windows.Services;

namespace SmartInput.Platform.Windows.DependencyInjection;

public static class WindowsPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddSmartInputWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<TextReplacementSessionCoordinator>();
        services.AddSingleton<ITextReplacementSessionNotifier>(sp =>
            sp.GetRequiredService<TextReplacementSessionCoordinator>());
        services.AddSingleton<IInputObservationFilter, WindowsInputObservationFilter>();
        services.AddSingleton<IInputMonitor, WindowsInputMonitor>();
        services.AddSingleton<IActiveApplicationService, WindowsActiveApplicationService>();
        services.AddSingleton<ITextReplacementService, WindowsTextReplacementService>();
        services.AddSingleton<ISecureInputDetector, WindowsSecureInputDetector>();
        services.AddSingleton<IBoundaryKeyPairingTracker>(sp =>
            new BoundaryKeyPairingTracker(
                BoundaryKeyPairingTracker.DefaultKeyUpWaitMilliseconds,
                sp.GetRequiredService<ITextReplacementSessionNotifier>()));
        services.AddSingleton<IBoundaryKeyInterceptor, WindowsBoundaryKeyInterceptor>();
        services.AddSingleton<IPredictionTabInterceptor, WindowsPredictionTabInterceptor>();
        services.AddSingleton<IPredictionEscInterceptor, WindowsPredictionEscInterceptor>();
        services.AddSingleton<IBoundaryKeyDeliveryService, WindowsBoundaryKeyDeliveryService>();
        services.AddSingleton<IKeyboardCharacterResolver, WindowsKeyboardCharacterResolver>();
        services.AddSingleton<IKeyboardModifierState, WindowsKeyboardModifierState>();
        services.AddSingleton<IKeyboardInputLanguageService, WindowsKeyboardInputLanguageService>();
        services.AddSingleton<ISelectedTextService, WindowsSelectedTextService>();
        services.AddSingleton<IGlobalHotkeyService, WindowsGlobalHotkeyService>();
        services.AddSingleton<ISystemTrayPlatformService, WindowsSystemTrayPlatformService>();
        services.AddSingleton<ICaretPositionService, WindowsCaretPositionService>();
        services.AddSingleton<IPredictionOverlayService, WindowsPredictionOverlayService>();
        services.AddSingleton<ICorrectionNotificationOverlayService, WindowsCorrectionNotificationOverlayService>();

        return services;
    }
}
