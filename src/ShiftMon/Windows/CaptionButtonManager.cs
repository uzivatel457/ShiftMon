using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ShiftMon.App;
using ShiftMon.Display;
using ShiftMon.Native;
using ShiftMon.Profiles;

namespace ShiftMon.Windows;

internal sealed class CaptionButtonManager : IDisposable
{
    const string ClassName = "ShiftMon.CaptionButton";

    readonly Func<AppSettings> _settings;
    readonly Action<string> _notify;
    readonly uint _ourPid = (uint)Environment.ProcessId;
    readonly Dictionary<IntPtr, CaptionButton> _buttons = new();
    readonly List<IntPtr> _hooks = [];
    readonly System.Windows.Forms.Timer _timer;
    NativeMethods.WinEventDelegate? _eventProc;
    NativeMethods.WndProc? _wndProc;
    SynchronizationContext? _ui;
    bool _classRegistered;
    bool _hooksInstalled;
    bool _disposed;
    int _posted;
    readonly HashSet<IntPtr> _pending = [];
    DateTime _themeRead = DateTime.MinValue;
    bool _appsUseLightTheme = true;

    public CaptionButtonManager(Func<AppSettings> settings, Action<string> notify)
    {
        _settings = settings;
        _notify = notify;
        _timer = new System.Windows.Forms.Timer { Interval = 400 };
        _timer.Tick += (_, _) => RefreshAll();
    }

    public void Start()
    {
        _ui ??= SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        RegisterClass();
        if (!_hooksInstalled)
        {
            _eventProc = OnWinEvent;
            Hook(NativeMethods.EventObjectCreate, NativeMethods.EventObjectHide);
            Hook(NativeMethods.EventObjectLocationChange, NativeMethods.EventObjectLocationChange);
            Hook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground);
            Hook(NativeMethods.EventSystemMinimizeStart, NativeMethods.EventSystemMinimizeEnd);
            Hook(NativeMethods.EventSystemMoveSizeEnd, NativeMethods.EventSystemMoveSizeEnd);
            Hook(NativeMethods.EventObjectCloaked, NativeMethods.EventObjectUncloaked);
            _hooksInstalled = true;
            AppLog.Info("Tlačítka v záhlaví jsou zapnutá.");
        }

