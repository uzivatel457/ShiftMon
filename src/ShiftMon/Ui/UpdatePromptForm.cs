namespace ShiftMon.Ui;

internal sealed class UpdatePromptForm : Form
{
    public UpdatePromptForm(string version, string notes, bool offerInstall)
    {
        Text = "ShiftMon";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(520, 360);
        BackColor = Color.White;

        var title = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(20, 16, 480, 48),
            Font = new Font(Font, FontStyle.Bold),
            Text = offerInstall
                ? App.Loc.T($"Je k dispozici ShiftMon {version}", $"ShiftMon {version} is available")
                : App.Loc.T($"Co je nového ve verzi {version}", $"What's new in version {version}")
        };
        var body = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            Bounds = new Rectangle(20, 72, 480, 220),
            Text = notes,
            BackColor = Color.White
        };
        var accept = new Button
        {
            Text = offerInstall ? App.Loc.T("Aktualizovat", "Update") : "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(120, 34),
            Padding = new Padding(12, 4, 12, 4),
            BackColor = Color.FromArgb(31, 111, 184),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        accept.FlatAppearance.BorderSize = 0;
        var later = new Button
        {
            Text = App.Loc.T("Později", "Later"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(110, 34),
            Padding = new Padding(12, 4, 12, 4)
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 12, 8),
            WrapContents = false
        };
        actions.Controls.Add(later);
        actions.Controls.Add(accept);
        if (!offerInstall)
            later.Visible = false;
        Controls.Add(title);
        Controls.Add(body);
        Controls.Add(actions);
        AcceptButton = accept;
        CancelButton = later;
    }
}
