using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using ShiftMon.Profiles;

namespace ShiftMon.App;

internal static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled, string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key == null)
            return;

        if (enabled)
            key.SetValue(AppPaths.AppName, $"\"{executable}\"");
        else
            key.DeleteValue(AppPaths.AppName, throwOnMissingValue: false);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(AppPaths.AppName) is string;
    }
}

internal static class ShortcutService
{
    public static void Create(string shortcutPath, string target, string arguments, string description, string? iconPath = null)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("Nelze vytvořit zástupce (WScript.Shell).");
        dynamic shell = Activator.CreateInstance(shellType)
                        ?? throw new InvalidOperationException("Nelze vytvořit zástupce.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = target;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
        shortcut.Description = description;
        shortcut.IconLocation = string.IsNullOrWhiteSpace(iconPath) ? target + ",0" : iconPath + ",0";
        shortcut.Save();
        NotifyChanged(shortcutPath);
    }

    public static void UpdateProfileShortcut(DisplayProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ShortcutPath) || !File.Exists(profile.ShortcutPath))
            return;
        var icon = ProfileIcon.Write(profile);
        Create(
            profile.ShortcutPath,
            AppPaths.CurrentExecutable,
            "--apply-profile \"" + profile.Id + "\"",
            Loc.T("ShiftMon profil: ", "ShiftMon profile: ") + profile.Name,
            icon);
    }

    public static void NotifyChanged(string path)
    {
        try
        {
            SHChangeNotify(0x00002000, 0x0005, path, IntPtr.Zero);
        }
        catch
        {
            // Plocha si ikonu načte nejpozději při dalším zobrazení.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern void SHChangeNotify(uint eventId, uint flags, string item, IntPtr item2);

    public static string? TargetOf(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            return (string?)shortcut.TargetPath;
        }
        catch
        {
            return null;
        }
    }

    public static string DesktopShortcut(string profileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(profileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = "ShiftMon profil";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), safe + ".lnk");
    }
}

internal static class InstallService
{
    public static bool IsInstalled =>
        string.Equals(
            Path.GetFullPath(AppPaths.CurrentExecutable),
            Path.GetFullPath(Path.Combine(AppPaths.InstallDirectory, "ShiftMon.exe")),
            StringComparison.OrdinalIgnoreCase);

