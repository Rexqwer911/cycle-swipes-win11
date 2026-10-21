using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CycleSwipes;

/// <summary>Current Windows theme: the taskbar and apps switch independently.</summary>
internal static class SystemTheme
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool TaskbarIsDark => !IsLight("SystemUsesLightTheme");
    public static bool AppsAreDark => !IsLight("AppsUseLightTheme");

    private static bool IsLight(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(valueName) is not int value || value != 0;
    }
}

/// <summary>Tray icon: two opposite arrows. Drawn for the tray size and the taskbar color.</summary>
internal static class TrayIcon
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(bool paused)
    {
        int size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Color color = SystemTheme.TaskbarIsDark ? Color.White : Color.FromArgb(0x1B, 0x1B, 0x1B);
            using var pen = new Pen(Color.FromArgb(paused ? 110 : 255, color), size * 0.105f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            float s = size;
            DrawArrow(g, pen, tailX: 0.16f * s, headX: 0.84f * s, y: 0.33f * s, head: 0.17f * s);
            DrawArrow(g, pen, tailX: 0.84f * s, headX: 0.16f * s, y: 0.67f * s, head: 0.17f * s);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            // The clone owns its data, so the original HICON can be freed right away.
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawArrow(Graphics g, Pen pen, float tailX, float headX, float y, float head)
    {
        float back = headX > tailX ? headX - head : headX + head;
        g.DrawLine(pen, tailX, y, headX, y);
        g.DrawLines(pen, [new PointF(back, y - head), new PointF(headX, y), new PointF(back, y + head)]);
    }
}

/// <summary>Flat menu in the Windows theme colors; check mark without a box or highlight.</summary>
internal sealed class TrayMenuRenderer(bool dark) : ToolStripProfessionalRenderer(new Colors(dark))
{
    private readonly Color _text = dark ? Color.White : Color.FromArgb(0x1B, 0x1B, 0x1B);

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = _text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        Rectangle r = e.ImageRectangle;
        float s = Math.Min(r.Width, r.Height);
        float x = r.X + (r.Width - s) / 2, y = r.Y + (r.Height - s) / 2;

        var state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_text, s * 0.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.DrawLines(pen,
        [
            new PointF(x + 0.20f * s, y + 0.52f * s),
            new PointF(x + 0.42f * s, y + 0.74f * s),
            new PointF(x + 0.82f * s, y + 0.30f * s),
        ]);
        e.Graphics.Restore(state);
    }

    private sealed class Colors(bool dark) : ProfessionalColorTable
    {
        private readonly Color _background = dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF9, 0xF9, 0xF9);
        private readonly Color _border = dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xD0, 0xD0, 0xD0);
        private readonly Color _selected = dark ? Color.FromArgb(0x3D, 0x3D, 0x3D) : Color.FromArgb(0xE9, 0xE9, 0xE9);

        public override Color ToolStripDropDownBackground => _background;
        public override Color ImageMarginGradientBegin => _background;
        public override Color ImageMarginGradientMiddle => _background;
        public override Color ImageMarginGradientEnd => _background;
        public override Color MenuBorder => _border;
        public override Color MenuItemSelected => _selected;
        public override Color MenuItemBorder => _selected;
        public override Color SeparatorDark => _border;
        public override Color SeparatorLight => _border;
    }
}
