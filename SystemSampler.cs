using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Windows.Win32;
using Windows.Win32.System.Power;
using Windows.Win32.System.SystemInformation;

namespace WinDock;

internal sealed partial class SystemSampler : IDisposable
{
    private static readonly string[] SkipNicMarkers =
    [
        "hyper-v",
        "vethernet",
        "wsl",
        "virtualbox",
        "vmware",
        "loopback",
        "pseudo",
        "tunnel",
    ];

    private readonly PdhQuery _pdh = new();
    private readonly PdhCounter? _diskRead;
    private readonly PdhCounter? _diskWrite;
    private readonly PdhCounter? _gpu;

    private bool _hasCpu;
    private ulong _idle;
    private ulong _kernel;
    private ulong _user;

    private bool _hasNet;
    private ulong _netIn;
    private ulong _netOut;
    private long _stamp;
    private int _nicTicks;
    private NetworkInterface[] _nics = [];

    public SystemSampler()
    {
        _diskRead = _pdh.TryAdd(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
        _diskWrite = _pdh.TryAdd(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
        _gpu = _pdh.TryAdd(@"\GPU Engine(*)\Utilization Percentage");
        if (_pdh.HasCounters)
        {
            _pdh.Collect();
        }

        _stamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public StatsSnapshot Sample()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = (now - _stamp) / (double)System.Diagnostics.Stopwatch.Frequency;
        _stamp = now;

        ReadNetwork(out var netIn, out var netOut);
        double? netDown = null;
        double? netUp = null;
        if (_hasNet && elapsed > 0.2)
        {
            netDown = NonNegative((netIn - _netIn) / elapsed);
            netUp = NonNegative((netOut - _netOut) / elapsed);
        }

        _hasNet = true;
        _netIn = netIn;
        _netOut = netOut;

        double? diskRead = null;
        double? diskWrite = null;
        double? gpu = null;
        if (_pdh.HasCounters && _pdh.Collect())
        {
            diskRead = _diskRead is { } diskReadCounter ? _pdh.ReadDouble(diskReadCounter) : null;
            diskWrite = _diskWrite is { } diskWriteCounter ? _pdh.ReadDouble(diskWriteCounter) : null;
            gpu = _gpu is { } gpuCounter ? _pdh.ReadGpuPercent(gpuCounter) : null;
        }

        ReadMemory(out var memoryPercent, out var memoryUsed);
        return new StatsSnapshot(
            ReadCpu(),
            memoryPercent,
            memoryUsed,
            diskRead,
            diskWrite,
            netDown,
            netUp,
            gpu,
            ReadBattery());
    }

    public void Dispose() => _pdh.Dispose();

    private unsafe double? ReadCpu()
    {
        System.Runtime.InteropServices.ComTypes.FILETIME idle;
        System.Runtime.InteropServices.ComTypes.FILETIME kernel;
        System.Runtime.InteropServices.ComTypes.FILETIME user;
        if (!PInvoke.GetSystemTimes(&idle, &kernel, &user))
        {
            return null;
        }

        var idleTicks = FileTime(idle);
        var kernelTicks = FileTime(kernel);
        var userTicks = FileTime(user);
        double? percent = null;
        if (_hasCpu)
        {
            var total = (kernelTicks - _kernel) + (userTicks - _user);
            var idleDelta = idleTicks - _idle;
            percent = total == 0 ? 0 : (total - idleDelta) * 100d / total;
        }

        _hasCpu = true;
        _idle = idleTicks;
        _kernel = kernelTicks;
        _user = userTicks;
        return percent;
    }

    private static void ReadMemory(out double percent, out ulong used)
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!PInvoke.GlobalMemoryStatusEx(ref status))
        {
            percent = 0;
            used = 0;
            return;
        }

        percent = status.dwMemoryLoad;
        used = status.ullTotalPhys - status.ullAvailPhys;
    }

    private static BatterySnapshot? ReadBattery()
    {
        if (!PInvoke.GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            return null;
        }

        const byte noBattery = 128;
        const byte charging = 8;
        if ((status.BatteryFlag & noBattery) != 0 || status.BatteryLifePercent == 255)
        {
            return null;
        }

        return new BatterySnapshot(status.BatteryLifePercent, (status.BatteryFlag & charging) != 0);
    }

    private void ReadNetwork(out ulong received, out ulong sent)
    {
        received = 0;
        sent = 0;
        if (_nicTicks == 0)
        {
            _nics = NetworkInterface.GetAllNetworkInterfaces().Where(IncludeNic).ToArray();
        }

        _nicTicks = (_nicTicks + 1) % 30;
        foreach (var nic in _nics)
        {
            try
            {
                var stats = nic.GetIPStatistics();
                received += (ulong)Math.Max(0, stats.BytesReceived);
                sent += (ulong)Math.Max(0, stats.BytesSent);
            }
            catch (NetworkInformationException)
            {
                _nicTicks = 0;
            }
        }
    }

