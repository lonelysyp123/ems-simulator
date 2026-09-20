using EssSimulator.Configuration;

namespace EssSimulator.Protocol.Ptp;

/// <summary>
/// IEEE 1588 Ordinary Clock，Slave-only，P2P 时延，软件戳。
/// 不改操作系统时钟；电气仿真不调用本类。
/// </summary>
internal sealed class PtpP2PSlave
{
    private const long LockedAccuracyNs = 100_000;
    private const long AcquiringAccuracyNs = 1_000_000;
    private const long UnlockedAccuracyNs = 1_000_000_000;
    private const double OffsetServoGain = 0.35;
    private const double DelayServoGain = 0.25;

    private readonly PtpProtocolConfig _cfg;
    private readonly byte[] _clockIdentity;
    private readonly ushort _portNumber;
    private readonly object _gate = new();

    private PtpSyncStatus _status = PtpSyncStatus.Unlocked;
    private long _clockOffsetNs;
    private long _meanPathDelayNs;
    private bool _delayValid;
    private long _residualOffsetNs;
    private int _goodCycles;
    private long _lastSyncLocalNs;
    private long _lastPdelayTxLocalNs;
    private long _holdoverStartLocalNs;
    private long _lastTickLocalNs;
    private ushort _pdelaySeq;
    private ushort _pendingPdelaySeq;
    private long _pendingPdelayT1Ns;
    private bool _pdelayAwaitingResp;
    private bool _pdelayTwoStep;
    private long _pdelayT2Ns;
    private long _pdelayT4Ns;
    private long _pdelayRespCorrectionNs;
    private byte[]? _gmClockId;
    private byte _gmClockClass;
    private PendingSync? _pendingSync;
    private bool _offsetInitialized;

    private readonly struct PendingSync
    {
        public byte[] SourceClockId { get; init; }
        public ushort SequenceId { get; init; }
        public ushort SourcePort { get; init; }
        public long RxLocalNs { get; init; }
        public double CorrectionNs { get; init; }
        public long OriginNs { get; init; }
        public bool HasOrigin { get; init; }
    }

    public PtpP2PSlave(PtpProtocolConfig cfg, ReadOnlySpan<byte> clockIdentity)
    {
        _cfg = cfg;
        _clockIdentity = clockIdentity.Length >= 8 ? clockIdentity[..8].ToArray() : new byte[8];
        _portNumber = cfg.PortNumber == 0 ? (ushort)1 : cfg.PortNumber;
    }

    public byte[] ClockIdentity => _clockIdentity;
    public ushort PortNumber => _portNumber;

