namespace WinDock;

internal static class StatText
{
    private const uint Warm = 0x0040B0FF;
    private const uint Hot = 0x006060FF;

    public const string IconCpu = "\uE950";
    public const string IconMemory = "\uE964";
    public const string IconDisk = "\uEDA2";
    public const string IconNetwork = "\uE839";
    public const string IconGpu = "\uE7F4";
    public const string IconBattery = "\uEBA0";
    public const string IconCalendar = "\uE787";
    public const string IconClock = "\uE916";
    public const string IconPower = "\uE7E8";
    public const string IconLauncher = "\uE71D";

    public static string FormatLine(StatsSnapshot stats)
    {
        return string.Join("    ", Build(stats, new DockSettings(), DateTime.Now).Select(static module => module.Combined));
    }

    public static IReadOnlyList<ModuleView> Build(StatsSnapshot stats, DockSettings settings, DateTime now)
    {
        var palette = settings.Palette();
        var modules = new List<ModuleView>();
        foreach (var module in settings.Modules)
        {
            if (!module.Enabled)
            {
                continue;
            }

            switch (module.Id)
            {
                case "cpu":
                    modules.Add(Percent(module.Id, IconCpu, "CPU", stats.CpuPercent, palette));
                    break;
                case "ram":
                    modules.Add(new(module.Id, IconMemory, $"RAM {stats.MemoryPercent:0}%  {Gigabytes(stats.MemoryUsedBytes)}", ColorFor(stats.MemoryPercent, palette)));
                    break;
                case "disk":
                    modules.Add(Rate(module.Id, IconDisk, "DSK", stats.DiskReadBytesPerSecond, stats.DiskWriteBytesPerSecond, palette));
                    break;
                case "net":
                    modules.Add(Rate(module.Id, IconNetwork, "NET", stats.NetDownBytesPerSecond, stats.NetUpBytesPerSecond, palette));
                    break;
                case "gpu":
                    modules.Add(Percent(module.Id, IconGpu, "GPU", stats.GpuPercent, palette));
                    break;
                case "battery" when stats.Battery is BatterySnapshot battery:
                    var mark = battery.Charging ? "+" : string.Empty;
                    modules.Add(new(module.Id, IconBattery, $"BAT {battery.Percent}%{mark}", ColorFor(battery.Percent, palette)));
                    break;
                case "clock":
                    modules.Add(new(module.Id, IconCalendar, settings.FormatDate(now), palette.Text, IconClock, settings.FormatTime(now)));
                    break;
            }
        }

        return modules;
    }

    public static string FormatRate(double bytesPerSecond)
    {
        var value = Math.Max(0, bytesPerSecond);
        if (value < 1024 * 1024)
        {
            return $"{value / 1024:0.0} KB/s";
        }

        return $"{value / (1024 * 1024):0.00} MB/s";
    }

    private static ModuleView Percent(string id, string icon, string label, double? percent, BarPalette palette)
    {
        if (percent is not double value)
        {
            return new(id, icon, $"{label} —", palette.Muted);
        }

        var clamped = Math.Clamp(value, 0, 100);
        return new(id, icon, $"{label} {clamped:0}%", ColorFor(clamped, palette));
    }

    private static ModuleView Rate(string id, string icon, string label, double? down, double? up, BarPalette palette)
    {
        if (down is not double downValue || up is not double upValue)
        {
            return new(id, icon, $"{label} —", palette.Muted);
        }

        return new(id, icon, $"{label} ↓ {FormatRate(downValue)}  ↑ {FormatRate(upValue)}", palette.Text);
    }

    private static string Gigabytes(ulong bytes) => $"{bytes / 1024d / 1024d / 1024d:0.0} GB";

    private static uint ColorFor(double percent, BarPalette palette)
    {
        if (percent >= 95)
        {
            return Hot;
        }

        if (percent >= 85)
        {
            return Warm;
        }

        return palette.Text;
    }
}

internal readonly record struct ModuleView(string Id, string Icon, string Text, uint ColorRef, string ExtraIcon = "", string ExtraText = "")
{
    public string Combined => ExtraText.Length == 0 ? Text : $"{Text}  {ExtraText}";
}
