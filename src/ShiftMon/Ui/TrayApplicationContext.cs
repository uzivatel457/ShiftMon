using Microsoft.Win32;
using ShiftMon.App;
using ShiftMon.Display;
using ShiftMon.Profiles;
using ShiftMon.Windows;

namespace ShiftMon.Ui;

internal sealed class TrayApplicationContext : ApplicationContext
{
    readonly AppSettings _settings;
    readonly ProfileStore _store;
    readonly NotifyIcon _tray;
    readonly CaptionButtonManager _buttons;
    readonly SingleInstance _instance;
    MainForm? _form;
    bool _applying;

    public TrayApplicationContext(AppSettings settings, ProfileStore store, SingleInstance instance, string[] args)
    {
        _settings = settings;
        _store = store;
        _instance = instance;
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "ShiftMon"
        };
        try
        {
            _tray.Icon = Icon.ExtractAssociatedIcon(AppPaths.CurrentExecutable) ?? SystemIcons.Application;
        }
        catch
        {
            _tray.Icon = SystemIcons.Application;
        }

        _tray.ContextMenuStrip = BuildMenu();
        _tray.DoubleClick += (_, _) => ShowMain(null);
        _buttons = new CaptionButtonManager(() => _settings, message =>
            _tray.ShowBalloonTip(2500, "ShiftMon", message, ToolTipIcon.Info));
        if (_settings.CaptionButton)
            _buttons.Start();

        _instance.StartServer(HandleCommand, SynchronizationContext.Current!);
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Application.ApplicationExit += (_, _) => Cleanup();

        if (!HasArg(args, "--apply-profile", out _))
        {
            var startup = new System.Windows.Forms.Timer { Interval = 1500 };
            startup.Tick += (_, _) =>
            {
                startup.Stop();
                startup.Dispose();
                OfferReleaseNotes();
                if (_settings.CheckUpdates && SynchronizationContext.Current != null)
                    UpdateService.Schedule(_settings, SynchronizationContext.Current);
            };
            startup.Start();
        }

