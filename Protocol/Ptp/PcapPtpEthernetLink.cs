using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using log4net;

namespace EssSimulator.Protocol.Ptp;

internal interface IPtpEthernetLink : IDisposable
{
    byte[] SourceMac { get; }
    bool TryReceive(out byte[] frame, out long rxSoftwareNs);
    bool TrySend(ReadOnlySpan<byte> frame);
}

/// <summary>libpcap / Npcap 收发 Ethertype 0x88F7。与 GOOSE（0x88B8）滤镜分离，互不影响。</summary>
internal sealed class PcapPtpEthernetLink : IPtpEthernetLink
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PcapPtpEthernetLink));
    private const int SnapLen = 256;
    private const int TimeoutMs = 50;
    private const string Filter = "ether proto 0x88f7 or (vlan and ether proto 0x88f7)";

    private readonly IntPtr _pcap;
    private readonly PtpSoftwareClock _clock;
    private bool _disposed;

    public byte[] SourceMac { get; }

    private PcapPtpEthernetLink(IntPtr pcap, byte[] mac, PtpSoftwareClock clock)
    {
        _pcap = pcap;
        SourceMac = mac;
        _clock = clock;
    }

    public static bool TryOpen(string device, PtpSoftwareClock clock, out PcapPtpEthernetLink? link, out string error)
    {
        link = null;
        error = string.Empty;
        if (!PcapNative.IsAvailable)
        {
            error = OperatingSystem.IsWindows()
                ? "未找到 wpcap（请安装 Npcap，与 GOOSE 相同）"
                : "未找到 libpcap";
            return false;
        }

        var err = new byte[PcapNative.ErrbufSize];
        IntPtr p = IntPtr.Zero;
        try
        {
            p = PcapNative.OpenLive(device, SnapLen, promiscuous: 1, TimeoutMs, err);
            if (p == IntPtr.Zero)
            {
                error = PcapNative.ErrbufToString(err);
                return false;
            }

            if (!PcapNative.TrySetFilter(p, Filter, out var filterError))
            {
                PcapNative.Close(p);
                error = filterError;
                return false;
            }

            byte[] mac = ResolveMac(device);
            link = new PcapPtpEthernetLink(p, mac, clock);
            return true;
        }
        catch (DllNotFoundException ex)
        {
            if (p != IntPtr.Zero)
                PcapNative.Close(p);
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            if (p != IntPtr.Zero)
                PcapNative.Close(p);
            error = ex.Message;
            Log.Warn("打开 PTP 网卡失败", ex);
            return false;
        }
    }

    public bool TryReceive(out byte[] frame, out long rxSoftwareNs)
    {
        frame = Array.Empty<byte>();
        rxSoftwareNs = 0;
        if (_disposed)
            return false;

        int rc = PcapNative.NextEx(_pcap, out IntPtr hdr, out IntPtr data);
        rxSoftwareNs = _clock.NowNs;
        if (rc != 1 || hdr == IntPtr.Zero || data == IntPtr.Zero)
            return false;

        int caplen = PcapNative.ReadCaplen(hdr);
        if (caplen <= 0)
            return false;
        caplen = Math.Min(caplen, SnapLen);
        frame = new byte[caplen];
        Marshal.Copy(data, frame, 0, caplen);
        return true;
    }

    public bool TrySend(ReadOnlySpan<byte> frame)
    {
        if (_disposed || frame.Length == 0)
            return false;
        byte[] copy = frame.ToArray();
        int rc = PcapNative.SendPacket(_pcap, copy, copy.Length);
        return rc == 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { PcapNative.Close(_pcap); }
        catch (Exception ex) { Log.Debug("关闭 PTP pcap", ex); }
    }

    public static bool TryResolveDevice(string? requested, string? gooseFallback, out string device, out string detail)
    {
        device = string.Empty;
        string name = string.IsNullOrWhiteSpace(requested) ? (gooseFallback ?? "") : requested.Trim();
        if (string.Equals(name, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "off", StringComparison.OrdinalIgnoreCase))
        {
            detail = "网卡已关闭";
            return false;
        }

        if (!PcapNative.TryListDevices(out var devices, out detail))
            return false;
        if (devices.Count == 0)
        {
            detail = "pcap 未枚举到网卡";
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
            name = OperatingSystem.IsWindows() ? "0" : (OperatingSystem.IsMacOS() ? "en0" : "eth0");

        if (int.TryParse(name, out int index))
        {
            var usable = devices.Where(d => !d.Contains("loopback", StringComparison.OrdinalIgnoreCase)
                                            && !d.Contains("lo0", StringComparison.OrdinalIgnoreCase)).ToList();
            if (index >= 0 && index < usable.Count)
            {
                device = usable[index];
                detail = device;
                return true;
            }

            if (index >= 0 && index < devices.Count)
            {
                device = devices[index];
                detail = device;
                return true;
            }

            detail = $"网卡索引 {index} 超出范围（共 {devices.Count}）";
            return false;
        }

        var exact = devices.FirstOrDefault(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            device = exact;
            detail = device;
            return true;
        }

        var contains = devices.FirstOrDefault(d => d.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (contains != null)
        {
            device = contains;
            detail = device;
            return true;
        }

        // Windows Npcap 名称是 \Device\NPF_{GUID}，允许直接当设备名打开
        device = name;
        detail = name;
        return true;
    }

    private static byte[] ResolveMac(string pcapName)
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var bytes = nic.GetPhysicalAddress().GetAddressBytes();
                if (bytes.Length != 6 || bytes.All(b => b == 0))
                    continue;
                if (string.Equals(nic.Name, pcapName, StringComparison.OrdinalIgnoreCase)
                    || pcapName.Contains(nic.Id, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(nic.Description)
                        && pcapName.Contains(nic.Description, StringComparison.OrdinalIgnoreCase)))
                {
                    return bytes;
                }
            }
        }
        catch
        {
            // 回退本地管理地址，PortIdentity 仍稳定于本次进程
        }

        return new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x01 };
    }
}

