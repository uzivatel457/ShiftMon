using ShiftMon.App;
using ShiftMon.Profiles;

namespace ShiftMon.Ui;

internal sealed class MonitorCanvas : UserControl
{
    readonly List<MonitorProfileEntry> _disabled = [];
    readonly List<MonitorProfileEntry> _enabled = [];
    MonitorProfileEntry? _drag;
    Point _dragStart;
    int _originX;
    int _originY;
    bool _moved;
    bool _viewFrozen;
    float _viewScale = 1;
    int _viewMinX;
    int _viewMinY;
    float _viewOffX;
    float _viewOffY;

    public MonitorCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(54, 60, 68);
        Cursor = Cursors.Default;
    }

    public IReadOnlyList<MonitorProfileEntry> Monitors { get; private set; } = [];
    public MonitorProfileEntry? Selected { get; private set; }
    public bool Interactive { get; set; } = true;
    public bool Dragging => _drag != null;

    public event EventHandler? SelectionChanged;
    public event EventHandler? LayoutEdited;

    public void SetMonitors(IReadOnlyList<MonitorProfileEntry> monitors, MonitorProfileEntry? selected)
    {
        Monitors = monitors;
        Selected = selected;
        Invalidate();
    }

    public void Select(MonitorProfileEntry? entry)
    {
        Selected = entry;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        Split();
        if (_enabled.Count == 0 && _disabled.Count == 0)
        {
            TextRenderer.DrawText(g, Loc.T("Žádný monitor", "No monitors"), Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        using var nameFont = new Font(Font.FontFamily, 9f, FontStyle.Bold);
        using var small = new Font(Font.FontFamily, 8f);
        var laid = LayoutEnabled();
        foreach (var item in laid)
        {
            if (!ReferenceEquals(item.Entry, _drag))
                DrawMonitor(g, item.Entry, item.Rect, nameFont, small, enabled: true);
        }
        foreach (var item in laid)
        {
            if (ReferenceEquals(item.Entry, _drag))
                DrawMonitor(g, item.Entry, item.Rect, nameFont, small, enabled: true);
        }
        if (_disabled.Count > 0)
        {
            TextRenderer.DrawText(g, Loc.T("Vypnuté", "Off"), small, new Rectangle(10, DisabledTop() - 16, 160, 16), Color.FromArgb(180, 186, 194),
                TextFormatFlags.Left);
            var x = 10;
            foreach (var entry in _disabled)
            {
                var rect = new Rectangle(x, DisabledTop(), 108, 36);
                DrawMonitor(g, entry, rect, small, small, enabled: false);
                x += 116;
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var hit = Hit(e.Location);
        Selected = hit;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (Interactive && hit is { Enabled: true, Primary: false })
        {
            _drag = hit;
            _dragStart = e.Location;
            _originX = hit.X;
            _originY = hit.Y;
            _moved = false;
            Split();
            _viewScale = EnabledScale(out _viewMinX, out _viewMinY, out _viewOffX, out _viewOffY);
            _viewFrozen = _viewScale > 0.0001f;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == null)
        {
            Cursor = Interactive && Hit(e.Location) is { Enabled: true, Primary: false } ? Cursors.SizeAll : Cursors.Default;
            base.OnMouseMove(e);
            return;
        }

        if (_viewScale <= 0.0001f)
            return;
        var dx = (int)Math.Round((e.X - _dragStart.X) / _viewScale);
        var dy = (int)Math.Round((e.Y - _dragStart.Y) / _viewScale);
        _drag.X = _originX + dx;
        _drag.Y = _originY + dy;
        if (dx != 0 || dy != 0)
            _moved = true;
        Cursor = Cursors.SizeAll;
        Invalidate();
        Update();
        LayoutEdited?.Invoke(this, EventArgs.Empty);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_drag != null && _moved)
        {
            Snap(_drag);
            LayoutEdited?.Invoke(this, EventArgs.Empty);
        }
        _drag = null;
        _viewFrozen = false;
        Capture = false;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseUp(e);
    }

    void DrawMonitor(Graphics g, MonitorProfileEntry entry, Rectangle rect, Font titleFont, Font detailFont, bool enabled)
    {
        var selected = ReferenceEquals(entry, Selected);
        var dragging = ReferenceEquals(entry, _drag);
        var fillColor = !enabled
            ? Color.FromArgb(70, 74, 80)
            : selected || dragging ? Color.FromArgb(0, 120, 212) : Color.FromArgb(58, 58, 62);
        using var fill = new SolidBrush(fillColor);
        using var pen = new Pen(selected || dragging ? Color.FromArgb(120, 190, 245) : Color.FromArgb(90, 90, 94), selected || dragging ? 2f : 1f);
        g.FillRectangle(fill, rect);
        if (!enabled)
        {
            using var hatch = new System.Drawing.Drawing2D.HatchBrush(
                System.Drawing.Drawing2D.HatchStyle.Percent30, Color.FromArgb(90, 90, 90), Color.FromArgb(40, 40, 40));
            g.FillRectangle(hatch, rect);
        }
        g.DrawRectangle(pen, rect);
        var number = enabled ? (_enabled.OrderBy(m => m.X).ThenBy(m => m.Y).ToList().IndexOf(entry) + 1).ToString() : "–";
        TextRenderer.DrawText(g, number, titleFont, new Rectangle(rect.X, rect.Y + 4, rect.Width, rect.Height / 2), Color.White,
            TextFormatFlags.HorizontalCenter);
        if (rect.Height > 28)
        {
            var detail = entry.Primary ? Loc.T("primární", "primary") : $"{entry.Width}×{entry.Height}";
            TextRenderer.DrawText(g, detail, detailFont, new Rectangle(rect.X + 2, rect.Bottom - 18, rect.Width - 4, 16),
                Color.FromArgb(210, 216, 224), TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    MonitorProfileEntry? Hit(Point point)
    {
        foreach (var item in LayoutEnabled())
        {
            if (item.Rect.Contains(point))
                return item.Entry;
        }

        if (_disabled.Count == 0)
            return null;
        var x = 10;
        foreach (var entry in _disabled)
        {
            if (new Rectangle(x, DisabledTop(), 108, 36).Contains(point))
                return entry;
            x += 116;
        }
        return null;
    }

    void Snap(MonitorProfileEntry item)
    {
        var others = _enabled.Where(m => !ReferenceEquals(m, item)).ToList();
        var xs = new List<int>();
        var ys = new List<int>();
        foreach (var other in others)
        {
            xs.Add(other.X);
            xs.Add(other.X + other.Width);
            xs.Add(other.X - item.Width);
            xs.Add(other.X + other.Width - item.Width);
            ys.Add(other.Y);
            ys.Add(other.Y + other.Height);
            ys.Add(other.Y - item.Height);
            ys.Add(other.Y + other.Height - item.Height);
        }
        item.X = Closest(item.X, xs, 24);
        item.Y = Closest(item.Y, ys, 24);
    }

    static int Closest(int value, IEnumerable<int> edges, int threshold)
    {
        var best = value;
        var bestDelta = threshold + 1;
        foreach (var edge in edges)
        {
            var delta = Math.Abs(edge - value);
            if (delta <= threshold && delta < bestDelta)
            {
                best = edge;
                bestDelta = delta;
            }
        }
        return best;
    }

    void Split()
    {
        _enabled.Clear();
        _disabled.Clear();
        _enabled.AddRange(Monitors.Where(m => m.Enabled));
        _disabled.AddRange(Monitors.Where(m => !m.Enabled));
    }

    int DisabledTop() => _disabled.Count == 0 ? Height : Height - 48;

    List<(MonitorProfileEntry Entry, Rectangle Rect)> LayoutEnabled()
    {
        var result = new List<(MonitorProfileEntry, Rectangle)>();
        if (_enabled.Count == 0)
            return result;
        var scale = View(out var minX, out var minY, out var offsetX, out var offsetY);
        foreach (var entry in _enabled)
        {
            var rect = Rectangle.Round(new RectangleF(
                offsetX + (entry.X - minX) * scale,
                offsetY + (entry.Y - minY) * scale,
                Math.Max(36, entry.Width * scale - 8),
                Math.Max(28, entry.Height * scale - 8)));
            result.Add((entry, rect));
        }
        return result;
    }

    float View(out int minX, out int minY, out float offsetX, out float offsetY)
    {
        if (_viewFrozen)
        {
            minX = _viewMinX;
            minY = _viewMinY;
            offsetX = _viewOffX;
            offsetY = _viewOffY;
            return _viewScale;
        }

        return EnabledScale(out minX, out minY, out offsetX, out offsetY);
    }

    float EnabledScale(out int minX, out int minY, out float offsetX, out float offsetY)
    {
        minX = minY = 0;
        offsetX = offsetY = 12;
        if (_enabled.Count == 0)
            return 1;
        minX = _enabled.Min(m => m.X);
        minY = _enabled.Min(m => m.Y);
        var spanX = Math.Max(1, _enabled.Max(m => m.X + m.Width) - minX);
        var spanY = Math.Max(1, _enabled.Max(m => m.Y + m.Height) - minY);
        var bottom = _disabled.Count == 0 ? Height - 16 : DisabledTop() - 24;
        var area = new Rectangle(12, 12, Math.Max(20, Width - 24), Math.Max(20, bottom - 12));
        var scale = Math.Min(area.Width / (float)spanX, area.Height / (float)spanY);
        var usedW = spanX * scale;
        var usedH = spanY * scale;
        offsetX = area.Left + (area.Width - usedW) / 2f;
        offsetY = area.Top + (area.Height - usedH) / 2f;
        return scale;
    }
}
