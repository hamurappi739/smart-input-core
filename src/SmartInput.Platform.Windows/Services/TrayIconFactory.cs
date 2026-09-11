using System.Drawing;
using System.Drawing.Imaging;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

internal static class TrayIconFactory
{
    public static Icon CreateDefaultIcon()
    {
        var resourceName = "SmartInput.Platform.Windows.Assets.smart-input-v2.ico";
        var resource = typeof(TrayIconFactory).Assembly.GetManifestResourceStream(resourceName);

        if (resource is not null)
        {
            using (resource)
            using (var icon = new Icon(resource))
            {
                return (Icon)icon.Clone();
            }
        }

        // Keep a deterministic fallback so a missing/corrupt resource can
        // never prevent ResidentHost from starting its tray message thread.
        return CreateFallbackIcon();
    }

    private static Icon CreateFallbackIcon()
    {
        using var bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var blueBrush = new SolidBrush(Color.FromArgb(0, 122, 255));
            graphics.FillRoundedRectangle(blueBrush, 1, 1, 14, 14, 4);

            using var bubbleBrush = new SolidBrush(Color.White);
            graphics.FillRoundedRectangle(bubbleBrush, 4, 4, 8, 6, 2);
            var tail = new[] { new Point(6, 9), new Point(6, 12), new Point(9, 9) };
            graphics.FillPolygon(bubbleBrush, tail);

            using var checkPen = new Pen(Color.FromArgb(0, 122, 255), 1.25f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };
            graphics.DrawLines(checkPen, new[] { new Point(5, 7), new Point(7, 8), new Point(10, 5) });
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            Win32Tray.DestroyIcon(handle);
        }
    }

    private static void FillRoundedRectangle(this Graphics graphics, Brush brush, float x, float y, float width, float height, float radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
