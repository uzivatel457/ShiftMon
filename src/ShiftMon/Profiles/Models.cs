namespace ShiftMon.Profiles;

internal sealed class DisplayMode
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Frequency { get; set; }
    public int Orientation { get; set; }
    public int Bits { get; set; }

    public string ResolutionLabel => $"{Width} × {Height}";
}

internal sealed class MonitorState
{
    public string AdapterName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Primary { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Frequency { get; set; }
    public int Orientation { get; set; }
    public int BitsPerPixel { get; set; } = 32;
    public List<DisplayMode> Modes { get; set; } = [];

    public string ShortAdapter =>
        AdapterName.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            ? AdapterName[4..]
            : AdapterName;

    public string Label =>
        string.IsNullOrWhiteSpace(FriendlyName)
            ? ShortAdapter
            : $"{FriendlyName} ({ShortAdapter})";
}

internal sealed class MonitorProfileEntry
{
    public string DeviceId { get; set; } = "";
    public string AdapterName { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Primary { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Frequency { get; set; }
    public int Orientation { get; set; }
    public int BitsPerPixel { get; set; } = 32;

    public string ShortAdapter =>
        AdapterName.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            ? AdapterName[4..]
            : AdapterName;

    public string Label =>
        string.IsNullOrWhiteSpace(FriendlyName)
            ? ShortAdapter
            : $"{FriendlyName} ({ShortAdapter})";

    public MonitorProfileEntry Clone() => new()
    {
        DeviceId = DeviceId,
        AdapterName = AdapterName,
        FriendlyName = FriendlyName,
        Enabled = Enabled,
        Primary = Primary,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Frequency = Frequency,
        Orientation = Orientation,
        BitsPerPixel = BitsPerPixel
    };
}

internal sealed class DisplayProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = "Profil";
    public string? ShortcutPath { get; set; }
    public List<MonitorProfileEntry> Monitors { get; set; } = [];

    public DisplayProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        ShortcutPath = ShortcutPath,
        Monitors = Monitors.Select(m => m.Clone()).ToList()
    };
}

internal sealed class LiveMonitor
{
    public string DeviceName { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int WorkX { get; init; }
    public int WorkY { get; init; }
    public int WorkWidth { get; init; }
    public int WorkHeight { get; init; }
    public bool Primary { get; init; }
    public int Number { get; set; }

    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int WorkRight => WorkX + WorkWidth;
    public int WorkBottom => WorkY + WorkHeight;
}
