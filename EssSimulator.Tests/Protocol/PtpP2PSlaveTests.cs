using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.Protocol.Ptp;

namespace EssSimulator.Tests.Protocol;

public class PtpP2PSlaveTests
{
    private static readonly byte[] SlaveMac = { 0x02, 0x00, 0x00, 0x00, 0x00, 0x01 };
    private static readonly byte[] GmMac = { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };
    private static readonly byte[] GmClockId = PtpWire.ClockIdentityFromMac(GmMac);

    private static PtpP2PSlave NewSlave()
    {
        var cfg = new PtpProtocolConfig
        {
            Enabled = true,
            DomainNumber = 0,
            LockThresholdNs = 100_000,
            SyncTimeoutSec = 3,
            HoldoverLimitSec = 5,
            AcquireCycles = 3,
            PdelayIntervalMs = 1,
            PortNumber = 1
        };
        return new PtpP2PSlave(cfg, PtpWire.ClockIdentityFromMac(SlaveMac));
    }

    [Fact]
    public void Codec_RoundtripsTimestampAndHeader()
    {
        const long ns = 1_700_000_000_123L;
        var buf = new byte[10];
        PtpWire.WriteTimestamp(buf, 0, ns);
        Assert.True(PtpWire.TryReadTimestamp(buf, 0, out var back));
        Assert.Equal(ns, back);

        var payload = PtpWire.BuildPdelayReq(0, PtpWire.ClockIdentityFromMac(SlaveMac), 1, 7);
        Assert.True(PtpWire.TryParseHeader(payload, out var hdr));
        Assert.Equal(PtpMessageType.PdelayReq, hdr.MessageType);
        Assert.Equal((ushort)7, hdr.SequenceId);
        Assert.Equal(PtpWire.PdelayReqLength, hdr.MessageLength);
    }

    [Fact]
    public void Ethernet_StripsVlanTag()
    {
        var ptp = PtpWire.BuildPdelayReq(0, PtpWire.ClockIdentityFromMac(SlaveMac), 1, 1);
        var inner = PtpWire.BuildEthernetFrame(PtpWire.PeerDelayMac, SlaveMac, ptp);
        var vlan = new byte[inner.Length + 4];
        inner.AsSpan(0, 12).CopyTo(vlan);
        vlan[12] = 0x81;
        vlan[13] = 0x00;
        vlan[14] = 0x00;
        vlan[15] = 0x00;
        inner.AsSpan(12).CopyTo(vlan.AsSpan(16));

        Assert.True(PtpWire.TryGetPtpPayload(vlan, out var payload));
        Assert.True(PtpWire.TryParseHeader(payload, out var hdr));
        Assert.Equal(PtpMessageType.PdelayReq, hdr.MessageType);
    }

    [Fact]
    public void Ignores_WrongDomain()
    {
        var slave = NewSlave();
        long t1 = 1_000_000_000;
        Assert.True(slave.TryCreatePdelayReq(t1, out var req));
        slave.NotePdelayReqTransmitted(req.SequenceId, t1);

        var resp = BuildPdelayRespOneStep(slave, req.SequenceId, t2: t1 + 800, turnaroundNs: 100, domain: 4);
        slave.OnEthernetFrame(Wrap(resp), t1 + 1700);
        Assert.False(slave.Snapshot.MeanPathDelayNs > 0);
    }

    [Fact]
    public void P2P_TwoStepSync_LocksThenHoldover()
    {
        var slave = NewSlave();
        long t = 10_000_000_000;

        Assert.True(slave.TryCreatePdelayReq(t, out var req));
        slave.NotePdelayReqTransmitted(req.SequenceId, t);
        const long delay = 800;
        var resp = BuildPdelayRespOneStep(slave, req.SequenceId, t2: t + delay, turnaroundNs: 100, domain: 0);
        slave.OnEthernetFrame(Wrap(resp), t + delay + 100 + delay);

        Assert.InRange(slave.Snapshot.MeanPathDelayNs, delay - 50, delay + 50);

        slave.OnEthernetFrame(Wrap(BuildAnnounce()), t + 2_000_000);

        for (int i = 0; i < 4; i++)
        {
            long t1Master = 20_000_000_000 + i * 1_000_000_000L;
            long rx = t1Master + slave.Snapshot.MeanPathDelayNs;
            slave.OnEthernetFrame(Wrap(BuildSyncTwoStep(i)), rx);
            slave.OnEthernetFrame(Wrap(BuildFollowUp(i, t1Master)), rx + 50);
            slave.Tick(rx + 100);
        }

        Assert.Equal(PtpSyncStatus.Locked, slave.Snapshot.Status);
        Assert.False(slave.Snapshot.LostAlarm);
        Assert.InRange(Math.Abs(slave.Snapshot.OffsetFromMasterNs), 0, 1000);

        long later = 20_000_000_000L + 4_000_000_000L + 4_000_000_000L;
        slave.Tick(later);
        Assert.Equal(PtpSyncStatus.Holdover, slave.Snapshot.Status);
        Assert.True(slave.Snapshot.LostAlarm);

        slave.Tick(later + 6_000_000_000L);
        Assert.Equal(PtpSyncStatus.Unlocked, slave.Snapshot.Status);
    }

