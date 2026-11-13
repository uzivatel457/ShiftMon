using System.Runtime.InteropServices;
using System.Text;
using ShiftMon.App;
using ShiftMon.Native;
using ShiftMon.Profiles;

namespace ShiftMon.Display;

internal sealed class ApplyResult
{
    public bool Success { get; init; }
    public bool Unchanged { get; init; }
    public string Message { get; init; } = "";
    public List<MonitorProfileEntry>? Previous { get; init; }

    public static ApplyResult Ok(List<MonitorProfileEntry> previous, string message) =>
        new() { Success = true, Previous = previous, Message = message };

    public static ApplyResult NoChange(string message) =>
        new() { Success = true, Unchanged = true, Message = message };

    public static ApplyResult Fail(string message) =>
        new() { Success = false, Message = message };
}

internal static class DisplayService
{
    static readonly Dictionary<string, List<DisplayMode>> ModeCache = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearModeCache() => ModeCache.Clear();

    public static List<MonitorState> Query()
    {
        var result = new List<MonitorState>();
        for (uint i = 0; i < 64; i++)
        {
            var adapter = NativeMethods.NewDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0))
                break;

            if ((adapter.StateFlags & NativeMethods.DisplayDeviceMirroringDriver) != 0)
                continue;
            if (string.IsNullOrWhiteSpace(adapter.DeviceName))
                continue;

