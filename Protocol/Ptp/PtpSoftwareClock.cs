using System.Diagnostics;

namespace EssSimulator.Protocol.Ptp;

/// <summary>模拟器侧软件时钟：Stopwatch 单调钟映射到 Unix 纳秒，不驯服 OS 时钟。</summary>
internal sealed class PtpSoftwareClock
{
    private readonly long _frequency;
    private readonly long _startStamp;
    private readonly long _startUnixNs;

    public PtpSoftwareClock(DateTimeOffset? startUtc = null)
    {
        _frequency = Stopwatch.Frequency;
        _startStamp = Stopwatch.GetTimestamp();
        var start = startUtc ?? DateTimeOffset.UtcNow;
        _startUnixNs = (start - DateTimeOffset.UnixEpoch).Ticks * 100;
    }

    public long NowNs
    {
        get
        {
            long delta = Stopwatch.GetTimestamp() - _startStamp;
            return _startUnixNs + delta * 1_000_000_000L / _frequency;
        }
    }
}
