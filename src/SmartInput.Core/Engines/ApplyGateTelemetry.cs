namespace SmartInput.Core.Engines;

/// <summary>
/// Test and audit counters for final Apply gate invocation. Reset at audit phase boundaries.
/// </summary>
public static class ApplyGateTelemetry
{
    public static int GateCallCount { get; private set; }
    public static int GateAllowedCount { get; private set; }
    public static int GateDeniedCount { get; private set; }
    public static int GateBypassCount { get; private set; }

    public static void Reset()
    {
        GateCallCount = 0;
        GateAllowedCount = 0;
        GateDeniedCount = 0;
        GateBypassCount = 0;
    }

    internal static void RecordCall(bool allowed)
    {
        GateCallCount++;
        if (allowed)
        {
            GateAllowedCount++;
        }
        else
        {
            GateDeniedCount++;
        }
    }

    public static void RecordBypass()
    {
        GateBypassCount++;
    }
}
