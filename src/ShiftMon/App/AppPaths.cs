namespace ShiftMon.App;

internal static class AppPaths
{
    public const string AppName = "ShiftMon";
    public const string Version = "1.1.0";
    public const string GitHubOwner = "uzivatel457";
    public const string GitHubRepo = "ShiftMon";
    public const string SetupFileName = "ShiftMon-Setup.exe";

    public static string GitHubReleases =>
        $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string ProfilesFile => Path.Combine(DataDirectory, "profiles.json");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string PendingRevertFile => Path.Combine(DataDirectory, "pending-revert.json");

    public static string CurrentExecutable =>
        Environment.ProcessPath ?? Application.ExecutablePath;
}
