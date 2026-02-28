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
        _banner = new BannerForm(cfg);
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

        _banner.SetLanguage(NativeMethods.GetLayoutShortName(_lastLayout));

        _pollTimer = new Timer { Interval = 70 };
        _pollTimer.Tick += (_, _) => PollLayout();
        _pollTimer.Start();
    }

    private void PollLayout()
    {
        var current = NativeMethods.GetForegroundKeyboardLayout();
        if (current == IntPtr.Zero)
            return;

        var layoutChanged = current != _lastLayout;
        if (layoutChanged)
        {
            _lastLayout = current;
            _banner.OnLayoutSwitched(NativeMethods.GetLayoutShortName(current));
        }

        if (TryResolveAnchorPoint(out var anchor))
            _banner.UpdateAnchor(anchor);
        else
            _banner.ShowCenteredIfNeeded();
    }

    private bool TryResolveAnchorPoint(out Point p)
    {
        if (NativeMethods.TryGetCaretScreenPoint(out p))
        {
            if (NativeMethods.IsValidAnchorForForegroundWindow(p) && NativeMethods.IsPointInsideFocusedControl(p, 12))
                return true;
        }

        if (NativeMethods.TryGetCaretScreenPointViaMsaa(out p))
        {
            if (NativeMethods.IsValidAnchorForForegroundWindow(p) && NativeMethods.IsPointInsideForegroundWindow(p, 24))
                return true;
        }

        if (NativeMethods.TryGetCaretScreenPointViaUIA(out p))
        {
            if (NativeMethods.IsValidAnchorForForegroundWindow(p) && NativeMethods.IsPointInsideForegroundWindow(p, 24))
                return true;
        }

        // Hard fallback: always follow current mouse position (with offset).
        if (NativeMethods.TryGetMouseAnchor(_banner.MouseFallbackOffsetX, _banner.MouseFallbackOffsetY, out p))
            return true;

        p = default;
        return false;
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
    private readonly AppSettings _cfg;
    public int MouseFallbackOffsetX => _cfg.MouseFallbackOffsetX;
    public int MouseFallbackOffsetY => _cfg.MouseFallbackOffsetY;
    private readonly Label _label;
    private readonly Timer _hideTimer;
    private readonly Timer _animTimer;

    private Point _anchor;
    private bool _hasAnchor;
    private string _lang = "EN";
    private float _currentScale;
    private float _baseScale;
    private float _switchScale;
    private DateTime _pulseStartedAtUtc;
    private int _cornerRadius;

    public BannerForm(AppSettings cfg)
    {
        _cfg = cfg;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Opacity = 0.68;

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };
        Controls.Add(_label);

        _baseScale = Math.Clamp(_cfg.PersistentBannerScale, 0.2f, 1.0f);
        _switchScale = Math.Max(_baseScale, Math.Clamp(_cfg.SwitchBannerScale, _baseScale, 2.2f));
        _currentScale = _cfg.PersistentBannerEnabled ? _baseScale : _switchScale;

        _hideTimer = new Timer { Interval = Math.Clamp(_cfg.BannerDurationMs, 300, 1200) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        _animTimer = new Timer { Interval = 16 };
        _animTimer.Tick += (_, _) => TickPulseAnimation();

        ApplyScale(_currentScale);

        if (_cfg.PersistentBannerEnabled)
            Show();
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
        e.Graphics.DrawRoundedRectangle(pen, r, _cornerRadius);
    }

    public void SetLanguage(string lang)
    {
        _lang = string.IsNullOrWhiteSpace(lang) ? "--" : lang;
        _label.Text = _lang;
        ApplyScale(_currentScale);
    }

    public void OnLayoutSwitched(string lang)
    {
        SetLanguage(lang);

        if (_cfg.PersistentBannerEnabled)
        {
            _pulseStartedAtUtc = DateTime.UtcNow;
            if (!_animTimer.Enabled)
                _animTimer.Start();

            if (!Visible)
                Show();

            return;
        }

        // Legacy mode: only temporary switch banner.
        _currentScale = _switchScale;
        ApplyScale(_currentScale);
        PlaceNearAnchorOrCenter();
        Show();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void UpdateAnchor(Point caretPoint)
    {
        _anchor = caretPoint;
        _hasAnchor = true;
        PlaceNearAnchorOrCenter();

        if (_cfg.PersistentBannerEnabled && !Visible)
            Show();
    }

    public void ShowCenteredIfNeeded()
    {
        if (!_cfg.PersistentBannerEnabled)
            return;

        if (!Visible)
            Show();

        var wa = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
    }

    private void TickPulseAnimation()
    {
        var durationMs = Math.Clamp(_cfg.BannerDurationMs, 300, 1200);
        var elapsedMs = (DateTime.UtcNow - _pulseStartedAtUtc).TotalMilliseconds;

        if (elapsedMs >= durationMs)
        {
            _animTimer.Stop();
            _currentScale = _baseScale;
            ApplyScale(_currentScale);
            PlaceNearAnchorOrCenter();
            return;
        }

        // Smooth single pulse: base -> switch -> base.
        var t = (float)(elapsedMs / durationMs); // 0..1
        var pulse = MathF.Sin(MathF.PI * t);     // 0..1..0
        _currentScale = _baseScale + (_switchScale - _baseScale) * pulse;
        ApplyScale(_currentScale);
        PlaceNearAnchorOrCenter();
    }

    private void ApplyScale(float scale)
    {
        var fontSize = Math.Clamp(_cfg.BaseFontSize * scale, 6f, 28f);
        _label.Font = new Font("Segoe UI", fontSize, FontStyle.Bold);

        var measured = TextRenderer.MeasureText(_lang, _label.Font);
        var pad = (int)Math.Round(18 * scale);

        Width = Math.Max(28, measured.Width + pad);
        Height = Math.Max(14, (int)Math.Round(30 * scale));
        _cornerRadius = Math.Max(3, (int)Math.Round(8 * scale));

        Invalidate();
    }

    private void PlaceNearAnchorOrCenter()
    {
        if (!_hasAnchor)
        {
            ShowCenteredIfNeeded();
            return;
        }

        var offset = Math.Clamp(_cfg.BannerOffsetPx, 0, 80);
        var sb = Screen.FromPoint(_anchor).Bounds;
        var x = Math.Max(sb.Left, Math.Min(sb.Right - Width, _anchor.X + offset));
        var y = Math.Max(sb.Top, Math.Min(sb.Bottom - Height, _anchor.Y - Height - offset));

        Location = new Point(x, y);
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
    public bool PersistentBannerEnabled { get; init; } = true;

    // Scales are relative to base switch-banner size.
    // Example defaults: persistent 0.5 (area is ~4x smaller), switch 1.0.
    public float PersistentBannerScale { get; init; } = 0.5f;
    public float SwitchBannerScale { get; init; } = 1.0f;
    public float BaseFontSize { get; init; } = 10f;
    public int MouseFallbackOffsetX { get; init; } = 14;
    public int MouseFallbackOffsetY { get; init; } = 16;

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

        point = new Point((r.Left + r.Right) / 2, r.Bottom - 12);
        return true;
    }

    public static bool TryGetMouseAnchor(int offsetX, int offsetY, out Point point)
    {
        var p = Cursor.Position;
        point = new Point(p.X + Math.Clamp(offsetX, 0, 120), p.Y + Math.Clamp(offsetY, 0, 120));
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

        var dx = Math.Abs(p.X - r.Left);
        var dy = Math.Abs(p.Y - r.Top);
        return dx < 24 && dy < 24;
    }

    public static bool IsPointInsideForegroundWindow(Point p, int tolerancePx)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        if (!GetWindowRect(hwnd, out var r)) return false;

        return p.X >= r.Left - tolerancePx && p.X <= r.Right + tolerancePx
            && p.Y >= r.Top - tolerancePx && p.Y <= r.Bottom + tolerancePx;
    }

    public static bool IsValidAnchorForForegroundWindow(Point p)
    {
        if (p.X <= 0 && p.Y <= 0)
            return false;

        if (IsLikelyWindowTopLeftAnchor(p))
            return false;

        // Reject stale caret points from previously active windows.
        return IsPointInsideForegroundWindow(p, 48);
    }

    public static bool TryGetCaretScreenPointViaMsaa(out Point point)
    {
        point = default;

        try
        {
            if (!TryGetFocusedTargetWindow(out var target)) return false;

            const uint OBJID_CARET = 0xFFFFFFF8;
            var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");

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
            var t = Type.GetTypeFromProgID("UIAutomationClient.CUIAutomation8")
                    ?? Type.GetTypeFromProgID("UIAutomationClient.CUIAutomation");
            if (t is null) return false;

            dynamic automation = Activator.CreateInstance(t)!;
            dynamic focused = automation.GetFocusedElement();
            if (focused is null) return false;

            const int UIA_TextPattern2Id = 10024;
            const int UIA_TextUnit_Character = 0;
            const int UIA_EndEndpoint = 1;

            dynamic pattern = focused.GetCurrentPattern(UIA_TextPattern2Id);
            if (pattern is null) return false;

            bool isActive;
            dynamic range = pattern.GetCaretRange(out isActive);
            if (range is null) return false;

            double[]? rects = null;
            try { rects = (double[])range.GetBoundingRectangles(); } catch { }

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
            var name = sb.ToString();
            return name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant();
        }

        return $"0x{lcid:X4}";
    }
}