            var monitor = NativeMethods.NewDisplayDevice();
            var hasMonitor = NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0);
            var enabled = (adapter.StateFlags & NativeMethods.DisplayDeviceAttachedToDesktop) != 0;
            if (!enabled && !hasMonitor)
                continue;

            var iface = NativeMethods.NewDisplayDevice();
            var hasIface = NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref iface, NativeMethods.EddGetDeviceInterfaceName);

            var current = NativeMethods.NewDevMode();
            var hasCurrent = NativeMethods.EnumDisplaySettingsEx(adapter.DeviceName, NativeMethods.EnumCurrentSettings, ref current, 0);
            var registry = NativeMethods.NewDevMode();
            var hasRegistry = NativeMethods.EnumDisplaySettingsEx(adapter.DeviceName, NativeMethods.EnumRegistrySettings, ref registry, 0);
            var modes = GetModes(adapter.DeviceName);

            var source = hasCurrent ? current : registry;
            var width = source.dmPelsWidth;
            var height = source.dmPelsHeight;
            var frequency = source.dmDisplayFrequency;
            var orientation = source.dmDisplayOrientation;
            var bits = source.dmBitsPerPel <= 0 ? 32 : source.dmBitsPerPel;
            var x = hasCurrent ? current.dmPositionX : 0;
            var y = hasCurrent ? current.dmPositionY : 0;

            if (!enabled || width <= 0 || height <= 0)
            {
                if (hasRegistry && registry.dmPelsWidth > 0 && registry.dmPelsHeight > 0)
                {
                    width = registry.dmPelsWidth;
                    height = registry.dmPelsHeight;
                    frequency = registry.dmDisplayFrequency;
                    orientation = registry.dmDisplayOrientation;
                    bits = registry.dmBitsPerPel <= 0 ? 32 : registry.dmBitsPerPel;
                }
                else if (modes.Count > 0)
                {
                    var best = modes.OrderByDescending(m => (long)m.Width * m.Height).ThenByDescending(m => m.Frequency).First();
                    width = best.Width;
                    height = best.Height;
                    frequency = best.Frequency;
                    bits = best.Bits <= 0 ? 32 : best.Bits;
                }
            }

            var friendly = hasMonitor ? monitor.DeviceString?.Trim() : adapter.DeviceString?.Trim();
            if (string.IsNullOrWhiteSpace(friendly) || friendly.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
                friendly = string.IsNullOrWhiteSpace(adapter.DeviceString) ? adapter.DeviceName : adapter.DeviceString.Trim();

            result.Add(new MonitorState
            {
                AdapterName = adapter.DeviceName.Trim(),
                DeviceId = FirstId(hasIface ? iface.DeviceID : null, hasMonitor ? monitor.DeviceID : null, adapter.DeviceName),
                FriendlyName = friendly.Trim(),
                Enabled = enabled,
                Primary = enabled && (adapter.StateFlags & NativeMethods.DisplayDevicePrimaryDevice) != 0,
                X = enabled ? x : 0,
                Y = enabled ? y : 0,
                Width = Math.Max(0, width),
                Height = Math.Max(0, height),
                Frequency = Math.Max(0, frequency),
                Orientation = orientation,
                BitsPerPixel = bits,
                Modes = modes
            });
        }

        result = DedupeMonitors(result);
        if (result.Count(m => m.Primary) == 0)
        {
            var first = result.FirstOrDefault(m => m.Enabled);
            if (first != null)
                first.Primary = true;
        }

        return result;
    }

    static List<MonitorState> DedupeMonitors(List<MonitorState> items)
    {
        var live = GetLiveMonitors()
            .Select(monitor => monitor.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items
            .GroupBy(monitor => ProfileLayout.StableKey(monitor.DeviceId, monitor.AdapterName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(monitor => monitor.Enabled)
                .ThenByDescending(monitor => live.Contains(monitor.AdapterName))
                .ThenBy(monitor => monitor.AdapterName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(monitor => monitor.Enabled && monitor.Primary)
            .ThenBy(monitor => monitor.Enabled ? monitor.X : int.MaxValue)
            .ThenBy(monitor => monitor.Y)
            .ToList();
    }

    public static List<LiveMonitor> GetLiveMonitors()
    {
        var found = new List<LiveMonitor>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = string.Empty
            };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return true;

            found.Add(new LiveMonitor
            {
                DeviceName = (info.szDevice ?? "").Trim(),
                X = info.rcMonitor.Left,
                Y = info.rcMonitor.Top,
                Width = info.rcMonitor.Width,
                Height = info.rcMonitor.Height,
                WorkX = info.rcWork.Left,
                WorkY = info.rcWork.Top,
                WorkWidth = info.rcWork.Width,
                WorkHeight = info.rcWork.Height,
                Primary = (info.dwFlags & NativeMethods.MonitorInfoPrimary) != 0
            });
            return true;
        }, IntPtr.Zero);

        var ordered = found.OrderBy(m => m.X).ThenBy(m => m.Y).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Number = i + 1;
        return ordered;
    }

    public static List<DisplayMode> GetModes(string adapterName)
    {
        if (ModeCache.TryGetValue(adapterName, out var cached))
            return cached;

        var modes = new List<DisplayMode>();
        var seen = new HashSet<string>();
        for (var i = 0; i < 400; i++)
        {
            var mode = NativeMethods.NewDevMode();
            if (!NativeMethods.EnumDisplaySettingsEx(adapterName, i, ref mode, 0))
                break;
            if (mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0)
                continue;

            var key = $"{mode.dmPelsWidth}x{mode.dmPelsHeight}@{mode.dmDisplayFrequency}@{mode.dmDisplayOrientation}@{mode.dmBitsPerPel}";
            if (!seen.Add(key))
                continue;

            modes.Add(new DisplayMode
            {
                Width = mode.dmPelsWidth,
                Height = mode.dmPelsHeight,
                Frequency = mode.dmDisplayFrequency,
                Orientation = mode.dmDisplayOrientation,
                Bits = mode.dmBitsPerPel
            });
        }

        ModeCache[adapterName] = modes;
        return modes;
    }

    public static ApplyResult Apply(IReadOnlyList<MonitorProfileEntry> desired)
    {
        try
        {
            var normalized = ProfileLayout.Normalize(desired);
            var enabled = normalized.Where(m => m.Enabled).ToList();
            if (enabled.Count == 0)
                return ApplyResult.Fail(Loc.T("Alespoň jeden monitor musí zůstat zapnutý.", "At least one monitor must stay on."));
            if (enabled.Any(m => m.Width < 640 || m.Height < 480))
                return ApplyResult.Fail(Loc.T("Rozlišení zapnutého monitoru je příliš malé.", "The resolution of an enabled monitor is too small."));
            if (ProfileLayout.HasOverlap(enabled))
                return ApplyResult.Fail(Loc.T("Zapnuté monitory se překrývají. Posuňte je tak, aby se jejich plochy nekryly.", "Enabled monitors overlap. Move them so their areas do not cover each other."));

            var current = Query();
            var plan = new List<(MonitorState Device, MonitorProfileEntry Item)>();
            var missing = new List<string>();
            foreach (var item in normalized)
            {
                var device = Match(current, item);
                if (device == null)
                {
                    missing.Add(string.IsNullOrWhiteSpace(item.FriendlyName) ? item.AdapterName : item.FriendlyName);
                    continue;
                }

                plan.Add((device, item));
            }

            if (plan.Count == 0)
                return ApplyResult.Fail(Loc.T("Žádný monitor z profilu teď není připojený.", "None of the monitors in this profile are connected."));
            if (plan.All(p => !p.Item.Enabled))
                return ApplyResult.Fail(Loc.T("Zapnuté monitory z profilu nejsou připojené.", "The monitors that should be on are not connected.") + MissingSuffix(missing));

            if (IsSame(plan))
                return ApplyResult.NoChange(Loc.T("Tohle rozložení už je aktivní.", "This layout is already active."));

            var previous = current.Select(ProfileLayout.FromState).ToList();
            var error = Commit(plan);
            if (error != null)
            {
                AppLog.Info("Přepnutí selhalo, vracím předchozí stav: " + error);
                var rollbackPlan = new List<(MonitorState Device, MonitorProfileEntry Item)>();
                foreach (var item in previous)
                {
                    var device = Match(current, item);
                    if (device != null)
                        rollbackPlan.Add((device, item));
                }

                var rollback = Commit(rollbackPlan);

                var message = Loc.T("Monitory se nepodařilo přepnout. ", "The monitors could not be switched. ") + error;
                if (rollback != null)
                    message += Loc.T(" Nepodařilo se ani vrátit původní stav: ", " The previous layout could not be restored either: ") + rollback;
                return ApplyResult.Fail(message);
            }

            var text = missing.Count == 0
                ? Loc.T("Rozložení monitorů bylo změněno.", "The monitor layout was changed.")
                : Loc.T("Rozložení bylo změněno. Nepřipojené: ", "The layout was changed. Not connected: ") + string.Join(", ", missing);
            return ApplyResult.Ok(previous, text);
        }
        catch (Exception ex)
        {
            AppLog.Info("Apply exception: " + ex);
            return ApplyResult.Fail(Loc.T("Chyba při změně monitorů: ", "Error while changing monitors: ") + ex.Message);
        }
    }

    public static string DescribeChange(int code) => code switch
    {
        0 => Loc.T("v pořádku", "ok"),
        1 => Loc.T("změna chce restart", "a restart is required"),
        -1 => Loc.T("změna selhala", "the change failed"),
        -2 => Loc.T("režim monitor nepodporuje", "the monitor does not support this mode"),
        -3 => Loc.T("nastavení se nepodařilo zapsat", "the settings could not be saved"),
        -4 => Loc.T("neplatné příznaky", "invalid flags"),
        -5 => Loc.T("neplatný parametr", "invalid parameter"),
        -6 => Loc.T("neplatné rozložení více monitorů", "invalid multi-monitor layout"),
        _ => Loc.T("kód ", "code ") + code
    };

    static string? Commit(List<(MonitorState Device, MonitorProfileEntry Item)> plan)
    {
        var ordered = plan
            .OrderByDescending(p => p.Item.Enabled && p.Item.Primary)
            .ThenByDescending(p => p.Item.Enabled)
            .ThenBy(p => p.Item.X)
            .ToList();

        foreach (var step in ordered)
        {
            var dm = NativeMethods.NewDevMode();
            var have = NativeMethods.EnumDisplaySettingsEx(step.Device.AdapterName, NativeMethods.EnumCurrentSettings, ref dm, 0)
                       || NativeMethods.EnumDisplaySettingsEx(step.Device.AdapterName, NativeMethods.EnumRegistrySettings, ref dm, 0);
            if (!have)
                dm = NativeMethods.NewDevMode();

            dm.dmDriverExtra = 0;
            var flags = NativeMethods.CdsUpdateRegistry | NativeMethods.CdsNoReset;
            if (step.Item.Enabled)
            {
                dm.dmPelsWidth = step.Item.Width;
                dm.dmPelsHeight = step.Item.Height;
                dm.dmBitsPerPel = step.Item.BitsPerPixel <= 0 ? 32 : step.Item.BitsPerPixel;
                dm.dmDisplayOrientation = step.Item.Orientation;
                dm.dmPositionX = step.Item.X;
                dm.dmPositionY = step.Item.Y;
                dm.dmFields = NativeMethods.DmPosition | NativeMethods.DmPelsWidth | NativeMethods.DmPelsHeight
                              | NativeMethods.DmBitsPerPel | NativeMethods.DmDisplayOrientation;
                if (step.Item.Frequency > 1)
                {
                    dm.dmDisplayFrequency = step.Item.Frequency;
                    dm.dmFields |= NativeMethods.DmDisplayFrequency;
                }

                if (step.Item.Primary)
                    flags |= NativeMethods.CdsSetPrimary;
            }
            else
            {
                dm.dmPelsWidth = 0;
                dm.dmPelsHeight = 0;
                dm.dmPositionX = 0;
                dm.dmPositionY = 0;
                dm.dmFields = NativeMethods.DmPosition | NativeMethods.DmPelsWidth | NativeMethods.DmPelsHeight;
            }

            var rc = NativeMethods.ChangeDisplaySettingsEx(step.Device.AdapterName, ref dm, IntPtr.Zero, flags, IntPtr.Zero);
            AppLog.Info($"CDS {step.Device.AdapterName} enabled={step.Item.Enabled} {step.Item.Width}x{step.Item.Height} @{step.Item.X},{step.Item.Y} primary={step.Item.Primary} freq={step.Item.Frequency} -> {rc}");
            if (rc != 0 && rc != 1)
                return $"{step.Device.Label}: {DescribeChange(rc)}";
        }

        var commit = NativeMethods.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        AppLog.Info("CDS commit -> " + commit);
        if (commit != 0 && commit != 1)
            return Loc.T("Uložení rozložení selhalo: ", "Saving the layout failed: ") + DescribeChange(commit);
        return null;
    }

    static bool IsSame(List<(MonitorState Device, MonitorProfileEntry Item)> plan)
    {
        foreach (var step in plan)
        {
            var device = step.Device;
            var item = step.Item;
            if (device.Enabled != item.Enabled || device.Primary != item.Primary)
                return false;
            if (!item.Enabled)
                continue;
            if (device.X != item.X || device.Y != item.Y || device.Width != item.Width || device.Height != item.Height)
                return false;
            if (device.Orientation != item.Orientation)
                return false;
            if (item.Frequency > 1 && Math.Abs(device.Frequency - item.Frequency) > 1)
                return false;
        }

        return true;
    }

    public static MonitorState? Match(IReadOnlyList<MonitorState> current, MonitorProfileEntry item)
    {
        if (!string.IsNullOrWhiteSpace(item.DeviceId))
        {
            var byId = current.FirstOrDefault(m => string.Equals(m.DeviceId, item.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(item.AdapterName))
        {
            var byAdapter = current.FirstOrDefault(m => string.Equals(m.AdapterName, item.AdapterName, StringComparison.OrdinalIgnoreCase));
            if (byAdapter != null)
                return byAdapter;
        }

        if (!string.IsNullOrWhiteSpace(item.FriendlyName))
        {
            var byName = current.Where(m => string.Equals(m.FriendlyName, item.FriendlyName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 1)
                return byName[0];
        }

        return null;
    }

    static string FirstId(string? interfaceName, string? hardwareId, string adapterName)
    {
        if (!string.IsNullOrWhiteSpace(interfaceName))
            return interfaceName.Trim();
        if (!string.IsNullOrWhiteSpace(hardwareId))
            return hardwareId.Trim();
        return adapterName.Trim();
    }

    static string MissingSuffix(List<string> missing) =>
        missing.Count == 0 ? "" : Loc.T(" Nepřipojené: ", " Not connected: ") + string.Join(", ", missing);

    public static string Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("DEVMODE=" + Marshal.SizeOf<NativeMethods.DEVMODE>());
        sb.AppendLine("DISPLAY_DEVICE=" + Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>());
        sb.AppendLine("TITLEBARINFOEX=" + Marshal.SizeOf<NativeMethods.TITLEBARINFOEX>());
        foreach (var monitor in Query())
        {
            sb.AppendLine($"--- {monitor.Label}");
            sb.AppendLine($"    id={monitor.DeviceId}");
            sb.AppendLine($"    enabled={monitor.Enabled} primary={monitor.Primary} {monitor.Width}x{monitor.Height} @{monitor.X},{monitor.Y} {monitor.Frequency}Hz ori={monitor.Orientation} bits={monitor.BitsPerPixel} modes={monitor.Modes.Count}");
        }

        sb.AppendLine("live:");
        foreach (var live in GetLiveMonitors())
            sb.AppendLine($"    #{live.Number} {live.DeviceName} bounds={live.X},{live.Y} {live.Width}x{live.Height} work={live.WorkX},{live.WorkY} {live.WorkWidth}x{live.WorkHeight} primary={live.Primary}");
        return sb.ToString();
    }
}
