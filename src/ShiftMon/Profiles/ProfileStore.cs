using System.Text.Json;
using ShiftMon.App;
using ShiftMon.Display;

namespace ShiftMon.Profiles;

internal sealed class ProfileStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public List<DisplayProfile> Profiles { get; private set; } = [];

    public void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ProfilesFile))
            {
                Profiles = [];
                return;
            }

            var file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(AppPaths.ProfilesFile), JsonOptions);
            Profiles = file?.Profiles ?? [];
        }
        catch (Exception ex)
        {
            AppLog.Info("Profil se nepodařilo načíst: " + ex.Message);
            Profiles = [];
        }
    }

    public void EnsureReady()
    {
        var changed = false;
        foreach (var profile in Profiles)
        {
            var unique = ProfileLayout.Dedupe(profile.Monitors);
            if (unique.Count != profile.Monitors.Count)
            {
                profile.Monitors = unique;
                changed = true;
            }
        }

        if (Profiles.Count == 0)
        {
            Profiles.Add(new DisplayProfile
            {
                Name = Loc.T("Profil 1", "Profile 1"),
                Monitors = DisplayService.Query().Select(ProfileLayout.FromState).ToList()
            });
            changed = true;
        }

        if (changed)
            Save();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var file = new ProfileFile { Version = 1, Profiles = Profiles };
        File.WriteAllText(AppPaths.ProfilesFile, JsonSerializer.Serialize(file, JsonOptions));
    }

    public DisplayProfile? Find(string key)
    {
        var byId = Profiles.FirstOrDefault(p => string.Equals(p.Id, key, StringComparison.OrdinalIgnoreCase));
        if (byId != null)
            return byId;

        var byName = Profiles.Where(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    public void WritePending(IReadOnlyList<MonitorProfileEntry> monitors)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var profile = new DisplayProfile
        {
            Id = "pending",
            Name = "Předchozí rozložení",
            Monitors = monitors.Select(m => m.Clone()).ToList()
        };
        File.WriteAllText(AppPaths.PendingRevertFile, JsonSerializer.Serialize(profile, JsonOptions));
    }

    public DisplayProfile? ReadPending()
    {
        try
        {
            if (!File.Exists(AppPaths.PendingRevertFile))
                return null;
            return JsonSerializer.Deserialize<DisplayProfile>(File.ReadAllText(AppPaths.PendingRevertFile), JsonOptions);
        }
        catch (Exception ex)
        {
            AppLog.Info("Pending revert je poškozený: " + ex.Message);
            return null;
        }
    }

    public void ClearPending()
    {
        try
        {
            if (File.Exists(AppPaths.PendingRevertFile))
                File.Delete(AppPaths.PendingRevertFile);
        }
        catch (Exception ex)
        {
            AppLog.Info("Pending revert nešel smazat: " + ex.Message);
        }
    }

    sealed class ProfileFile
    {
        public int Version { get; set; }
        public List<DisplayProfile> Profiles { get; set; } = [];
    }
}