    [Fact]
    public void Hub_DefaultIsDisabled_NoLostAlarm()
    {
        PtpClockHub.Instance.Reset();
        var snap = PtpClockHub.Instance.Current;
        Assert.Equal(PtpSyncStatus.Disabled, snap.Status);
        Assert.False(snap.LostAlarm);
        Assert.False(snap.Enabled);
    }

    [Fact]
    public void MapPcsState_CopiesHubSnapshot_AndResetsWithoutAlarm()
    {
        try
        {
            PtpClockHub.Instance.Publish(new PtpClockSnapshot
            {
                Status = PtpSyncStatus.Locked,
                OffsetFromMasterNs = 42,
                MeanPathDelayNs = 800,
                TimeAccuracyNs = 100_000,
                LostAlarm = false
            });
            var dst = new PcsData();
            PcsMapper.MapPcsState(new PcsState(), dst, null!);
            Assert.Equal((ushort)PtpSyncStatus.Locked, dst.PtpSyncStatus);
            Assert.Equal(42, dst.PtpOffsetFromMasterNs);
            Assert.Equal(800, dst.PtpMeanPathDelayNs);
            Assert.False(dst.PtpLostAlarm);
        }
        finally
        {
            PtpClockHub.Instance.Reset();
            var dst = new PcsData();
            PcsMapper.MapPcsState(new PcsState(), dst, null!);
            Assert.Equal((ushort)PtpSyncStatus.Disabled, dst.PtpSyncStatus);
            Assert.False(dst.PtpLostAlarm);
        }
    }

    private static byte[] Wrap(byte[] ptp) =>
        PtpWire.BuildEthernetFrame(PtpWire.PeerDelayMac, GmMac, ptp);

    private static byte[] BuildPdelayRespOneStep(PtpP2PSlave slave, ushort seq, long t2, long turnaroundNs, byte domain)
    {
        long corr = PtpWire.CorrectionFromNs(turnaroundNs);
        var buf = PtpWire.BuildHeader(
            PtpMessageType.PdelayResp, PtpWire.PdelayRespLength, domain, flags: 0, corr,
            GmClockId, portNumber: 1, seq, control: 5, logInterval: unchecked((sbyte)0x7F));
        PtpWire.WriteTimestamp(buf, PtpWire.HeaderLength, t2);
        slave.ClockIdentity.CopyTo(buf, PtpWire.HeaderLength + PtpWire.TimestampLength);
        PtpWire.WriteU16(buf, PtpWire.HeaderLength + PtpWire.TimestampLength + 8, slave.PortNumber);
        return buf;
    }

    private static byte[] BuildAnnounce()
    {
        var buf = PtpWire.BuildHeader(
            PtpMessageType.Announce, PtpWire.AnnounceLength, 0, flags: 0, 0,
            GmClockId, 1, 1, control: 5, logInterval: 0);
        buf[48] = 6;
        return buf;
    }

    private static byte[] BuildSyncTwoStep(int seq)
    {
        return PtpWire.BuildHeader(
            PtpMessageType.Sync, PtpWire.SyncLength, 0, PtpWire.TwoStepFlag, 0,
            GmClockId, 1, (ushort)seq, control: 0, logInterval: 0);
    }

    private static byte[] BuildFollowUp(int seq, long originNs)
    {
        var buf = PtpWire.BuildHeader(
            PtpMessageType.FollowUp, PtpWire.FollowUpLength, 0, 0, 0,
            GmClockId, 1, (ushort)seq, control: 2, logInterval: 0);
        PtpWire.WriteTimestamp(buf, PtpWire.HeaderLength, originNs);
        return buf;
    }
}
