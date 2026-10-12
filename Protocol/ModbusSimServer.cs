using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EssSimulator.Configuration;
using EssSimulator.DataExchange;
using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.Protocol.Modbus;
using log4net;

namespace EssSimulator
{
    public class MapEntry
    {
        public int     Address      { get; set; }
        public int     FunctionCode { get; set; }
        public string? ParamName    { get; set; }
        public int     Scale        { get; set; }
        public string? Description  { get; set; }
        public string? ModelSim     { get; set; }
        public int     Size         { get; set; }
        public string? Type         { get; set; }
    }

    /// <summary>
    /// Modbus TCP 从站门面类（仿真设备：simEmu / simBms / simEm）。
    /// 走 <see cref="DataExchangeSession"/> 与内部仿真模型绑定；LocalControl 见 <c>LocalControl/</c> 模块。
    /// 传输层（端口监听/从站网络）由 <see cref="ModbusPortHub"/> 统一提供，支持同端口多设备共享。
    /// </summary>
    public class ModbusSimServer : IModbusRegisterServer, IProtocolLayerServer
    {
        private readonly ILog              _log = LogManager.GetLogger(typeof(ModbusSimServer));
        private readonly IModbusSlave      _slave;
        private readonly ModbusParser      _parser;
        private readonly DeviceInfoDto     _deviceInfo;
        private readonly EssSimulator.Protocol.Modbus.ModbusPointMap  _pointMap;
        private readonly IModbusSyncBackend _dataSync;

        public ModbusSimServer(
            string mapFilePath,
            int modbusPort,
            string serverName,
            int clusterCount = 0,
            DataExchangeOptions? dataExchangeOptions = null,
            IReadOnlyList<EssUnitConfig>? essUnits = null,
            int? emuDeviceIdOverride = null,
            int pcsIndex = 0,
            int? pvDeviceIdOverride = null,
            int? inverterIndex = null)
        {
            RackCount = clusterCount;
            _pointMap = new EssSimulator.Protocol.Modbus.ModbusPointMap(
                mapFilePath, serverName, clusterCount, emuDeviceIdOverride, pcsIndex: pcsIndex,
                pvDeviceIdOverride: pvDeviceIdOverride, inverterIndex: inverterIndex);

            _deviceInfo = new DeviceInfoDto
            {
                ip             = "0.0.0.0",
                port           = modbusPort,
                slaveId        = 1,
                connectType    = "ModbusTCP",
                collectionCycle = 1000,
                name           = serverName
            };
            _slave  = new ModbusTCPSlave(_deviceInfo, _pointMap.RawMaps, clusterCount);
            _parser = new ModbusParser(_pointMap.RawMaps);

            var options = dataExchangeOptions ?? new DataExchangeOptions();
            var catalog = PointCatalogLoader.FromPointMap(_pointMap, serverName, options, essUnits);
            var session = new DataExchangeSession(
                _slave, _parser, catalog, _deviceInfo, options, clusterCount);
            _dataSync = session;
            PointStore = session.PointStore;
            KeepDataPathOnDisconnect = serverName.StartsWith("simEmu", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 尝试连接并启动所有 worker 线程。
        /// 最多重试 <paramref name="maxRetries"/> 次，超出后记录错误并返回 false。
        /// </summary>
        public bool Start(int maxRetries = 30)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                // 首次尝试立即连接，只有重试时才等待，避免大量从站启动时线性累积 1s 延迟。
                if (attempt > 1)
                    Thread.Sleep(1000);
                if (_slave == null)
                {
                    _log.Error($"Slave is null, DeviceName: {_deviceInfo.name}");
                    return false;
                }
                _slave.DeviceConnect();
                if (_slave.GetCommunicatorState())
                {
                    _dataSync.Start();
                    IsDataPathReady = true;
                    return true;
                }
                _log.Warn($"{_deviceInfo.name} 连接失败，第 {attempt}/{maxRetries} 次重试...");
            }
            _log.Error($"{_deviceInfo.name} 经 {maxRetries} 次重试仍无法连接，放弃启动。");
            return false;
        }

        public void Stop()
        {
            try { _dataSync.Stop(); }
            catch (Exception ex) { _log.Error("Stop error", ex); }
            IsDataPathReady = false;
            _slave.DeviceDisconnect();
        }

