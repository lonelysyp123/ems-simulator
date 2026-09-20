namespace EssSimulator.Protocol.Ptp;

public enum PtpSyncStatus : ushort
{
    Disabled = 0,
    Unlocked = 1,
    Acquiring = 2,
    Locked = 3,
    Holdover = 4
}

public enum PtpMessageType : byte
{
    Sync = 0x0,
    DelayReq = 0x1,
    PdelayReq = 0x2,
    PdelayResp = 0x3,
    FollowUp = 0x8,
    DelayResp = 0x9,
    PdelayRespFollowUp = 0xA,
    Announce = 0xB
}

public readonly struct PtpClockSnapshot
{
    public static PtpClockSnapshot Disabled { get; } = new()
    {
        Status = PtpSyncStatus.Disabled,
        TimeAccuracyNs = 0
    };

    public PtpSyncStatus Status { get; init; }
    public long OffsetFromMasterNs { get; init; }
    public long MeanPathDelayNs { get; init; }
    public long TimeAccuracyNs { get; init; }
    public long LocalTimeUnixNs { get; init; }
    public bool LostAlarm { get; init; }
    public byte ClockClass { get; init; }
    public ushort SequenceId { get; init; }

    public bool Enabled => Status != PtpSyncStatus.Disabled;
}

public readonly struct PtpHeader
{
    public byte TransportSpecific { get; init; }
    public PtpMessageType MessageType { get; init; }
    public byte Version { get; init; }
    public ushort MessageLength { get; init; }
    public byte DomainNumber { get; init; }
    public ushort FlagField { get; init; }
    public long CorrectionField { get; init; }
    public byte[] SourceClockIdentity { get; init; }
    public ushort SourcePortNumber { get; init; }
    public ushort SequenceId { get; init; }
    public byte Control { get; init; }
    public sbyte LogMessageInterval { get; init; }

    public bool TwoStep => (FlagField & PtpWire.TwoStepFlag) != 0;
}

public readonly struct PtpTxFrame
{
    public required byte[] DestinationMac { get; init; }
    public required byte[] Payload { get; init; }
    public required ushort SequenceId { get; init; }
}
