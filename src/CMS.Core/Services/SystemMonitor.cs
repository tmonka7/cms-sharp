using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CMS.Core.Services;

/// <summary>A point-in-time reading of machine load, shown on the dashboard.</summary>
public sealed class SystemSnapshot
{
    public double CpuPercent { get; set; }

    public double MemoryPercent { get; set; }

    public double DiskPercent { get; set; }

    public long TotalMemoryBytes { get; set; }

    public long UsedMemoryBytes { get; set; }

    public long TotalDiskBytes { get; set; }

    public long UsedDiskBytes { get; set; }

    public long FreeDiskBytes => Math.Max(0, TotalDiskBytes - UsedDiskBytes);

    public TimeSpan Uptime { get; set; }

    public double NetworkMbps { get; set; }

    public string UptimeText =>
        (int)Uptime.TotalDays + " days " + Uptime.Hours + " hours " + Uptime.Minutes + " minutes";

    public string StorageText =>
        SystemMonitor.FormatBytes(UsedDiskBytes) + " / " + SystemMonitor.FormatBytes(TotalDiskBytes);

    public string MemoryText =>
        SystemMonitor.FormatBytes(UsedMemoryBytes) + " / " + SystemMonitor.FormatBytes(TotalMemoryBytes);
}

/// <summary>
/// Samples CPU, memory, disk and network through Win32 APIs. It uses no
/// performance-counter service and no remote telemetry, so it works on a
/// locked-down host.
/// </summary>
public sealed class SystemMonitor
{
    private string _storageRoot;
    private long _lastIdle;
    private long _lastKernel;
    private long _lastUser;
    private long _lastNetworkBytes;
    private DateTime _lastNetworkSample = DateTime.UtcNow;

    public SystemMonitor(string storageRoot) => _storageRoot = storageRoot;

    public void SetStorageRoot(string storageRoot) => _storageRoot = storageRoot;

    public SystemSnapshot Sample()
    {
        long totalMemory, usedMemory, totalDisk, usedDisk;

        var snapshot = new SystemSnapshot
        {
            CpuPercent = Math.Round(SampleCpu(), 1),
            MemoryPercent = Math.Round(SampleMemory(out totalMemory, out usedMemory), 1),
            DiskPercent = Math.Round(SampleDisk(out totalDisk, out usedDisk), 1),
            Uptime = TimeSpan.FromMilliseconds(GetTickCount64()),
            NetworkMbps = Math.Round(SampleNetwork(), 2)
        };

        snapshot.TotalMemoryBytes = totalMemory;
        snapshot.UsedMemoryBytes = usedMemory;
        snapshot.TotalDiskBytes = totalDisk;
        snapshot.UsedDiskBytes = usedDisk;

        return snapshot;
    }

    /// <summary>
    /// CPU load from the delta between two GetSystemTimes calls. The first call
    /// only establishes the baseline and reports zero.
    /// </summary>
    private double SampleCpu()
    {
        FILETIME idle, kernel, user;
        if (!GetSystemTimes(out idle, out kernel, out user))
        {
            return 0;
        }

        var idleTicks = ToTicks(idle);
        var kernelTicks = ToTicks(kernel);
        var userTicks = ToTicks(user);

        if (_lastKernel == 0)
        {
            _lastIdle = idleTicks;
            _lastKernel = kernelTicks;
            _lastUser = userTicks;
            return 0;
        }

        var idleDelta = idleTicks - _lastIdle;
        var kernelDelta = kernelTicks - _lastKernel;
        var userDelta = userTicks - _lastUser;

        _lastIdle = idleTicks;
        _lastKernel = kernelTicks;
        _lastUser = userTicks;

        // Kernel time already includes idle time, so total is kernel + user.
        var total = kernelDelta + userDelta;
        if (total <= 0)
        {
            return 0;
        }

        return MathEx.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
    }

    private static double SampleMemory(out long totalBytes, out long usedBytes)
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };

        if (!GlobalMemoryStatusEx(ref status))
        {
            totalBytes = 0;
            usedBytes = 0;
            return 0;
        }

        totalBytes = (long)status.ullTotalPhys;
        usedBytes = (long)(status.ullTotalPhys - status.ullAvailPhys);
        return status.dwMemoryLoad;
    }

    private double SampleDisk(out long totalBytes, out long usedBytes)
    {
        totalBytes = 0;
        usedBytes = 0;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_storageRoot));
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return 0;
            }

            totalBytes = drive.TotalSize;
            usedBytes = drive.TotalSize - drive.AvailableFreeSpace;
            return totalBytes == 0 ? 0 : usedBytes * 100.0 / totalBytes;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private double SampleNetwork()
    {
        try
        {
            long total = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                total += nic.GetIPStatistics().BytesReceived;
            }

            var now = DateTime.UtcNow;
            var elapsed = (now - _lastNetworkSample).TotalSeconds;

            if (_lastNetworkBytes == 0 || elapsed <= 0)
            {
                _lastNetworkBytes = total;
                _lastNetworkSample = now;
                return 0;
            }

            var mbps = (total - _lastNetworkBytes) * 8.0 / 1_000_000.0 / elapsed;
            _lastNetworkBytes = total;
            _lastNetworkSample = now;

            return Math.Max(0, mbps);
        }
        catch (NetworkInformationException)
        {
            return 0;
        }
    }

    /// <summary>The primary IPv4 address, gateway and DNS server of this host.</summary>
    public static NetworkSummary DescribeNetwork()
    {
        var summary = new NetworkSummary();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var properties = nic.GetIPProperties();

                var address = properties.UnicastAddresses.FirstOrDefault(
                    a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                if (address == null)
                {
                    continue;
                }

                summary.IpAddress = address.Address.ToString();
                summary.SubnetMask = address.IPv4Mask?.ToString() ?? string.Empty;
                summary.Adapter = nic.Name;

                var gateway = properties.GatewayAddresses.FirstOrDefault(
                    g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                if (gateway != null)
                {
                    summary.Gateway = gateway.Address.ToString();
                }

                var dns = properties.DnsAddresses.FirstOrDefault(
                    d => d.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                if (dns != null)
                {
                    summary.Dns = dns.ToString();
                }

                if (!string.IsNullOrEmpty(summary.Gateway))
                {
                    break;
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Leave the summary blank rather than failing the screen.
        }

        return summary;
    }

    private static long ToTicks(FILETIME time)
        => ((long)time.dwHighDateTime << 32) | time.dwLowDateTime;

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString("0.##") + " " + units[unit];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();
}

/// <summary>Host network details for the System Information screen.</summary>
public sealed class NetworkSummary
{
    public string Adapter { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string SubnetMask { get; set; } = string.Empty;

    public string Gateway { get; set; } = string.Empty;

    public string Dns { get; set; } = string.Empty;
}
