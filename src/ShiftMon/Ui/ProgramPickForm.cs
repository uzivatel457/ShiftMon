using ShiftMon.App;
using ShiftMon.Windows;

namespace ShiftMon.Ui;

internal sealed class ProgramPickForm : Form
{
    readonly ListBox _list = new() { IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill };

    public RunningProgram? Selected { get; private set; }

    public ProgramPickForm(IReadOnlyList<RunningProgram> programs)
    {
        Text = Loc.T("Vyberte program", "Choose a program");
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 360);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Text = Loc.T(
                "Vyberte otevřený program. Tlačítko v záhlaví se u něj přestane zobrazovat.",
                "Choose an open program. The title-bar button will no longer appear on it."),
            Padding = new Padding(0, 0, 0, 8)
        };
        foreach (var program in programs)
            _list.Items.Add(program);
        _list.DoubleClick += (_, _) => Accept();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false
        };
        var cancel = new Button { Text = Loc.T("Zrušit", "Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(88, 32) };
        var add = new Button { Text = Loc.T("Přidat", "Add"), AutoSize = true, MinimumSize = new Size(88, 32) };
        add.Click += (_, _) => Accept();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(add);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        body.Controls.Add(_list);
        body.Controls.Add(buttons);
        body.Controls.Add(hint);
        Controls.Add(body);
        CancelButton = cancel;
        AcceptButton = add;
        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
    }

    void Accept()
    {
        if (_list.SelectedItem is not RunningProgram program)
            return;
        Selected = program;
        DialogResult = DialogResult.OK;
        Close();
    }
}
