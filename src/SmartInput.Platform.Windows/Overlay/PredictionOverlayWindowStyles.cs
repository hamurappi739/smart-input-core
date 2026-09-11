namespace SmartInput.Platform.Windows.Overlay;

public static class PredictionOverlayWindowStyles
{
    public const int WsExLayered = 0x00080000;
    public const int WsExTopmost = 0x00000008;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolWindow = 0x00000080;

    public static int ExtendedStyles =>
        WsExLayered
        | WsExTopmost
        | WsExNoActivate
        | WsExTransparent
        | WsExToolWindow;
}
