using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Timer = System.Windows.Forms.Timer;

namespace LangLayoutBeacon;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BeaconAppContext());
    }
}

internal sealed class BeaconAppContext : ApplicationContext
{
    private readonly Timer _pollTimer;
    private readonly BannerForm _banner;
    private IntPtr _lastLayout;

    public BeaconAppContext()
    {
        var cfg = AppSettings.Load();
        _banner = new BannerForm(cfg.BannerDurationMs, cfg.BannerOffsetPx);
        _lastLayout = NativeMethods.GetForegroundKeyboardLayout();

        _pollTimer = new Timer { Interval = 70 };
        _pollTimer.Tick += (_, _) => PollLayout();
        _pollTimer.Start();
    }

    private void PollLayout()
    {
        var current = NativeMethods.GetForegroundKeyboardLayout();
        if (current == IntPtr.Zero || current == _lastLayout)
            return;

        _lastLayout = current;
        var lang = NativeMethods.GetLayoutShortName(current);

        if (NativeMethods.TryGetCaretScreenPoint(out var p))
            _banner.ShowNear(p, lang);
        else
            _banner.ShowCentered(lang);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _banner.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class BannerForm : Form
{
    private readonly Timer _hideTimer;
    private readonly Label _label;
    private readonly int _offsetPx;

    public BannerForm(int bannerDurationMs, int bannerOffsetPx)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Opacity = 0.68;
        Width = 72;
        Height = 30;

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        Controls.Add(_label);

        _offsetPx = Math.Clamp(bannerOffsetPx, 0, 40);
        _hideTimer = new Timer { Interval = Math.Clamp(bannerDurationMs, 300, 700) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOPMOST = 0x00000008;

            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var pen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        e.Graphics.DrawRoundedRectangle(pen, r, 8);
    }

    public void ShowNear(Point caretPoint, string lang)
    {
        _label.Text = lang;
        Width = Math.Max(64, TextRenderer.MeasureText(lang, _label.Font).Width + 18);

        var x = Math.Max(0, Math.Min(Screen.PrimaryScreen!.Bounds.Width - Width, caretPoint.X + _offsetPx));
        var y = Math.Max(0, Math.Min(Screen.PrimaryScreen!.Bounds.Height - Height, caretPoint.Y - Height - _offsetPx));

        Location = new Point(x, y);
        Show();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void ShowCentered(string lang)
    {
        _label.Text = lang;
        Width = Math.Max(64, TextRenderer.MeasureText(lang, _label.Font).Width + 18);

        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);

        Show();
        _hideTimer.Stop();
        _hideTimer.Start();
    }
}

internal static class GraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle bounds, int radius)
    {
        using var gp = new GraphicsPath();
        int d = radius * 2;
        gp.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        gp.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        gp.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        gp.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        gp.CloseFigure();
        g.DrawPath(pen, gp);
    }
}

internal sealed class AppSettings
{
    public int BannerDurationMs { get; init; } = 520;
    public int BannerOffsetPx { get; init; } = 10;

    public static AppSettings Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return new AppSettings();

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppSettings>(json);
            return cfg ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int LCIDToLocaleName(uint lcid, StringBuilder localeName, int cchLocaleName, uint flags);

    public static IntPtr GetForegroundKeyboardLayout()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        var tid = GetWindowThreadProcessId(hwnd, out _);
        return GetKeyboardLayout(tid);
    }

    public static bool TryGetCaretScreenPoint(out Point point)
    {
        point = default;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var tid = GetWindowThreadProcessId(hwnd, out _);
        var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };

        if (!GetGUIThreadInfo(tid, ref gti)) return false;

        var target = gti.hwndCaret != IntPtr.Zero ? gti.hwndCaret : gti.hwndFocus;
        if (target == IntPtr.Zero) return false;

        var p = new POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Bottom };
        if (!ClientToScreen(target, ref p)) return false;

        point = new Point(p.X, p.Y);
        return true;
    }

    public static string GetLayoutShortName(IntPtr hkl)
    {
        var lcid = (uint)((ulong)hkl.ToInt64() & 0xFFFF);
        var sb = new StringBuilder(85);
        if (LCIDToLocaleName(lcid, sb, sb.Capacity, 0) > 0)
        {
            var name = sb.ToString(); // e.g. en-US, ru-RU
            return name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant();
        }

        return $"0x{lcid:X4}";
    }
}
