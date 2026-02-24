using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Accessibility;
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
    private readonly NotifyIcon _tray;
    private IntPtr _lastLayout;

    public BeaconAppContext()
    {
        var cfg = AppSettings.Load();
        _banner = new BannerForm(cfg.BannerDurationMs, cfg.BannerOffsetPx);
        _lastLayout = NativeMethods.GetForegroundKeyboardLayout();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _tray = new NotifyIcon
        {
            Icon = appIcon,
            Text = "LangLayoutBeacon",
            Visible = true,
            ContextMenuStrip = menu
        };

        _tray.BalloonTipTitle = "LangLayoutBeacon";
        _tray.BalloonTipText = "Running. Right-click tray icon to exit.";
        _tray.BalloonTipIcon = ToolTipIcon.Info;
        _tray.ShowBalloonTip(1200);

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
        {
            // Accept native caret point only when it looks valid for focused control/window.
            if (!NativeMethods.IsLikelyWindowTopLeftAnchor(p) && NativeMethods.IsPointInsideFocusedControl(p, 12))
            {
                _banner.ShowNear(p, lang);
                return;
            }
        }

        // Secondary attempt via MSAA caret object (OBJID_CARET).
        if (NativeMethods.TryGetCaretScreenPointViaMsaa(out var msaa))
        {
            _banner.ShowNear(msaa, lang);
            return;
        }

        // Tertiary attempt via UI Automation text caret (works in many modern apps).
        if (NativeMethods.TryGetCaretScreenPointViaUIA(out var uia))
        {
            _banner.ShowNear(uia, lang);
            return;
        }

        // Final fallback: bottom-center of focused text control/window.
        if (NativeMethods.TryGetFocusedControlBottomCenter(out var anchor))
            _banner.ShowNear(anchor, lang);
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
            _tray.Visible = false;
            _tray.Dispose();
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

        var sb = Screen.FromPoint(caretPoint).Bounds;
        var x = Math.Max(sb.Left, Math.Min(sb.Right - Width, caretPoint.X + _offsetPx));
        var y = Math.Max(sb.Top, Math.Min(sb.Bottom - Height, caretPoint.Y - Height - _offsetPx));

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

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd,
        uint dwObjectID,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppvObject);

    public static IntPtr GetForegroundKeyboardLayout()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        var tid = GetWindowThreadProcessId(hwnd, out _);
        return GetKeyboardLayout(tid);
    }

    private static bool TryGetFocusedTargetWindow(out IntPtr target)
    {
        target = IntPtr.Zero;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var tid = GetWindowThreadProcessId(hwnd, out _);
        var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(tid, ref gti)) return false;

        target = gti.hwndCaret != IntPtr.Zero ? gti.hwndCaret
            : gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus
            : gti.hwndActive;

        return target != IntPtr.Zero;
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

    public static bool TryGetFocusedControlBottomCenter(out Point point)
    {
        point = default;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var tid = GetWindowThreadProcessId(hwnd, out _);
        var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(tid, ref gti)) return false;

        var target = gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : gti.hwndActive;
        if (target == IntPtr.Zero) return false;

        if (!GetWindowRect(target, out var r)) return false;

        // Anchor near bottom-center of focused text control/window.
        point = new Point((r.Left + r.Right) / 2, r.Bottom - 12);
        return true;
    }

    public static bool IsPointInsideFocusedControl(Point p, int tolerancePx)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var tid = GetWindowThreadProcessId(hwnd, out _);
        var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(tid, ref gti)) return false;

        var target = gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : gti.hwndActive;
        if (target == IntPtr.Zero) return false;
        if (!GetWindowRect(target, out var r)) return false;

        return p.X >= r.Left - tolerancePx && p.X <= r.Right + tolerancePx
            && p.Y >= r.Top - tolerancePx && p.Y <= r.Bottom + tolerancePx;
    }

    public static bool IsLikelyWindowTopLeftAnchor(Point p)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        if (!GetWindowRect(hwnd, out var r)) return false;

        // If reported caret is basically window origin area, it's usually not real caret.
        var dx = Math.Abs(p.X - r.Left);
        var dy = Math.Abs(p.Y - r.Top);
        return dx < 24 && dy < 24;
    }

    public static bool TryGetCaretScreenPointViaMsaa(out Point point)
    {
        point = default;

        try
        {
            if (!TryGetFocusedTargetWindow(out var target)) return false;

            const uint OBJID_CARET = 0xFFFFFFF8;
            var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); // IAccessible

            var hr = AccessibleObjectFromWindow(target, OBJID_CARET, ref iid, out var accObj);
            if (hr != 0 || accObj is not IAccessible acc) return false;

            object childId = 0;
            acc.accLocation(out var left, out var top, out var width, out var height, childId);
            if (width <= 0 && height <= 0) return false;

            point = new Point(left, top + Math.Max(1, height));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetCaretScreenPointViaUIA(out Point point)
    {
        point = default;

        try
        {
            // Late-bound COM to avoid explicit UIAutomation assembly dependency.
            var t = Type.GetTypeFromProgID("UIAutomationClient.CUIAutomation8")
                    ?? Type.GetTypeFromProgID("UIAutomationClient.CUIAutomation");
            if (t is null) return false;

            dynamic automation = Activator.CreateInstance(t)!;
            dynamic focused = automation.GetFocusedElement();
            if (focused is null) return false;

            const int UIA_TextPattern2Id = 10024;
            const int UIA_TextUnit_Character = 0;
            const int UIA_StartEndpoint = 0;
            const int UIA_EndEndpoint = 1;

            dynamic pattern = focused.GetCurrentPattern(UIA_TextPattern2Id);
            if (pattern is null) return false;

            bool isActive;
            dynamic range = pattern.GetCaretRange(out isActive);
            if (range is null) return false;

            double[]? rects = null;
            try { rects = (double[])range.GetBoundingRectangles(); } catch { }

            // For degenerate caret ranges UIA may return empty. Expand to a character range.
            if (rects is null || rects.Length < 4)
            {
                dynamic probe = range.Clone();
                try
                {
                    probe.ExpandToEnclosingUnit(UIA_TextUnit_Character);
                    rects = (double[])probe.GetBoundingRectangles();
                }
                catch
                {
                    try
                    {
                        probe.MoveEndpointByUnit(UIA_EndEndpoint, UIA_TextUnit_Character, 1);
                        rects = (double[])probe.GetBoundingRectangles();
                    }
                    catch { }
                }
            }

            if (rects is null || rects.Length < 4) return false;

            var x = (int)Math.Round(rects[0]);
            var y = (int)Math.Round(rects[1] + rects[3]);
            point = new Point(x, y);
            return true;
        }
        catch
        {
            return false;
        }
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
