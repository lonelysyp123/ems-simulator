using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.DataExchange;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.EssSimModelApi;
using EssSimulator.LocalControl;
using EssSimulator.Protocol.Modbus;
using log4net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EssSimulator
{
    /// <summary>
    /// 将所有 Modbus 从站的创建与启动封装为 IHostedService，
    /// 替代 Program.cs 中的 for 循环硬编码启动逻辑。
    /// 端口/从站号分配与启动由 <see cref="ProtocolLayerManager"/> 统一编排（支持同端口共享）。
    /// </summary>
    public class ModbusHostedService : IHostedService
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ModbusHostedService));
        private readonly SimulatorConfig _cfg;
        private readonly DataExchangeOptions _dataExchange;
        private readonly ProtocolLayerManager _manager;

        public ModbusHostedService(
            IOptions<SimulatorConfig> opts,
            IOptions<DataExchangeOptions> dataExchangeOpts)
            : this(opts, dataExchangeOpts, ProtocolLayerManager.Instance)
        {
        }

        internal ModbusHostedService(
            IOptions<SimulatorConfig> opts,
            IOptions<DataExchangeOptions> dataExchangeOpts,
            ProtocolLayerManager manager)
        {
            _cfg = opts.Value;
            _dataExchange = dataExchangeOpts.Value;
            _manager = manager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // 将所有 Modbus TCP 连接放到后台线程中异步执行，
            // 避免阻塞 Host 的 StartAsync 调用链。
            Task.Run(() =>
            {
                try
                {
                    var store = SimulatorHost.Instance;
                    var bmsCfg = _cfg.GetBmsDeviceConfigs();

                    // BMS Modbus 服务（每个储能通道一个）
                    for (int i = 0; i < _cfg.UnitCount; i++)
                    {
                        string name = $"simBms{i + 1}";
                        int clusterCount = i < bmsCfg.Count
                            ? bmsCfg[i].ClusterCount
                            : new BmsDeviceConfig().ClusterCount;
                        var server = new ModbusSimServer("bms_bank.csv", 0, name, clusterCount, _dataExchange);
                        store.Register(name, server);
                        _manager.RegisterDevice(server, ProtocolDeviceType.Bms, "bms_bank.csv");
                    }

                    // PCS (EMU) Modbus 服务：一台 PCS 一个 simEmu，点表绑 emuN.PcsList[pcsIndex]
                    foreach (var ep in EmuProtocolLayout.Enumerate(_cfg))
                    {
                        string name = ep.ServerName;
                        var pcs = new ModbusSimServer(
                            "emu.csv", 0, name,
                            dataExchangeOptions: _dataExchange,
                            essUnits: _cfg.Devices,
                            emuDeviceIdOverride: ep.UnitId,
                            pcsIndex: ep.PcsIndex0);
                        store.Register(name, pcs);
                        _manager.RegisterDevice(pcs, ProtocolDeviceType.Emu, "emu.csv");
                    }

                    // 光伏 Logger / 低压电表
                    for (int i = 0; i < _cfg.PvUnitCount; i++)
                    {
                        string loggerName = $"simPv{i + 1}";
                        var logger = new ModbusSimServer("pv_logger.csv", 0, loggerName, dataExchangeOptions: _dataExchange);
                        store.Register(loggerName, logger);
                        _manager.RegisterDevice(logger, ProtocolDeviceType.PvLogger, "pv_logger.csv");

                        string meterName = $"simPvMeter{i + 1}";
                        var meter = new ModbusSimServer("pv_apm810.csv", 0, meterName, dataExchangeOptions: _dataExchange);
                        store.Register(meterName, meter);
                        _manager.RegisterDevice(meter, ProtocolDeviceType.PvMeter, "pv_apm810.csv");
                    }

                    foreach (var ep in PvInverterProtocolLayout.Enumerate(_cfg))
                    {
                        var inverter = new ModbusSimServer(
                            "pv_inverter.csv", 0, ep.ServerName,
                            dataExchangeOptions: _dataExchange,
                            pvDeviceIdOverride: ep.UnitId,
                            inverterIndex: ep.InverterIndex0);
                        store.Register(ep.ServerName, inverter);
                        _manager.RegisterDevice(inverter, ProtocolDeviceType.PvInverter, "pv_inverter.csv");
                    }

                    // 电表 Modbus 服务
                    var em = new ModbusSimServer("em.csv", 0, "simEm", dataExchangeOptions: _dataExchange);
                    store.Register("simEm", em);
                    _manager.RegisterDevice(em, ProtocolDeviceType.Em, "em.csv");

                    if (_cfg.EnableSvg)
                    {
                        var svg = new ModbusSimServer("svg.csv", 0, "simSvg", dataExchangeOptions: _dataExchange);
                        store.Register("simSvg", svg);
                        _manager.RegisterDevice(svg, ProtocolDeviceType.Svg, "svg.csv");
                    }

                    // 按端口计划（默认配置 + protocol-ports.json 覆盖）统一分配端口并启动
                    var result = _manager.StartAll(_cfg);
                    Log.Info($"Modbus 从站注册完成，共 {result.Devices.Count} 个设备，成功启动 {result.Devices.Count(d => d.Started)} 个");
                    foreach (var device in result.Devices.Where(d => d.Errors.Count > 0))
                    {
                        foreach (var error in device.Errors)
                            Log.Error($"{device.Name}: {error}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Modbus 从站启动失败（dpc/esscmd 将提示找不到 Modbus 设备）。请确认 `pointmaps/models/` 下存在对应设备类型的点表，并检查 `configs/topology/device-models.json` 选型。", ex);
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try { _manager.StopAll(); }
            catch (Exception ex) { Log.Warn("关闭 Modbus 服务时异常", ex); }
            return Task.CompletedTask;
        }
    }
}