        _timer.Start();
        RefreshAll();
    }

    public void Stop()
    {
        _timer.Stop();
        foreach (var hook in _hooks)
            NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
        foreach (var button in _buttons.Values)
            button.Destroy();
        _buttons.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _timer.Dispose();
    }

    void Hook(uint min, uint max)
    {
        var hook = NativeMethods.SetWinEventHook(min, max, IntPtr.Zero, _eventProc!, 0, 0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        if (hook != IntPtr.Zero)
            _hooks.Add(hook);
    }

    void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero || _disposed)
            return;

        lock (_pending)
        {
            _pending.Add(hwnd);
            if (Interlocked.Exchange(ref _posted, 1) == 1)
                return;
        }

        var ui = _ui;
        if (ui == null)
        {
            Interlocked.Exchange(ref _posted, 0);
            return;
        }

        ui.Post(_ => Drain(), null);
    }

    void Drain()
    {
        List<IntPtr> batch;
        lock (_pending)
        {
            batch = _pending.ToList();
            _pending.Clear();
            Interlocked.Exchange(ref _posted, 0);
        }

        if (!_settings().CaptionButton)
            return;

        foreach (var hwnd in batch)
            UpdateOne(hwnd);
    }

    void RefreshAll()
    {
        if (_disposed || !_settings().CaptionButton)
        {
            if (_buttons.Count > 0)
                StopButtonsOnly();
            return;
        }

        var live = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (WindowEligibility.IsEligible(hwnd, _ourPid))
                live.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        foreach (var hwnd in _buttons.Keys.ToArray())
        {
            if (!live.Contains(hwnd))
                Remove(hwnd);
        }

        foreach (var hwnd in live)
            UpdateOne(hwnd);
    }

    void StopButtonsOnly()
    {
        foreach (var button in _buttons.Values)
            button.Destroy();
        _buttons.Clear();
    }

    void UpdateOne(IntPtr hwnd)
    {
        if (!WindowEligibility.IsEligible(hwnd, _ourPid) || !WindowEligibility.TryGetButtonRect(hwnd, out var rect))
        {
            Remove(hwnd);
            return;
        }

        if (!_buttons.TryGetValue(hwnd, out var button))
        {
            button = new CaptionButton(hwnd, (owner, right) => _ui?.Post(_ => OnClick(owner, right), null));
            if (!button.Created)
                return;
            _buttons[hwnd] = button;
        }

        button.Move(rect, CaptionColor(hwnd));
    }

    void Remove(IntPtr hwnd)
    {
        if (!_buttons.Remove(hwnd, out var button))
            return;
        button.Destroy();
    }

    void OnClick(IntPtr owner, bool rightButton)
    {
        try
        {
            var monitors = DisplayService.GetLiveMonitors();
            if (monitors.Count < 2)
            {
                _notify(Loc.T("Je připojen jen jeden monitor.", "Only one monitor is connected."));
                return;
            }

            var currentHandle = NativeMethods.MonitorFromWindow(owner, NativeMethods.MonitorDefaultToNearest);
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = string.Empty
            };
            NativeMethods.GetMonitorInfo(currentHandle, ref info);
            var currentName = (info.szDevice ?? "").Trim();
            var current = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, currentName, StringComparison.OrdinalIgnoreCase))
                          ?? monitors[0];

            var pick = rightButton || (monitors.Count > 2 && _settings().PickerWhenMany);
            LiveMonitor? target;
            if (pick)
            {
                if (!NativeMethods.GetWindowRect(owner, out var ownerRect))
                    return;
                target = MonitorPickerForm.Pick(monitors, current, ownerRect);
            }
            else
            {
                var index = monitors.FindIndex(m => string.Equals(m.DeviceName, current.DeviceName, StringComparison.OrdinalIgnoreCase));
                target = monitors[(index + 1) % monitors.Count];
            }

            if (target == null || string.Equals(target.DeviceName, current.DeviceName, StringComparison.OrdinalIgnoreCase))
                return;

            if (!WindowMover.MoveTo(owner, target, monitors))
                _notify(Loc.T("Okno se nepodařilo přesunout.", "The window could not be moved."));
            else
                NativeMethods.SetForegroundWindow(owner);
        }
        catch (Exception ex)
        {
            AppLog.Info("Přesun okna selhal: " + ex.Message);
            _notify(Loc.T("Okno se nepodařilo přesunout.", "The window could not be moved."));
        }
    }

    Color CaptionColor(IntPtr hwnd)
    {
        if (DateTime.UtcNow - _themeRead > TimeSpan.FromSeconds(2))
        {
            _themeRead = DateTime.UtcNow;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                _appsUseLightTheme = key?.GetValue("AppsUseLightTheme") is int value ? value != 0 : true;
            }
            catch
            {
                _appsUseLightTheme = true;
            }
        }

        var immersiveDark = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, out int dark, sizeof(int)) == 0 && dark != 0;
        var wantDark = immersiveDark || !_appsUseLightTheme;
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCaptionColor, out int color, sizeof(int)) == 0
            && unchecked((uint)color) != 0xFFFFFFFF)
        {
            var caption = Color.FromArgb(color & 0xFF, (color >> 8) & 0xFF, (color >> 16) & 0xFF);
            var captionIsLight = caption.R + caption.G + caption.B > 420;
            if (captionIsLight != wantDark)
                return caption;
        }

        return wantDark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
    }

    void RegisterClass()
    {
        if (_classRegistered)
            return;

        _wndProc = ButtonWndProc;
        var wc = new NativeMethods.WNDCLASS
        {
            style = NativeMethods.CsHredraw | NativeMethods.CsVredraw,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = NativeMethods.GetModuleHandle(null),
            hCursor = NativeMethods.LoadCursor(IntPtr.Zero, (IntPtr)32512),
            lpszClassName = ClassName
        };
        var atom = NativeMethods.RegisterClass(ref wc);
        if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
            AppLog.Info("RegisterClass selhal: " + Marshal.GetLastWin32Error());
        _classRegistered = atom != 0 || Marshal.GetLastWin32Error() == 1410;
    }

    static IntPtr ButtonWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (CaptionButton.Lookup(hwnd) is { } button)
            return button.Dispatch(msg, wParam, lParam);
        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}

internal sealed class CaptionButton
{
    static readonly Dictionary<IntPtr, CaptionButton> Live = new();

    readonly IntPtr _owner;
    readonly Action<IntPtr, bool> _click;
    bool _hover;
    bool _pressed;
    bool _tracking;
    int _width = 20;
    int _height = 20;
    Color _back = Color.FromArgb(243, 243, 243);

    public IntPtr Handle { get; }
    public bool Created => Handle != IntPtr.Zero;

    public CaptionButton(IntPtr owner, Action<IntPtr, bool> click)
    {
        _owner = owner;
        _click = click;
        Handle = NativeMethods.CreateWindowEx(
            NativeMethods.WsExLayered | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow,
            "ShiftMon.CaptionButton",
            "",
            NativeMethods.WsPopup,
            0, 0, 20, 20,
            owner,
            IntPtr.Zero,
            NativeMethods.GetModuleHandle(null),
            IntPtr.Zero);
        if (Handle != IntPtr.Zero)
            Live[Handle] = this;
    }

    public static CaptionButton? Lookup(IntPtr hwnd) => Live.TryGetValue(hwnd, out var button) ? button : null;

    public void Move(NativeMethods.RECT rect, Color back)
    {
        _back = back;
        _width = Math.Max(1, rect.Width);
        _height = Math.Max(1, rect.Height);
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, rect.Left, rect.Top, _width, _height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        Present();
    }

    public void Destroy()
    {
        if (Handle != IntPtr.Zero)
        {
            Live.Remove(Handle);
            NativeMethods.DestroyWindow(Handle);
        }
    }

