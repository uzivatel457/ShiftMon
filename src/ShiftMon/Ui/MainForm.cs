using ShiftMon.App;
using ShiftMon.Display;
using ShiftMon.Profiles;
using ShiftMon.Windows;

namespace ShiftMon.Ui;

internal sealed class MainForm : Form
{
    readonly ProfileStore _store;
    readonly AppSettings _settings;
    readonly Action<DisplayProfile> _apply;
    readonly Action _identify;
    readonly Action<bool> _buttonsChanged;
    readonly Action _languageChanged;
    readonly Action _workplaceChanged;

    readonly ListBox _profiles = new() { IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    FlowLayoutPanel? _profileButtons;
    readonly MonitorCanvas _canvas = new() { Dock = DockStyle.Top, Height = 210 };
    readonly Label _monitorTitle = new() { AutoSize = false, Height = 28, AutoEllipsis = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    readonly Label _status = new() { AutoSize = false, Height = 52, Dock = DockStyle.Bottom, ForeColor = Theme.Muted, Padding = new Padding(8, 6, 8, 0) };
    readonly ThemedCheckBox _enabled = new();
    readonly ThemedCheckBox _primary = new();
    readonly ComboBox _resolution = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    readonly ComboBox _refresh = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly ComboBox _orientation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    readonly ThemedNumber _posX = new() { Minimum = -20000, Maximum = 20000, Width = 108 };
    readonly ThemedNumber _posY = new() { Minimum = -20000, Maximum = 20000, Width = 108 };
    readonly ThemedCheckBox _caption = new();
    readonly CheckedListBox _hiddenPrograms = new()
    {
        IntegralHeight = false,
        BorderStyle = BorderStyle.FixedSingle,
        CheckOnClick = true,
        Width = 640,
        Height = 150
    };
    bool _loadingHidden;
    readonly ThemedCheckBox _picker = new();
    readonly ThemedCheckBox _startup = new();
    readonly ThemedCheckBox _workplace = new();
    readonly ThemedCheckBox _updates = new();
    readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    TabControl? _tabs;
    bool _loading;
    bool _dirty;
    bool _allowClose;

    public MainForm(ProfileStore store, AppSettings settings, Action<DisplayProfile> apply, Action identify, Action<bool> buttonsChanged, Action languageChanged, Action workplaceChanged)
    {
        _store = store;
        _settings = settings;
        _apply = apply;
        _identify = identify;
        _buttonsChanged = buttonsChanged;
        _languageChanged = languageChanged;
        _workplaceChanged = workplaceChanged;

        Text = "ShiftMon " + AppPaths.Version;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
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
        _workplace.Text = Loc.T("Rozpoznávat pracoviště podle připojených monitorů", "Recognize the workplace from the connected monitors");
        _updates.Text = Loc.T("Hledat nové verze na GitHubu", "Check GitHub for new versions");
        _monitorTitle.Text = Loc.T("Vyberte monitor", "Select a monitor");

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = Theme.Surface,
            Padding = new Padding(16, 10, 16, 12)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var title = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 2),
            BackColor = Theme.Surface,
            Padding = new Padding(0)
        };
        title.Controls.Add(new Label
        {
            Text = "ShiftMon",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 10, 0),
            BackColor = Theme.Surface
        });
        title.Controls.Add(new Label
        {
            Text = AppPaths.Version,
            Font = new Font("Segoe UI", 11f),
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            ForeColor = Theme.Muted,
            BackColor = Theme.Surface
        });
        header.Controls.Add(title);
        header.Controls.Add(new Label
        {
            Text = Loc.T(
                "Tlačítko v záhlaví přesune okno na jiný monitor. Profily přepínají zapnutí a pozici obrazovek.",
                "The title-bar button moves a window to another monitor. Profiles switch which screens are on and where they sit."),
            AutoSize = true,
            MaximumSize = new Size(1040, 0),
            Margin = new Padding(0),
            ForeColor = Theme.Subtitle
        });