        /// <summary>
        /// 仅启动 DataExchange / 寄存器影子，不绑定 Modbus TCP。
        /// 供 IEC 61850-only 的 PCS 以及 LC 进程内抄数使用。
        /// </summary>
        public bool StartDataPathOnly()
        {
            _dataSync.Start();
            IsDataPathReady = true;
            return true;
        }

        // ── 协议层编排接口（IProtocolLayerServer）────────────────

        public string ServerName => _deviceInfo.name ?? string.Empty;
        public int Port => _deviceInfo.port;
        public byte SlaveId => _deviceInfo.slaveId;
        public int RackCount { get; }
        public IProtocolPointStore PointStore { get; }
        /// <summary>DataExchange 已运行（即使未监听 Modbus TCP）。</summary>
        public bool IsDataPathReady { get; private set; }
        /// <summary>链路 off 时是否保留点影子（EMU 默认保留，供 61850 / LC）。</summary>
        public bool KeepDataPathOnDisconnect { get; set; }

        /// <summary>已加载的点表（bank + 可选 rack），供协议层地址查重使用。</summary>
        public EssSimulator.Protocol.Modbus.ModbusPointMap PointMap => _pointMap;

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

        /// <summary>Modbus TCP 监听是否处于活动状态（端口已绑定）。</summary>
        public bool IsOnline => _slave.GetCommunicatorState();

        /// <summary>
        /// 开启或关闭对外 Modbus 连接：关闭时停止数据同步并释放 TCP 监听，外部客户端无法连接。
        /// </summary>
        public bool SetOnline(bool online, int maxRetries = 30)
        {
            if (online)
            {
                if (IsOnline) return true;
                return Start(maxRetries);
            }

            if (!IsOnline) return true;
            if (KeepDataPathOnDisconnect)
            {
                _slave.DeviceDisconnect();
                return !IsOnline;
            }

            Stop();
            return !IsOnline;
        }

        // ── 外部调用接口（保持与旧版相同签名）──────────────────────

        /// <summary>数据点列表（FunctionCode 3/4），供外部查点名使用。</summary>
        public IReadOnlyList<MapEntry> DataMaps => _pointMap.DataMaps;

        /// <summary>控制点列表（FunctionCode 5/6），如 pcs1_startstop。</summary>
        public IReadOnlyList<MapEntry> ControlMaps => _pointMap.ControlMaps;

        /// <summary>簇级控制点（bms_rack FC5/6，如门限 yc1322）。</summary>
        public IReadOnlyList<MapEntry> RackControlMaps => _pointMap.RackControlMaps;

        public void SetDataObjectByMesurePointName(string name, object value)
            => _dataSync.SetDataObjectByMesurePointName(name, value);

        public bool TrySetRackControl(int rackIndex, string name, object value, out string message)
            => _dataSync.TrySetRackControl(rackIndex, name, value, out message);

        /// <summary>模型控制量回写 Modbus（如联锁停机后清启停线圈）。</summary>
        public void PublishControlToSlave(string name, object value)
            => _dataSync.PublishControlToSlave(name, value);

        public void SetDataStoreByMesurePointName(string name, object value)
        {
            var buf = new Dictionary<string, object> { { name, value } };
            try
            {
                if (_slave is ModbusSlave modbusSlave)
                {
                    using (modbusSlave.SuppressWriteNotifications())
                        _slave.Write(buf);
                }
                else
                    _slave.Write(buf);
            }
            catch (Exception ex) { _log.Error("即时写入 Modbus 失败", ex); }
            _dataSync.InvalidateDataShadow(name);
        }

        public object? GetDataObjectByMesurePointName(string name)
            => _dataSync.GetDataObjectByMesurePointName(name, _slave, _parser);

        public void InvalidateDataShadow(string name) =>
            _dataSync.InvalidateDataShadow(name);

        // ── 静态工具：解析 CSV ModelSim 列字符串 ────────────────────

        public static ModesimModel? GetModelParam(string modelstring)
        {
            if (string.IsNullOrWhiteSpace(modelstring) || !modelstring.Contains("model"))
                return null;

            var model = new ModesimModel();
            int i = 0;
            foreach (var pair in modelstring.Split('|'))
            {
                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    string val = parts[1].Trim('"');
                    switch (i)
                    {
                        case 0: model.ModelType = val; break;
                        case 1: model.Arg1       = val; break;
                        case 2: model.Arg2       = val; break;
                        case 3: model.Arg3       = val; break;
                        case 4: model.Arg4       = val; break;
                    }
                }
                i++;
            }
            return model;
        }
    }
}
