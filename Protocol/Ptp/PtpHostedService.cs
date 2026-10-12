using EssSimulator.Configuration;
using log4net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EssSimulator.Protocol.Ptp;

/// <summary>
/// L2 P2P 软件从钟。Enabled=false 时不占网卡、不发报文，PCS 对时状态为 Disabled。
/// </summary>
public sealed class PtpHostedService : IHostedService, IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PtpHostedService));
    private readonly SimulatorConfig _cfg;
    private CancellationTokenSource? _run;
    private Task? _loop;
    private IPtpEthernetLink? _link;

    public PtpHostedService(IOptions<SimulatorConfig> opts)
    {
        _cfg = opts.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var ptp = _cfg.Protocol.Ptp ?? new PtpProtocolConfig();
        if (!ptp.Enabled)
        {
            PtpClockHub.Instance.Reset();
            return Task.CompletedTask;
        }

        _run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _run.Token;
        _loop = Task.Run(() => Run(ptp, ct), ct);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try { _run?.Cancel(); } catch { /* ignore */ }
        if (_loop != null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
            catch { /* shutdown */ }
        }

        DisposeLink();
        PtpClockHub.Instance.Reset();
    }

    public void Dispose()
    {
        try { _run?.Cancel(); } catch { /* ignore */ }
        DisposeLink();
        _run?.Dispose();
    }

    private void Run(PtpProtocolConfig ptp, CancellationToken ct)
    {
        try
        {
            if (!PcapPtpEthernetLink.TryResolveDevice(ptp.Interface, _cfg.Protocol.EmuIec61850GooseInterface,
                    out string device, out string resolveDetail))
            {
                Log.Warn($"[PTP] 未启动：{resolveDetail}");
                PublishUnlocked();
                WaitUntilCancelled(ct);
                return;
            }

            var clock = new PtpSoftwareClock();
            if (!PcapPtpEthernetLink.TryOpen(device, clock, out var link, out string openError) || link == null)
            {
                Log.Warn($"[PTP] 打开网卡「{device}」失败：{openError}。PCS 保持未对时，电气功能不受影响。");
                PublishUnlocked();
                WaitUntilCancelled(ct);
                return;
            }

            _link = link;
            var slave = new PtpP2PSlave(ptp, PtpWire.ClockIdentityFromMac(link.SourceMac));
            Log.Info($"[PTP] L2 P2P 软件从钟已启动 iface={device} domain={ptp.DomainNumber}");

            while (!ct.IsCancellationRequested)
            {
                int n = 0;
                while (n++ < 32 && link.TryReceive(out var frame, out long rxNs))
                    slave.OnEthernetFrame(frame, rxNs);

                long now = clock.NowNs;
                if (slave.TryCreatePdelayReq(now, out var tx))
                {
                    var wire = PtpWire.BuildEthernetFrame(tx.DestinationMac, link.SourceMac, tx.Payload);
                    long t1 = clock.NowNs;
                    if (link.TrySend(wire))
                        slave.NotePdelayReqTransmitted(tx.SequenceId, t1);
                }

                slave.Tick(now);
                PtpClockHub.Instance.Publish(slave.Snapshot);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            Log.Error("[PTP] 从钟循环异常（已隔离，不影响仿真）", ex);
        }
        finally
        {
            DisposeLink();
            PtpClockHub.Instance.Reset();
        }
    }

    private static void PublishUnlocked()
    {
        PtpClockHub.Instance.Publish(new PtpClockSnapshot
        {
            Status = PtpSyncStatus.Unlocked,
            LostAlarm = true,
            TimeAccuracyNs = 1_000_000_000
        });
    }

    private static void WaitUntilCancelled(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
                ct.WaitHandle.WaitOne(500);
        }
        catch (ObjectDisposedException)
        {
            // shutdown
        }
    }

    private void DisposeLink()
    {
        try { _link?.Dispose(); }
        catch (Exception ex) { Log.Debug("关闭 PTP 链路", ex); }
        _link = null;
    }
}