        var tabs = new ThemedTabControl { Dock = DockStyle.Fill, BackColor = Theme.Window };
        _tabs = tabs;
        tabs.TabPages.Add(BuildProfilesTab());
        tabs.TabPages.Add(BuildOptionsTab());
        Controls.Add(tabs);
        Theme.StyleTree(this);
        Theme.Attach(this);
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

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var height = _resolution.Height;
        _posX.MatchHeight(height);
        _posY.MatchHeight(height);
        FitProfileButtons();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitProfileButtons();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(FitProfileButtons);
    }

    bool _fittingButtons;

    void FitProfileButtons()
    {
        if (_fittingButtons || _profileButtons == null || _profileButtons.Controls.Count == 0)
            return;
        var bottom = _profileButtons.Padding.Vertical;
        foreach (Control button in _profileButtons.Controls)
            bottom += button.Height + button.Margin.Vertical;
        if (bottom <= 0 || _profileButtons.Height == bottom)
            return;
        _fittingButtons = true;
        _profileButtons.Height = bottom;
        _fittingButtons = false;
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
        var page = new TabPage(Loc.T("Profily", "Profiles")) { Padding = new Padding(10), BackColor = Theme.Window };
        var left = new Panel { Dock = DockStyle.Left, Width = 320, Padding = new Padding(0, 0, 12, 0) };
        _profiles.Dock = DockStyle.Fill;
        _profiles.DisplayMember = nameof(DisplayProfile.Name);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 2)
        };
        buttons.Controls.Add(Accent(Loc.T("Nový z aktuálního rozložení", "New from current layout"), CreateFromCurrent));
        buttons.Controls.Add(Command(Loc.T("Přejmenovat", "Rename"), RenameSelected));
        buttons.Controls.Add(Command(Loc.T("Smazat", "Delete"), DeleteSelected));
        foreach (Control button in buttons.Controls)
            button.SizeChanged += (_, _) => FitProfileButtons();
        _profileButtons = buttons;
        left.Controls.Add(_profiles);
        left.Controls.Add(buttons);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(12) };
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
        position.Controls.Add(new Label { Text = "Y", AutoSize = true, Margin = new Padding(8, 4, 6, 0) });
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
            ForeColor = Theme.Hint
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
        var page = new TabPage(Loc.T("Možnosti", "Options")) { Padding = new Padding(16), BackColor = Theme.Surface };
        _caption.Checked = _settings.CaptionButton;
        _picker.Checked = _settings.PickerWhenMany;
        _startup.Checked = _settings.RunAtStartup;
        _workplace.Checked = _settings.RecognizeWorkplace;
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
        _theme.Items.Add(Loc.T("Automaticky dle Windows", "Match Windows"));
        _theme.Items.Add(Loc.T("Světlý", "Light"));
        _theme.Items.Add(Loc.T("Tmavý", "Dark"));
        _theme.SelectedIndex = _settings.Theme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };
        _theme.SelectedIndexChanged += (_, _) => ThemePicked();
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
        _workplace.CheckedChanged += (_, _) =>
        {
            _settings.RecognizeWorkplace = _workplace.Checked;
            _settings.Save();
            if (_workplace.Checked)
                _workplaceChanged();
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
        var themeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        themeRow.Controls.Add(new Label
        {
            Text = Loc.T("Vzhled", "Appearance"),
            AutoSize = true,
            Padding = new Padding(0, 6, 12, 0)
        });
        themeRow.Controls.Add(_theme);
        layout.Controls.Add(themeRow);
        layout.Controls.Add(_caption);
        layout.Controls.Add(BuildHiddenPrograms());
        layout.Controls.Add(_picker);
        layout.Controls.Add(_startup);
        layout.Controls.Add(_workplace);
        layout.Controls.Add(Note(Loc.T(
            "Když se připojené monitory shodují s jedním uloženým profilem, ShiftMon ho nabídne. V práci tedy profil s tamními obrazovkami, doma profil s domácí obrazovkou. Dva profily se stejnými monitory se samy nepřepnou.",
            "When the connected monitors match one saved profile, ShiftMon offers it. At work that is the profile with the work screens, at home the profile with the home screen. Two profiles that use the same monitors are not switched automatically.")));
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
            "Zástupce na ploše spustí uložený profil. Jeho ikona ukazuje jen zapnuté obrazovky v jejich rozložení.",
            "A desktop shortcut applies a saved profile. Its icon shows only the screens that are on, in their layout.")));
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

    Control BuildHiddenPrograms()
    {
        var panel = new Panel { Width = 680, Height = 230, Margin = new Padding(0, 6, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = Loc.T("Tlačítko se nezobrazí u těchto programů", "Do not show the button for these programs"),
            AutoSize = true,
            Location = new Point(0, 0)
        });
        _hiddenPrograms.Location = new Point(0, 24);
        _hiddenPrograms.ItemCheck += HiddenProgramChecked;
        ReloadHiddenPrograms();
        panel.Controls.Add(_hiddenPrograms);
        var row = new FlowLayoutPanel
        {
            Location = new Point(0, 180),
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty
        };
        row.Controls.Add(Command(Loc.T("Přidat program", "Add a program"), AddProgramFile));
        panel.Controls.Add(row);
        return panel;
    }

    void ReloadHiddenPrograms()
    {
        var selected = (_hiddenPrograms.SelectedItem as HiddenProgram)?.Executable;
        _loadingHidden = true;
        _hiddenPrograms.BeginUpdate();
        _hiddenPrograms.Items.Clear();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in _settings.ButtonHiddenPrograms.OrderBy(program => program.ToString(), StringComparer.CurrentCultureIgnoreCase))
        {
            if (!listed.Add(program.Executable))
                continue;
            _hiddenPrograms.Items.Add(program, true);
        }

        foreach (var running in WindowEligibility.RunningPrograms((uint)Environment.ProcessId, null))
        {
            if (!listed.Add(running.Executable))
                continue;
            _hiddenPrograms.Items.Add(new HiddenProgram { Executable = running.Executable, Name = running.Name }, false);
        }

        _hiddenPrograms.EndUpdate();
        _loadingHidden = false;
        if (!string.IsNullOrEmpty(selected))
            SelectHidden(selected);
    }

    void HiddenProgramChecked(object? sender, ItemCheckEventArgs e)
    {
        if (_loadingHidden || _hiddenPrograms.Items[e.Index] is not HiddenProgram program)
            return;
        var hide = e.NewValue == CheckState.Checked;
        BeginInvoke(() => SetButtonHidden(program, hide));
    }

    void SetButtonHidden(HiddenProgram program, bool hide)
    {
        var fileName = Path.GetFileName(program.Executable).Trim();
        if (fileName.Length == 0)
            return;
        var exists = _settings.HidesButtonFor(fileName);
        if (hide && !exists)
            _settings.ButtonHiddenPrograms.Add(new HiddenProgram { Executable = fileName, Name = program.Name });
        else if (!hide && exists)
            _settings.ButtonHiddenPrograms.RemoveAll(item => item.Executable.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        else
            return;
        _settings.Save();
    }

    void AddProgramFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = Loc.T("Vyberte program", "Choose a program"),
            Filter = Loc.T("Programy (*.exe)|*.exe|Všechny soubory (*.*)|*.*", "Programs (*.exe)|*.exe|All files (*.*)|*.*"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        RememberHidden(Path.GetFileName(dialog.FileName), WindowEligibility.FriendlyName(dialog.FileName));
    }

    void RememberHidden(string executable, string name)
    {
        var fileName = Path.GetFileName(executable).Trim();
        if (fileName.Length == 0)
            return;
        if (_settings.HidesButtonFor(fileName))
        {
            ReloadHiddenPrograms();
            SelectHidden(fileName);
            return;
        }

        _settings.ButtonHiddenPrograms.Add(new HiddenProgram { Executable = fileName, Name = name.Trim() });
        _settings.Save();
        ReloadHiddenPrograms();
        SelectHidden(fileName);
    }

    void SelectHidden(string fileName)
    {
        var added = _hiddenPrograms.Items.Cast<HiddenProgram>().FirstOrDefault(program =>
            program.Executable.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (added != null)
            _hiddenPrograms.SelectedItem = added;
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
        if (!string.IsNullOrWhiteSpace(profile.ShortcutPath) && File.Exists(profile.ShortcutPath))
        {
            try
            {
                ShortcutService.Create(
                    profile.ShortcutPath,
                    AppPaths.CurrentExecutable,
                    "--apply-profile \"" + profile.Id + "\"",
                    Loc.T("ShiftMon profil: ", "ShiftMon profile: ") + profile.Name);
            }
            catch (Exception ex)
            {
                AppLog.Info("Ikona zástupce: " + ex.Message);
            }
        }
        _store.Profiles.Remove(profile);
        _store.Save();
        ProfileIcon.Delete(profile.Id);
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
        try
        {
            ShortcutService.UpdateProfileShortcut(profile);
        }
        catch (Exception ex)
        {
            AppLog.Info("Ikona zástupce: " + ex.Message);
        }
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
            ShortcutService.Create(
                path,
                AppPaths.CurrentExecutable,
                "--apply-profile \"" + profile.Id + "\"",
                Loc.T("ShiftMon profil: ", "ShiftMon profile: ") + profile.Name,
                ProfileIcon.Write(profile));
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
        _posX.Editable = has && monitor!.Enabled && !monitor.Primary;
        _posY.Editable = has && monitor!.Enabled && !monitor.Primary;
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

    void ThemePicked()
    {
        if (_loading)
            return;
        var choice = _theme.SelectedIndex switch
        {
            1 => "light",
            2 => "dark",
            _ => "auto"
        };
        if (string.Equals(choice, _settings.Theme, StringComparison.OrdinalIgnoreCase))
            return;
        _settings.Theme = choice;
        _settings.Save();
        if (_dirty)
            SaveSelected(announce: false);
        Theme.Use(choice);
        BeginInvoke(_languageChanged);
    }

    public int SelectedTabIndex => _tabs?.SelectedIndex ?? 0;

    public void SelectTab(int index)
    {
        if (_tabs == null || index < 0 || index >= _tabs.TabCount)
            return;
        _tabs.SelectedIndex = index;
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
            Font = Font,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text
        };
        Theme.Attach(form);
        var text = new TextBox { Text = initial, Bounds = new Rectangle(16, 40, 348, 24) };
        form.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(16, 16) });
        form.Controls.Add(text);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(196, 82, 80, 30) };
        var cancel = new Button { Text = Loc.T("Zrušit", "Cancel"), DialogResult = DialogResult.Cancel, Bounds = new Rectangle(284, 82, 80, 30) };
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Theme.StyleTree(form);
        return form.ShowDialog(this) == DialogResult.OK ? text.Text : null;
    }

    static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, row == 5 ? 40 : 36));
        if (label.Length > 0)
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Top;
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
        button.BackColor = Theme.Accent;
        button.ForeColor = Theme.AccentText;
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
        ForeColor = Theme.Note
    };

    static int ParseWidth(string label) => int.TryParse(label.Split('×')[0].Trim(), out var value) ? value : 0;
    static int ParseHeight(string label)
    {
        var parts = label.Split('×');
        return parts.Length > 1 && int.TryParse(parts[1].Trim(), out var value) ? value : 0;
    }
}
