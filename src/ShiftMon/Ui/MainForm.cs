using ShiftMon.App;
using ShiftMon.Display;
using ShiftMon.Profiles;

namespace ShiftMon.Ui;

internal sealed class MainForm : Form
{
    readonly ProfileStore _store;
    readonly AppSettings _settings;
    readonly Action<DisplayProfile> _apply;
    readonly Action _identify;
    readonly Action<bool> _buttonsChanged;
    readonly Action _languageChanged;

    readonly ListBox _profiles = new() { IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    readonly MonitorCanvas _canvas = new() { Dock = DockStyle.Top, Height = 210 };
    readonly Label _monitorTitle = new() { AutoSize = false, Height = 28, AutoEllipsis = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    readonly Label _status = new() { AutoSize = false, Height = 52, Dock = DockStyle.Bottom, ForeColor = Color.FromArgb(70, 76, 86), Padding = new Padding(8, 6, 8, 0) };
    readonly CheckBox _enabled = new() { AutoSize = true };
    readonly CheckBox _primary = new() { AutoSize = true };
    readonly ComboBox _resolution = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    readonly ComboBox _refresh = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly ComboBox _orientation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    readonly NumericUpDown _posX = new() { Minimum = -20000, Maximum = 20000, Width = 90 };
    readonly NumericUpDown _posY = new() { Minimum = -20000, Maximum = 20000, Width = 90 };
    readonly CheckBox _caption = new() { AutoSize = true };
    readonly CheckBox _picker = new() { AutoSize = true };
    readonly CheckBox _startup = new() { AutoSize = true };
    readonly CheckBox _updates = new() { AutoSize = true };
    readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    bool _loading;
    bool _dirty;
    bool _allowClose;

    public MainForm(ProfileStore store, AppSettings settings, Action<DisplayProfile> apply, Action identify, Action<bool> buttonsChanged, Action languageChanged)
    {
        _store = store;
        _settings = settings;
        _apply = apply;
        _identify = identify;
        _buttonsChanged = buttonsChanged;
        _languageChanged = languageChanged;

        Text = "ShiftMon";
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(244, 246, 248);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 740);
        MinimumSize = new Size(1000, 680);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(AppPaths.CurrentExecutable);
        }
        catch
        {
            // Ikona v okně je jen doplněk.
        }

        _enabled.Text = Loc.T("Zapnutý", "Enabled");
        _primary.Text = Loc.T("Primární", "Primary");
        _caption.Text = Loc.T("Zobrazovat tlačítko v záhlaví oken", "Show a button in window title bars");
        _picker.Text = Loc.T("Při třech a více monitorech otevřít výběr obrazovky", "With three or more monitors, open a screen picker");
        _startup.Text = Loc.T("Spouštět ShiftMon po přihlášení do Windows", "Start ShiftMon when you sign in to Windows");
        _updates.Text = Loc.T("Hledat nové verze na GitHubu", "Check GitHub for new versions");
        _monitorTitle.Text = Loc.T("Vyberte monitor", "Select a monitor");

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = Color.White,
            Padding = new Padding(16, 10, 16, 12)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = "ShiftMon",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2)
        });
        header.Controls.Add(new Label
        {
            Text = Loc.T(
                "Tlačítko v záhlaví přesune okno na jiný monitor. Profily přepínají zapnutí a pozici obrazovek.",
                "The title-bar button moves a window to another monitor. Profiles switch which screens are on and where they sit."),
            AutoSize = true,
            MaximumSize = new Size(1040, 0),
            Margin = new Padding(0),
            ForeColor = Color.FromArgb(80, 88, 98)
        });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildProfilesTab());
        tabs.TabPages.Add(BuildOptionsTab());
        Controls.Add(tabs);
        Controls.Add(header);

        _profiles.SelectedIndexChanged += (_, _) => SelectProfile(_profiles.SelectedItem as DisplayProfile, confirmDirty: true);
        _canvas.SelectionChanged += (_, _) => ShowMonitor(_canvas.Selected);
        _canvas.LayoutEdited += (_, _) =>
        {
            if (_loading)
                return;
            MarkDirty();
            if (_canvas.Dragging)
                SyncDragPosition();
            else
                ShowMonitor(_canvas.Selected);
        };
        _enabled.CheckedChanged += (_, _) => ToggleEnabled();
        _primary.CheckedChanged += (_, _) => TogglePrimary();
        _resolution.SelectedIndexChanged += (_, _) => ApplyResolution();
        _refresh.SelectedIndexChanged += (_, _) => ApplyRefresh();
        _orientation.SelectedIndexChanged += (_, _) => ApplyOrientation();
        _posX.ValueChanged += (_, _) => ApplyPosition();
        _posY.ValueChanged += (_, _) => ApplyPosition();

        _store.EnsureReady();
        ReloadList(_store.Profiles.FirstOrDefault());
    }

    public string? SelectedProfileId => (_profiles.SelectedItem as DisplayProfile)?.Id;

    public void SaveIfDirty()
    {
        if (_dirty)
            SaveSelected(announce: false);
    }

    public void AllowExit() => _allowClose = true;

    public void FocusProfile(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        var profile = _store.Find(id);
        if (profile != null)
            _profiles.SelectedItem = profile;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (_dirty && !ConfirmDiscardOrSave())
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    TabPage BuildProfilesTab()
    {
        var page = new TabPage(Loc.T("Profily", "Profiles")) { Padding = new Padding(10), BackColor = Color.FromArgb(244, 246, 248) };
        var left = new Panel { Dock = DockStyle.Left, Width = 320, Padding = new Padding(0, 0, 12, 0) };
        _profiles.Dock = DockStyle.Fill;
        _profiles.DisplayMember = nameof(DisplayProfile.Name);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 132, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        buttons.Controls.Add(Accent(Loc.T("Nový z aktuálního rozložení", "New from current layout"), CreateFromCurrent));
        buttons.Controls.Add(Command(Loc.T("Přejmenovat", "Rename"), RenameSelected));
        buttons.Controls.Add(Command(Loc.T("Smazat", "Delete"), DeleteSelected));
        left.Controls.Add(_profiles);
        left.Controls.Add(buttons);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        actions.Controls.Add(Accent(Loc.T("Použít", "Apply"), ApplySelected));
        actions.Controls.Add(Command(Loc.T("Uložit", "Save"), () => SaveSelected(announce: true)));
        actions.Controls.Add(Command(Loc.T("Zástupce na ploše", "Desktop shortcut"), CreateShortcut));
        actions.Controls.Add(Command(Loc.T("Identifikovat", "Identify"), _identify));

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(0, 8, 0, 0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var flags = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        flags.Controls.Add(_enabled);
        flags.Controls.Add(_primary);
        AddRow(fields, 0, Loc.T("Monitor", "Monitor"), _monitorTitle);
        _monitorTitle.Dock = DockStyle.Fill;
        AddRow(fields, 1, "", flags);
        AddRow(fields, 2, Loc.T("Rozlišení", "Resolution"), _resolution);
        AddRow(fields, 3, Loc.T("Obnovovací frekvence", "Refresh rate"), _refresh);
        AddRow(fields, 4, Loc.T("Orientace", "Orientation"), _orientation);
        var position = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        position.Controls.Add(_posX);
        position.Controls.Add(new Label { Text = "Y", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        position.Controls.Add(_posY);
        AddRow(fields, 5, Loc.T("Pozice X", "Position X"), position);

        _orientation.Items.AddRange(
        [
            Loc.T("Na šířku", "Landscape"),
            Loc.T("Na výšku (doprava)", "Portrait (right)"),
            Loc.T("Vzhůru nohama", "Upside down"),
            Loc.T("Na výšku (doleva)", "Portrait (left)")
        ]);
        var hint = new Label
        {
            Text = Loc.T(
                "Primární monitor je v počátku. Ostatní přesuňte tažením. Po použití máte 15 sekund na potvrzení.",
                "The primary monitor sits at the origin. Drag the others to move them. After you apply a layout you have 15 seconds to confirm it."),
            AutoSize = false,
            Height = 44,
            Dock = DockStyle.Bottom,
            ForeColor = Color.FromArgb(90, 98, 110)
        };
        right.Controls.Add(fields);
        right.Controls.Add(hint);
        right.Controls.Add(actions);
        right.Controls.Add(_status);
        right.Controls.Add(_canvas);
        page.Controls.Add(right);
        page.Controls.Add(left);
        return page;
    }

    TabPage BuildOptionsTab()
    {
        var page = new TabPage(Loc.T("Možnosti", "Options")) { Padding = new Padding(16), BackColor = Color.White };
        _caption.Checked = _settings.CaptionButton;
        _picker.Checked = _settings.PickerWhenMany;
        _startup.Checked = _settings.RunAtStartup;
        _updates.Checked = _settings.CheckUpdates;
        _language.Items.Add(Loc.T("Automaticky dle systému", "Match Windows language"));
        _language.Items.Add("Česky");
        _language.Items.Add("English");
        _language.SelectedIndex = _settings.Language switch
        {
            "cs" => 1,
            "en" => 2,
            _ => 0
        };
        _language.SelectedIndexChanged += (_, _) => LanguagePicked();
        _caption.CheckedChanged += (_, _) =>
        {
            _settings.CaptionButton = _caption.Checked;
            _settings.Save();
            _buttonsChanged(_caption.Checked);
        };
        _picker.CheckedChanged += (_, _) =>
        {
            _settings.PickerWhenMany = _picker.Checked;
            _settings.Save();
        };
        _startup.CheckedChanged += (_, _) =>
        {
            _settings.RunAtStartup = _startup.Checked;
            _settings.Save();
            StartupRegistration.Apply(_startup.Checked, AppPaths.CurrentExecutable);
        };
        _updates.CheckedChanged += (_, _) =>
        {
            _settings.CheckUpdates = _updates.Checked;
            _settings.Save();
        };

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var languageRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        languageRow.Controls.Add(new Label
        {
            Text = Loc.T("Jazyk", "Language"),
            AutoSize = true,
            Padding = new Padding(0, 6, 12, 0)
        });
        languageRow.Controls.Add(_language);
        layout.Controls.Add(languageRow);
        layout.Controls.Add(_caption);
        layout.Controls.Add(_picker);
        layout.Controls.Add(_startup);
        layout.Controls.Add(_updates);
        layout.Controls.Add(Command(Loc.T("Zkontrolovat aktualizace", "Check for updates"), () => _ = UpdateService.CheckNowAsync()));
        layout.Controls.Add(Command(Loc.T("Seznam změn", "Change list"), () =>
        {
            using var notes = new UpdatePromptForm(AppPaths.Version, ReleaseNotes.Current, offerInstall: false);
            notes.ShowDialog(this);
        }));
        layout.Controls.Add(Note(Loc.T(
            "Levé tlačítko v záhlaví přesune okno na druhý monitor. Pravým tlačítkem vyberete obrazovku, když je jich víc.",
            "The left title-bar button moves the window to the other monitor. Right-click it to pick a screen when there are more than two.")));
        layout.Controls.Add(Note(Loc.T(
            "Tlačítko se přidá k oknům se systémovým záhlavím. Celoobrazovkové hry a aplikace s vlastním rámem ho nemusí zobrazit.",
            "The button is added to windows that use the system title bar. Full-screen games and apps with their own frame may not show it.")));
        layout.Controls.Add(Note(Loc.T(
            "Zástupce na ploše spustí uložený profil: jeden monitor může být vypnutý, oba mohou být zapnuté v uložené pozici.",
            "A desktop shortcut applies a saved profile. One monitor can be off, or both can be on in the saved position.")));
        layout.Controls.Add(Note(Loc.T("Nastavení a profily: ", "Settings and profiles: ") + AppPaths.DataDirectory));
        layout.Controls.Add(Note(InstallService.IsInstalled
            ? Loc.T("Nainstalováno ve složce ", "Installed in ") + AppPaths.InstallDirectory
            : Loc.T("Aplikace zatím běží bez instalace. Instalátor ji zařadí do Windows a do spuštění po přihlášení.",
                "The app is running without an install. The installer adds it to Windows and to startup.")));
        if (InstallService.IsInstalled)
            layout.Controls.Add(Command(Loc.T("Odinstalovat ShiftMon", "Uninstall ShiftMon"), Uninstall));
        page.Controls.Add(layout);
        return page;
    }

    void CreateFromCurrent()
    {
        if (_dirty && !ConfirmDiscardOrSave())
            return;
        var name = Prompt(Loc.T("Nový profil", "New profile"), Loc.T("Název profilu:", "Profile name:"), NextName());
        if (string.IsNullOrWhiteSpace(name))
            return;
        var profile = new DisplayProfile
        {
            Name = name.Trim(),
            Monitors = DisplayService.Query().Select(ProfileLayout.FromState).ToList()
        };
        _store.Profiles.Add(profile);
        _store.Save();
        _dirty = false;
        ReloadList(profile);
        _status.Text = Loc.T($"Profil „{profile.Name}“ uložil aktuální rozložení.", $"Profile “{profile.Name}” saved the current layout.");
    }

    void RenameSelected()
    {
        if (_profiles.SelectedItem is not DisplayProfile profile)
            return;
        var name = Prompt(Loc.T("Přejmenovat", "Rename"), Loc.T("Nový název:", "New name:"), profile.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;
        profile.Name = name.Trim();
        _store.Save();
        ReloadList(profile);
    }

    void DeleteSelected()
    {
        if (_profiles.SelectedItem is not DisplayProfile profile)
            return;
        if (_store.Profiles.Count <= 1)
        {
            _status.Text = Loc.T("Poslední profil musí zůstat. Upravte ho, nebo ho přejmenujte.", "The last profile has to stay. Edit it, or rename it.");
            return;
        }
        if (MessageBox.Show(this, Loc.T($"Smazat profil „{profile.Name}“?", $"Delete profile “{profile.Name}”?"), "ShiftMon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _store.Profiles.Remove(profile);
        _store.Save();
        _dirty = false;
        ReloadList(_store.Profiles.FirstOrDefault());
        _status.Text = Loc.T("Profil byl smazán.", "The profile was deleted.");
    }

    void ApplySelected()
    {
        if (_profiles.SelectedItem is not DisplayProfile profile)
        {
            _status.Text = Loc.T("Vyberte profil.", "Select a profile.");
            return;
        }
        if (!SaveSelected(announce: false))
            return;
        _apply(profile);
    }

    bool SaveSelected(bool announce)
    {
        if (_profiles.SelectedItem is not DisplayProfile profile)
            return false;
        var enabled = profile.Monitors.Where(m => m.Enabled).ToList();
        if (enabled.Count == 0)
        {
            _status.Text = Loc.T("Alespoň jeden monitor musí zůstat zapnutý.", "At least one monitor must stay on.");
            return false;
        }
        if (ProfileLayout.HasOverlap(enabled))
        {
            _status.Text = Loc.T("Zapnuté monitory se překrývají.", "Enabled monitors overlap.");
            return false;
        }
        _store.Save();
        _dirty = false;
        if (announce)
            _status.Text = Loc.T($"Profil „{profile.Name}“ je uložený.", $"Profile “{profile.Name}” is saved.");
        return true;
    }

    void CreateShortcut()
    {
        if (_profiles.SelectedItem is not DisplayProfile profile)
            return;
        if (!SaveSelected(announce: false))
            return;
        try
        {
            var path = ShortcutService.DesktopShortcut(profile.Name);
            if (!string.IsNullOrWhiteSpace(profile.ShortcutPath) && !string.Equals(profile.ShortcutPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(profile.ShortcutPath))
                File.Delete(profile.ShortcutPath);
            ShortcutService.Create(path, AppPaths.CurrentExecutable, "--apply-profile \"" + profile.Id + "\"", Loc.T("ShiftMon profil: ", "ShiftMon profile: ") + profile.Name);
            profile.ShortcutPath = path;
            _store.Save();
            _status.Text = Loc.T("Zástupce je na ploše: ", "Shortcut is on the desktop: ") + Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            AppLog.Info("Zástupce: " + ex);
            MessageBox.Show(this, Loc.T("Zástupce se nepodařilo vytvořit.\n", "The shortcut could not be created.\n") + ex.Message, "ShiftMon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void SelectProfile(DisplayProfile? profile, bool confirmDirty)
    {
        if (_loading)
            return;
        if (confirmDirty && _dirty)
        {
            var id = profile?.Id;
            if (!ConfirmDiscardOrSave())
                return;
            profile = string.IsNullOrEmpty(id) ? _store.Profiles.FirstOrDefault() : _store.Find(id) ?? _store.Profiles.FirstOrDefault();
            ReloadList(profile);
            return;
        }

        ShowProfile(profile);
    }

    void ShowProfile(DisplayProfile? profile)
    {
        _loading = true;
        if (profile == null)
            profile = _store.Profiles.FirstOrDefault();
        if (profile == null)
        {
            ShowMonitor(null);
            _loading = false;
            return;
        }

        _canvas.Interactive = true;
        _canvas.SetMonitors(profile.Monitors, profile.Monitors.FirstOrDefault());
        ShowMonitor(profile.Monitors.FirstOrDefault());
        _status.Text = Loc.T($"Profil „{profile.Name}“.", $"Profile “{profile.Name}”.");
        _dirty = false;
        _loading = false;
    }

    void SyncDragPosition()
    {
        if (_canvas.Selected is not { } monitor)
            return;
        _loading = true;
        _posX.Value = Math.Clamp(monitor.X, (int)_posX.Minimum, (int)_posX.Maximum);
        _posY.Value = Math.Clamp(monitor.Y, (int)_posY.Minimum, (int)_posY.Maximum);
        _loading = false;
    }

    void ShowMonitor(MonitorProfileEntry? monitor)
    {
        _loading = true;
        var has = monitor != null;
        _enabled.Enabled = has;
        _primary.Enabled = has && monitor!.Enabled;
        _resolution.Enabled = has && monitor!.Enabled;
        _refresh.Enabled = has && monitor!.Enabled;
        _orientation.Enabled = has && monitor!.Enabled;
        _posX.Enabled = has && monitor!.Enabled && !monitor.Primary;
        _posY.Enabled = has && monitor!.Enabled && !monitor.Primary;
        if (monitor == null)
        {
            _monitorTitle.Text = Loc.T("Vyberte monitor", "Select a monitor");
            _loading = false;
            return;
        }

        _monitorTitle.Text = monitor.Label;
        _enabled.Checked = monitor.Enabled;
        _primary.Checked = monitor.Primary;
        FillResolutions(monitor);
        FillRefresh(monitor);
        _orientation.SelectedIndex = Math.Clamp(monitor.Orientation, 0, 3);
        _posX.Value = Math.Clamp(monitor.X, (int)_posX.Minimum, (int)_posX.Maximum);
        _posY.Value = Math.Clamp(monitor.Y, (int)_posY.Minimum, (int)_posY.Maximum);
        _loading = false;
        _canvas.Select(monitor);
    }

    void FillResolutions(MonitorProfileEntry monitor)
    {
        _resolution.Items.Clear();
        var modes = DisplayService.GetModes(monitor.AdapterName);
        var sizes = modes.Select(m => m.ResolutionLabel).Distinct().ToList();
        var current = $"{monitor.Width} × {monitor.Height}";
        if (!sizes.Contains(current) && monitor.Width > 0)
            sizes.Insert(0, current);
        foreach (var size in sizes.OrderByDescending(ParseWidth).ThenByDescending(ParseHeight))
            _resolution.Items.Add(size);
        _resolution.SelectedItem = current;
    }

    void FillRefresh(MonitorProfileEntry monitor)
    {
        _refresh.Items.Clear();
        var modes = DisplayService.GetModes(monitor.AdapterName)
            .Where(m => m.Width == monitor.Width && m.Height == monitor.Height)
            .Select(m => m.Frequency)
            .Where(f => f > 1)
            .Distinct()
            .OrderByDescending(f => f)
            .ToList();
        if (monitor.Frequency > 1 && !modes.Contains(monitor.Frequency))
            modes.Insert(0, monitor.Frequency);
        foreach (var frequency in modes)
            _refresh.Items.Add(frequency + " Hz");
        if (_refresh.Items.Count == 0)
            _refresh.Items.Add(Loc.T("Výchozí", "Default"));
        var wanted = monitor.Frequency > 1 ? monitor.Frequency + " Hz" : null;
        _refresh.SelectedItem = wanted != null && _refresh.Items.Contains(wanted) ? wanted : _refresh.Items[0];
    }

    void ToggleEnabled()
    {
        if (_loading || Current is not { } monitor || _profiles.SelectedItem is not DisplayProfile profile)
            return;
        monitor.Enabled = _enabled.Checked;
        if (monitor.Enabled)
        {
            if (monitor.Width < 640)
            {
                var mode = DisplayService.GetModes(monitor.AdapterName).OrderByDescending(m => (long)m.Width * m.Height).FirstOrDefault();
                if (mode != null)
                {
                    monitor.Width = mode.Width;
                    monitor.Height = mode.Height;
                    monitor.Frequency = mode.Frequency;
                }
            }
            if (profile.Monitors.Count(m => m.Enabled) == 1)
                ProfileLayout.MakePrimary(profile.Monitors, monitor);
            else if (profile.Monitors.Any(m => m.Enabled && !ReferenceEquals(m, monitor) && ProfileLayout.Overlaps(m, monitor)))
                ProfileLayout.PlaceToTheRight(monitor, profile.Monitors);
        }
        else if (monitor.Primary)
        {
            var replacement = profile.Monitors.Where(m => m.Enabled && !ReferenceEquals(m, monitor)).OrderBy(m => m.X).FirstOrDefault();
            if (replacement != null)
                ProfileLayout.MakePrimary(profile.Monitors, replacement);
            monitor.Primary = false;
        }
        MarkDirty();
        _canvas.SetMonitors(profile.Monitors, monitor);
        ShowMonitor(monitor);
    }

    void TogglePrimary()
    {
        if (_loading || Current is not { } monitor || _profiles.SelectedItem is not DisplayProfile profile)
            return;
        if (!_primary.Checked)
        {
            _loading = true;
            _primary.Checked = monitor.Primary;
            _loading = false;
            return;
        }
        ProfileLayout.MakePrimary(profile.Monitors, monitor);
        MarkDirty();
        _canvas.SetMonitors(profile.Monitors, monitor);
        ShowMonitor(monitor);
    }

    void ApplyResolution()
    {
        if (_loading || Current is not { } monitor || _resolution.SelectedItem is not string label)
            return;
        var parts = label.Split('×', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
            return;
        monitor.Width = width;
        monitor.Height = height;
        var frequency = DisplayService.GetModes(monitor.AdapterName)
            .Where(m => m.Width == width && m.Height == height && m.Frequency > 1)
            .Select(m => m.Frequency)
            .OrderByDescending(f => f)
            .Cast<int?>()
            .FirstOrDefault();
        if (frequency is > 1)
            monitor.Frequency = frequency.Value;
        MarkDirty();
        FillRefresh(monitor);
        _canvas.Invalidate();
    }

    void ApplyRefresh()
    {
        if (_loading || Current is not { } monitor || _refresh.SelectedItem is not string label)
            return;
        var number = label.Replace("Hz", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (int.TryParse(number, out var frequency))
        {
            monitor.Frequency = frequency;
            MarkDirty();
        }
    }

    void ApplyOrientation()
    {
        if (_loading || Current is not { } monitor || _orientation.SelectedIndex < 0)
            return;
        monitor.Orientation = _orientation.SelectedIndex;
        MarkDirty();
    }

    void ApplyPosition()
    {
        if (_loading || Current is not { } monitor || monitor.Primary)
            return;
        monitor.X = (int)_posX.Value;
        monitor.Y = (int)_posY.Value;
        MarkDirty();
        _canvas.Invalidate();
    }

    void Uninstall()
    {
        if (MessageBox.Show(this, Loc.T("Odebrat ShiftMon z Windows? Profily v datech aplikace zůstanou.", "Remove ShiftMon from Windows? Profiles in the app data will stay."), "ShiftMon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        InstallService.Uninstall();
        InstallService.ScheduleDeleteInstallDirectory();
        _allowClose = true;
        Application.Exit();
    }

    bool ConfirmDiscardOrSave()
    {
        var answer = MessageBox.Show(this, Loc.T("Uložit změny v profilu?", "Save the changes to this profile?"), "ShiftMon", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel)
            return false;
        if (answer == DialogResult.Yes)
            return SaveSelected(announce: false);
        _store.Load();
        _store.EnsureReady();
        _dirty = false;
        return true;
    }

    void ReloadList(DisplayProfile? select)
    {
        _loading = true;
        _profiles.DataSource = null;
        _profiles.DataSource = _store.Profiles;
        _profiles.DisplayMember = nameof(DisplayProfile.Name);
        if (select != null)
            _profiles.SelectedItem = select;
        _loading = false;
        ShowProfile(_profiles.SelectedItem as DisplayProfile);
    }

    void MarkDirty()
    {
        if (_loading)
            return;
        _dirty = true;
        if (_profiles.SelectedItem is DisplayProfile profile)
            _status.Text = Loc.T($"Profil „{profile.Name}“ má neuložené změny.", $"Profile “{profile.Name}” has unsaved changes.");
    }

    void LanguagePicked()
    {
        if (_loading)
            return;
        var choice = _language.SelectedIndex switch
        {
            1 => "cs",
            2 => "en",
            _ => "auto"
        };
        if (string.Equals(choice, _settings.Language, StringComparison.OrdinalIgnoreCase))
            return;
        _settings.Language = choice;
        _settings.Save();
        if (_dirty)
            SaveSelected(announce: false);
        Loc.Use(choice);
        BeginInvoke(_languageChanged);
    }

    MonitorProfileEntry? Current => _canvas.Selected;

    string NextName()
    {
        var index = _store.Profiles.Count + 1;
        var prefix = Loc.T("Profil ", "Profile ");
        var name = prefix + index;
        while (_store.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            name = prefix + index;
        }
        return name;
    }

    string? Prompt(string title, string label, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(380, 130),
            MinimizeBox = false,
            MaximizeBox = false,
            Font = Font
        };
        var text = new TextBox { Text = initial, Bounds = new Rectangle(16, 40, 348, 24) };
        form.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(16, 16) });
        form.Controls.Add(text);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(196, 82, 80, 30) };
        var cancel = new Button { Text = Loc.T("Zrušit", "Cancel"), DialogResult = DialogResult.Cancel, Bounds = new Rectangle(284, 82, 80, 30) };
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(this) == DialogResult.OK ? text.Text : null;
    }

    static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, row == 6 ? 48 : 36));
        if (label.Length > 0)
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        control.Anchor = AnchorStyles.Left;
        table.Controls.Add(control, label.Length > 0 ? 1 : 0, row);
        if (label.Length == 0)
            table.SetColumnSpan(control, 2);
    }

    static Button Command(string text, Action click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(96, 34),
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 8, 6),
            UseVisualStyleBackColor = true
        };
        button.Click += (_, _) => click();
        return button;
    }

    static Button Accent(string text, Action click)
    {
        var button = Command(text, click);
        button.BackColor = Color.FromArgb(31, 111, 184);
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    static Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(760, 0),
        Margin = new Padding(0, 8, 0, 0),
        ForeColor = Color.FromArgb(60, 68, 78)
    };

    static int ParseWidth(string label) => int.TryParse(label.Split('×')[0].Trim(), out var value) ? value : 0;
    static int ParseHeight(string label)
    {
        var parts = label.Split('×');
        return parts.Length > 1 && int.TryParse(parts[1].Trim(), out var value) ? value : 0;
    }
}
