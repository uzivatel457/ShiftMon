using Microsoft.Win32;
using ShiftMon.Native;

namespace ShiftMon.App;

internal static class Theme
{
    public static bool Dark { get; private set; }

    public static Color Window => Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(244, 246, 248);
    public static Color Surface => Dark ? Color.FromArgb(43, 43, 43) : Color.White;
    public static Color Text => Dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(28, 28, 28);
    public static Color Muted => Dark ? Color.FromArgb(186, 190, 198) : Color.FromArgb(70, 76, 86);
    public static Color Subtitle => Dark ? Color.FromArgb(176, 182, 192) : Color.FromArgb(80, 88, 98);
    public static Color Hint => Dark ? Color.FromArgb(168, 174, 184) : Color.FromArgb(90, 98, 110);
    public static Color Note => Dark ? Color.FromArgb(176, 182, 192) : Color.FromArgb(60, 68, 78);
    public static Color Input => Dark ? Color.FromArgb(28, 28, 28) : Color.White;
    public static Color ButtonFace => Dark ? Color.FromArgb(58, 58, 58) : SystemColors.Control;
    public static Color Line => Dark ? Color.FromArgb(92, 92, 92) : Color.FromArgb(210, 214, 220);
    public static Color Accent => Color.FromArgb(0, 120, 212);
    public static Color AccentText => Color.White;
    public static Color MenuHover => Dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(229, 241, 251);

    public static void Use(string? mode)
    {
        Dark = mode switch
        {
            "light" => false,
            "dark" => true,
            _ => !WindowsUsesLightApps()
        };
    }

    public static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    public static void Attach(Form form)
    {
        form.HandleCreated += (_, _) => ApplyTitleBar(form);
        if (form.IsHandleCreated)
            ApplyTitleBar(form);
    }

    public static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated)
            return;
        try
        {
            var dark = Dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
            NativeMethods.DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));
        }
        catch
        {
            // Záhlaví zůstane ve výchozí barvě Windows.
        }
    }

    public static void StyleTree(Control root)
    {
        StyleOne(root);
        foreach (Control child in root.Controls)
            StyleTree(child);
    }

    internal sealed class ThemeMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Accent;
        public override Color MenuItemSelected => MenuHover;
        public override Color MenuItemSelectedGradientBegin => MenuHover;
        public override Color MenuItemSelectedGradientEnd => MenuHover;
        public override Color MenuItemPressedGradientBegin => MenuHover;
        public override Color MenuItemPressedGradientEnd => MenuHover;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Surface;
    }

    static void StyleOne(Control control)
    {
        switch (control)
        {
            case TextBox or ListBox or CheckedListBox or ComboBox or NumericUpDown:
                control.BackColor = Input;
                control.ForeColor = Text;
                if (control is ComboBox combo && Dark)
                    StyleCombo(combo);
                break;
            case Button button when button.FlatStyle != FlatStyle.Flat:
                if (!Dark)
                    break;
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = ButtonFace;
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = Line;
                button.FlatAppearance.BorderSize = 1;
                break;
            case Label label when label.ForeColor.ToArgb() == SystemColors.ControlText.ToArgb()
                                 || label.ForeColor.ToArgb() == Color.Black.ToArgb():
                label.ForeColor = Text;
                break;
            case CheckBox or RadioButton:
                control.ForeColor = Text;
                break;
        }
    }

    static void StyleCombo(ComboBox combo)
    {
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Input;
        combo.ForeColor = Text;
        if (combo.DrawMode == DrawMode.OwnerDrawFixed)
            return;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= combo.Items.Count)
                return;
            var highlight = combo.DroppedDown && (e.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(highlight ? Accent : Input);
            e.Graphics.FillRectangle(fill, e.Bounds);
            var color = highlight ? AccentText : combo.Enabled ? Text : Muted;
            TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString(), e.Font ?? combo.Font,
                new Rectangle(e.Bounds.X + 4, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 6), e.Bounds.Height),
                color, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
    }
}

internal sealed class ThemedTabControl : TabControl
{
    public ThemedTabControl()
    {
        if (!Theme.Dark)
            return;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(140, 28);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected = e.Index == SelectedIndex;
        using var fill = new SolidBrush(selected ? Theme.Surface : Theme.Window);
        e.Graphics.FillRectangle(fill, e.Bounds);
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, e.Bounds, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (!Theme.Dark || m.Msg != 0x000F || TabCount == 0)
            return;
        using var graphics = Graphics.FromHwnd(Handle);
        var right = GetTabRect(TabCount - 1).Right;
        if (right >= Width)
            return;
        using var rest = new SolidBrush(Theme.Window);
        graphics.FillRectangle(rest, new Rectangle(right, 0, Width - right, ItemSize.Height + 3));
    }
}

