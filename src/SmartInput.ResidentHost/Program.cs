using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartInput.ResidentHost.Services;
using SmartInput.Runtime.DependencyInjection;

namespace SmartInput.ResidentHost;

internal static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                // Keyboard text must never be written to console or a file.
                logging.ClearProviders();
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                services.AddSmartInputRuntime();
                services.AddSingleton<ResidentSettingsReloadCoordinator>();
                services.AddSingleton<ResidentSnippetReloadCoordinator>();
                services.AddSingleton<ResidentLearningReloadCoordinator>();
                services.AddSingleton<ResidentUserDictionaryReloadCoordinator>();
                services.AddSingleton<ResidentPredictionFeatureCoordinator>();
                services.AddSingleton<ResidentTrayCoordinator>();
                services.AddHostedService<ResidentHostService>();
                services.AddHostedService<RustShadowAuditStatusPipeService>();
                services.AddHostedService<ResidentRuntimeStatusPipeService>();
            })
            .Build();

        await host.RunAsync().ConfigureAwait(false);
    }
}
