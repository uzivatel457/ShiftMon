using System.Runtime.InteropServices;
using System.Text;
using ShiftMon.Native;

namespace ShiftMon.Windows;

internal static class WindowEligibility
{
    static readonly HashSet<string> SkipClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "DV2ControlHost",
        "MsgrIMEWindowClass",
        "SysShadow",
        "DummyDWMListenerWindow",
        "Windows.UI.Core.CoreWindow",
        "ForegroundStaging",
        "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
        "Shell_InputSwitchDismissOverlay",
        "Shell_InputSwitchTopLevelWindow",
        "TaskListThumbnailWnd",
        "TaskListOverlayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "ShiftMon.CaptionButton"
    };

    public static bool IsEligible(IntPtr hwnd, uint ourPid)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
            return false;
        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsHungAppWindow(hwnd))
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == ourPid)
            return false;

        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlStyle);
        if ((style & NativeMethods.WsChild) != 0 || (style & NativeMethods.WsCaption) != NativeMethods.WsCaption)
            return false;

        var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        if ((ex & NativeMethods.WsExToolWindow) != 0 || (ex & NativeMethods.WsExTransparent) != 0)
            return false;

        var className = ClassName(hwnd);
        if (SkipClasses.Contains(className))
            return false;

        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width < 160 || rect.Height < 40)
            return false;

        return true;
    }

    public static bool TryGetButtonRect(IntPtr hwnd, out NativeMethods.RECT rect)
    {
        rect = default;
        if (!NativeMethods.GetWindowRect(hwnd, out var window))
            return false;

        if (TryFromTitleBar(hwnd, window, out rect) || TryFromCaptionBounds(hwnd, window, out rect))
        {
            ClampToMonitor(hwnd, ref rect);
            return Fits(window, rect);
        }

        return false;
    }

    public static string ClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static string Title(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    static bool TryFromTitleBar(IntPtr hwnd, NativeMethods.RECT window, out NativeMethods.RECT rect)
    {
        rect = default;
        var info = new NativeMethods.TITLEBARINFOEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.TITLEBARINFOEX>()
        };
        var ok = NativeMethods.SendMessageTimeout(
            hwnd,
            NativeMethods.WmGetTitleBarInfoEx,
            IntPtr.Zero,
            ref info,
            NativeMethods.SmtoAbortIfHung | NativeMethods.SmtoBlock,
            80,
            out _);
        if (ok == IntPtr.Zero)
            return false;

        var found = false;
        var anchor = default(NativeMethods.RECT);
        var bestLeft = int.MaxValue;
        for (var i = 2; i <= 5; i++)
        {
            var state = info.State(i);
            if ((state & NativeMethods.StateSystemInvisible) != 0 || (state & NativeMethods.StateSystemOffscreen) != 0)
                continue;

            var button = info.Rect(i);
            if (button.Width is < 8 or > 220 || button.Height is < 8 or > 140)
                continue;
            if (button.Left < bestLeft)
            {
                bestLeft = button.Left;
                anchor = button;
                found = true;
            }
        }

        if (!found)
            return false;

        rect = new NativeMethods.RECT
        {
            Left = anchor.Left - anchor.Width,
            Top = anchor.Top,
            Right = anchor.Left,
            Bottom = anchor.Bottom
        };
        return Fits(window, rect);
    }

    static bool TryFromCaptionBounds(IntPtr hwnd, NativeMethods.RECT window, out NativeMethods.RECT rect)
    {
        rect = default;
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCaptionButtonBounds, out NativeMethods.RECT relative, Marshal.SizeOf<NativeMethods.RECT>()) != 0)
            return false;
        if (relative.Width < 20 || relative.Height is < 8 or > 140)
            return false;

        var width = Math.Clamp(relative.Height, 16, 80);
        rect = new NativeMethods.RECT
        {
            Left = window.Left + relative.Left - width,
            Top = window.Top + relative.Top,
            Right = window.Left + relative.Left,
            Bottom = window.Top + relative.Top + relative.Height
        };
        return Fits(window, rect);
    }

    static void ClampToMonitor(IntPtr hwnd, ref NativeMethods.RECT rect)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            szDevice = string.Empty
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return;
        if (rect.Top >= info.rcMonitor.Top)
            return;

        var shift = info.rcMonitor.Top - rect.Top;
        rect.Top += shift;
        rect.Bottom += shift;
    }

    static bool Fits(NativeMethods.RECT window, NativeMethods.RECT button)
    {
        if (button.Width is < 12 or > 160 || button.Height is < 12 or > 140)
            return false;
        if (button.Left < window.Left + 8)
            return false;
        if (button.Right > window.Right + 8)
            return false;
        if (button.Top < window.Top - 30 || button.Top > window.Top + 90)
            return false;
        return true;
    }
}
