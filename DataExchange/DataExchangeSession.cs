using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.DataExchange.Effects;
using EssSimulator.DataExchange.Pipeline;
using EssSimulator.DataExchange.Plugins;
using EssSimulator.Protocol.Modbus;
using log4net;

namespace EssSimulator.DataExchange
{
    /// <summary>
    /// 点表驱动数据交互会话：遥测/控制/反馈三管道（替代 ModbusDataSync）。
    /// </summary>
    public sealed class DataExchangeSession : IModbusSyncBackend
    {
        private readonly IModbusSlave _slave;
        private readonly ModbusParser _parser;
        private readonly PointCatalog _catalog;
        private readonly DeviceInfoDto _deviceInfo;
        private readonly DataExchangeOptions _options;
        private readonly int _clusterCount;
        private readonly ShadowStore _shadow = new();
        private readonly ILog _log = LogManager.GetLogger(typeof(DataExchangeSession));

        private readonly ISimulationDataAdapter _simulation;
        private readonly TelemetryPipeline _telemetryPipeline;
        private readonly RackTelemetryPipeline? _rackTelemetryPipeline;
        private readonly ControlPipeline _controlPipeline;
        private readonly RackControlPipeline? _rackControlPipeline;
        private readonly ControlFeedbackPipeline _feedbackPipeline;
        private readonly IModbusRegisterAdapter _modbusAdapter;
        private readonly IProtocolPointStore _pointStore;
        private readonly ControlEffectRegistry _effects;

        private readonly object _controlGate = new();

        /// <summary>共享调度线程执行本会话某一拍时的互斥门；Stop 用它等待在途的一拍结束。</summary>
        internal readonly object TickGate = new();

        private readonly ControlDrainCoalescer _controlDrain = new();
        private volatile bool _running;
        private bool _hadPreviousSession;

        /// <summary>设备名（调度器日志用）。</summary>
        internal string DeviceName => _deviceInfo.name ?? "?";

        public DataExchangeSession(
            IModbusSlave slave,
            ModbusParser parser,
            PointCatalog catalog,
            DeviceInfoDto deviceInfo,
            DataExchangeOptions options,
            int clusterCount = 0)
        {
            _slave = slave;
            _parser = parser;
            _catalog = catalog;
            _deviceInfo = deviceInfo;
            _options = options;
            _clusterCount = clusterCount;

            _simulation = new ReflectionSimulationAdapter();
            _pointStore = new ProtocolPointStore(new ModbusRegisterAdapter(slave, parser, deviceInfo));
            _modbusAdapter = _pointStore;

            _effects = new ControlEffectRegistry();
            var telemetryPlugins = new TelemetryPluginRegistry();
            string serverName = deviceInfo.name ?? string.Empty;
            if (IsEmuLikeDevice(serverName))
            {
                // simLc（LC 系统级点表）与 simEmu 同构：控制点作用于首机组 EMU 虚拟模型，
                // 插件/效果注册保持一致，联动链路相同。
                _effects
                    .Register(new EmuPcsControlEffect(RefreshOperationStatusTelemetry))
                    .Register(new EmuUnitBreakerEffect());
                telemetryPlugins.Register(new TrinaEmuFaultWordPlugin());
            }
            else if (serverName.StartsWith("simBms", StringComparison.OrdinalIgnoreCase))
            {
                _effects.Register(new BmsLinkControlEffect());
            }
            else if (serverName.StartsWith("simPvInv", StringComparison.OrdinalIgnoreCase))
            {
                // 逆变器最大可发由模型 AvailableAcPowerKw 计算（点表 yc29）
                telemetryPlugins.Register(new PvInverterMaxAvailablePlugin());
            }
            else if (serverName.StartsWith("simPv", StringComparison.OrdinalIgnoreCase)
                     && !serverName.StartsWith("simPvMeter", StringComparison.OrdinalIgnoreCase))
            {
                // PV Logger（simPvN）的 yc20 最大可调有功：读点时按上限/辐照实时汇总，
                // 避免模型每拍 Refresh 与门面步末覆盖之间的采样窗口。
                telemetryPlugins.Register(new PvLoggerMaxAdjustablePlugin());
            }

            bool logChanges = ShouldLogChanges(deviceInfo.name);

            _telemetryPipeline = new TelemetryPipeline(catalog, _simulation, _modbusAdapter, _shadow, telemetryPlugins);
            if (clusterCount > 0 && catalog.RackTelemetryPoints.Count > 0)
            {
                _rackTelemetryPipeline = new RackTelemetryPipeline(
                    catalog.RackTelemetryPoints,
                    _simulation,
                    _modbusAdapter,
                    clusterCount,
                    deviceInfo.slaveId);
            }

            _controlPipeline = new ControlPipeline(
                catalog, _simulation, _modbusAdapter, parser, _shadow, _effects, serverName, logChanges);
            if (clusterCount > 0 && catalog.RackControlPoints.Count > 0)
            {
                _rackControlPipeline = new RackControlPipeline(
                    catalog.RackControlPoints,
                    _simulation,
                    _modbusAdapter,
                    parser,
                    _shadow,
                    clusterCount,
                    deviceInfo.slaveId,
                    serverName,
                    logChanges);
            }

            _feedbackPipeline = new ControlFeedbackPipeline(
                catalog, _simulation, _modbusAdapter, _shadow, serverName, logChanges);

            if (_options.ControlEventDriven && slave is ModbusSlave modbusSlave)
                modbusSlave.ExternalControlWrite += OnExternalControlWrite;
        }