internal static class PcapNative
{
    public const int ErrbufSize = 256;
    private static readonly bool Windows = OperatingSystem.IsWindows();

    public static bool IsAvailable
    {
        get
        {
            try
            {
                return Windows ? Win.Probe() : Unix.Probe();
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static IntPtr OpenLive(string device, int snaplen, int promiscuous, int timeoutMs, byte[] errbuf)
        => Windows
            ? Win.pcap_open_live(device, snaplen, promiscuous, timeoutMs, errbuf)
            : Unix.pcap_open_live(device, snaplen, promiscuous, timeoutMs, errbuf);

    public static void Close(IntPtr p)
    {
        if (p == IntPtr.Zero)
            return;
        if (Windows) Win.pcap_close(p);
        else Unix.pcap_close(p);
    }

    public static int NextEx(IntPtr p, out IntPtr header, out IntPtr data)
        => Windows ? Win.pcap_next_ex(p, out header, out data) : Unix.pcap_next_ex(p, out header, out data);

    public static int SendPacket(IntPtr p, byte[] buf, int size)
        => Windows ? Win.pcap_sendpacket(p, buf, size) : Unix.pcap_sendpacket(p, buf, size);

    public static bool TrySetFilter(IntPtr p, string filter, out string error)
    {
        error = string.Empty;
        IntPtr fp = Marshal.AllocHGlobal(64);
        try
        {
            for (int i = 0; i < 64; i++)
                Marshal.WriteByte(fp, i, 0);
            int rc = Windows
                ? Win.pcap_compile(p, fp, filter, 1, 0xFFFFFFFF)
                : Unix.pcap_compile(p, fp, filter, 1, 0xFFFFFFFF);
            if (rc != 0)
            {
                error = "pcap_compile 失败（ether proto 0x88f7）";
                return false;
            }

            rc = Windows ? Win.pcap_setfilter(p, fp) : Unix.pcap_setfilter(p, fp);
            if (rc != 0)
            {
                error = "pcap_setfilter 失败";
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(fp);
        }
    }

    public static int ReadCaplen(IntPtr hdr)
    {
        // Windows x64 Npcap: timeval 8 字节；Unix/macOS LP64: timeval 16 字节。
        int offset = Windows ? 8 : 16;
        return Marshal.ReadInt32(hdr, offset);
    }

    public static string ErrbufToString(byte[] errbuf)
    {
        int n = Array.IndexOf(errbuf, (byte)0);
        if (n < 0) n = errbuf.Length;
        return System.Text.Encoding.UTF8.GetString(errbuf, 0, n);
    }

    public static bool TryListDevices(out List<string> names, out string error)
    {
        names = new List<string>();
        error = string.Empty;
        var err = new byte[ErrbufSize];
        int rc = Windows ? Win.pcap_findalldevs(out IntPtr all, err) : Unix.pcap_findalldevs(out all, err);
        if (rc != 0 || all == IntPtr.Zero)
        {
            error = "pcap_findalldevs 失败";
            return false;
        }

        try
        {
            IntPtr cur = all;
            while (cur != IntPtr.Zero)
            {
                var namePtr = Marshal.ReadIntPtr(cur, IntPtr.Size);
                if (namePtr != IntPtr.Zero)
                    names.Add(Marshal.PtrToStringAnsi(namePtr) ?? "");
                cur = Marshal.ReadIntPtr(cur, 0);
            }
        }
        finally
        {
            if (Windows) Win.pcap_freealldevs(all);
            else Unix.pcap_freealldevs(all);
        }

        return names.Count > 0;
    }

    private static class Win
    {
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern IntPtr pcap_open_live(string device, int snaplen, int promisc, int to_ms, byte[] errbuf);

        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern void pcap_close(IntPtr p);

        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pcap_next_ex(IntPtr p, out IntPtr pkt_header, out IntPtr pkt_data);

        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pcap_sendpacket(IntPtr p, byte[] buf, int size);

        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int pcap_compile(IntPtr p, IntPtr fp, string str, int optimize, uint netmask);

        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pcap_setfilter(IntPtr p, IntPtr fp);

        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pcap_findalldevs(out IntPtr alldevs, byte[] errbuf);

        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern void pcap_freealldevs(IntPtr alldevs);

        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr pcap_lib_version();

        public static bool Probe()
        {
            var v = pcap_lib_version();
            return v != IntPtr.Zero;
        }
    }

    private static class Unix
    {
        [DllImport("pcap", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern IntPtr pcap_open_live(string device, int snaplen, int promisc, int to_ms, byte[] errbuf);

        [DllImport("pcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern void pcap_close(IntPtr p);

        [DllImport("pcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pcap_next_ex(IntPtr p, out IntPtr pkt_header, out IntPtr pkt_data);

        [DllImport("pcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pcap_sendpacket(IntPtr p, byte[] buf, int size);

        [DllImport("pcap", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int pcap_compile(IntPtr p, IntPtr fp, string str, int optimize, uint netmask);

        [DllImport("pcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pcap_setfilter(IntPtr p, IntPtr fp);

        [DllImport("pcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pcap_findalldevs(out IntPtr alldevs, byte[] errbuf);

        [DllImport("pcap", CallingConvention = CallingConvention.Cdecl)]
        public static extern void pcap_freealldevs(IntPtr alldevs);

        [DllImport("pcap", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr pcap_lib_version();

        public static bool Probe()
        {
            var v = pcap_lib_version();
            return v != IntPtr.Zero;
        }
    }
}