internal sealed class ThemedCheckBox : CheckBox
{
    public ThemedCheckBox()
    {
        AutoSize = true;
        if (!Theme.Dark)
            return;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        ForeColor = Theme.Text;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!Theme.Dark)
            return base.GetPreferredSize(proposedSize);
        var text = TextRenderer.MeasureText(Text, Font);
        var box = Box;
        return new Size(box + 8 + text.Width, Math.Max(box + 4, text.Height + 2));
    }

    int Box => Math.Max(14, Font.Height - 2);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Theme.Dark)
        {
            base.OnPaint(e);
            return;
        }

        var g = e.Graphics;
        using (var back = new SolidBrush(Parent?.BackColor ?? Theme.Surface))
            g.FillRectangle(back, ClientRectangle);
        var box = Box;
        var rect = new Rectangle(0, Math.Max(0, (Height - box) / 2), box, box);
        var enabled = Enabled;
        using (var fill = new SolidBrush(Checked ? (enabled ? Theme.Accent : Theme.ButtonFace) : Theme.Input))
            g.FillRectangle(fill, rect);
        using (var pen = new Pen(Checked && enabled ? Theme.Accent : (enabled ? Theme.Line : Theme.Muted)))
            g.DrawRectangle(pen, rect);
        if (Checked)
        {
            using var tick = new Pen(Color.White, 1.8f);
            g.DrawLines(tick, new[]
            {
                new Point(rect.X + 3, rect.Y + box / 2),
                new Point(rect.X + box / 2 - 1, rect.Y + box - 4),
                new Point(rect.X + box - 3, rect.Y + 3)
            });
        }

        TextRenderer.DrawText(g, Text, Font, new Rectangle(rect.Right + 6, 0, Math.Max(0, Width - rect.Right - 6), Height),
            enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

internal sealed class ThemedNumber : Control
{
    int _value;
    bool _editable = true;
    string? _draft;
    bool _replace = true;

    public ThemedNumber()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Width = 108;
        Height = 26;
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        TabStop = true;
        Cursor = Cursors.IBeam;
    }

    public int Minimum { get; set; } = -20000;
    public int Maximum { get; set; } = 20000;

    public int Value
    {
        get => _value;
        set => SetValue(value);
    }

    public bool Editable
    {
        get => _editable;
        set
        {
            _editable = value;
            if (!value)
                _draft = null;
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;

    public void MatchHeight(int height)
    {
        if (height > 8)
            Height = height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Input);
        DrawFrame(g);
        var textRect = new Rectangle(6, 2, Math.Max(1, Width - ButtonWidth - 10), Math.Max(1, Height - 4));
        TextRenderer.DrawText(g, _draft ?? _value.ToString(), Font, textRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        var buttons = Width - ButtonWidth;
        var mid = Height / 2;
        var arrow = _editable ? Theme.Text : Theme.Muted;
        DrawArrow(g, new Rectangle(buttons + 1, 2, ButtonWidth - 3, mid - 3), up: true, arrow);
        DrawArrow(g, new Rectangle(buttons + 1, mid + 2, ButtonWidth - 3, Height - mid - 4), up: false, arrow);
    }

    void DrawFrame(Graphics g)
    {
        var outer = Theme.Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(122, 122, 122);
        var inner = Theme.Dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(200, 200, 200);
        using var outerBrush = new SolidBrush(outer);
        using var innerBrush = new SolidBrush(inner);
        g.FillRectangle(outerBrush, 0, 0, Width, 1);
        g.FillRectangle(outerBrush, 0, Height - 1, Width, 1);
        g.FillRectangle(outerBrush, 0, 0, 1, Height);
        g.FillRectangle(outerBrush, Width - 1, 0, 1, Height);
        if (Width > 4 && Height > 4)
        {
            g.FillRectangle(innerBrush, 1, 1, Width - 2, 1);
            g.FillRectangle(innerBrush, 1, Height - 2, Width - 2, 1);
            g.FillRectangle(innerBrush, 1, 1, 1, Height - 2);
            g.FillRectangle(innerBrush, Width - 2, 1, 1, Height - 2);
        }

        var split = Width - ButtonWidth;
        g.FillRectangle(outerBrush, split, 1, 1, Height - 2);
        g.FillRectangle(innerBrush, split, Height / 2, ButtonWidth - 1, 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.X >= Width - ButtonWidth)
        {
            Step(e.Y < Height / 2 ? 1 : -1);
            return;
        }
        _replace = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Step(e.Delta > 0 ? 1 : -1);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Up)
        {
            Step(1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Down)
        {
            Step(-1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Back && _editable)
        {
            _draft = _replace ? "" : (_draft ?? _value.ToString());
            if (_draft.Length > 0)
                _draft = _draft[..^1];
            _replace = false;
            Invalidate();
            e.Handled = true;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!_editable || e.KeyChar == '\b')
            return;
        if (e.KeyChar == '\r')
        {
            CommitDraft();
            e.Handled = true;
            return;
        }
        if (!char.IsDigit(e.KeyChar) && e.KeyChar != '-')
            return;
        var current = _replace ? "" : (_draft ?? _value.ToString());
        _draft = current + e.KeyChar;
        _replace = false;
        if (int.TryParse(_draft, out var parsed))
            SetValue(parsed, keepDraft: true);
        else
            Invalidate();
        e.Handled = true;
    }

    protected override void OnLeave(EventArgs e)
    {
        CommitDraft();
        base.OnLeave(e);
    }

    void Step(int delta)
    {
        if (!_editable)
            return;
        _draft = null;
        SetValue(_value + delta, keepDraft: false);
    }

    void CommitDraft()
    {
        if (_draft == null)
            return;
        if (int.TryParse(_draft, out var parsed))
            SetValue(parsed, keepDraft: false);
        _draft = null;
        _replace = true;
        Invalidate();
    }

    void SetValue(int value, bool keepDraft = false)
    {
        var next = Math.Clamp(value, Minimum, Maximum);
        var changed = next != _value;
        _value = next;
        if (!keepDraft)
            _draft = null;
        Invalidate();
        if (changed)
            ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    int ButtonWidth => Math.Max(18, Height / 2 + 4);

    static void DrawArrow(Graphics g, Rectangle area, bool up, Color color)
    {
        var x = area.Left + area.Width / 2;
        var y = area.Top + area.Height / 2;
        Point[] points = up
            ? [new Point(x - 3, y + 1), new Point(x, y - 2), new Point(x + 3, y + 1)]
            : [new Point(x - 3, y - 1), new Point(x, y + 2), new Point(x + 3, y - 1)];
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, points);
    }
}
