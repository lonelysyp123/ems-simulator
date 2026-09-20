namespace EssSimulator.Protocol.Ptp;

/// <summary>IEEE 1588-2008 报文编解码（大端）。不涉及网卡。</summary>
internal static class PtpWire
{
    public const int HeaderLength = 34;
    public const int TimestampLength = 10;
    public const int PortIdentityLength = 10;
    public const int SyncLength = 44;
    public const int FollowUpLength = 44;
    public const int AnnounceLength = 64;
    public const int PdelayReqLength = 54;
    public const int PdelayRespLength = 54;
    public const byte Version2 = 2;
    public const ushort TwoStepFlag = 0x0200;
    public const ushort EtherType = 0x88F7;
    public const int EthernetHeaderLength = 14;
    public const int VlanTagLength = 4;
    public const ushort VlanEtherType = 0x8100;

    public static readonly byte[] PeerDelayMac =
        { 0x01, 0x80, 0xC2, 0x00, 0x00, 0x0E };

    public static readonly byte[] NonPeerMulticastMac =
        { 0x01, 0x1B, 0x19, 0x00, 0x00, 0x00 };

    public static bool TryParseHeader(ReadOnlySpan<byte> ptp, out PtpHeader header)
    {
        header = default;
        if (ptp.Length < HeaderLength)
            return false;

        byte tsmt = ptp[0];
        byte ver = (byte)(ptp[1] & 0x0F);
        if (ver != Version2)
            return false;

        ushort length = ReadU16(ptp, 2);
        if (length < HeaderLength)
            return false;

        var clockId = new byte[8];
        ptp.Slice(20, 8).CopyTo(clockId);

        header = new PtpHeader
        {
            TransportSpecific = (byte)(tsmt >> 4),
            MessageType = (PtpMessageType)(tsmt & 0x0F),
            Version = ver,
            MessageLength = length,
            DomainNumber = ptp[4],
            FlagField = ReadU16(ptp, 6),
            CorrectionField = ReadI64(ptp, 8),
            SourceClockIdentity = clockId,
            SourcePortNumber = ReadU16(ptp, 28),
            SequenceId = ReadU16(ptp, 30),
            Control = ptp[32],
            LogMessageInterval = unchecked((sbyte)ptp[33])
        };
        return true;
    }

    public static bool TryReadTimestamp(ReadOnlySpan<byte> ptp, int offset, out long unixNs)
    {
        unixNs = 0;
        if (ptp.Length < offset + TimestampLength)
            return false;
        ulong seconds = ReadU48(ptp, offset);
        uint nano = ReadU32(ptp, offset + 6);
        if (nano >= 1_000_000_000)
            return false;
        unixNs = (long)seconds * 1_000_000_000L + nano;
        return true;
    }

    public static void WriteTimestamp(Span<byte> dest, int offset, long unixNs)
    {
        unixNs = Math.Max(0, unixNs);
        ulong seconds = (ulong)(unixNs / 1_000_000_000L);
        uint nano = (uint)(unixNs % 1_000_000_000L);
        WriteU48(dest, offset, seconds);
        WriteU32(dest, offset + 6, nano);
    }

    public static double CorrectionToNs(long correctionField) => correctionField / 65536.0;

    public static long CorrectionFromNs(double ns) => (long)Math.Round(ns * 65536.0);

    public static bool ClockIdentityEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        => a.Length >= 8 && b.Length >= 8 && a[..8].SequenceEqual(b[..8]);

    public static bool TryReadPortIdentity(ReadOnlySpan<byte> ptp, int offset, out byte[] clockId, out ushort port)
    {
        clockId = Array.Empty<byte>();
        port = 0;
        if (ptp.Length < offset + PortIdentityLength)
            return false;
        clockId = ptp.Slice(offset, 8).ToArray();
        port = ReadU16(ptp, offset + 8);
        return true;
    }

    public static byte[] BuildHeader(
        PtpMessageType type,
        ushort messageLength,
        byte domain,
        ushort flags,
        long correction,
        ReadOnlySpan<byte> clockIdentity,
        ushort portNumber,
        ushort sequenceId,
        byte control,
        sbyte logInterval)
    {
        var buf = new byte[messageLength];
        buf[0] = (byte)((byte)type & 0x0F);
        buf[1] = Version2;
        WriteU16(buf, 2, messageLength);
        buf[4] = domain;
        WriteU16(buf, 6, flags);
        WriteI64(buf, 8, correction);
        clockIdentity[..8].CopyTo(buf.AsSpan(20, 8));
        WriteU16(buf, 28, portNumber);
        WriteU16(buf, 30, sequenceId);
        buf[32] = control;
        buf[33] = unchecked((byte)logInterval);
        return buf;
    }

