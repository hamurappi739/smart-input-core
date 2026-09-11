using Avalonia;
using Avalonia.Headless;

namespace SmartInput.App.Tests;

public sealed class AvaloniaTestFixture : IDisposable
{
    private static int _initialized;

    public AvaloniaTestFixture()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
        }
    }

    public void Dispose()
    {
    }

    private sealed class Application : Avalonia.Application
    {
    }
}