    private static bool IncludeNic(NetworkInterface nic)
    {
        if (nic.OperationalStatus != OperationalStatus.Up)
        {
            return false;
        }

        if (nic.NetworkInterfaceType is not (
            NetworkInterfaceType.Ethernet or
            NetworkInterfaceType.GigabitEthernet or
            NetworkInterfaceType.Wireless80211 or
            NetworkInterfaceType.FastEthernetT or
            NetworkInterfaceType.FastEthernetFx))
        {
            return false;
        }

        var description = nic.Description;
        var name = nic.Name;
        return !SkipNicMarkers.Any(marker => description.Contains(marker, StringComparison.OrdinalIgnoreCase) || name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static ulong FileTime(System.Runtime.InteropServices.ComTypes.FILETIME value)
        => ((ulong)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    private static double NonNegative(double value) => value < 0 ? 0 : value;
}

internal sealed partial class SystemSampler
{
    private sealed class PdhQuery : IDisposable
    {
        private readonly nint _query;
        private readonly bool _open;
        private bool _primed;

        public PdhQuery()
        {
            _open = PInvoke.PdhOpenQuery(null!, 0, out _query) == 0;
        }

        public bool HasCounters { get; private set; }

        public PdhCounter? TryAdd(string path)
        {
            if (!_open)
            {
                return null;
            }

            if (PInvoke.PdhAddEnglishCounter(_query, path, 0, out var counter) != 0)
            {
                return null;
            }

            HasCounters = true;
            return new PdhCounter(counter);
        }

        public bool Collect()
        {
            if (!_open || PInvoke.PdhCollectQueryData(_query) != 0)
            {
                return false;
            }

            var ready = _primed;
            _primed = true;
            return ready;
        }

        public unsafe double? ReadDouble(PdhCounter counter)
        {
            var status = PInvoke.PdhGetFormattedCounterValue(counter.Handle, Windows.Win32.System.Performance.PDH_FMT.PDH_FMT_DOUBLE, null, out var value);
            if (status != 0 || value.CStatus != 0)
            {
                return null;
            }

            var number = value.Anonymous.doubleValue;
            return double.IsFinite(number) ? Math.Max(0, number) : null;
        }

        public unsafe double? ReadGpuPercent(PdhCounter counter)
        {
            uint size = 0;
            var status = PInvoke.PdhGetFormattedCounterArray(counter.Handle, Windows.Win32.System.Performance.PDH_FMT.PDH_FMT_DOUBLE, ref size, out _, null);
            const uint moreData = 0x800007D2;
            if (status != moreData || size == 0)
            {
                return null;
            }

            var buffer = new byte[size];
            fixed (byte* pointer = buffer)
            {
                status = PInvoke.PdhGetFormattedCounterArray(
                    counter.Handle,
                    Windows.Win32.System.Performance.PDH_FMT.PDH_FMT_DOUBLE,
                    ref size,
                    out var count,
                    (Windows.Win32.System.Performance.PDH_FMT_COUNTERVALUE_ITEM_W*)pointer);
                if (status != 0)
                {
                    return null;
                }

                return GpuPercent(new Span<Windows.Win32.System.Performance.PDH_FMT_COUNTERVALUE_ITEM_W>(pointer, (int)count));
            }
        }

        public void Dispose()
        {
            if (_open)
            {
                PInvoke.PdhCloseQuery(_query);
            }
        }
    }

    private readonly record struct PdhCounter(nint Handle);

    private static double? GpuPercent(ReadOnlySpan<Windows.Win32.System.Performance.PDH_FMT_COUNTERVALUE_ITEM_W> items)
    {
        var perEngine = new Dictionary<(string Luid, string Engine), double>();
        foreach (var item in items)
        {
            if (item.FmtValue.CStatus != 0)
            {
                continue;
            }

            var name = item.szName.ToString();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var value = item.FmtValue.Anonymous.doubleValue;
            if (!double.IsFinite(value) || value < 0)
            {
                continue;
            }

            var match = EngineName().Match(name);
            if (!match.Success)
            {
                continue;
            }

            var key = (match.Groups["luid"].Value, match.Groups["eng"].Value);
            perEngine[key] = perEngine.GetValueOrDefault(key) + value;
        }

        if (perEngine.Count == 0)
        {
            return null;
        }

        return Math.Clamp(perEngine.Values.Max(), 0, 100);
    }

    [GeneratedRegex(@"luid_(?<luid>0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_\d+_eng_(?<eng>\d+)_engtype_", RegexOptions.CultureInvariant)]
    private static partial Regex EngineName();
}
