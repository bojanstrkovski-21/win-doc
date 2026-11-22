using System.Text.Json;
using Microsoft.Win32;

namespace WinDock;

internal enum DockEdge
{
    Top,
    Bottom,
    Left,
    Right,
}

internal enum ModuleAlign
{
    Left,
    Center,
    Right,
}

internal enum DateFormat
{
    WeekdayShortMonth,
    Numeric,
    ShortWeekday,
    FullMonth,
}

internal enum TimeFormat
{
    Hour24,
    Hour12,
}

internal sealed class ModuleSetting
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

internal sealed class DockSettings
{
    public DockEdge Edge { get; set; } = DockEdge.Top;
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 12;
    public int Opacity { get; set; } = 255;
    public string Blur { get; set; } = "None";
    public bool CustomizeBlur { get; set; }
    public bool BlurBehind { get; set; } = true;
    public bool AcrylicTint { get; set; }
    public bool Floating { get; set; }
    public int Gap { get; set; } = 8;
    public ModuleAlign Align { get; set; } = ModuleAlign.Left;

    /// <summary>Gap in device-independent pixels between an icon and its text, and between modules.</summary>
    public int Spacing { get; set; } = 16;

    /// <summary>0 is a milky frost and 100 is the clearest glass. Windows does not expose a blur radius.</summary>
    public int BlurAmount { get; set; } = 80;
    public DateFormat Date { get; set; } = DateFormat.WeekdayShortMonth;
    public TimeFormat Time { get; set; } = TimeFormat.Hour24;
    public string Theme { get; set; } = "system";
    public List<ModuleSetting> Modules { get; set; } = DefaultModules();

    public static List<ModuleSetting> DefaultModules() =>
    [
        new() { Id = "cpu" },
        new() { Id = "ram" },
        new() { Id = "disk" },
        new() { Id = "net" },
        new() { Id = "gpu" },
        new() { Id = "battery" },
        new() { Id = "clock" },
    ];

    public static string ModuleName(string id) => id switch
    {
        "cpu" => "CPU",
        "ram" => "Memory",
        "disk" => "Disk",
        "net" => "Network",
        "gpu" => "GPU",
        "battery" => "Battery",
        "clock" => "Date and time",
        _ => id,
    };

    public BarPalette Palette()
    {
        if (Theme == "system")
        {
            return SystemUsesLightTheme() ? BarThemes.Light : BarThemes.Dark;
        }

        return BarThemes.ById.TryGetValue(Theme, out var palette) ? palette : BarThemes.Dark;
    }

    public string FormatDate(DateTime now) => Date switch
    {
        DateFormat.Numeric => now.ToString("dd.MM.yyyy"),
        DateFormat.ShortWeekday => now.ToString("ddd d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
        DateFormat.FullMonth => now.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
        _ => now.ToString("dddd d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
    };

    public string FormatTime(DateTime now) => Time switch
    {
        TimeFormat.Hour12 => now.ToString("hh:mm tt", System.Globalization.CultureInfo.InvariantCulture),
        _ => now.ToString("HH:mm"),
    };

    public void Normalize()
    {
        FontSize = Math.Clamp(FontSize, 8, 28);
        Opacity = Math.Clamp(Opacity, 40, 255);
        Gap = Math.Clamp(Gap, 0, 64);
        Spacing = Math.Clamp(Spacing, 0, 48);
        BlurAmount = Math.Clamp(BlurAmount, 0, 100);
        if (string.IsNullOrWhiteSpace(FontFamily))
        {
            FontFamily = "Segoe UI";
        }

        var known = DefaultModules();
        var ordered = new List<ModuleSetting>();
        foreach (var module in Modules)
        {
            if (known.Any(item => item.Id == module.Id) && ordered.All(item => item.Id != module.Id))
            {
                ordered.Add(module);
            }
        }

        foreach (var module in known.Where(item => ordered.All(existing => existing.Id != item.Id)))
        {
            ordered.Add(module);
        }

        Modules = ordered;
    }

    public static DockSettings Load()
    {
        try
        {
            var path = Path();
            if (!File.Exists(path))
            {
                return new DockSettings();
            }

            var settings = JsonSerializer.Deserialize<DockSettings>(File.ReadAllText(path)) ?? new DockSettings();
            settings.Normalize();
            return settings;
        }
        catch (IOException)
        {
            return new DockSettings();
        }
        catch (JsonException)
        {
            return new DockSettings();
        }
    }

    public void Save()
    {
        Normalize();
        var path = Path();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Path()
        => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "win-dock", "settings.json");

    private static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }
}

internal readonly record struct BarPalette(uint Background, uint Text, uint Muted, uint Accent, uint Border);

internal static class BarThemes
{
    public static readonly BarPalette Dark = new(0x00181818, 0x00F2F2F2, 0x00A0A0A0, 0x00E8A060, 0x003A3A3A);
    public static readonly BarPalette Light = new(0x00F3F3F3, 0x001A1A1A, 0x005C5C5C, 0x00B45A14, 0x00D0D0D0);

    public static readonly IReadOnlyList<(string Id, string Label)> Choices =
    [
        ("system", "System"),
        ("archboi-dark", "Archboi Dark"),
        ("archboi-light", "Arch-Boki Light"),
        ("current", "Current"),
        ("dawnfox", "Dawnfox"),
        ("nightfox", "Nightfox"),
        ("everforest", "Everforest"),
        ("kanagawa", "Kanagawa Dragon"),
        ("nord", "Nord"),
    ];

    public static readonly Dictionary<string, BarPalette> ById = new()
    {
        ["archboi-dark"] = Rgb("1d283a", "c9e79a", "94a3b8", "98f6e3", "141c2a"),
        ["archboi-light"] = Rgb("e6e5e0", "2d3142", "4f5d75", "a83a18", "dddcd6"),
        ["current"] = Rgb("1c1f26", "f2f4f8", "c5cad3", "4c8dff", "16191f"),
        ["dawnfox"] = Rgb("faf4ed", "575279", "9893a5", "b4637a", "ebe0df"),
        ["nightfox"] = Rgb("192330", "cdcecf", "738091", "719cd6", "131a24"),
        ["everforest"] = Rgb("2d353b", "d3c6aa", "859289", "a7c080", "232a2e"),
        ["kanagawa"] = Rgb("181616", "c5c9c5", "a6a69c", "8ba4b0", "12120f"),
        ["nord"] = Rgb("2e3440", "eceff4", "d8dee9", "81a1c1", "434c5e"),
    };

    private static BarPalette Rgb(string background, string text, string muted, string accent, string border)
        => new(ColorRef(background), ColorRef(text), ColorRef(muted), ColorRef(accent), ColorRef(border));

    private static uint ColorRef(string hex)
    {
        var value = Convert.ToUInt32(hex, 16);
        var red = (value >> 16) & 0xFF;
        var green = (value >> 8) & 0xFF;
        var blue = value & 0xFF;
        return (blue << 16) | (green << 8) | red;
    }
}
