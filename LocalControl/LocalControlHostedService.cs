using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.DataExchange.Config;
using EssSimulator.Protocol.Modbus;
using log4net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// LocalControl 独立托管服务：启动 simLc* Modbus 从站并运行 EMU↔LC 报文转发引擎。
    /// 与仿真主路径解耦，仅在 <see cref="ProtocolConfig.EnableLocalControl"/> 为 true 时启用。
    /// </summary>
    public sealed class LocalControlHostedService : BackgroundService
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(LocalControlHostedService));
        private readonly SimulatorConfig _cfg;
        private readonly DataExchangeOptions _dataExchange;
        private readonly List<LocalControlModbusServer> _servers = new();
        private readonly LcRuntimeBase _runtime;

        public LocalControlHostedService(
            IOptions<SimulatorConfig> simOptions,
            IOptions<DataExchangeOptions> dataExchangeOptions)
        {
            _cfg = simOptions.Value;
            _dataExchange = dataExchangeOptions.Value;
            _runtime = LcRuntimeFactory.Create(Log);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_cfg.Protocol.EnableLocalControl)
                return;

            await StartServersWhenEmuReadyAsync(stoppingToken);
            if (_servers.Count == 0)
                return;

            int emuCount = _cfg.EffectiveEssUnitCount;
            var store = SimulatorHost.Instance;

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                for (int lcIdx = 0; lcIdx < _servers.Count; lcIdx++)
                {
                    var lc = _servers[lcIdx];
                    try
                    {
                        _runtime.RunCycle(store.Get<ModbusSimServer>, lc, lcIdx, emuPerGroup: 1, emuCount);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"LC 桥接周期异常 [{lc.ServerName}]", ex);
                    }
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var server in _servers)
            {
                try
                {
                    if (server.IsOnline)
                        server.Stop();
                }
                catch (Exception ex) { Log.Warn("关闭 LocalControl Modbus 服务时异常", ex); }
            }

            _servers.Clear();
            return base.StopAsync(cancellationToken);
        }

        private async Task StartServersWhenEmuReadyAsync(CancellationToken stoppingToken)
        {
            var store = SimulatorHost.Instance;
            int emuCount = _cfg.EffectiveEssUnitCount;
            if (emuCount <= 0)
                return;

            int lcCount = emuCount;

            for (int attempt = 0; attempt < 120 && !stoppingToken.IsCancellationRequested; attempt++)
            {
                var emu = store.Get<ModbusSimServer>("simEmu1");
                if (emu != null && emu.IsDataPathReady)
                    break;

                await Task.Delay(500, stoppingToken);
            }

            for (int i = 0; i < lcCount; i++)
            {
                string name = $"simLc{i + 1}";
                int firstEmuId = i + 1;
                int groupCount = LcLayout.ExpandGroupCountForUnit(_cfg, i);
                var server = new LocalControlModbusServer(
                    groupCount, 0, name, firstEmuId,
                    essUnits: _cfg.ResolveEssUnitsOrFallback(),
                    dataExchangeOptions: _dataExchange);
                store.Register(name, server);
                // 由协议层管理器按端口计划分配端口/从站号并启动（可与其它设备共享端口）
                var report = ProtocolLayerManager.Instance.RegisterAndStart(server, ProtocolDeviceType.Lc, "lc.csv");
                _servers.Add(server);
                if (server.IsOnline)
                    Log.Info($"[LocalControl] {name} 已启动，端口 {server.Port}（从站号 {server.SlaveId}），runtime={_runtime.GetType().Name}");
                else
                    Log.Error($"[LocalControl] {name} 启动失败：{string.Join("；", report.Errors)}");
            }
        }
    }
}
