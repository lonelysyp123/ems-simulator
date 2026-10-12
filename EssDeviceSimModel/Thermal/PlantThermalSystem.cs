using System;
using System.Collections.Generic;
using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Battery;
using log4net;

namespace EssSimulator.EssDeviceSimModel.Thermal
{
    /// <summary>
    /// 电站热子系统：气候 + 每路 BMS 柜体热区。
    /// 由 <see cref="PlantEngine"/> 在电气步之后、PCS/BMS 耦合之前推进。
    /// </summary>
    public sealed class PlantThermalSystem
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PlantThermalSystem));

        private readonly ThermalRuntimeConfig _cfg;
        private readonly ClimateModel _climate;
        private readonly List<BmsCabinetThermalZone> _cabinets;

        public PlantThermalSystem(ThermalRuntimeConfig cfg, int bmsChannelCount, DateTime initialTime)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _climate = new ClimateModel(cfg.Climate);
            _cabinets = new List<BmsCabinetThermalZone>(Math.Max(0, bmsChannelCount));

            double t0 = _climate.EvaluateOutdoorCelsius(initialTime);
            for (int i = 0; i < bmsChannelCount; i++)
                _cabinets.Add(new BmsCabinetThermalZone($"bms{i + 1}", cfg.Cabinet, t0));

            Log.Info(
                $"[PlantThermal] 已初始化 {_cabinets.Count} 个 BMS 柜体热区（Enabled={cfg.Enabled}, HVAC={cfg.Cabinet.HvacCoolingPowerW:F0} W）");
        }

        public bool Enabled => _cfg.Enabled;
        public ClimateModel Climate => _climate;
        public IReadOnlyList<BmsCabinetThermalZone> Cabinets => _cabinets;
        public ThermalProbeBiasesConfig ProbeBiases => _cfg.ProbeBiases;
        public double CabinetHvacCoolingPowerW => _cfg.Cabinet.HvacCoolingPowerW;
        public double CabinetHvacSetpointCelsius => _cfg.Cabinet.HvacSetpointCelsius;

        public double OutdoorCelsius { get; private set; } = 25;

        /// <summary>
        /// 供 BMS 电芯散热环境温：启用热网时为柜内空气；否则为 Fixed 或 25°C。
        /// </summary>
        public double GetBmsAmbientCelsius(int bmsIndex)
        {
            if (!_cfg.Enabled)
                return _cfg.Climate.FixedCelsius ?? 25.0;

            if (bmsIndex < 0 || bmsIndex >= _cabinets.Count)
                return OutdoorCelsius;

            return _cabinets[bmsIndex].CabinetAirCelsius;
        }

        /// <summary>推进气候边界与各柜体热网络一步。</summary>
        public void Step(DateTime simTime, TimeSpan dt)
        {
            OutdoorCelsius = _climate.EvaluateOutdoorCelsius(simTime);
            ThermalAgingContext.ApplyFrom(_cfg.Feedback);

            if (!_cfg.Enabled)
                return;

            foreach (var cabinet in _cabinets)
                cabinet.Step(dt, OutdoorCelsius);
        }

        public ThermalFeedbackConfig Feedback => _cfg.Feedback;

        /// <summary>运行时开关空调（各柜共用配置）。</summary>
        public void SetHvacEnabled(bool enabled) => _cfg.Cabinet.HvacEnabled = enabled;

        /// <summary>运行时修改空调设定点（°C）。</summary>
        public void SetHvacSetpointCelsius(double setpointCelsius) =>
            _cfg.Cabinet.HvacSetpointCelsius = setpointCelsius;

        public bool HvacEnabled => _cfg.Cabinet.HvacEnabled && _cfg.Cabinet.HvacCoolingPowerW > 0;

        /// <summary>运行时开关指定柜体空调（外部控制：1 开 / 0 关）。</summary>
        public void SetCabinetAirConditioningOn(int bmsIndex, bool on)
        {
            if (bmsIndex >= 0 && bmsIndex < _cabinets.Count)
                _cabinets[bmsIndex].AirConditioningOn = on;
        }

        /// <summary>运行时修改指定柜体空调制冷设定温度（°C），如 20。</summary>
        public void SetCabinetCoolingSetpointC(int bmsIndex, double setpointCelsius)
        {
            if (bmsIndex >= 0 && bmsIndex < _cabinets.Count)
                _cabinets[bmsIndex].CoolingSetpointCelsius = setpointCelsius;
        }

        /// <summary>根据本步 BMS 电流登记电池欧姆损耗，供下一热步注入。</summary>
        public void RecordBatteryHeatFromRack(int bmsIndex, BatteryRackSimulator rack, double rackCurrentA)
        {
            if (!_cfg.Enabled || bmsIndex < 0 || bmsIndex >= _cabinets.Count)
                return;

            RecordCabinetHeatWatts(bmsIndex, EstimateRackOhmicLossWatts(rack, rackCurrentA));
        }

        /// <summary>直接登记柜体电池侧热注入（W）；由 <see cref="IElectricalLossSource"/> 提供。</summary>
        public void RecordCabinetHeatWatts(int bmsIndex, double heatWatts)
        {
            if (!_cfg.Enabled || bmsIndex < 0 || bmsIndex >= _cabinets.Count)
                return;

            _cabinets[bmsIndex].PendingBatteryHeatW = Math.Max(0, heatWatts);
        }

        /// <summary>
        /// 堆级等效欧姆损耗：串并联电芯路径 + Pack/簇/堆附加内阻。
        /// </summary>
        public static double EstimateRackOhmicLossWatts(BatteryRackSimulator rack, double rackCurrentA)
        {
            if (rack == null || Math.Abs(rackCurrentA) < 1e-9)
                return 0;

            var cfg = rack.GetRackConfig();
            var cluster = cfg.ClusterConfig;
            var pack = cluster?.PackConfig;
            if (cluster == null || pack == null)
                return rackCurrentA * rackCurrentA * Math.Max(1e-6, cfg.RackInternalResistance);

            // 与电芯模型默认内阻同量级（0.0002 Ω）；串并联等效到堆端口
            const double cellInternalResistanceOhm = 0.0002;
            int seriesCells = Math.Max(1, pack.SeriesCount) * Math.Max(1, cluster.PackCount);
            int parallelCells = Math.Max(1, pack.ParallelCount);
            int clusterCount = Math.Max(1, cfg.ClusterCount);

            double rCellString = seriesCells * cellInternalResistanceOhm / parallelCells;
            double rPerCluster = rCellString
                + pack.PackInternalResistance * Math.Max(1, cluster.PackCount)
                + cluster.ClusterInternalResistance;
            double rEq = rPerCluster / clusterCount + cfg.RackInternalResistance;
            return rackCurrentA * rackCurrentA * Math.Max(1e-9, rEq);
        }
    }
}
