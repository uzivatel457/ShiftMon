using System.Globalization;

namespace ShiftMon.App;

internal static class Loc
{
    public static bool Czech { get; private set; } = true;

    public static void Use(string? preference)
    {
        Czech = preference switch
        {
            "cs" => true,
            "en" => false,
            _ => CultureInfo.CurrentUICulture.Name.StartsWith("cs", StringComparison.OrdinalIgnoreCase)
        };
    }

    public static string T(string czech, string english) => Czech ? czech : english;
}