        /// <summary>协议门面共用点影子（Modbus 与 IEC 61850）。</summary>
        public IProtocolPointStore PointStore => _pointStore;

        private void OnExternalControlWrite(byte slaveId)
        {
            if (!_running)
                return;

            // bank slaveId == base；簇从站 = base + rackIndex + 1
            if (_rackControlPipeline != null && slaveId > _deviceInfo.slaveId)
                _controlDrain.SignalRack(slaveId - _deviceInfo.slaveId - 1, DrainSignaled);
            else
                _controlDrain.SignalBank(DrainSignaled);
        }

        private void DrainSignaled(bool bank, IReadOnlyList<int> racks)
        {
            try
            {
                if (bank)
                    DrainControlPipeline();
                foreach (var rackId in racks)
                    DrainRackControlPipeline(rackId);
            }
            catch (Exception ex)
            {
                _log.Error($"Event-driven control drain error [{_deviceInfo.name}]", ex);
            }
        }

        private bool TryWriteControlRegister(string name, object value, out object appliedModbusValue)
        {
            appliedModbusValue = value;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var binding = _catalog.FindControl(name);
            if (binding == null)
                return false;

            appliedModbusValue = ControlValueCoercion.CoerceForModbusRegister(binding, value);
            lock (_controlGate)
            {
                _modbusAdapter.WritePoints(
                    new Dictionary<string, object> { { name, appliedModbusValue } },
                    applyScale: false);
                DrainControlPipeline();
            }
            return true;
        }

        /// <summary>dpc / imperative 写簇级控制点（如门限 yc1322）。</summary>
        public bool TryWriteRackControlRegister(int rackIndex, string name, object value, out string message)
        {
            message = string.Empty;
            if (_rackControlPipeline == null)
            {
                message = "当前设备无簇级控制点表";
                return false;
            }

            if (rackIndex < 0 || rackIndex >= _clusterCount)
            {
                message = $"簇索引越界: r{rackIndex}（有效 0..{_clusterCount - 1}）";
                return false;
            }

            var binding = _catalog.FindRackControl(name);
            if (binding == null)
            {
                message = $"找不到簇级控制点 {name}";
                return false;
            }

            object modbusValue = value;
            if (binding.Entry.FunctionCode == 5)
            {
                bool coil = value switch
                {
                    bool b => b,
                    string s when bool.TryParse(s, out var bv) => bv,
                    _ => Convert.ToDouble(value) != 0
                };
                modbusValue = coil ? 1 : 0;
            }

            byte slaveId = (byte)(_deviceInfo.slaveId + rackIndex + 1);
            _modbusAdapter.WritePoints(
                new Dictionary<string, object> { { binding.ParamName, modbusValue } },
                slaveId,
                applyScale: false);
            DrainRackControlPipeline(rackIndex);
            message =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {_deviceInfo.name}.r{rackIndex}.{name} " +
                $"控制点写入寄存器原始值 {modbusValue}（工程值=原始值/Scale，经簇控制管道解析）";
            return true;
        }

        private void DrainControlPipeline()
        {
            lock (_controlGate)
            {
                if (!_running)
                    return;

                _controlPipeline.RunOnce();
            }
        }

        private void DrainRackControlPipeline(int? rackIndex = null)
        {
            if (_rackControlPipeline == null)
                return;

            lock (_controlGate)
            {
                if (!_running)
                    return;

                if (rackIndex.HasValue)
                    _rackControlPipeline.RunForRack(rackIndex.Value);
                else
                    _rackControlPipeline.RunOnce();
            }
        }

