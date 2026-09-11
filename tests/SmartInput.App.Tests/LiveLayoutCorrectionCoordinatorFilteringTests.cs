using SmartInput.App.Services;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.App.Tests;

public sealed class LiveLayoutCorrectionCoordinatorFilteringTests
{
    [Fact]
    public void OrdinaryKeyUp_DoesNotEnterTextCorrectionPolicyPipeline()
    {
        var observation = new KeyboardObservationEventArgs
        {
            EventType = KeyEventType.KeyUp,
            VirtualKeyCode = 0x41,
        };

        Assert.False(LiveLayoutCorrectionCoordinator.ShouldProcessForTextCorrection(observation));
    }

    [Fact]
    public void KeyDown_StillEntersTextCorrectionPipeline()
    {
        var observation = new KeyboardObservationEventArgs
        {
            EventType = KeyEventType.KeyDown,
            VirtualKeyCode = 0x41,
        };

        Assert.True(LiveLayoutCorrectionCoordinator.ShouldProcessForTextCorrection(observation));
    }
}
