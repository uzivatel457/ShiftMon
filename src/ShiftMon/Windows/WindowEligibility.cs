using System.Diagnostics;
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

    public static bool IsEligible(IntPtr hwnd, uint ourPid) => IsEligible(hwnd, ourPid, null);

    public static bool IsEligible(IntPtr hwnd, uint ourPid, IReadOnlySet<string>? hiddenExecutables)
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

        if (hiddenExecutables is { Count: > 0 } && TryGetProgram(hwnd, out var fileName, out _) && hiddenExecutables.Contains(fileName))
            return false;

        return true;
    }

    public static List<RunningProgram> RunningPrograms(uint ourPid, IReadOnlySet<string>? skip)
    {
        var found = new Dictionary<string, RunningProgram>(StringComparer.OrdinalIgnoreCase);
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsEligible(hwnd, ourPid))
                return true;
            if (!TryGetProgram(hwnd, out var fileName, out var fullPath))
                return true;
            if (skip != null && skip.Contains(fileName))
                return true;
            if (found.ContainsKey(fileName))
                return true;
            found[fileName] = new RunningProgram(fileName, FriendlyName(fullPath), Title(hwnd));
            return true;
        }, IntPtr.Zero);
        return found.Values.OrderBy(program => program.ToString(), StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool TryGetProgram(IntPtr hwnd, out string fileName, out string fullPath)
    {
        fileName = "";
        fullPath = "";
        if (!TryQueryProcess(hwnd, out fileName, out fullPath))
            return false;
        if (!fileName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            return true;

        var child = NativeMethods.GetWindow(hwnd, NativeMethods.GwChild);
        if (child != IntPtr.Zero
            && TryQueryProcess(child, out var childName, out var childPath)
            && !childName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName = childName;
            fullPath = childPath;
        }
        return true;
    }

    public static string FriendlyName(string fullPath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(fullPath);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
            {
                if (!string.IsNullOrWhiteSpace(candidate)
                    && !candidate.Equals(Path.GetFileName(fullPath), StringComparison.OrdinalIgnoreCase))
                    return candidate.Trim();
            }
        }
        catch
        {
            // Popis souboru je jen popisek v seznamu.
        }

        var bare = Path.GetFileNameWithoutExtension(fullPath);
        return string.IsNullOrWhiteSpace(bare) ? Path.GetFileName(fullPath) : bare;
    }

    static bool TryQueryProcess(IntPtr hwnd, out string fileName, out string fullPath)
    {
        fileName = "";
        fullPath = "";
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return false;
        var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
            return false;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size) || size <= 0)
                return false;
            fullPath = buffer.ToString(0, size);
            fileName = Path.GetFileName(fullPath);
            return fileName.Length > 0;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
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

internal sealed record RunningProgram(string Executable, string Name, string SampleTitle)
{
    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Equals(Executable, StringComparison.OrdinalIgnoreCase))
            return Executable;
        return Name + " (" + Executable + ")";
    }
}
