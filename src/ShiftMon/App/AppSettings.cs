using System.Text.Json;
using ShiftMon.Profiles;

namespace ShiftMon.App;

internal sealed class AppSettings
{
    public bool CaptionButton { get; set; } = true;
    public bool PickerWhenMany { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public bool SeenTrayHint { get; set; }
    public bool SeenWelcome { get; set; }
    public string Language { get; set; } = "auto";
    public bool CheckUpdates { get; set; } = true;
    public string LastUpdateCheck { get; set; } = "";
    public string SeenRelease { get; set; } = "";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
                return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Info("Nastavení se nepodařilo načíst: " + ex.Message);
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
    }
}
