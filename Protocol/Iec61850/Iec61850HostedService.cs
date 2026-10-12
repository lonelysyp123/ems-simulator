using EssSimulator.Configuration;
using log4net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EssSimulator.Protocol.Iec61850
{
    /// <summary>在 EMU Modbus/点影子就绪后启动各 PCS 的 IEC 61850 IED。</summary>
    public sealed class Iec61850HostedService : IHostedService
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Iec61850HostedService));
        private readonly SimulatorConfig _cfg;
        private CancellationTokenSource? _run;

        public Iec61850HostedService(IOptions<SimulatorConfig> opts)
        {
            _cfg = opts.Value;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ct = _run.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_cfg.EffectiveEssUnitCount <= 0)
                        return;

                    var store = Core.SimulatorHost.Instance;
                    for (int attempt = 0; attempt < 120 && !ct.IsCancellationRequested; attempt++)
                    {
                        var emu = store.Get<ModbusSimServer>("simEmu1");
                        if (emu != null && emu.IsDataPathReady)
                            break;
                        await Task.Delay(250, ct).ConfigureAwait(false);
                    }

                    if (ct.IsCancellationRequested)
                        return;

                    Iec61850LayerManager.Instance.StartAll(_cfg);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log.Error("IEC 61850 IED 启动失败", ex);
                }
            }, ct);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try { _run?.Cancel(); } catch { /* ignore */ }
            try { Iec61850LayerManager.Instance.StopAll(); }
            catch (Exception ex) { Log.Warn("关闭 IEC 61850 服务时异常", ex); }
            _run?.Dispose();
            return Task.CompletedTask;
        }
    }
}
