using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public class ApplicationProfileTests
{
    [Fact]
    public void NewProfile_HasUniqueIdAndEnabledByDefault()
    {
        var profile = new ApplicationProfile
        {
            DisplayName = "Visual Studio",
            ProcessName = "devenv",
        };

        Assert.False(string.IsNullOrWhiteSpace(profile.Id));
        Assert.True(profile.IsEnabled);
        Assert.Equal("Visual Studio", profile.DisplayName);
        Assert.Equal("devenv", profile.ProcessName);
    }
}
