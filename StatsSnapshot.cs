namespace WinDock;

internal readonly record struct BatterySnapshot(int Percent, bool Charging);

internal readonly record struct StatsSnapshot(
    double? CpuPercent,
    double MemoryPercent,
    ulong MemoryUsedBytes,
    double? DiskReadBytesPerSecond,
    double? DiskWriteBytesPerSecond,
    double? NetDownBytesPerSecond,
    double? NetUpBytesPerSecond,
    double? GpuPercent,
    BatterySnapshot? Battery);