    public static byte[] BuildPdelayReq(
        byte domain,
        ReadOnlySpan<byte> clockIdentity,
        ushort portNumber,
        ushort sequenceId)
    {
        var buf = BuildHeader(
            PtpMessageType.PdelayReq, PdelayReqLength, domain, 0, 0,
            clockIdentity, portNumber, sequenceId, control: 5, logInterval: unchecked((sbyte)0x7F));
        return buf;
    }

    /// <summary>从以太网帧取出 PTP 载荷（可剥 VLAN）。</summary>
    public static bool TryGetPtpPayload(ReadOnlySpan<byte> frame, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (frame.Length < EthernetHeaderLength + HeaderLength)
            return false;

        int offset = 12;
        ushort type = ReadU16(frame, offset);
        offset += 2;
        if (type == VlanEtherType)
        {
            if (frame.Length < EthernetHeaderLength + VlanTagLength + HeaderLength)
                return false;
            type = ReadU16(frame, 16);
            offset = 18;
        }

        if (type != EtherType)
            return false;
        payload = frame[offset..];
        return payload.Length >= HeaderLength;
    }

    public static byte[] BuildEthernetFrame(ReadOnlySpan<byte> destMac, ReadOnlySpan<byte> srcMac, ReadOnlySpan<byte> ptp)
    {
        int len = Math.Max(60, EthernetHeaderLength + ptp.Length);
        var frame = new byte[len];
        destMac[..6].CopyTo(frame);
        srcMac[..6].CopyTo(frame.AsSpan(6));
        WriteU16(frame, 12, EtherType);
        ptp.CopyTo(frame.AsSpan(EthernetHeaderLength));
        return frame;
    }

    public static byte[] ClockIdentityFromMac(ReadOnlySpan<byte> mac)
    {
        var id = new byte[8];
        id[0] = mac[0];
        id[1] = mac[1];
        id[2] = mac[2];
        id[3] = 0xFF;
        id[4] = 0xFE;
        id[5] = mac[3];
        id[6] = mac[4];
        id[7] = mac[5];
        return id;
    }

    public static ushort ReadU16(ReadOnlySpan<byte> s, int o) =>
        (ushort)((s[o] << 8) | s[o + 1]);

    public static uint ReadU32(ReadOnlySpan<byte> s, int o) =>
        (uint)((s[o] << 24) | (s[o + 1] << 16) | (s[o + 2] << 8) | s[o + 3]);

    public static ulong ReadU48(ReadOnlySpan<byte> s, int o) =>
        ((ulong)s[o] << 40) | ((ulong)s[o + 1] << 32) | ((ulong)s[o + 2] << 24)
        | ((ulong)s[o + 3] << 16) | ((ulong)s[o + 4] << 8) | s[o + 5];

    public static long ReadI64(ReadOnlySpan<byte> s, int o)
    {
        ulong u = ((ulong)ReadU32(s, o) << 32) | ReadU32(s, o + 4);
        return unchecked((long)u);
    }

    public static void WriteU16(Span<byte> s, int o, ushort v)
    {
        s[o] = (byte)(v >> 8);
        s[o + 1] = (byte)v;
    }

    public static void WriteU32(Span<byte> s, int o, uint v)
    {
        s[o] = (byte)(v >> 24);
        s[o + 1] = (byte)(v >> 16);
        s[o + 2] = (byte)(v >> 8);
        s[o + 3] = (byte)v;
    }

    public static void WriteU48(Span<byte> s, int o, ulong v)
    {
        s[o] = (byte)(v >> 40);
        s[o + 1] = (byte)(v >> 32);
        s[o + 2] = (byte)(v >> 24);
        s[o + 3] = (byte)(v >> 16);
        s[o + 4] = (byte)(v >> 8);
        s[o + 5] = (byte)v;
    }

    public static void WriteI64(Span<byte> s, int o, long v)
    {
        ulong u = unchecked((ulong)v);
        WriteU32(s, o, (uint)(u >> 32));
        WriteU32(s, o + 4, (uint)u);
    }
}
