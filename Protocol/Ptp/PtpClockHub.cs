namespace EssSimulator.Protocol.Ptp;

/// <summary>进程内 PTP 钟快照。默认 Disabled，未启用时 PCS 告警不置位。</summary>
public sealed class PtpClockHub
{
    public static PtpClockHub Instance { get; } = new();

    private readonly object _gate = new();
    private PtpClockSnapshot _current = PtpClockSnapshot.Disabled;

    public PtpClockSnapshot Current
    {
        get { lock (_gate) return _current; }
    }

    internal void Publish(PtpClockSnapshot snapshot)
    {
        lock (_gate) _current = snapshot;
    }

    internal void Reset()
    {
        lock (_gate) _current = PtpClockSnapshot.Disabled;
    }
}
