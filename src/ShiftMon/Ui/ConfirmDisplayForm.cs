using ShiftMon.App;
using ShiftMon.Display;
using ShiftMon.Profiles;

namespace ShiftMon.Ui;

internal sealed class ConfirmDisplayForm : Form
{
    readonly Label _countdown;
    readonly System.Windows.Forms.Timer _timer;
    int _seconds = 15;

    ConfirmDisplayForm(string profileName)
    {
        Text = "ShiftMon";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = true;
        ClientSize = new Size(520, 190);
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.White;

        var title = new Label
        {
            Text = Loc.T($"Ponechat profil „{profileName}“?", $"Keep profile “{profileName}”?"),
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Location = new Point(20, 16),
            Font = new Font(Font, FontStyle.Bold)
        };
        var detail = new Label
        {
            Text = Loc.T(
                "Když nic nezvolíte, monitory se vrátí do předchozího rozložení.",
                "If you do not choose, the monitors return to the previous layout."),
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Location = new Point(20, 52),
            ForeColor = Color.FromArgb(70, 76, 86)
        };
        _countdown = new Label
        {
            Text = Loc.T("Zbývá 15 s", "15 s left"),
            AutoSize = true,
            Location = new Point(20, 108),
            ForeColor = Color.FromArgb(31, 111, 184)
        };
        var keep = new Button
        {
            Text = Loc.T("Ponechat", "Keep"),
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(110, 34),
            Padding = new Padding(12, 4, 12, 4),
            BackColor = Color.FromArgb(31, 111, 184),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        keep.FlatAppearance.BorderSize = 0;
        var revert = new Button
        {
            Text = Loc.T("Vrátit", "Revert"),
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
        actions.Controls.Add(revert);
        actions.Controls.Add(keep);
        Controls.AddRange([title, detail, _countdown, actions]);
        AcceptButton = keep;
        CancelButton = revert;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            _seconds--;
            _countdown.Text = _seconds <= 0 ? Loc.T("Vracím zpět…", "Reverting…") : Loc.T($"Zbývá {_seconds} s", $"{_seconds} s left");
            if (_seconds <= 0)
            {
                _timer.Stop();
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
    }

    public static bool Ask(string profileName)
    {
        using var form = new ConfirmDisplayForm(profileName);
        var monitors = DisplayService.GetLiveMonitors();
        var primary = monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();
        if (primary != null)
        {
            var x = primary.WorkX + Math.Max(0, (primary.WorkWidth - form.Width) / 2);
            var y = primary.WorkY + Math.Max(0, (primary.WorkHeight - form.Height) / 2);
            form.Location = new Point(x, y);
        }
        else
        {
            form.StartPosition = FormStartPosition.CenterScreen;
        }

        form._timer.Start();
        return form.ShowDialog() == DialogResult.OK;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