    public PtpClockSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return BuildSnapshotUnlocked(_lastTickLocalNs);
            }
        }
    }

    public void Tick(long nowLocalNs)
    {
        lock (_gate)
        {
            ApplyHoldoverDriftUnlocked(nowLocalNs);
            UpdateStateFromTimeoutsUnlocked(nowLocalNs);
            _lastTickLocalNs = nowLocalNs;
        }
    }

    public bool TryCreatePdelayReq(long nowLocalNs, out PtpTxFrame frame)
    {
        frame = default;
        lock (_gate)
        {
            long intervalNs = Math.Max(100, _cfg.PdelayIntervalMs) * 1_000_000L;
            if (_lastPdelayTxLocalNs != 0 && nowLocalNs - _lastPdelayTxLocalNs < intervalNs)
                return false;

            _pdelaySeq++;
            if (_pdelaySeq == 0)
                _pdelaySeq = 1;

            var payload = PtpWire.BuildPdelayReq(_cfg.DomainNumber, _clockIdentity, _portNumber, _pdelaySeq);
            frame = new PtpTxFrame
            {
                DestinationMac = PtpWire.PeerDelayMac,
                Payload = payload,
                SequenceId = _pdelaySeq
            };
            return true;
        }
    }

    public void NotePdelayReqTransmitted(ushort sequenceId, long txLocalNs)
    {
        lock (_gate)
        {
            _pendingPdelaySeq = sequenceId;
            _pendingPdelayT1Ns = txLocalNs;
            _pdelayAwaitingResp = true;
            _pdelayTwoStep = false;
            _lastPdelayTxLocalNs = txLocalNs;
        }
    }

    public void OnEthernetFrame(ReadOnlySpan<byte> frame, long rxLocalNs)
    {
        if (!PtpWire.TryGetPtpPayload(frame, out var payload))
            return;
        OnPtpPayload(payload, rxLocalNs);
    }

    public void OnPtpPayload(ReadOnlySpan<byte> ptp, long rxLocalNs)
    {
        if (!PtpWire.TryParseHeader(ptp, out var hdr))
            return;
        if (hdr.DomainNumber != _cfg.DomainNumber)
            return;
        if (PtpWire.ClockIdentityEquals(hdr.SourceClockIdentity, _clockIdentity))
            return;

        lock (_gate)
        {
            switch (hdr.MessageType)
            {
                case PtpMessageType.Announce:
                    OnAnnounceUnlocked(hdr, ptp);
                    break;
                case PtpMessageType.Sync:
                    OnSyncUnlocked(hdr, ptp, rxLocalNs);
                    break;
                case PtpMessageType.FollowUp:
                    OnFollowUpUnlocked(hdr, ptp);
                    break;
                case PtpMessageType.PdelayResp:
                    OnPdelayRespUnlocked(hdr, ptp, rxLocalNs);
                    break;
                case PtpMessageType.PdelayRespFollowUp:
                    OnPdelayRespFollowUpUnlocked(hdr, ptp);
                    break;
            }

            _lastTickLocalNs = rxLocalNs;
        }
    }

    private void OnAnnounceUnlocked(PtpHeader hdr, ReadOnlySpan<byte> ptp)
    {
        if (ptp.Length < PtpWire.AnnounceLength)
            return;
        byte clockClass = ptp[48];
        if (_gmClockId == null || PtpWire.ClockIdentityEquals(_gmClockId, hdr.SourceClockIdentity))
        {
            _gmClockId = hdr.SourceClockIdentity.ToArray();
            _gmClockClass = clockClass;
        }
    }

    private void OnSyncUnlocked(PtpHeader hdr, ReadOnlySpan<byte> ptp, long rxLocalNs)
    {
        if (ptp.Length < PtpWire.SyncLength)
            return;

        bool hasOrigin = PtpWire.TryReadTimestamp(ptp, PtpWire.HeaderLength, out var originNs) && originNs != 0;
        var pending = new PendingSync
        {
            SourceClockId = hdr.SourceClockIdentity.ToArray(),
            SequenceId = hdr.SequenceId,
            SourcePort = hdr.SourcePortNumber,
            RxLocalNs = rxLocalNs,
            CorrectionNs = PtpWire.CorrectionToNs(hdr.CorrectionField),
            OriginNs = originNs,
            HasOrigin = hasOrigin
        };

        if (hdr.TwoStep)
        {
            _pendingSync = pending;
            return;
        }

        if (!hasOrigin)
            return;
        ApplySyncUnlocked(pending, originNs, pending.CorrectionNs);
    }

    private void OnFollowUpUnlocked(PtpHeader hdr, ReadOnlySpan<byte> ptp)
    {
        if (_pendingSync is not { } pending)
            return;
        if (pending.SequenceId != hdr.SequenceId || pending.SourcePort != hdr.SourcePortNumber)
            return;
        if (!PtpWire.ClockIdentityEquals(pending.SourceClockId, hdr.SourceClockIdentity))
            return;
        if (!PtpWire.TryReadTimestamp(ptp, PtpWire.HeaderLength, out var originNs))
            return;

        double corr = pending.CorrectionNs + PtpWire.CorrectionToNs(hdr.CorrectionField);
        ApplySyncUnlocked(pending, originNs, corr);
        _pendingSync = null;
    }

    private void ApplySyncUnlocked(PendingSync pending, long t1Ns, double correctionNs)
    {
        if (!_delayValid)
        {
            if (_status == PtpSyncStatus.Unlocked)
                _status = PtpSyncStatus.Acquiring;
            _lastSyncLocalNs = pending.RxLocalNs;
            return;
        }

        double masterAtRx = t1Ns + correctionNs + _meanPathDelayNs;
        double measuredOffset = pending.RxLocalNs - masterAtRx - _cfg.AsymmetryCompensationNs;

        if (!_offsetInitialized)
        {
            _clockOffsetNs = (long)Math.Round(measuredOffset);
            _offsetInitialized = true;
        }
        else
        {
            double err = measuredOffset - _clockOffsetNs;
            _clockOffsetNs += (long)Math.Round(OffsetServoGain * err);
        }

        _residualOffsetNs = (long)Math.Round(measuredOffset - _clockOffsetNs);
        _lastSyncLocalNs = pending.RxLocalNs;
        _gmClockId ??= pending.SourceClockId.ToArray();

        long absResidual = Math.Abs(_residualOffsetNs);
        int need = Math.Max(1, _cfg.AcquireCycles);
        if (absResidual <= _cfg.LockThresholdNs)
        {
            _goodCycles++;
            if (_goodCycles >= need)
            {
                _status = PtpSyncStatus.Locked;
                _holdoverStartLocalNs = 0;
            }
            else if (_status != PtpSyncStatus.Locked)
            {
                _status = PtpSyncStatus.Acquiring;
            }
        }
        else
        {
            _goodCycles = 0;
            if (_status == PtpSyncStatus.Locked)
                _status = PtpSyncStatus.Acquiring;
            else if (_status == PtpSyncStatus.Unlocked || _status == PtpSyncStatus.Holdover)
                _status = PtpSyncStatus.Acquiring;
        }
    }

    private void OnPdelayRespUnlocked(PtpHeader hdr, ReadOnlySpan<byte> ptp, long rxLocalNs)
    {
        if (!_pdelayAwaitingResp || hdr.SequenceId != _pendingPdelaySeq)
            return;
        if (ptp.Length < PtpWire.PdelayRespLength)
            return;
        if (!PtpWire.TryReadPortIdentity(ptp, PtpWire.HeaderLength + PtpWire.TimestampLength, out var reqId, out var reqPort))
            return;
        if (!PtpWire.ClockIdentityEquals(reqId, _clockIdentity) || reqPort != _portNumber)
            return;
        if (!PtpWire.TryReadTimestamp(ptp, PtpWire.HeaderLength, out var t2Ns))
            return;

        _pdelayT2Ns = t2Ns;
        _pdelayT4Ns = rxLocalNs;
        _pdelayRespCorrectionNs = (long)Math.Round(PtpWire.CorrectionToNs(hdr.CorrectionField));

        if (hdr.TwoStep)
        {
            _pdelayTwoStep = true;
            return;
        }

        // 一步法：correction 含 (t3-t2)，t3 = t2 + correction
        long t3Ns = t2Ns + _pdelayRespCorrectionNs;
        FinishPdelayUnlocked(_pendingPdelayT1Ns, t2Ns, t3Ns, rxLocalNs);
    }

    private void OnPdelayRespFollowUpUnlocked(PtpHeader hdr, ReadOnlySpan<byte> ptp)
    {
        if (!_pdelayAwaitingResp || !_pdelayTwoStep || hdr.SequenceId != _pendingPdelaySeq)
            return;
        if (!PtpWire.TryReadPortIdentity(ptp, PtpWire.HeaderLength + PtpWire.TimestampLength, out var reqId, out var reqPort))
            return;
        if (!PtpWire.ClockIdentityEquals(reqId, _clockIdentity) || reqPort != _portNumber)
            return;
        if (!PtpWire.TryReadTimestamp(ptp, PtpWire.HeaderLength, out var t3Ns))
            return;

        t3Ns += (long)Math.Round(PtpWire.CorrectionToNs(hdr.CorrectionField));
        FinishPdelayUnlocked(_pendingPdelayT1Ns, _pdelayT2Ns, t3Ns, _pdelayT4Ns);
    }

    private void FinishPdelayUnlocked(long t1, long t2, long t3, long t4)
    {
        _pdelayAwaitingResp = false;
        _pdelayTwoStep = false;
        double sample = ((t2 - t1) + (t4 - t3)) / 2.0;
        if (sample < 0 || sample > 1_000_000_000)
            return;

        if (!_delayValid)
        {
            _meanPathDelayNs = (long)Math.Round(sample);
            _delayValid = true;
        }
        else
        {
            _meanPathDelayNs += (long)Math.Round(DelayServoGain * (sample - _meanPathDelayNs));
        }
    }

    private void ApplyHoldoverDriftUnlocked(long nowLocalNs)
    {
        if (_status != PtpSyncStatus.Holdover || _lastTickLocalNs == 0 || nowLocalNs <= _lastTickLocalNs)
            return;
        long dtNs = nowLocalNs - _lastTickLocalNs;
        _clockOffsetNs += (long)Math.Round(dtNs * (_cfg.OscillatorDriftPpb / 1_000_000_000.0));
    }

    private void UpdateStateFromTimeoutsUnlocked(long nowLocalNs)
    {
        if (_lastSyncLocalNs == 0)
        {
            if (_status != PtpSyncStatus.Disabled)
                _status = PtpSyncStatus.Unlocked;
            return;
        }

        long syncTimeoutNs = Math.Max(1, _cfg.SyncTimeoutSec) * 1_000_000_000L;
        long sinceSync = nowLocalNs - _lastSyncLocalNs;
        if (sinceSync <= syncTimeoutNs)
            return;

        if (_status == PtpSyncStatus.Locked || _status == PtpSyncStatus.Acquiring)
        {
            _status = PtpSyncStatus.Holdover;
            _holdoverStartLocalNs = nowLocalNs;
            _goodCycles = 0;
            return;
        }

        if (_status == PtpSyncStatus.Holdover)
        {
            long holdNs = Math.Max(1, _cfg.HoldoverLimitSec) * 1_000_000_000L;
            if (nowLocalNs - _holdoverStartLocalNs >= holdNs)
            {
                _status = PtpSyncStatus.Unlocked;
                _offsetInitialized = false;
                _goodCycles = 0;
            }
        }
    }

    private PtpClockSnapshot BuildSnapshotUnlocked(long nowLocalNs)
    {
        long ptpNs = nowLocalNs == 0 ? 0 : nowLocalNs - _clockOffsetNs;
        long accuracy = _status switch
        {
            PtpSyncStatus.Locked => LockedAccuracyNs,
            PtpSyncStatus.Acquiring => AcquiringAccuracyNs,
            PtpSyncStatus.Holdover => HoldoverAccuracyUnlocked(nowLocalNs),
            _ => UnlockedAccuracyNs
        };

        return new PtpClockSnapshot
        {
            Status = _status,
            OffsetFromMasterNs = _residualOffsetNs,
            MeanPathDelayNs = _meanPathDelayNs,
            TimeAccuracyNs = accuracy,
            LocalTimeUnixNs = ptpNs,
            LostAlarm = _status is PtpSyncStatus.Unlocked or PtpSyncStatus.Holdover,
            ClockClass = _gmClockClass,
            SequenceId = _pdelaySeq
        };
    }

    private long HoldoverAccuracyUnlocked(long nowLocalNs)
    {
        long elapsed = _holdoverStartLocalNs == 0 ? 0 : Math.Max(0, nowLocalNs - _holdoverStartLocalNs);
        long drift = (long)Math.Round(elapsed * (_cfg.OscillatorDriftPpb / 1_000_000_000.0));
        return LockedAccuracyNs + Math.Abs(drift);
    }
}