    public static void WaitForOtherInstances()
    {
        if (OtherCount() > 0)
            SingleInstance.TrySend("EXIT");
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline && OtherCount() > 0)
            Thread.Sleep(300);
    }

    public static void Install()
    {
        var source = AppPaths.CurrentExecutable;
        Directory.CreateDirectory(AppPaths.InstallDirectory);
        var target = Path.Combine(AppPaths.InstallDirectory, "ShiftMon.exe");
        var sourceDir = Path.GetDirectoryName(source)!;
        if (!string.Equals(Path.GetFullPath(sourceDir), Path.GetFullPath(AppPaths.InstallDirectory), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, target, overwrite: true);
            var keepRuntime = string.Equals(Path.GetFileName(source), "ShiftMon.exe", StringComparison.OrdinalIgnoreCase)
                              && File.Exists(Path.Combine(sourceDir, "ShiftMon.dll"));
            if (keepRuntime)
            {
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    var name = Path.GetFileName(file);
                    if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                        File.Copy(file, Path.Combine(AppPaths.InstallDirectory, name), overwrite: true);
                }

                var runtimes = Path.Combine(sourceDir, "runtimes");
                if (Directory.Exists(runtimes))
                    CopyDirectory(runtimes, Path.Combine(AppPaths.InstallDirectory, "runtimes"));
            }

            RemoveUnrelatedFiles(keepRuntime);
        }

        EnsureRegistered();
        var settings = AppSettings.Load();
        settings.RunAtStartup = true;
        settings.Save();
        StartupRegistration.Apply(true, target);
        AppLog.Info("Instalace dokončena do " + AppPaths.InstallDirectory);
    }

    public static void EnsureRegistered()
    {
        var target = Path.Combine(AppPaths.InstallDirectory, "ShiftMon.exe");
        if (!File.Exists(target))
            return;
        CreateStartMenuShortcut(target);
        WriteUninstallKey(target);
    }

    static void RemoveUnrelatedFiles(bool keepRuntime)
    {
        foreach (var file in Directory.GetFiles(AppPaths.InstallDirectory))
        {
            var name = Path.GetFileName(file);
            var keep = name.Equals("ShiftMon.exe", StringComparison.OrdinalIgnoreCase)
                       || (keepRuntime && (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                           || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                                           || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)));
            if (!keep)
                TryDelete(file);
        }

        foreach (var directory in Directory.GetDirectories(AppPaths.InstallDirectory))
        {
            if (!Path.GetFileName(directory).Equals("runtimes", StringComparison.OrdinalIgnoreCase))
                TryDeleteDirectory(directory);
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { AppLog.Info("Soubor zůstal: " + ex.Message); }
    }

    static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception ex) { AppLog.Info("Složka zůstala: " + ex.Message); }
    }

    static int OtherCount()
    {
        var me = Environment.ProcessId;
        var list = Process.GetProcessesByName("ShiftMon");
        try
        {
            return list.Count(process => process.Id != me);
        }
        finally
        {
            foreach (var process in list)
                process.Dispose();
        }
    }

    public static void Uninstall()
    {
        StartupRegistration.Apply(false, AppPaths.CurrentExecutable);
        RemoveShortcuts();
        try
        {
            using var parent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
            parent?.DeleteSubKeyTree(AppPaths.AppName, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            AppLog.Info("Uninstall klíč: " + ex.Message);
        }
    }

    public static void ScheduleDeleteInstallDirectory()
    {
        if (!IsInstalled)
            return;

        var directory = AppPaths.InstallDirectory;
        var script = Path.Combine(Path.GetTempPath(), "shiftmon-uninstall.cmd");
        File.WriteAllText(script, "@echo off\r\ntimeout /t 2 /nobreak >nul\r\nrmdir /s /q \"" + directory + "\"\r\ndel \"%~f0\"\r\n", Encoding.ASCII);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = script,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        });
    }

    public static void CreateStartMenuShortcut(string executable)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        Directory.CreateDirectory(programs);
        ShortcutService.Create(
            Path.Combine(programs, "ShiftMon.lnk"),
            executable,
            "",
            "ShiftMon — tlačítko v záhlaví a profily monitorů");
    }

    static void WriteUninstallKey(string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppPaths.AppName);
        key.SetValue("DisplayName", "ShiftMon");
        key.SetValue("DisplayVersion", AppPaths.Version);
        key.SetValue("Publisher", "ShiftMon");
        key.SetValue("InstallLocation", AppPaths.InstallDirectory);
        key.SetValue("DisplayIcon", executable);
        key.SetValue("UninstallString", $"\"{executable}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{executable}\" --uninstall");
        key.SetValue("URLInfoAbout", AppPaths.GitHubReleases);
        key.SetValue("HelpLink", AppPaths.GitHubReleases);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        long bytes = 0;
        if (Directory.Exists(AppPaths.InstallDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.InstallDirectory, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(file).Length;
        }
        key.SetValue("EstimatedSize", (int)Math.Max(1, bytes / 1024), RegistryValueKind.DWord);
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(destination, name), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    static void RemoveShortcuts()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup)
        };
        var exeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ShiftMon.exe" };
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var link in Directory.GetFiles(folder, "*.lnk"))
            {
                var target = ShortcutService.TargetOf(link);
                if (target != null && exeNames.Contains(Path.GetFileName(target)))
                {
                    try { File.Delete(link); } catch { /* zástupce může být zamčený */ }
                }
            }
        }
    }
}
