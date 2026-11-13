using ShiftMon.Display;
using ShiftMon.Native;
using ShiftMon.Profiles;

namespace ShiftMon.Windows;

internal static class WindowMoveMath
{
    public readonly record struct PixelRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
    }

    public static PixelRect MoveToMonitor(PixelRect window, PixelRect sourceWork, PixelRect targetWork)
    {
        var relX = window.X - sourceWork.X;
        var relY = window.Y - sourceWork.Y;
        var width = window.Width;
        var height = window.Height;
        if (width > targetWork.Width)
            width = targetWork.Width;
        if (height > targetWork.Height)
            height = targetWork.Height;

        var x = targetWork.X + relX;
        var y = targetWork.Y + relY;
        if (x + width > targetWork.Right)
            x = targetWork.Right - width;
        if (y + height > targetWork.Bottom)
            y = targetWork.Bottom - height;
        if (x < targetWork.X)
            x = targetWork.X;
        if (y < targetWork.Y)
            y = targetWork.Y;

        return new PixelRect(x, y, width, height);
    }
}

internal static class WindowMover
{
    public static bool MoveTo(IntPtr hwnd, LiveMonitor target, IReadOnlyList<LiveMonitor> monitors)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return false;

        var sourceHandle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            szDevice = string.Empty
        };
        if (!NativeMethods.GetMonitorInfo(sourceHandle, ref info))
            return false;

        var source = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, (info.szDevice ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (source == null || string.Equals(source.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase))
            return false;

        var zoomed = NativeMethods.IsZoomed(hwnd);
        NativeMethods.SendMessage(hwnd, NativeMethods.WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        try
        {
            if (zoomed)
                NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
                return false;

            var moved = WindowMoveMath.MoveToMonitor(
                new WindowMoveMath.PixelRect(rect.Left, rect.Top, rect.Width, rect.Height),
                new WindowMoveMath.PixelRect(source.WorkX, source.WorkY, source.WorkWidth, source.WorkHeight),
                new WindowMoveMath.PixelRect(target.WorkX, target.WorkY, target.WorkWidth, target.WorkHeight));

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, moved.X, moved.Y, moved.Width, moved.Height,
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);

            if (zoomed)
                NativeMethods.ShowWindow(hwnd, NativeMethods.SwMaximize);

            return true;
        }
        finally
        {
            NativeMethods.SendMessage(hwnd, NativeMethods.WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            NativeMethods.RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.RdwInvalidate | NativeMethods.RdwAllChildren | NativeMethods.RdwUpdateNow | NativeMethods.RdwFrame);
        }
    }
}