        /// <summary>EMU 及与 EMU 同构的 LC 系统级设备。</summary>
        internal static bool IsEmuLikeDevice(string? serverName) =>
            serverName?.StartsWith("simEmu", StringComparison.OrdinalIgnoreCase) == true
            || serverName?.StartsWith("simLc", StringComparison.OrdinalIgnoreCase) == true;

        private static bool ShouldLogChanges(string? serverName) =>
            IsEmuLikeDevice(serverName)
            || serverName?.StartsWith("simBms", StringComparison.OrdinalIgnoreCase) == true
            || serverName?.StartsWith("simPvInv", StringComparison.OrdinalIgnoreCase) == true;

        private int TelemetryIntervalForDevice =>
            IsEmuLikeDevice(_deviceInfo.name)
                ? Math.Max(10, _options.EmuTelemetryIntervalMs)
                : Math.Max(10, _options.TelemetryIntervalMs);

        public void Start()
        {
            if (_running) return;
            _running = true;

            // 仅 Modbus 重连（link off→on）时清空 shadow 并同步全量回写；冷启动由后台遥测线程首轮写入，避免阻塞启动。
            if (_hadPreviousSession)
            {
                _shadow.ClearTelemetry();
                _rackTelemetryPipeline?.ClearShadow();
                RefreshTelemetryAfterReconnect();
            }

            _modbusAdapter.WriteDefaults(_catalog.DefaultValues);
            InitializeControlRegistersFromSimulation();

            // 交给共享调度器：每台设备不再各起 3~4 条线程，而是所有会话的同一条管道共用一条线程
            var scheduler = DataExchangeScheduler.Instance;
            scheduler.Register(this, DataExchangeScheduler.Pipeline.Telemetry, TelemetryIntervalForDevice, TickTelemetry);
            if (_rackTelemetryPipeline != null)
                scheduler.Register(this, DataExchangeScheduler.Pipeline.RackTelemetry, TelemetryIntervalForDevice, TickRackTelemetry);
            scheduler.Register(this, DataExchangeScheduler.Pipeline.Control, ControlIntervalMs, TickControl);
            scheduler.Register(this, DataExchangeScheduler.Pipeline.Feedback, ControlIntervalMs, TickFeedback);
            string rackInfo = _rackTelemetryPipeline != null ? $", rackTelemetry={TelemetryIntervalForDevice}ms×{_clusterCount}" : string.Empty;
            string controlMode = _options.ControlEventDriven ? "event+poll" : "poll";
            _log.Info($"[DataExchange] {_deviceInfo.name} 已启动（telemetry={TelemetryIntervalForDevice}ms, control={controlMode}/{_options.ControlPollIntervalMs}ms, feedback={_options.ControlPollIntervalMs}ms{rackInfo}）");
        }

        public void Stop()
        {
            if (!_running && !_hadPreviousSession)
                return;

            _running = false;
            _hadPreviousSession = true;
            DataExchangeScheduler.Instance.Unregister(this);

            // 注销后不会再派新的一拍；这里等正在执行的那拍收尾（原来是 Join(2000)）
            if (Monitor.TryEnter(TickGate, 2000))
                Monitor.Exit(TickGate);
        }

        /// <summary>
        /// 外部 imperative 写控制点（如 dpc set）：先写 Modbus 寄存器，再走控制管道，
        /// 与 EMS/mbpoll 写线圈同路径，自动触发绑定的副作用（如 PCS 启停逻辑）。
        /// </summary>
        public void SetDataObjectByMesurePointName(string name, object value)
        {
            if (!TryWriteControlRegister(name, value, out _))
                _log.Warn($"Imperative control write failed: {_deviceInfo.name}.{name}");
        }

        public bool TrySetRackControl(int rackIndex, string name, object value, out string message) =>
            TryWriteRackControlRegister(rackIndex, name, value, out message);

        /// <summary>仿真 → Modbus 反馈（不回灌控制管道，避免重复触发副作用）。</summary>
        public void PublishControlToSlave(string name, object value)
        {
            lock (_controlGate)
            {
                if (!_feedbackPipeline.PublishImmediate(name, value, out _))
                    _log.Warn($"Control feedback publish failed: {_deviceInfo.name}.{name}");
            }
        }

        public void InvalidateDataShadow(string name) =>
            _shadow.InvalidateTelemetry(name);

