using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Microsoft.Extensions.Hosting;

namespace EssSimulator.EssDeviceSimModel
{
    using EssSimulator.Configuration;
    using EssSimulator.EssDeviceSimModel.Battery;
    using EssSimulator.EssDeviceSimModel.Diagnostics;
    using EssSimulator.EssDeviceSimModel.Devices;
    using EssSimulator.EssDeviceSimModel.Model;
    using EssSimulator.EssDeviceSimModel.Plant;
    using EssSimulator.EssDeviceSimModel.Propagation;
    using EssSimulator.EssDeviceSimModel.Solver;
    using EssSimulator.EssDeviceSimModel.Thermal;
    using EssSimulator.EssDeviceSimModel.Pv;
    using System;
    using System.Collections.Generic;

    public class EnergyStorageSystem : BackgroundService
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(EnergyStorageSystem));
        public IReadOnlyList<BatteryRackSimulator> _batteryRacks { get; }

        /// <summary>新电气网络 BMS 设备（与 _batteryRacks 一一对应）。</summary>
        public IReadOnlyList<BmsRackDevice> _bmsRackDevices { get; }

        /// <summary>PCS 列表，索引 i 对应第 i+1 个 PCS。与电气网络 PcsDevices 共用实例。</summary>
        public IReadOnlyList<PcsDevice> _pcsList { get; }

        /// <summary>光伏单元列表（组态/配置展开；纯光伏工程可无储能通道）。</summary>
        public IReadOnlyList<PvUnitDevice> PvUnits { get; }

        /// <summary>各储能单元下属 PCS 台数（EMU 拓扑：每单元 = 1 个 EMU 虚拟模型聚合 N 台 PCS）。</summary>
        public IReadOnlyList<int> PcsPerUnit => _pcsPerUnit;
        private IReadOnlyList<int> _pcsPerUnit = Array.Empty<int>();

        /// <summary>PCS 通道索引 → 所属单元索引 的映射表。</summary>
        private IReadOnlyList<int> _unitIndexByPcs = Array.Empty<int>();
        private IReadOnlyList<string?> _unitMeterSourceBusIds = Array.Empty<string?>();

        /// <summary>PCS 通道（0 基）所属储能单元索引；越界时就近钉住边界。</summary>
        public int UnitIndexOfPcs(int pcsSimIndex)
        {
            if (_unitIndexByPcs.Count == 0) return 0;
            int idx = Math.Clamp(pcsSimIndex, 0, _unitIndexByPcs.Count - 1);
            return _unitIndexByPcs[idx];
        }

        /// <summary>指定单元第一台 PCS 的全局通道索引（0 基）。</summary>
        public int PcsBaseIndexOfUnit(int unit)
        {
            int baseIdx = 0;
            for (int u = 0; u < unit && u < _pcsPerUnit.Count; u++)
                baseIdx += _pcsPerUnit[u];
            return baseIdx;
        }

        /// <summary>组态解析的单元电表抽头母线；未接线时为 null。</summary>
        public string? GetUnitMeterSourceBusId(int unitIndex) =>
            unitIndex >= 0 && unitIndex < _unitMeterSourceBusIds.Count
                ? _unitMeterSourceBusIds[unitIndex]
                : null;

        /// <summary>指定单元下属 PCS 台数。</summary>
        public int PcsCountOfUnit(int unit) =>
            unit >= 0 && unit < _pcsPerUnit.Count ? _pcsPerUnit[unit] : 0;

        //public GridState _gridState;
        public Breaker _breaker { get; set; } //断路器
        public IReadOnlyList<Breaker> _unitBreakers { get; }                 // 单元变前高压断路器（每个 Unit 1 个）
        public TransformerDevice _mainTransformer { get; set; }              // 220kV/35kV 主变
        public IReadOnlyList<TransformerDevice> _unitTransformers { get; }   // 35kV/690V 单元变（每个 Unit 1 台）
        /// <summary>35kV 负载设备（与电气网络 Load 共用实例）。</summary>
        public LoadDevice _loadDevice { get; private set; }

        /// <summary>兼容 GUI 路径 ess._loadSimulator.ActivePower。</summary>
        public LoadDevice _loadSimulator => _loadDevice;

        /// <summary>220kV 并网点（PCC）线电压（V），与并网电表、无功调压闭环一致。</summary>
        public double PccLineVoltageV { get; private set; }

        /// <summary>35kV 站内母线线电压（V），由 PCC 电压按额定变比推导。</summary>
        public double StationBus35LineVoltageV { get; private set; }

        /// <summary>新电气网络（阶段 4 起始终初始化）。</summary>
        public ElectricalNetwork ElectricalNetwork => _electricalNetwork;

        /// <summary>电气输出订阅路由（设备间互相注册）。</summary>
        public ElectricalSignalRouter SignalRouter { get; }

        /// <summary>径向前推回代引擎（生产路径唯一电气步进）。</summary>
        public RadialPowerSweepEngine PowerSweepEngine { get; }

        /// <summary>径向网络母线图。</summary>
        public RadialNetworkGraph RadialGraph { get; }

        /// <summary>
        /// 电站物理步进门面。主循环只应调用 <see cref="PlantEngine.Step"/>；
        /// 设备编排与热网络扩展落在此引擎内，而非 Host 定时器回调里。
        /// </summary>
        public PlantEngine PlantEngine { get; private set; } = null!;

        /// <summary>电站热子系统（气候 + BMS 柜体）。</summary>
        public PlantThermalSystem Thermal { get; private set; } = null!;

        /// <summary>设备耦合图（PCS–BMS 直流边等）。</summary>
        public PlantCouplingGraph CouplingGraph { get; private set; } = null!;

        /// <summary>PCS 物理配置（供网络控制面与设备步进使用）。</summary>
        internal PcsPhysicalConfig PcsPhysicalConfig => _pcsCfg;

        private readonly ElectricalNetwork _electricalNetwork;
        private readonly int _propagationIntervalMs;

        public EnergyStorageSystem(
            SimulatorConfig simCfg,
            PcsPhysicalConfig pcsCfg,
            TransformerConfig transCfg,
            UnitTransformerConfig unitTransCfg,
            LoadConfig loadCfg,
            PccConfig pccCfg,
            MeterConfig meterCfg)
        {
            var racks = new List<BatteryRackSimulator>();
            var bmsRackDevices = new List<BmsRackDevice>();
            var pcsList = new List<PcsDevice>();
            var bmsDeviceConfigs = simCfg.GetBmsDeviceConfigs();
            var pcsDeviceConfigs = simCfg.GetPcsDeviceConfigs();
            int unitCount = simCfg.EffectiveEssUnitCount;
            // 通道总数 = 各单元 PCS 台数之和（未配置时 GetPcsCountsPerUnit 回退每单元 2 台）
            int channelCount = Math.Max(pcsDeviceConfigs.Count, unitCount);

            // 单元 → PCS 台数 / 通道 → 单元 映射（替代硬编码每单元 2 台）
            var pcsPerUnit = simCfg.GetPcsCountsPerUnit();
            var unitIndexByChannel = new List<int>();
            for (int u = 0; u < pcsPerUnit.Count; u++)
                for (int k = 0; k < pcsPerUnit[u]; k++)
                    unitIndexByChannel.Add(u);
            while (unitIndexByChannel.Count < channelCount)
                unitIndexByChannel.Add(Math.Max(0, unitCount - 1));
            _pcsPerUnit = pcsPerUnit;
            _unitIndexByPcs = unitIndexByChannel;
            _unitMeterSourceBusIds = simCfg.Devices.Select(d => d.UnitMeterSourceBusId).ToList();

            for (int i = 0; i < channelCount; i++)
            {
                var bmsCfg = i < bmsDeviceConfigs.Count ? bmsDeviceConfigs[i] : new BmsDeviceConfig();
                var rack = BmsRackFactory.CreateRack(bmsCfg);
                racks.Add(rack);
                int u = UnitIndexOfPcs(i);
                int ch = i - PcsBaseIndexOfUnit(u);
                var bmsDev = new BmsRackDevice($"bms_u{u}_ch{ch}", rack);
                bmsDev.DisplayLabel = $"bms{i + 1}";
                bmsRackDevices.Add(bmsDev);
            }

            for (int i = 0; i < channelCount; i++)
            {
                var pcsDeviceCfg = i < pcsDeviceConfigs.Count ? pcsDeviceConfigs[i] : new Configuration.PcsDeviceConfig();
                var rampCfg = pcsDeviceCfg.PcsRamp ?? simCfg.Runtime.PcsRamp;
                var cfg = PcsDeviceFactory.CreateConfig(pcsCfg, rampCfg);
                int u = UnitIndexOfPcs(i);
                int ch = i - PcsBaseIndexOfUnit(u);
                var pcs = PcsDeviceFactory.Create($"pcs_u{u}_ch{ch}", cfg);
                pcs.DisplayLabel = $"pcs{i + 1}";
                pcsList.Add(pcs);
            }

            _batteryRacks = racks;
            _bmsRackDevices = bmsRackDevices;
            _pcsList      = pcsList;
            PvUnits = CreatePvUnits(simCfg);

            _breaker = new Breaker();

            // 单元断路器（默认合闸，允许通过 emu.poweronoff 控制）
            var unitBreakers = new List<Breaker>();
            for (int u = 0; u < unitCount; u++)
            {
                var brk = new Breaker();
                brk.IsClosed = true;
                unitBreakers.Add(brk);
            }
            _unitBreakers = unitBreakers;

            _mainTransformer = TransformerDeviceFactory.Create(
                "main_transformer",
                TransformerDeviceFactory.CreateConfig(transCfg));

            var unitTransformers = new List<TransformerDevice>();
            var dualEars = new List<IReadOnlyList<DualEarTransformerDevice>>();
            var unitTransDeviceCfg = TransformerDeviceFactory.CreateConfig(unitTransCfg);
            for (int u = 0; u < unitCount; u++)
            {
                var splits = u < simCfg.Devices.Count
                    ? simCfg.Devices[u].ResolveSplitTransformers()
                    : Array.Empty<SplitTransformerRuntimeConfig>();
                if (splits.Count > 0)
                {
                    var list = new List<DualEarTransformerDevice>();
                    for (int t = 0; t < splits.Count; t++)
                    {
                        string id = t == 0 ? $"unit_transformer_u{u}" : $"unit_transformer_u{u}_t{t}";
                        list.Add(TransformerDeviceFactory.CreateDualEar(id, splits[t]));
                    }

                    dualEars.Add(list);
                    unitTransformers.Add(list[0].Through);
                }
                else
                {
                    dualEars.Add(Array.Empty<DualEarTransformerDevice>());
                    unitTransformers.Add(TransformerDeviceFactory.Create($"unit_transformer_u{u}", unitTransDeviceCfg));
                }
            }
            _unitTransformers = unitTransformers;

            _loadDevice = LoadDeviceFactory.Create("load_35", loadCfg);

            // 保存仿真时钟参数
            double integrationMult = simCfg.IntegrationStepMultiplier;
            _integrationMultiplier = integrationMult > 0 ? integrationMult : 1.0;
            _lastCycleUtc = DateTime.UtcNow;
            _transCfg  = transCfg;
            _pcsCfg    = pcsCfg;
            _propagationIntervalMs = Math.Max(10, simCfg.Runtime.PropagationIntervalMs);
            PccLineVoltageV = pccCfg.NominalLineVoltage;
            StationBus35LineVoltageV = pccCfg.StationBusNominalLineVoltage;

            SignalRouter = new ElectricalSignalRouter();

            _electricalNetwork = NetworkTopologyBuilder.Build(
                simCfg, pcsCfg, transCfg, unitTransCfg, loadCfg, pccCfg,
                meterCfg: meterCfg,
                bmsRackDevices: _bmsRackDevices,
                externalPcsDevices: pcsList,
                externalMainTransformer: _mainTransformer,
                externalUnitTransformers: unitTransformers,
                externalDualEars: dualEars,
                externalLoadDevice: _loadDevice,
                legacyEss: this,
                pcsPerUnit: _pcsPerUnit);

            RadialGraph = new RadialNetworkGraph(_electricalNetwork, pccCfg, pcsCfg, PvUnits);
            PowerSweepEngine = new RadialPowerSweepEngine(
                RadialGraph,
                this,
                pccCfg,
                pcsCfg,
                simCfg.Runtime.PropagationQuvMaxIterations,
                simCfg.Runtime.PropagationVoltageTolerancePu);
            _log.Info($"[EnergyStorageSystem] 母线前推回代已启用（{_propagationIntervalMs} ms）");

            PlantEngine = new PlantEngine(this);
            Thermal = new PlantThermalSystem(simCfg.Runtime.Thermal, channelCount, DateTime.UtcNow);
            CouplingGraph = PlantCouplingGraph.BuildDefault(_pcsList, _bmsRackDevices);
        }

        private static IReadOnlyList<PvUnitDevice> CreatePvUnits(SimulatorConfig simCfg)
        {
            var list = new List<PvUnitDevice>();
            var configs = simCfg.PvUnits ?? new List<PvUnitRuntimeConfig>();
            for (int i = 0; i < configs.Count; i++)
                list.Add(PvUnitDevice.FromRuntime($"pv{i + 1}", configs[i]));
            return list;
        }

        /// <summary>
        /// 电气步之前更新光伏出力：方阵温度/入射角 → MPPT 最大功率 → 35 kV 母线贡献。
        /// </summary>
        internal void StepPvUnits(DateTime simTime, TimeSpan elapsed)
        {
            if (PvUnits.Count == 0)
                return;

            bool gridOk = IsMainBreakerClosed && PccLineVoltageV > 1.0;
            double freq = _electricalNetwork.SystemFrequencyHz > 1.0
                ? _electricalNetwork.SystemFrequencyHz
                : 50;

            foreach (var pv in PvUnits)
            {
                pv.UpdateGridState(pv.AcNominalLineVoltageV, freq, gridOk);
                pv.Update(simTime, elapsed);
            }
        }

        /// <summary>阶段 4 起固定为 Solver 主路径 + 网络控制面。</summary>
        public bool UsesNetworkControlPlane => _electricalNetwork != null;

        internal void ApplyNetworkGridVoltages(double pccLineVoltageV, double stationBus35LineVoltageV)
        {
            PccLineVoltageV = pccLineVoltageV;
            StationBus35LineVoltageV = stationBus35LineVoltageV;
        }

        /// <summary>主断路器是否合闸（以电气网络为准）。</summary>
        public bool IsMainBreakerClosed =>
            NetworkControlBridge.IsBreakerClosed(_electricalNetwork.MainBreaker);

        /// <summary>单元高压断路器是否合闸（以电气网络为准）。</summary>
        public bool IsUnitBreakerClosed(int unitIndex)
        {
            if (unitIndex < 0 || unitIndex >= _unitBreakers.Count)
                return false;

            if (unitIndex < _electricalNetwork.UnitBreakers.Count)
                return NetworkControlBridge.IsBreakerClosed(_electricalNetwork.UnitBreakers[unitIndex]);

            return _unitBreakers[unitIndex].IsClosed;
        }

        /// <summary>单元高压断路器是否处于跳闸锁存。</summary>
        public bool IsUnitBreakerTripped(int unitIndex)
        {
            if (unitIndex < 0 || unitIndex >= _electricalNetwork.UnitBreakers.Count)
                return false;
            return _electricalNetwork.UnitBreakers[unitIndex].SwitchState.IsTripped;
        }

        /// <summary>设定主断路器合/分（写入电气网络并投影至 Legacy）。</summary>
        public void SetMainBreakerClosed(bool closed)
        {
            bool was = IsMainBreakerClosed;
            NetworkControlBridge.ApplyMainBreakerClosed(_electricalNetwork, _breaker, _loadDevice, closed);
            SimStateChangeLogger.BreakerChanged("主断", was, closed);
        }

        /// <summary>设定单元高压断路器合/分（写入电气网络并投影至 Legacy）。</summary>
        public void SetUnitBreakerClosed(int unitIndex, bool closed)
        {
            bool was = IsUnitBreakerClosed(unitIndex);
            NetworkControlBridge.ApplyUnitBreakerClosed(_electricalNetwork, _unitBreakers, unitIndex, closed);
            SimStateChangeLogger.BreakerChanged($"单元{unitIndex + 1}", was, closed);
        }

        /// <summary>复位单元高压跳闸锁存（不合闸）。</summary>
        public void ResetUnitBreakerTrip(int unitIndex) =>
            NetworkControlBridge.ResetUnitBreakerTrip(_electricalNetwork, unitIndex);

        /// <summary>设定负载计划并同步至电气网络 Load 设备。</summary>
        public void SetLoadCharacteristic(string characteristic, double value) =>
            _loadDevice.SetLoadCharacteristic(characteristic, value);

        /// <summary>运行时设定仿真电网额定线电压（V）。仅主断闭合时对外体现为 PCC 电压基准。</summary>
        public bool TrySetGridVoltage(double lineVoltageV, out string message)
        {
            message = string.Empty;
            if (lineVoltageV <= 0)
            {
                message = "电网电压必须大于 0（V，例如 220000）";
                return false;
            }

            if (lineVoltageV > 1_000_000)
            {
                message = "电网电压超出合理范围（≤ 1000000 V）";
                return false;
            }

            try
            {
                _electricalNetwork.Grid.SetNominalLineVoltage(lineVoltageV);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                message = ex.Message;
                return false;
            }

            message = $"电网额定线电压 = {lineVoltageV} V（主断闭合后 PCC 在此基础上叠加 Q-U 偏移）";
            return true;
        }

        /// <summary>运行时设定仿真电网额定频率（Hz）。并网时 PCS 跟网、电表频率取此值。</summary>
        public bool TrySetGridFrequency(double frequencyHz, out string message)
        {
            message = string.Empty;
            if (frequencyHz <= 0 || frequencyHz > 75)
            {
                message = "电网频率须在 (0, 75] Hz";
                return false;
            }

            try
            {
                _electricalNetwork.Grid.SetNominalFrequency(frequencyHz);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                message = ex.Message;
                return false;
            }

            message = $"电网额定频率 = {frequencyHz} Hz（主断闭合后 PCS 跟网、simEm.yc19 反映此值）";
            return true;
        }

        /// <summary>设定光伏方阵环境温度、光照入射角或地面反照率，下一步按 MPPT 重算最大放电功率。</summary>
        public bool TrySetPvArrayClimate(int pvNumber1Based, string side, string field, double value, out string message)
        {
            message = string.Empty;
            if (pvNumber1Based < 1 || pvNumber1Based > PvUnits.Count)
            {
                message = $"找不到 pv{pvNumber1Based}";
                return false;
            }

            var unit = PvUnits[pvNumber1Based - 1];
            var climate = unit.ArrayClimate(side);
            string label = string.Equals(side, "B", StringComparison.OrdinalIgnoreCase) ? "B" : "A";
            if (field.Equals("temperature", StringComparison.OrdinalIgnoreCase) ||
                field.Equals("temp", StringComparison.OrdinalIgnoreCase))
            {
                climate.SetAmbientTemperatureC(value);
                message = $"pv{pvNumber1Based} 方阵{label} 环境温度 = {climate.AmbientTemperatureC:0.##} ℃";
                return true;
            }

            if (field.Equals("angle", StringComparison.OrdinalIgnoreCase) ||
                field.Equals("incidence", StringComparison.OrdinalIgnoreCase))
            {
                climate.SetIncidenceAngleDeg(value);
                message = $"pv{pvNumber1Based} 方阵{label} 入射角 = {climate.IncidenceAngleDeg:0.##} °";
                return true;
            }

            if (field.Equals("albedo", StringComparison.OrdinalIgnoreCase) ||
                field.Equals("rear", StringComparison.OrdinalIgnoreCase))
            {
                climate.SetAlbedo(value);
                message = $"pv{pvNumber1Based} 方阵{label} 反照率 = {climate.Albedo:0.###}";
                return true;
            }

            message = "仅支持 temperature、angle 或 albedo";
            return false;
        }

        /// <summary>启停光伏单元（直驱设备；点表存在 logger 启停位绑定时由反馈管道自动回写）。</summary>
        public bool TrySetPvRun(int pvNumber1Based, bool run, out string message)
        {
            message = string.Empty;
            if (pvNumber1Based < 1 || pvNumber1Based > PvUnits.Count)
            {
                message = $"找不到 pv{pvNumber1Based}";
                return false;
            }

            var unit = PvUnits[pvNumber1Based - 1];
            unit.Logger.SubarrayOnOff = (ushort)(run ? 1 : 0);
            message = $"pv{pvNumber1Based} {(run ? "启动" : "停机")}";
            return true;
        }

        /// <summary>设定光伏单元有功/无功（kW/kvar，缺省项保留现值；setter 自带 clamp 并下发逆变器）。</summary>
        public bool TrySetPvPower(int pvNumber1Based, double? activeKw, double? reactiveKvar, out string message)
        {
            message = string.Empty;
            if (activeKw == null && reactiveKvar == null)
            {
                message = "有功与无功至少提供一项";
                return false;
            }

            if (pvNumber1Based < 1 || pvNumber1Based > PvUnits.Count)
            {
                message = $"找不到 pv{pvNumber1Based}";
                return false;
            }

            var logger = PvUnits[pvNumber1Based - 1].Logger;
            if (activeKw.HasValue)
                logger.SubarrayActivePowerKw = activeKw.Value;
            if (reactiveKvar.HasValue)
                logger.SubarrayReactivePowerKvar = reactiveKvar.Value;

            message = $"pv{pvNumber1Based} 有功设定 {logger.SubarrayActivePowerKw:0.##} kW · 无功设定 {logger.SubarrayReactivePowerKvar:0.##} kvar";
            return true;
        }

        /// <summary>写入 PCS 黑启动开关（含断路器联锁）；违规则返回 false 且不开启。</summary>
        public bool TrySetPcsBlackStart(int pcsSimIndex, bool requested)
        {
            if (pcsSimIndex < 0 || pcsSimIndex >= _pcsList.Count)
                return false;

            if (requested && BlackStartInterlock.IsStationShortCircuitRisk(this, pcsSimIndex, true))
                return false;

            _pcsList[pcsSimIndex].ApplyBlackStartEnabled(requested);
            return true;
        }

        /// <summary>扫描全部 PCS 黑启动联锁；违规时回调 pcsNumber（1-based）。</summary>
        public void ValidatePcsBlackStartInterlocks(Action<int> onViolation)
        {
            for (int i = 0; i < _pcsList.Count; i++)
            {
                if (!BlackStartInterlock.IsStationShortCircuitRisk(
                        this, i, _pcsList[i].GetCurrentState().BlackStartEnabled))
                    continue;

                onViolation(i + 1);
                return;
            }
        }

        /// <summary>EMU/控制面变更后刷新网络 PCS 端口（与 _pcsList 共用实例，无需重复同步）。</summary>
        public void PushPcsChannelToNetwork(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= _pcsList.Count)
                return;
            _pcsList[channelIndex].Step(new DeviceStepContext(), TimeSpan.Zero);
        }

        /// <summary>BMS 并网链路（Rack + DcLink + BmsRackDevice）。</summary>
        public void SetBmsPcsLinked(int channelIndex, bool linked)
        {
            if (channelIndex < 0 || channelIndex >= _bmsRackDevices.Count)
                return;
            NetworkControlBridge.SetBmsPcsLinked(_electricalNetwork, _bmsRackDevices[channelIndex], channelIndex, linked);
        }

        // 仿真时钟：每次主循环 / 电压源 SolveCycle 回调时，用真实墙钟间隔作为 dt
        private DateTime _lastCycleUtc;
        private readonly double _integrationMultiplier;

        /// <summary>电压源（Grid）激活周期（ms），主循环休眠间隔。</summary>
        public int LoopIntervalMs => _propagationIntervalMs;

        private (TimeSpan elapsed, TimeSpan integrationElapsed) AdvanceCycleClock()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastCycleUtc;
            _lastCycleUtc = now;

            if (elapsed <= TimeSpan.Zero)
                elapsed = TimeSpan.FromMilliseconds(1);

            var integrationElapsed = TimeSpan.FromTicks((long)(elapsed.Ticks * _integrationMultiplier));
            return (elapsed, integrationElapsed);
        }

        private readonly TransformerConfig _transCfg;
        private readonly PcsPhysicalConfig _pcsCfg;

        public string GetBlackStartSteadyLossShareMode() => _pcsCfg.BlackStartSteadyLossShareMode;

        /// <summary>
        /// 仿真主循环（IHostedService / BackgroundService）。
        /// 由 .NET Host 在 StartAsync 时调用，stoppingToken 取消时自动退出。
        /// 每 tick 只推进时钟并调用 <see cref="PlantEngine.Step"/>（导演逻辑已迁入引擎门面）。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.Info("[EnergyStorageSystem] 仿真主循环启动（经 PlantEngine.Step）");
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_propagationIntervalMs));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    var (elapsed, integrationElapsed) = AdvanceCycleClock();
                    DateTime simTime = DateTime.UtcNow;
                    PlantEngine.Step(simTime, elapsed, integrationElapsed);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止，不视为错误
                _log.Info("[EnergyStorageSystem] 仿真主循环收到停止信号，正常退出");
            }
            catch (Exception ex)
            {
                _log.Fatal("[EnergyStorageSystem] 仿真主循环发生未处理异常，已停止", ex);
                throw; // 重新抛出，让 Host 感知到服务崩溃
            }
        }

        /// <summary>
        /// 单元/主网侧无电：非黑启动则停机；黑启动保留离网建压（网侧不可用，由 EMS 启停+黑启动驱动）。
        /// </summary>
        internal void ApplyPcsGridWhenUnitDeenergized(int pcsSimIndex, PcsDevice pcs)
        {
            int unit = UnitIndexOfPcs(pcsSimIndex);
            double busV = GetUnitAcBusVoltage(unit);
            double energizedV = _pcsCfg.AcVoltageNominal * _pcsCfg.BlackStartBusEnergizedFraction;
            if (busV >= energizedV)
            {
                pcs.UpdateGridState(busV, _electricalNetwork.SystemFrequencyHz, false);
                return;
            }

            if (pcs.GetCurrentState().BlackStartEnabled)
            {
                pcs.UpdateGridState(0, _electricalNetwork.SystemFrequencyHz, false);
                return;
            }

            pcs.UpdateGridState(0, 0, false);
            pcs.ApplyBlackStartEnabled(false);
            pcs.TransitionToMode(OperationMode.Off, "单元/主网侧无电且非黑启动");
        }


        /// <summary>
        /// 同单元 690V 母线电压：单元变二次侧、35kV 反送折算，以及正在离网建压的 PCS 输出。
        /// 停机/跟网 PCS 的端子测量不是电源，不得回写进母线。
        /// </summary>
        public double GetUnitAcBusVoltage(int unitIndex)
        {
            double v = 0;
            if (unitIndex >= 0 && unitIndex < _unitTransformers.Count)
                v = Math.Max(v, _unitTransformers[unitIndex].GetCurrentState().SecondaryVoltage);
            // 仅在主断分闸且岛内已有构网反送时，用 35kV 折算补本单元 690V；
            // 避免额定初始化/并网残留的 StationBus35 把死母线误判成活母线。
            if (!IsMainBreakerClosed
                && IsUnitBreakerClosed(unitIndex)
                && unitIndex >= 0
                && unitIndex < _unitTransformers.Count)
            {
                double island35 = EssIslandBusLogic.EstimateIslandedBus35LineVoltageV(
                    _unitTransformers,
                    _unitBreakers,
                    _pcsList,
                    _pcsPerUnit);
                if (island35 > 1.0)
                {
                    double turns = Math.Max(_unitTransformers[unitIndex].TurnsRatio, 1e-6);
                    v = Math.Max(v, island35 / turns);
                }
            }
            int baseIdx = PcsBaseIndexOfUnit(unitIndex);
            int count = PcsCountOfUnit(unitIndex);
            double formingSum = 0;
            int forming = 0;
            for (int k = 0; k < count; k++)
            {
                int i = baseIdx + k;
                if (i < 0 || i >= _pcsList.Count)
                    continue;
                var st = _pcsList[i].GetCurrentState();
                if (!EssIslandBusLogic.IsPcsIslandVoltageBuilding(st) || st.AcVoltage <= 1.0)
                    continue;
                formingSum += st.AcVoltage;
                forming++;
            }
            if (forming > 0)
                v = Math.Max(v, formingSum / forming);
            return v;
        }

        private void RefreshUnitBlackStartBusContext(int unitIndex)
        {
            double busV = GetUnitAcBusVoltage(unitIndex);
            var (freqHz, phaseRad) = GetUnitFormingReference(unitIndex);
            int baseIdx = PcsBaseIndexOfUnit(unitIndex);
            int count = PcsCountOfUnit(unitIndex);
            for (int k = 0; k < count; k++)
            {
                int i = baseIdx + k;
                if (i >= 0 && i < _pcsList.Count)
                    _pcsList[i].RefreshBlackStartBusContext(busV, freqHz, phaseRad);
            }
        }

        /// <summary>同单元正在注入的构网源的频率与相位（算术/圆周平均；无源时为 0）。</summary>
        public (double FrequencyHz, double PhaseRad) GetUnitFormingReference(int unitIndex)
        {
            var sources = new List<(double VoltageV, double FrequencyHz, double PhaseRad)>();
            int baseIdx = PcsBaseIndexOfUnit(unitIndex);
            int count = PcsCountOfUnit(unitIndex);
            for (int k = 0; k < count; k++)
            {
                int i = baseIdx + k;
                if (i < 0 || i >= _pcsList.Count)
                    continue;
                var pcs = _pcsList[i];
                if (!pcs.TryGetIslandBusVoltageInjection(out var v, out var f) || v <= 1.0)
                    continue;
                sources.Add((v, f, pcs.FormingPhaseRad));
            }

            return EssIslandBusLogic.AverageFormingReference(sources);
        }

        /// <summary>同单元正在注入的构网源的孤岛电压设定算术平均（无源时为 0）。</summary>
        public double GetUnitFormingVoltageCommandV(int unitIndex)
        {
            var cmds = new List<double>();
            int baseIdx = PcsBaseIndexOfUnit(unitIndex);
            int count = PcsCountOfUnit(unitIndex);
            for (int k = 0; k < count; k++)
            {
                int i = baseIdx + k;
                if (i < 0 || i >= _pcsList.Count)
                    continue;
                var pcs = _pcsList[i];
                if (!pcs.TryGetIslandBusVoltageInjection(out var v, out _) || v <= 1.0)
                    continue;
                cmds.Add(pcs.GetCurrentState().IslandVoltageCommandV);
            }

            return EssIslandBusLogic.AverageFormingVoltageCommand(cmds);
        }

        /// <summary>PCS.Update 之后同步单元变与站用电分摊（见 <see cref="UnitTransformerIslandSync"/>）。</summary>
        internal void SyncUnitTransformerAfterPcsUpdate(DateTime simTime, TimeSpan simStep) =>
            UnitTransformerIslandSync.SyncAfterPcsUpdate(
                IsMainBreakerClosed,
                StationBus35LineVoltageV,
                _unitTransformers,
                _mainTransformer,
                _pcsList,
                IsUnitBreakerClosed,
                _pcsCfg,
                _pcsPerUnit,
                simTime,
                simStep);

        /// <summary>刷新各单元黑启动母线上下文（供 <see cref="PlantEngine"/> 调用）。</summary>
        internal void RefreshAllUnitBlackStartBusContexts()
        {
            int unitCount = _unitBreakers.Count;
            for (int u = 0; u < unitCount; u++)
                RefreshUnitBlackStartBusContext(u);
        }

        /// <summary>
        /// PCS↔BMS 耦合（兼容入口）：委托 <see cref="PlantCouplingGraph.StepCouplings"/>。
        /// </summary>
        internal void RunPcsBmsCoupling(DateTime simTime, TimeSpan elapsed, TimeSpan integrationElapsed) =>
            CouplingGraph.StepCouplings(Thermal, simTime, elapsed, integrationElapsed);

        // // 示例使用
        // public void EssMain(string[] args)
        // {
        //     if (args[0] != null)
        //     {
        //         modelName = args[0];
        //     }   
        // }

        //private static void PrintSystemState(EnergyStorageSystem ess)
        //{
        //    DateTime dt = DateTime.Now;
        //    var pcsState = ess.GetPcsState();
        //    var rackState = ess.GetBatteryRackState();
        //    Console.WriteLine($"{modelName}\t{dt.ToString()}" + $"ActivePower:{pcsState.ActivePower:F1}kW\tMinClusterSOC:{rackState.MinClusterSOC * 100:F1}%\t" );
        //}
    }
}