    public IntPtr Dispatch(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WmEraseBkgnd:
                return new IntPtr(1);
            case NativeMethods.WmPaint:
                Present();
                NativeMethods.ValidateRect(Handle, IntPtr.Zero);
                return IntPtr.Zero;
            case NativeMethods.WmMouseMove:
                _hover = true;
                if (!_tracking)
                {
                    var track = new NativeMethods.TRACKMOUSEEVENT
                    {
                        cbSize = Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
                        dwFlags = NativeMethods.TmeLeave,
                        hwndTrack = Handle
                    };
                    _tracking = NativeMethods.TrackMouseEvent(ref track);
                }
                Present();
                return IntPtr.Zero;
            case NativeMethods.WmMouseLeave:
                _hover = false;
                _pressed = false;
                _tracking = false;
                Present();
                return IntPtr.Zero;
            case NativeMethods.WmLButtonDown:
                _pressed = true;
                Present();
                return IntPtr.Zero;
            case NativeMethods.WmLButtonUp:
                _pressed = false;
                Present();
                _click(_owner, false);
                return IntPtr.Zero;
            case NativeMethods.WmRButtonUp:
                _click(_owner, true);
                return IntPtr.Zero;
            case NativeMethods.WmMouseActivate:
                return new IntPtr(NativeMethods.MaNoActivate);
            case NativeMethods.WmDestroy:
                Live.Remove(Handle);
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(Handle, msg, wParam, lParam);
    }

    void Present()
    {
        var width = _width;
        var height = _height;
        if (width < 2 || height < 2 || Handle == IntPtr.Zero)
            return;

        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            var bounds = new Rectangle(0, 0, width, height);
            var lightBar = _back.R + _back.G + _back.B > 380;
            if (_hover || _pressed)
            {
                var alpha = _pressed ? 56 : 32;
                using var overlay = new SolidBrush(lightBar
                    ? Color.FromArgb(alpha, 0, 0, 0)
                    : Color.FromArgb(alpha, 255, 255, 255));
                var inset = new Rectangle(2, 2, Math.Max(1, width - 4), Math.Max(1, height - 4));
                using var hover = Rounded(inset, Math.Max(3, height / 5));
                graphics.FillPath(overlay, hover);
            }

            var glyph = lightBar ? Color.FromArgb(32, 32, 32) : Color.FromArgb(255, 255, 255);
            DrawGlyph(graphics, bounds, glyph);
        }

        var locked = bitmap.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var info = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32
            }
        };
        var screen = NativeMethods.GetDC(IntPtr.Zero);
        var memory = NativeMethods.CreateCompatibleDC(screen);
        var dib = NativeMethods.CreateDIBSection(memory, ref info, 0, out var bits, IntPtr.Zero, 0);
        var previous = NativeMethods.SelectObject(memory, dib);
        try
        {
            var rowBytes = width * 4;
            var row = new byte[rowBytes];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), row, 0, rowBytes);
                Marshal.Copy(row, 0, IntPtr.Add(bits, y * rowBytes), rowBytes);
            }

            var origin = new NativeMethods.POINT();
            var size = new NativeMethods.SIZE { cx = width, cy = height };
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AcSrcAlpha
            };
            NativeMethods.UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, memory, ref origin, 0, ref blend, NativeMethods.UlwAlpha);
        }
        finally
        {
            bitmap.UnlockBits(locked);
            NativeMethods.SelectObject(memory, previous);
            if (dib != IntPtr.Zero)
                NativeMethods.DeleteObject(dib);
            NativeMethods.DeleteDC(memory);
            NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    static void DrawGlyph(Graphics graphics, Rectangle bounds, Color color)
    {
        var height = bounds.Height;
        var stroke = Math.Max(1.15f, height / 15f);
        using var pen = new Pen(color, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var screenWidth = height * 0.42f;
        var screenHeight = height * 0.32f;
        var arrowWidth = height * 0.15f;
        var gap = height * 0.09f;
        var total = screenWidth + gap + arrowWidth;
        var left = bounds.X + (bounds.Width - total) / 2f;
        var top = bounds.Y + (bounds.Height - (screenHeight + stroke * 2.2f)) / 2f;
        var screen = new RectangleF(left, top, screenWidth, screenHeight);
        using (var frame = Rounded(Rectangle.Round(screen), Math.Max(2, (int)(stroke * 1.8f))))
            graphics.DrawPath(pen, frame);
        var standY = screen.Bottom + stroke * 1.35f;
        var center = screen.X + screen.Width / 2f;
        graphics.DrawLine(pen, center - screenWidth * 0.22f, standY, center + screenWidth * 0.22f, standY);

        var arrowX = screen.Right + gap;
        var mid = bounds.Y + bounds.Height / 2f;
        var arm = height * 0.13f;
        graphics.DrawLines(pen,
        [
            new PointF(arrowX, mid - arm),
            new PointF(arrowX + arrowWidth, mid),
            new PointF(arrowX, mid + arm)
        ]);
    }

    static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Max(1, Math.Min(rect.Width, rect.Height) / 2));
        var d = radius * 2;
        if (rect.Width < 2 || rect.Height < 2 || d < 2)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
