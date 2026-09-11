using Microsoft.Extensions.DependencyInjection;
using SmartInput.App.Services;
using SmartInput.App.ViewModels;
using SmartInput.Core.Services;
using SmartInput.Core.Integration;
using SmartInput.Runtime.DependencyInjection;

namespace SmartInput.App.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmartInputApp(this IServiceCollection services)
    {
        services.AddSmartInputRuntime();

        // The settings UI normally runs separately from the resident keyboard
        // host. Override the in-process reader with a local aggregate-only
        // reader so the displayed counters are not a misleading second set.
        services.AddSingleton<IRustShadowAuditStatusReader, ResidentRustShadowAuditStatusReader>();
        services.AddSingleton<IResidentRuntimeStatusReader, ResidentRuntimeStatusReader>();

        services.AddSingleton<IMainWindowBootstrapper, AvaloniaMainWindowBootstrapper>();
        services.AddSingleton<IApplicationStartupCoordinator, ApplicationStartupCoordinator>();
        services.AddSingleton<IApplicationThemeService, ApplicationThemeService>();
        services.AddSingleton<IApplicationShell, ApplicationShell>();
        services.AddSingleton<IApplicationShutdownCoordinator, ApplicationShutdownCoordinator>();
        services.AddSingleton<ISystemTrayCoordinator, SystemTrayCoordinator>();
        services.AddSingleton<IPredictionPreviewService, PredictionPreviewService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<AdditionalViewModel>();
        services.AddSingleton<LanguagesViewModel>();
        services.AddSingleton<CorrectionsViewModel>();
        services.AddSingleton<MyDictionaryViewModel>();
        services.AddSingleton<SnippetsViewModel>();
        services.AddSingleton<ApplicationsViewModel>();
        services.AddSingleton<HotkeysViewModel>();
        services.AddSingleton<PrivacyViewModel>();
        services.AddSingleton<PredictionPreviewViewModel>();
        services.AddSingleton<PunctuationPreviewViewModel>();
        services.AddSingleton<KbmPreviewViewModel>();
        services.AddSingleton<AdvancedViewModel>();

        return services;
    }
}
