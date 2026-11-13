namespace ShiftMon.App;

internal static class AppLog
{
    static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "shiftmon.log");

    public static void Info(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
            lock (Gate)
            {
                var path = FilePath;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 200_000)
                    File.WriteAllText(path, line);
                else
                    File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Diagnostika nesmí shodit aplikaci.
        }
    }
}
