using System;
using EssSimulator.Configuration;

namespace EssSimulator.EssDeviceSimModel.Thermal
{
    /// <summary>
    /// 单个 BMS 柜体：室外(边界) — 外壳 — 柜内空气 — 电池等效热容。
    /// 电池损耗注入电池节点；空调闭环从空气节点抽热。
    /// </summary>
    public sealed class BmsCabinetThermalZone
    {
        public const string OutdoorId = "outdoor";
        public const string ShellId = "shell";
        public const string AirId = "air";
        public const string BatteryId = "battery";

        private readonly BmsCabinetThermalConfig _cfg;
        private readonly ThermalNetwork _network;
        private bool _hvacCoolingActive;

        public BmsCabinetThermalZone(string zoneId, BmsCabinetThermalConfig cfg, double initialTempCelsius)
        {
            ZoneId = zoneId ?? throw new ArgumentNullException(nameof(zoneId));
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _network = new ThermalNetwork();

            _network.AddNode(new ThermalNode(OutdoorId, 1, initialTempCelsius, isBoundary: true));
            _network.AddNode(new ThermalNode(ShellId, _cfg.ShellThermalCapacityJPerK, initialTempCelsius));
            _network.AddNode(new ThermalNode(AirId, _cfg.AirThermalCapacityJPerK, initialTempCelsius));
            _network.AddNode(new ThermalNode(BatteryId, _cfg.BatteryThermalCapacityJPerK, initialTempCelsius));

            _network.AddEdge(new ThermalEdge(OutdoorId, ShellId, _cfg.OutdoorToShellResistanceKPerW));
            _network.AddEdge(new ThermalEdge(ShellId, AirId, _cfg.ShellToAirResistanceKPerW));
            _network.AddEdge(new ThermalEdge(BatteryId, AirId, _cfg.BatteryToAirResistanceKPerW));

            AirConditioningOn = _cfg.HvacEnabled;
            CoolingSetpointCelsius = _cfg.HvacSetpointCelsius;
        }

        public string ZoneId { get; }

        /// <summary>空调开机状态（外部可控，默认取配置 HvacEnabled）。true 时按回差控制制冷，把柜内空气稳定在设定温度附近。</summary>
        public bool AirConditioningOn { get; set; }

        /// <summary>本柜制冷设定温度（°C），默认取配置 HvacSetpointCelsius；可经 BMS 空调控制点调整。</summary>
        public double CoolingSetpointCelsius { get; set; }

        public double CabinetAirCelsius => _network.GetNode(AirId).TemperatureCelsius;
        public double ShellCelsius => _network.GetNode(ShellId).TemperatureCelsius;
        public double BatteryNodeCelsius => _network.GetNode(BatteryId).TemperatureCelsius;
        public double OutdoorCelsius => _network.GetNode(OutdoorId).TemperatureCelsius;

        /// <summary>上一步电池欧姆损耗（W），本步注入电池节点。</summary>
        public double PendingBatteryHeatW { get; set; }

        /// <summary>本步实际空调抽热功率（W，正值表示制冷）。</summary>
        public double LastHvacCoolingWatts { get; private set; }

        /// <summary>空调压缩机是否处于制冷状态。</summary>
        public bool IsHvacCooling => _hvacCoolingActive && LastHvacCoolingWatts > 1e-3;

        public void Step(TimeSpan dt, double outdoorCelsius)
        {
            _network.GetNode(OutdoorId).TemperatureCelsius = outdoorCelsius;
            _network.GetNode(BatteryId).HeatInjectionW += PendingBatteryHeatW;

            LastHvacCoolingWatts = ApplyHvacCooling();

            _network.Step(dt);
            PendingBatteryHeatW = 0;
        }

        private double ApplyHvacCooling()
        {
            if (!AirConditioningOn || _cfg.HvacCoolingPowerW <= 0)
            {
                _hvacCoolingActive = false;
                return 0;
            }

            double airT = _network.GetNode(AirId).TemperatureCelsius;
            double set = CoolingSetpointCelsius;
            double hyst = Math.Max(0, _cfg.HvacHysteresisCelsius);

            // 回差：过热开启，冷却到设定关闭
            if (!_hvacCoolingActive && airT >= set + hyst)
                _hvacCoolingActive = true;
            else if (_hvacCoolingActive && airT <= set)
                _hvacCoolingActive = false;

            if (!_hvacCoolingActive)
                return 0;

            double error = airT - set;
            if (error <= 0)
            {
                _hvacCoolingActive = false;
                return 0;
            }

            double gain = Math.Max(0, _cfg.HvacProportionalGainWPerK);
            double cool = Math.Min(_cfg.HvacCoolingPowerW, error * gain);
            _network.GetNode(AirId).HeatInjectionW -= cool;
            return cool;
        }
    }
}
