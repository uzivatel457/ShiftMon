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
    public string Theme { get; set; } = "auto";
    public bool CheckUpdates { get; set; } = true;
    public bool RecognizeWorkplace { get; set; } = true;
    public string LastUpdateCheck { get; set; } = "";
    public string SeenRelease { get; set; } = "";
    public List<HiddenProgram> ButtonHiddenPrograms
    {
        get => _hidden;
        set => _hidden = value ?? [];
    }

    List<HiddenProgram> _hidden = [];

    public HashSet<string> HiddenExecutables()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in _hidden)
        {
            var name = Path.GetFileName(program.Executable).Trim();
            if (name.Length > 0)
                set.Add(name);
        }
        return set;
    }

    public bool HidesButtonFor(string executable)
    {
        var name = Path.GetFileName(executable).Trim();
        return name.Length > 0 && _hidden.Any(program => program.Executable.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

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

internal sealed class HiddenProgram
{
    public string Executable { get; set; } = "";
    public string Name { get; set; } = "";

    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Equals(Executable, StringComparison.OrdinalIgnoreCase))
            return Executable;
        return Name + " (" + Executable + ")";
    }
}
