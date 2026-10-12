using EssSimulator.Configuration;
using EssSimulator.DataExchange;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.Protocol.Modbus;
using log4net;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// LocalControl 专用 Modbus TCP 从站。
    /// 默认拼装 LC 片段、由协议桥与 simEmu 抄数/回写；选中互斥 EMU 点表时走 DataExchange。
    /// 传输层由 <see cref="ModbusPortHub"/> 统一提供，可与其它设备共享端口/从站号。
    /// </summary>
    public sealed class LocalControlModbusServer : IModbusRegisterServer, IProtocolLayerServer
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(LocalControlModbusServer));
        private readonly IModbusSlave _slave;
        private readonly ModbusParser _parser;
        private readonly DeviceInfoDto _deviceInfo;
        private readonly ModbusPointMap _pointMap;
        private readonly IModbusSyncBackend _backend;
        private readonly int _unitIndex0;

        /// <summary>
        /// 构造 LC 从站。未选互斥型号时按本单元组数拼装片段，遥测/控制由协议桥抄 simEmu；
        /// 选中 EMU 直控点表时按原 ModelSim 绑定走 DataExchange。
        /// <paramref name="firstEmuId"/> 为对应储能单元号（emuDeviceId 占位符替换）。
        /// </summary>
        public LocalControlModbusServer(
            int lcGroupCount,
            int modbusPort,
            string serverName,
            int? firstEmuId = null,
            IReadOnlyList<EssUnitConfig>? essUnits = null,
            DataExchangeOptions? dataExchangeOptions = null,
            string? selectionRoot = null)
        {
            var unit = ResolveEssUnit(essUnits, firstEmuId);
            int unitPcs = unit == null ? 0 : unit.PcsCount;
            bool exclusive = DeviceModelRegistry.TryGetExclusiveLcCsv(out _, selectionRoot)
                && LcLayout.ShouldUseExclusiveEmuMap(unitPcs);
            _pointMap = ModbusPointMap.ForLocalControl(
                serverName, lcGroupCount, firstEmuId, selectionRoot: selectionRoot, essUnits: essUnits);
            if (!exclusive)
                LcSystemMap.ApplyStartupDefaults(_pointMap.DefaultBuffer);
            LcGroupCount = Math.Max(1, lcGroupCount);
            EssUnits = essUnits;
            EssUnit = unit;
            _unitIndex0 = firstEmuId is int id && id >= 1 ? id - 1 : 0;
            _deviceInfo = new DeviceInfoDto
            {
                ip = "0.0.0.0",
                port = modbusPort,
                slaveId = 1,
                connectType = "ModbusTCP",
                collectionCycle = 1000,
                name = serverName
            };

            _slave = new ModbusTCPSlave(_deviceInfo, _pointMap.RawMaps, rackCount: 0);
            _parser = new ModbusParser(_pointMap.RawMaps);

            if (exclusive)
            {
                UsesDataExchange = true;
                var options = dataExchangeOptions ?? new DataExchangeOptions();
                var catalog = PointCatalogLoader.FromPointMap(_pointMap, serverName, options, essUnits);
                _backend = new DataExchangeSession(_slave, _parser, catalog, _deviceInfo, options, clusterCount: 0);
            }
            else
            {
                UsesDataExchange = false;
                _backend = new RegisterOnlyBackend(_slave, _parser, _pointMap);
            }
        }

        /// <summary>本从站对应储能单元的组数（扁平机组为 1）。</summary>
        public int LcGroupCount { get; }

        /// <summary>本从站对应的储能单元配置；无 Devices 回退时为 null。</summary>
        public EssUnitConfig? EssUnit { get; }

        /// <summary>全部储能单元构成，供按 PCS 解析 simEmu 名。</summary>
        public IReadOnlyList<EssUnitConfig>? EssUnits { get; }

        /// <summary>本单元内扁平 PCS 下标对应的协议从站名（与 pcs 全局编号 1:1）。</summary>
        public string EmuServerNameForPcs(int flatPcsIndex) =>
            EmuProtocolLayout.ServerNameFor(EssUnits, _unitIndex0, flatPcsIndex);

        /// <summary>点表含模型绑定、由 DataExchange 管道驱动（桥接引擎应跳过此类设备）。</summary>
        public bool UsesDataExchange { get; }

        private static EssUnitConfig? ResolveEssUnit(IReadOnlyList<EssUnitConfig>? essUnits, int? firstEmuId)
        {
            if (essUnits == null || firstEmuId is not int id || id < 1 || id > essUnits.Count)
                return null;
            return essUnits[id - 1];
        }

        public string ServerName => _deviceInfo.name ?? string.Empty;

        public bool IsOnline => _slave.GetCommunicatorState();

        public int Port => _deviceInfo.port;

        public byte SlaveId => _deviceInfo.slaveId;

        public int RackCount => 0;

        /// <summary>已加载的点表，供协议层地址查重使用。</summary>
        public ModbusPointMap PointMap => _pointMap;

        /// <summary>调整端口/从站号：要求先离线，由协议层管理器在重建时调用。</summary>
        public void Reconfigure(int port, byte slaveId)
        {
            if (IsOnline)
            {
                _log.Warn($"{ServerName} 在线时调整端口/从站号被忽略，请先停止服务");
                return;
            }
            _deviceInfo.port = port;
            _deviceInfo.slaveId = slaveId;
        }

        public IReadOnlyList<MapEntry> DataMaps => _pointMap.DataMaps;

        public IReadOnlyList<MapEntry> ControlMaps => _pointMap.ControlMaps;

        public IReadOnlyList<MapEntry> RackControlMaps => _pointMap.RackControlMaps;

        public void SetDataObjectByMesurePointName(string name, object value) =>
            _backend.SetDataObjectByMesurePointName(name, value);

        public bool TrySetRackControl(int rackIndex, string name, object value, out string message) =>
            _backend.TrySetRackControl(rackIndex, name, value, out message);

        public bool Start(int maxRetries = 30)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (attempt > 1)
                    Thread.Sleep(1000);

                _slave.DeviceConnect();
                if (_slave.GetCommunicatorState())
                {
                    _backend.Start();
                    return true;
                }

                _log.Warn($"{ServerName} 连接失败，第 {attempt}/{maxRetries} 次重试...");
            }

            _log.Error($"{ServerName} 经 {maxRetries} 次重试仍无法连接，放弃启动。");
            return false;
        }

        public void Stop()
        {
            try { _backend.Stop(); }
            catch (Exception ex) { _log.Error("Stop error", ex); }
            _slave.DeviceDisconnect();
        }

        public void PublishControlToSlave(string name, object value) =>
            _backend.PublishControlToSlave(name, value);

        public void SetDataStoreByMesurePointName(string name, object value)
        {
            var buf = new Dictionary<string, object> { { name, value } };
            try { _slave.Write(buf); }
            catch (Exception ex) { _log.Error("即时写入 Modbus 失败", ex); }
        }

        public object? GetDataObjectByMesurePointName(string name) =>
            _backend.GetDataObjectByMesurePointName(name, _slave, _parser);
    }
}
