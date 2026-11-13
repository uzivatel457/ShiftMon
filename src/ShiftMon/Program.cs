using System.Runtime.InteropServices;
using System.Text;
using ShiftMon.App;
using ShiftMon.Display;
using ShiftMon.Native;
using ShiftMon.Profiles;
using ShiftMon.Ui;
using ShiftMon.Windows;

namespace ShiftMon;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Any(a => a.Equals("--dump", StringComparison.OrdinalIgnoreCase)))
        {
            Report("shiftmon-dump.txt", DisplayService.Dump() + DumpWindows());
            return;
        }

        if (args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(SelfTest());
            return;
        }

        if (args.Any(a => a.Equals("--self-test-displays", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(DisplaySwitchTest());
            return;
        }

        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            SingleInstance.TrySend("EXIT");
            Thread.Sleep(700);
            InstallService.Uninstall();
            InstallService.ScheduleDeleteInstallDirectory();
            return;
        }

        if (!InstallService.IsInstalled || args.Any(a => a.Equals("--install", StringComparison.OrdinalIgnoreCase)))
        {
            var installed = Path.Combine(AppPaths.InstallDirectory, "ShiftMon.exe");
            var launchingCopy = !string.Equals(Path.GetFullPath(installed), Path.GetFullPath(AppPaths.CurrentExecutable), StringComparison.OrdinalIgnoreCase);
            if (launchingCopy)
            {
                InstallService.WaitForOtherInstances();
                InstallService.Install();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installed) { UseShellExecute = true });
                return;
            }

            InstallService.EnsureRegistered();
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            AppLog.Info("Chyba vlákna: " + e.Exception);
            MessageBox.Show(Loc.T("ShiftMon narazil na chybu.\n", "ShiftMon hit an error.\n") + e.Exception.Message, "ShiftMon", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        using var instance = SingleInstance.Acquire();
        if (!instance.IsPrimary)
        {
            var command = "SHOW";
            if (TakeArg(args, "--apply-profile", out var profile))
                command = "APPLY\t" + profile;
            if (!SingleInstance.TrySend(command))
                AppLog.Info("Běžící ShiftMon nepřijal příkaz: " + command);
            return;
        }

        if (InstallService.IsInstalled)
            InstallService.EnsureRegistered();
        var firstRun = !File.Exists(AppPaths.SettingsFile);
        var settings = AppSettings.Load();
        Loc.Use(settings.Language);
        Theme.Use(settings.Theme);
        if (firstRun && InstallService.IsInstalled)
        {
            settings.RunAtStartup = true;
            settings.Save();
        }
        if (settings.RunAtStartup)
            StartupRegistration.Apply(true, AppPaths.CurrentExecutable);

        var store = new ProfileStore();
        store.Load();
        store.EnsureReady();
        foreach (var profile in store.Profiles)
        {
            try
            {
                ShortcutService.UpdateProfileShortcut(profile);
            }
            catch (Exception ex)
            {
                AppLog.Info("Ikona zástupce: " + ex.Message);
            }
        }
        OfferPendingRevert(store);
        if (SynchronizationContext.Current == null)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.Run(new TrayApplicationContext(settings, store, instance, args));
    }

    static void OfferPendingRevert(ProfileStore store)
    {
        var pending = store.ReadPending();
        if (pending == null)
            return;
        var answer = MessageBox.Show(
            Loc.T("Předchozí změna monitorů nebyla potvrzena. Vrátit původní rozložení?", "The previous monitor change was not confirmed. Restore the original layout?"),
            "ShiftMon",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
        {
            var result = DisplayService.Apply(pending.Monitors);
            if (!result.Success)
                MessageBox.Show(result.Message, "ShiftMon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        store.ClearPending();
    }

    static int SelfTest()
    {
        var report = new StringBuilder();
        var failed = 0;
        void Check(string name, bool ok)
        {
            report.AppendLine((ok ? "OK  " : "FAIL") + " " + name);
            if (!ok)
                failed++;
        }

        Check("DEVMODE má 220 bajtů", Marshal.SizeOf<NativeMethods.DEVMODE>() == 220);
        Check("DISPLAY_DEVICE má 840 bajtů", Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() == 840);
        var moved = WindowMoveMath.MoveToMonitor(
            new WindowMoveMath.PixelRect(100, 80, 800, 600),
            new WindowMoveMath.PixelRect(0, 0, 1920, 1040),
            new WindowMoveMath.PixelRect(1920, 0, 1920, 1040));
        Check("přesun zachová relativní polohu", moved.X == 2020 && moved.Y == 80 && moved.Width == 800 && moved.Height == 600);
        var clamped = WindowMoveMath.MoveToMonitor(
            new WindowMoveMath.PixelRect(1500, 900, 800, 600),
            new WindowMoveMath.PixelRect(0, 0, 1920, 1080),
            new WindowMoveMath.PixelRect(1920, 0, 1280, 720));
        Check("přesun se vejde do cílového monitoru", clamped.X >= 1920 && clamped.Y >= 0 && clamped.Right <= 3200 && clamped.Bottom <= 720);

        var left = new MonitorProfileEntry { Enabled = true, Primary = false, X = -1920, Y = 0, Width = 1920, Height = 1080 };
        var right = new MonitorProfileEntry { Enabled = true, Primary = true, X = 0, Y = 0, Width = 1920, Height = 1080 };
        ProfileLayout.MakePrimary([left, right], left);
        Check("nový primární monitor je v počátku", left.Primary && left.X == 0 && left.Y == 0 && right.X == 1920);
        Check("dotyk hran není překryv", !ProfileLayout.Overlaps(left, right));
        right.X = 100;
        Check("překryv se pozná", ProfileLayout.Overlaps(left, right));
        var ghost = ProfileLayout.Dedupe(
        [
            new MonitorProfileEntry { AdapterName = @"\\.\DISPLAY1", DeviceId = @"\\?\DISPLAY#CMN#uid", Enabled = false },
            new MonitorProfileEntry { AdapterName = @"\\.\DISPLAY4", DeviceId = @"\\?\DISPLAY#CMN#uid", Enabled = false }
        ]);
        Check("stejný displej se nepočítá dvakrát", ghost.Count == 1 && ghost[0].AdapterName.EndsWith("DISPLAY1", StringComparison.OrdinalIgnoreCase));
        static MonitorState MonitorFor(string id) => new() { DeviceId = id, AdapterName = id };
        static MonitorProfileEntry EntryFor(string id) => new() { DeviceId = id, AdapterName = id, Enabled = true };
        var laptop = MonitorFor("laptop");
        var workA = MonitorFor("dell-a");
        var workB = MonitorFor("dell-b");
        var home = MonitorFor("home");
        var workProfile = new DisplayProfile { Name = "work", Monitors = [EntryFor("laptop"), EntryFor("dell-a"), EntryFor("dell-b")] };
        var homeProfile = new DisplayProfile { Name = "home", Monitors = [EntryFor("laptop"), EntryFor("home")] };
        var known = new List<DisplayProfile> { workProfile, homeProfile };
        Check("v práci se pozná profil práce", Workplace.Match(known, [laptop, workB, workA]) == workProfile);
        Check("doma se pozná profil domova", Workplace.Match(known, [home, laptop]) == homeProfile);
        Check("dva profily se stejnými monitory se neuhádnou", Workplace.Match(
            [workProfile, new DisplayProfile { Name = "work-2", Monitors = [EntryFor("laptop"), EntryFor("dell-a"), EntryFor("dell-b")] }],
            [laptop, workA, workB]) == null);
        Check("neznámá obrazovka nic nespustí", Workplace.Match(known, [MonitorFor("other")]) == null);

        try
        {
            report.AppendLine(DisplayService.Dump());
            Check("výpis monitorů", true);
        }
        catch (Exception ex)
        {
            report.AppendLine(ex.ToString());
            Check("výpis monitorů", false);
        }

        Report("shiftmon-selftest.txt", report.ToString());
        return failed == 0 ? 0 : 1;
    }

    static int DisplaySwitchTest()
    {
        var report = new StringBuilder();
        var current = DisplayService.Query();
        var enabled = current.Where(m => m.Enabled).ToList();
        report.AppendLine("Zapnuto: " + enabled.Count);
        if (enabled.Count < 2)
        {
            report.AppendLine("Test přepnutí přeskočen, je zapnutý jen jeden monitor.");
            Report("shiftmon-displaytest.txt", report.ToString());
            return 0;
        }

        var snapshot = current.Select(ProfileLayout.FromState).ToList();
        var secondary = snapshot.First(m => m.Enabled && !m.Primary);
        var changed = snapshot.Select(m => m.Clone()).ToList();
        changed.First(m => m.DeviceId == secondary.DeviceId).Enabled = false;
        var code = 1;
        try
        {
            var result = DisplayService.Apply(changed);
            report.AppendLine(result.Success ? "Vypnutí druhého monitoru prošlo: " + result.Message : "SELHALO: " + result.Message);
            Thread.Sleep(1500);
            var after = DisplayService.Query();
            var still = after.FirstOrDefault(m => m.DeviceId == secondary.DeviceId);
            var ok = result.Success && still is { Enabled: false } && after.Count(m => m.Enabled) == enabled.Count - 1;
            report.AppendLine(ok ? "Druhý monitor je vypnutý." : "Stav po vypnutí nesedí: " + (still == null ? "monitor zmizel" : still.Enabled.ToString()));
            code = ok ? 0 : 1;
        }
        finally
        {
            var back = DisplayService.Apply(snapshot);
            report.AppendLine(back.Success ? "Původní rozložení je zpět." : "NÁVRAT SELHAL: " + back.Message);
            Thread.Sleep(600);
            var restored = DisplayService.Query();
            var backOn = restored.FirstOrDefault(m => m.DeviceId == secondary.DeviceId);
            var restoredOk = back.Success && backOn is { Enabled: true };
            report.AppendLine(restoredOk ? "Druhý monitor je znovu zapnutý." : "Druhý monitor zůstal vypnutý.");
            if (!restoredOk)
                code = 1;
            Report("shiftmon-displaytest.txt", report.ToString());
        }

        return code;
    }

    static string DumpWindows()
    {
        var sb = new StringBuilder();
        var pid = (uint)Environment.ProcessId;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!WindowEligibility.IsEligible(hwnd, pid))
                return true;
            var has = WindowEligibility.TryGetButtonRect(hwnd, out var rect);
            sb.AppendLine($"{WindowEligibility.ClassName(hwnd)} | {WindowEligibility.Title(hwnd)} | button={(has ? $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height}" : "ne")}");
            return true;
        }, IntPtr.Zero);
        return sb.ToString();
    }

    static void Report(string fileName, string text)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllText(path, text);
        try
        {
            if (NativeMethods.AttachConsole(NativeMethods.AttachParentProcess))
                Console.WriteLine(text);
        }
        catch
        {
            // WinExe nemusí mít konzoli.
        }
    }

    static bool TakeArg(string[] args, string name, out string value)
    {
        value = "";
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) || i + 1 >= args.Length)
                continue;
            value = args[i + 1].Trim('"');
            return true;
        }
        return false;
    }
}
