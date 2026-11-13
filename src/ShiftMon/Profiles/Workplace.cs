using ShiftMon.Display;

namespace ShiftMon.Profiles;

internal static class Workplace
{
    public static DisplayProfile? Match(IReadOnlyList<DisplayProfile> profiles, IReadOnlyList<MonitorState> attached)
    {
        var present = Keys(attached.Select(monitor => ProfileLayout.StableKey(monitor.DeviceId, monitor.AdapterName)));
        if (present.Count == 0)
            return null;

        DisplayProfile? found = null;
        foreach (var profile in profiles)
        {
            var saved = Keys(profile.Monitors.Select(monitor => ProfileLayout.StableKey(monitor.DeviceId, monitor.AdapterName)));
            if (saved.Count == 0 || !saved.SetEquals(present))
                continue;
            if (found != null)
                return null;
            found = profile;
        }

        return found;
    }

    public static string Signature(IReadOnlyList<MonitorState> attached)
    {
        var keys = Keys(attached.Select(monitor => ProfileLayout.StableKey(monitor.DeviceId, monitor.AdapterName)));
        return string.Join("|", keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
    }

    static HashSet<string> Keys(IEnumerable<string> values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                set.Add(value);
        }

        return set;
    }
}