        /// <summary>PCS 控制后立即刷新 OperationStatus 遥测（跳过 100ms 遥测轮询等待）。</summary>
        private void RefreshOperationStatusTelemetry()
        {
            try
            {
                foreach (var binding in _catalog.TelemetryPoints)
                {
                    if (binding.Target.PropertyPath.EndsWith(".OperationStatus", StringComparison.Ordinal))
                        _shadow.InvalidateTelemetry(binding.ParamName);
                }

                _telemetryPipeline.RunOnce();
            }
            catch (Exception ex)
            {
                _log.Warn($"Immediate OperationStatus telemetry refresh failed [{_deviceInfo.name}]", ex);
            }
        }

        public object? GetDataObjectByMesurePointName(string name, IModbusSlave slave, ModbusParser parser) =>
            _modbusAdapter.ReadParsedPoint(name);

        private void RefreshTelemetryAfterReconnect()
        {
            try
            {
                _telemetryPipeline.RunOnce();
                _rackTelemetryPipeline?.RunOnce();
            }
            catch (Exception ex)
            {
                _log.Warn($"Initial telemetry refresh failed [{_deviceInfo.name}]", ex);
            }
        }

        private void InitializeControlRegistersFromSimulation()
        {
            try
            {
                var initCtl = new Dictionary<string, object>();
                foreach (var binding in _catalog.ControlPoints)
                {
                    var cur = _simulation.Read(binding.Target.FullPath);
                    if (cur != null)
                    {
                        initCtl[binding.ParamName] = cur;
                        _shadow.SeedControl(binding.ParamName, cur);
                    }
                }

                if (initCtl.Count > 0)
                    _modbusAdapter.WritePoints(initCtl);

                InitializeRackControlRegistersFromSimulation();
            }
            catch (Exception ex)
            {
                _log.Warn("Init control register defaults failed.", ex);
            }
        }

        /// <summary>
        /// 用簇模型当前门限回填各 rack 从站 Holding，并 seed 控制 shadow。
        /// 避免启动时寄存器为 0 被 RackControlPipeline 误写回模型，冲掉 ClusterThresholds 默认值。
        /// </summary>
        private void InitializeRackControlRegistersFromSimulation()
        {
            if (_rackControlPipeline == null || _clusterCount <= 0 || _catalog.RackControlPoints.Count == 0)
                return;

            for (int rackId = 0; rackId < _clusterCount; rackId++)
            {
                var initCtl = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var binding in _catalog.RackControlPoints)
                {
                    string path = binding.ResolvePath(rackId);
                    var cur = _simulation.Read(path);
                    if (cur == null)
                        continue;

                    initCtl[binding.ParamName] = cur;
                    _shadow.SeedControl($"{rackId}:{binding.ParamName}", cur);
                }

                if (initCtl.Count == 0)
                    continue;

                byte slaveId = (byte)(_deviceInfo.slaveId + rackId + 1);
                _modbusAdapter.WritePoints(initCtl, slaveId, applyScale: true);
            }
        }

        private int ControlIntervalMs => Math.Max(10, _options.ControlPollIntervalMs);

        private void TickTelemetry()
        {
            lock (TickGate)
            {
                if (!_running)
                    return;

                try
                {
                    _telemetryPipeline.RunOnce();
                }
                catch (Exception ex)
                {
                    _log.Error($"Telemetry loop error [{_deviceInfo.name}]", ex);
                }
            }
        }

        private void TickRackTelemetry()
        {
            if (_rackTelemetryPipeline == null)
                return;

            lock (TickGate)
            {
                if (!_running)
                    return;

                try
                {
                    _rackTelemetryPipeline.RunOnce();
                }
                catch (Exception ex)
                {
                    _log.Error($"Rack telemetry loop error [{_deviceInfo.name}]", ex);
                }
            }
        }

        private void TickControl()
        {
            lock (TickGate)
            {
                if (!_running)
                    return;

                try
                {
                    DrainControlPipeline();
                    DrainRackControlPipeline();
                }
                catch (Exception ex)
                {
                    _log.Error($"Control loop error [{_deviceInfo.name}]", ex);
                }
            }
        }

        private void TickFeedback()
        {
            lock (TickGate)
            {
                if (!_running)
                    return;

                try
                {
                    // 寄存器回填和 shadow 提交必须与控制快照读取互斥。
                    lock (_controlGate)
                        _feedbackPipeline.RunOnce();
                }
                catch (Exception ex)
                {
                    _log.Error($"Feedback loop error [{_deviceInfo.name}]", ex);
                }
            }
        }
    }
}
