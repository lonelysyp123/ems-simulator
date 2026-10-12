using log4net;

namespace EssSimulator.Protocol.Iec61850
{
    /// <summary>
    /// GOOSE 二层收发需要 raw 以太网。macOS 上 libiec61850 打不开 BPF 时会 abort，
    /// 必须在调用 GooseReceiver.Start 之前探测。
    /// </summary>
    internal static class Iec61850GooseEthernet
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Iec61850GooseEthernet));
        private static readonly object Gate = new();
        private static bool? _available;
        private static string _detail = string.Empty;

        public static bool CanUseRawEthernet(string? iface, out string detail)
        {
            if (string.IsNullOrWhiteSpace(iface))
            {
                detail = "未指定网卡";
                return false;
            }

            lock (Gate)
            {
                if (_available.HasValue)
                {
                    detail = _detail;
                    return _available.Value;
                }

                _available = Probe(out _detail);
                detail = _detail;
                if (!_available.Value)
                    Log.Warn($"[IEC61850] GOOSE 二层收发已跳过：{_detail}。MMS 不受影响。");
                return _available.Value;
            }
        }

        internal static void ResetCacheForTests()
        {
            lock (Gate)
            {
                _available = null;
                _detail = string.Empty;
            }
        }

        private static bool Probe(out string detail)
        {
            if (OperatingSystem.IsMacOS())
            {
                string last = "未找到 /dev/bpf*";
                foreach (var path in new[] { "/dev/bpf0", "/dev/bpf1", "/dev/bpf2" })
                {
                    if (!File.Exists(path))
                        continue;
                    try
                    {
                        using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                        detail = path;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        last = $"{path} 无法打开（{ex.Message}）。macOS GOOSE 需要 root 或 BPF 权限";
                    }
                }

                detail = last;
                return false;
            }

            detail = "ok";
            return true;
        }
    }
}