        if (HasArg(args, "--apply-profile", out var key))
            ApplyByKey(key!);
        else if (!_settings.SeenWelcome || args.Contains("--settings"))
        {
            _settings.SeenWelcome = true;
            _settings.Save();
            ShowMain(null);
        }
    }

    public void ShowMain(string? profileId)
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new MainForm(_store, _settings, ApplyInteractive, Identify, SetButtons, ApplyLanguageChange);
            _form.FormClosed += (_, _) => { if (_form is { IsDisposed: false }) { /* zůstává */ } };
        }

        _form.Show();
        _form.WindowState = FormWindowState.Normal;
        _form.BringToFront();
        _form.FocusProfile(profileId);
        if (!_settings.SeenTrayHint)
        {
            _settings.SeenTrayHint = true;
            _settings.Save();
            _tray.ShowBalloonTip(4000, "ShiftMon", Loc.T("Okno můžete zavřít. Aplikace zůstane v oznamovací oblasti a tlačítko v záhlaví zůstane aktivní.", "You can close the window. The app stays in the notification area and the title-bar button stays active."), ToolTipIcon.Info);
        }
    }

    void HandleCommand(string command)
    {
        if (command.Equals("SHOW", StringComparison.OrdinalIgnoreCase))
        {
            ShowMain(null);
            return;
        }

        if (command.Equals("EXIT", StringComparison.OrdinalIgnoreCase))
        {
            _form?.AllowExit();
            ExitThread();
            return;
        }

        if (command.StartsWith("APPLY\t", StringComparison.OrdinalIgnoreCase))
            ApplyByKey(command[6..]);
    }

    void ApplyByKey(string key)
    {
        var profile = _store.Find(key.Trim().Trim('"'));
        if (profile == null)
        {
            MessageBox.Show(Loc.T($"Profil „{key}“ nebyl nalezen.", $"Profile “{key}” was not found."), "ShiftMon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ApplyInteractive(profile);
    }

    void ApplyInteractive(DisplayProfile profile)
    {
        if (_applying)
            return;
        _applying = true;
        try
        {
            var result = DisplayService.Apply(profile.Monitors);
            if (!result.Success)
            {
                MessageBox.Show(result.Message, "ShiftMon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (result.Unchanged)
            {
                _tray.ShowBalloonTip(2000, "ShiftMon", Loc.T($"Profil „{profile.Name}“ už je aktivní.", $"Profile “{profile.Name}” is already active."), ToolTipIcon.Info);
                return;
            }

            if (result.Previous != null)
                _store.WritePending(result.Previous);

            var keep = ConfirmDisplayForm.Ask(profile.Name);
            if (!keep && result.Previous != null)
            {
                var back = DisplayService.Apply(result.Previous);
                if (!back.Success)
                    MessageBox.Show("Nepodařilo se vrátit původní rozložení.\n" + back.Message, "ShiftMon", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    _tray.ShowBalloonTip(2000, "ShiftMon", Loc.T("Původní rozložení monitorů je zpět.", "The previous monitor layout is back."), ToolTipIcon.Info);
            }
            else
            {
                _tray.ShowBalloonTip(2000, "ShiftMon", Loc.T($"Profil „{profile.Name}“ je aktivní.", $"Profile “{profile.Name}” is active."), ToolTipIcon.Info);
            }

            _store.ClearPending();
        }
        finally
        {
            _applying = false;
            RebuildMenu();
        }
    }

    void Identify()
    {
        var monitors = DisplayService.GetLiveMonitors();
        var forms = new List<Form>();
        foreach (var monitor in monitors)
        {
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Color.Black,
                Bounds = new Rectangle(monitor.X + monitor.Width / 2 - 70, monitor.Y + monitor.Height / 2 - 70, 140, 140)
            };
            form.Controls.Add(new Label
            {
                Text = monitor.Number.ToString(),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 48f, FontStyle.Bold),
                BackColor = Color.Black
            });
            form.Show();
            forms.Add(form);
        }

        var timer = new System.Windows.Forms.Timer { Interval = 3000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            foreach (var form in forms)
                form.Close();
        };
        timer.Start();
    }

    void SetButtons(bool enabled)
    {
        if (enabled)
            _buttons.Start();
        else
            _buttons.Stop();
    }

    void OnDisplayChanged(object? sender, EventArgs e)
    {
        DisplayService.ClearModeCache();
        RebuildMenu();
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RebuildMenu(menu);
        RebuildMenu(menu);
        return menu;
    }

    void RebuildMenu(ContextMenuStrip? menu = null)
    {
        menu ??= _tray.ContextMenuStrip;
        if (menu == null)
            return;
        menu.Items.Clear();
        menu.Items.Add(Loc.T("Otevřít ShiftMon", "Open ShiftMon"), null, (_, _) => ShowMain(null));
        var profiles = new ToolStripMenuItem(Loc.T("Použít profil", "Apply profile"));
        if (_store.Profiles.Count == 0)
            profiles.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Žádný profil", "No profile")) { Enabled = false });
        else
        {
            foreach (var profile in _store.Profiles)
            {
                var item = new ToolStripMenuItem(profile.Name);
                var captured = profile;
                item.Click += (_, _) => ApplyInteractive(captured);
                profiles.DropDownItems.Add(item);
            }
        }
        menu.Items.Add(profiles);
        var button = new ToolStripMenuItem(Loc.T("Tlačítko v záhlaví", "Title-bar button")) { Checked = _settings.CaptionButton, CheckOnClick = true };
        button.CheckedChanged += (_, _) =>
        {
            _settings.CaptionButton = button.Checked;
            _settings.Save();
            SetButtons(button.Checked);
        };
        menu.Items.Add(button);
        menu.Items.Add(Loc.T("Zkontrolovat aktualizace", "Check for updates"), null, (_, _) => _ = UpdateService.CheckNowAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("Ukončit", "Exit"), null, (_, _) =>
        {
            _form?.AllowExit();
            ExitThread();
        });
    }

    void OfferReleaseNotes()
    {
        var seen = _settings.SeenRelease;
        var show = !string.IsNullOrEmpty(seen)
            ? seen != AppPaths.Version
            : _settings.SeenWelcome;
        _settings.SeenRelease = AppPaths.Version;
        _settings.Save();
        if (!show)
            return;
        using var notes = new UpdatePromptForm(AppPaths.Version, ReleaseNotes.Current, offerInstall: false);
        notes.ShowDialog();
    }

    void ApplyLanguageChange()
    {
        var id = _form?.SelectedProfileId;
        if (_form is { IsDisposed: false })
        {
            _form.SaveIfDirty();
            _form.AllowExit();
            var form = _form;
            _form = null;
            form.Close();
        }

        RebuildMenu();
        ShowMain(id);
    }

    void Cleanup()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _buttons.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _instance.Dispose();
    }

    static bool HasArg(string[] args, string name, out string? value)
    {
        value = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 < args.Length)
                value = args[i + 1];
            return value != null;
        }
        return false;
    }
}
