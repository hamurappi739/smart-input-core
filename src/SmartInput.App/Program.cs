using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartInput.App.DependencyInjection;

namespace SmartInput.App;

public static class AppServices
{
    public static IServiceProvider Provider { get; private set; } = null!;

    internal static void Initialize(IServiceProvider provider)
    {
        Provider = provider;
    }
}

sealed class Program
{
    private const string RuntimeMutexName = @"Local\SmartInput.KeyboardRuntime.v1";

    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureUiRoleWhenResidentRuntimeIsAlreadyRunning();

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services => services.AddSmartInputApp())
            .Build();

        AppServices.Initialize(host.Services);

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureUiRoleWhenResidentRuntimeIsAlreadyRunning()
    {
        // ResidentHost owns the keyboard runtime. An explicit value still
        // wins, while a direct settings launch automatically becomes UI-only
        // when the resident process already owns the runtime mutex.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMARTINPUT_UI_ONLY")))
        {
            return;
        }

        try
        {
            using var runtimeMutex = Mutex.OpenExisting(RuntimeMutexName);
            Environment.SetEnvironmentVariable("SMARTINPUT_UI_ONLY", "1");
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No resident runtime exists; the app may initialize its own one.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the normal role if the platform denies mutex inspection.
        }
        catch (System.Security.SecurityException)
        {
            // Keep the normal role if the platform denies mutex inspection.
        }
    }
}
