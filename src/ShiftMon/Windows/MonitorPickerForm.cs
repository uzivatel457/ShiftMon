using ShiftMon.Native;
using ShiftMon.Profiles;

namespace ShiftMon.Windows;

internal sealed class MonitorPickerForm : Form
{
    readonly List<LiveMonitor> _monitors;
    readonly LiveMonitor _current;
    LiveMonitor? _hover;
    LiveMonitor? _chosen;

    MonitorPickerForm(List<LiveMonitor> monitors, LiveMonitor current)
    {
        _monitors = monitors;
        _current = current;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(48, 48, 48);
        KeyPreview = true;
        var size = MeasureBox();
        ClientSize = size;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 12f, FontStyle.Bold);
    }

    public static LiveMonitor? Pick(List<LiveMonitor> monitors, LiveMonitor current, NativeMethods.RECT anchor)
    {
        using var form = new MonitorPickerForm(monitors, current);
        var x = anchor.Left + (anchor.Width - form.Width) / 2;
        var y = anchor.Bottom + 6;
        var screen = Screen.FromPoint(new Point(anchor.Left, anchor.Top));
        if (y + form.Height > screen.WorkingArea.Bottom)
            y = anchor.Top - form.Height - 6;
        if (x + form.Width > screen.WorkingArea.Right)
            x = screen.WorkingArea.Right - form.Width - 4;
        if (x < screen.WorkingArea.Left)
            x = screen.WorkingArea.Left + 4;
        form.Location = new Point(x, y);
        return form.ShowDialog() == DialogResult.OK ? form._chosen : null;
    }

    bool _shown;

    Size MeasureBox()
    {
        var bounds = LayoutRects(new Rectangle(12, 12, 280, 140));
        if (bounds.Count == 0)
            return new Size(220, 90);
        var maxX = bounds.Max(p => p.Rect.Right) + 12;
        var maxY = bounds.Max(p => p.Rect.Bottom) + 12;
        return new Size(Math.Clamp(maxX, 180, 420), Math.Clamp(maxY, 80, 260));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        using var text = new SolidBrush(Color.White);
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        foreach (var item in LayoutRects(ClientRectangle))
        {
            var selected = string.Equals(item.Monitor.DeviceName, _current.DeviceName, StringComparison.OrdinalIgnoreCase);
            var hot = ReferenceEquals(item.Monitor, _hover);
            using var fill = new SolidBrush(selected ? Color.FromArgb(20, 20, 20) : Color.Black);
            using var pen = new Pen(hot || selected ? Color.FromArgb(70, 150, 230) : Color.FromArgb(90, 90, 90), selected ? 3f : 1.5f);
            e.Graphics.FillRectangle(fill, item.Rect);
            e.Graphics.DrawRectangle(pen, item.Rect);
            e.Graphics.DrawString(item.Monitor.Number.ToString(), Font, text, item.Rect, format);
        }
        format.Dispose();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = Hit(e.Location);
        if (!ReferenceEquals(hit, _hover))
        {
            _hover = hit;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var hit = Hit(e.Location);
        if (hit != null)
        {
            _chosen = hit;
            DialogResult = DialogResult.OK;
            Close();
        }
        base.OnMouseClick(e);
    }

    protected override void OnShown(EventArgs e)
    {
        _shown = true;
        base.OnShown(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        if (_shown && Visible)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
        base.OnDeactivate(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
        base.OnKeyDown(e);
    }

    protected override bool ShowWithoutActivation => false;

    LiveMonitor? Hit(Point point)
    {
        foreach (var item in LayoutRects(ClientRectangle))
        {
            if (item.Rect.Contains(point))
                return item.Monitor;
        }
        return null;
    }

    List<(LiveMonitor Monitor, Rectangle Rect)> LayoutRects(Rectangle area)
    {
        var list = new List<(LiveMonitor, Rectangle)>();
        if (_monitors.Count == 0)
            return list;

        var minX = _monitors.Min(m => m.X);
        var minY = _monitors.Min(m => m.Y);
        var maxX = _monitors.Max(m => m.Right);
        var maxY = _monitors.Max(m => m.Bottom);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var inner = Rectangle.Inflate(area, -8, -8);
        if (inner.Width < 20 || inner.Height < 20)
            inner = area;
        var scale = Math.Min(inner.Width / (float)spanX, inner.Height / (float)spanY);
        var usedW = spanX * scale;
        var usedH = spanY * scale;
        var ox = inner.Left + (inner.Width - usedW) / 2f;
        var oy = inner.Top + (inner.Height - usedH) / 2f;
        foreach (var monitor in _monitors)
        {
            var rect = Rectangle.Round(new RectangleF(
                ox + (monitor.X - minX) * scale,
                oy + (monitor.Y - minY) * scale,
                Math.Max(28, monitor.Width * scale - 6),
                Math.Max(22, monitor.Height * scale - 6)));
            list.Add((monitor, rect));
        }
        return list;
    }
}
