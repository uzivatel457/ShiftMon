using ShiftMon.Native;

namespace ShiftMon.Profiles;

internal static class ProfileLayout
{
    public static List<MonitorProfileEntry> Normalize(IReadOnlyList<MonitorProfileEntry> desired)
    {
        var list = desired.Select(m => m.Clone()).ToList();
        var enabled = list.Where(m => m.Enabled).ToList();
        if (enabled.Count == 0)
            return list;

        var primary = enabled.FirstOrDefault(m => m.Primary)
                      ?? enabled.OrderBy(m => m.X).ThenBy(m => m.Y).First();
        foreach (var monitor in list)
            monitor.Primary = ReferenceEquals(monitor, primary);

        var dx = primary.X;
        var dy = primary.Y;
        foreach (var monitor in enabled)
        {
            monitor.X -= dx;
            monitor.Y -= dy;
        }

        return list;
    }

    public static bool HasOverlap(IReadOnlyList<MonitorProfileEntry> enabled)
    {
        for (var i = 0; i < enabled.Count; i++)
        {
            for (var j = i + 1; j < enabled.Count; j++)
            {
                if (Overlaps(enabled[i], enabled[j]))
                    return true;
            }
        }

        return false;
    }

    public static bool Overlaps(MonitorProfileEntry a, MonitorProfileEntry b)
    {
        return a.X < b.X + b.Width
               && b.X < a.X + a.Width
               && a.Y < b.Y + b.Height
               && b.Y < a.Y + a.Height;
    }

    public static void MakePrimary(IList<MonitorProfileEntry> monitors, MonitorProfileEntry primary)
    {
        foreach (var monitor in monitors)
            monitor.Primary = ReferenceEquals(monitor, primary);

        if (!primary.Enabled)
            return;

        var dx = primary.X;
        var dy = primary.Y;
        foreach (var monitor in monitors.Where(m => m.Enabled))
        {
            monitor.X -= dx;
            monitor.Y -= dy;
        }
    }

    public static void PlaceToTheRight(MonitorProfileEntry item, IReadOnlyList<MonitorProfileEntry> all)
    {
        var others = all.Where(m => m.Enabled && !ReferenceEquals(m, item)).ToList();
        if (others.Count == 0)
        {
            item.X = 0;
            item.Y = 0;
            item.Primary = true;
            return;
        }

        item.X = others.Max(m => m.X + m.Width);
        item.Y = others.First(m => m.Primary).Y;
        item.Primary = false;
    }

    public static List<MonitorProfileEntry> Dedupe(IReadOnlyList<MonitorProfileEntry> monitors)
    {
        return monitors
            .GroupBy(StableKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(monitor => monitor.Enabled)
                .ThenBy(monitor => monitor.AdapterName, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToList();
    }

    public static string StableKey(string? deviceId, string? adapterName)
    {
        if (!string.IsNullOrWhiteSpace(deviceId) &&
            !deviceId.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
            return deviceId;
        return adapterName ?? "";
    }

    static string StableKey(MonitorProfileEntry monitor) => StableKey(monitor.DeviceId, monitor.AdapterName);

    public static MonitorProfileEntry FromState(MonitorState state) => new()
    {
        DeviceId = state.DeviceId,
        AdapterName = state.AdapterName,
        FriendlyName = state.FriendlyName,
        Enabled = state.Enabled,
        Primary = state.Primary,
        X = state.X,
        Y = state.Y,
        Width = state.Width,
        Height = state.Height,
        Frequency = state.Frequency,
        Orientation = state.Orientation,
        BitsPerPixel = state.BitsPerPixel <= 0 ? 32 : state.BitsPerPixel
    };
}
