using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Thermal;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;

namespace EssSimulator.EssSimModelApi.Mappers
{
    /// <summary>
    /// 将柜体热网络多点探针读数写入 BMS 辅机 DTO（空调 / 液冷 / 温湿度），供点表遥测。
    /// </summary>
    public static class BmsThermalProbeMapper
    {
        /// <summary>
        /// 确保辅机列表存在并填充探针温度。热仿真关闭时仍写入气候/固定环境温，避免寄存器全 0。
        /// </summary>
        public static void Apply(PlantThermalSystem thermal, int bmsIndex, BatteryManagementSystemData bmsData)
        {
            if (thermal == null || bmsData == null)
                return;

            EnsureAuxiliaries(bmsData);
            var biases = thermal.ProbeBiases;

            double outdoor = thermal.OutdoorCelsius;
            BmsCabinetThermalZone? zone = null;
            if (bmsIndex >= 0 && bmsIndex < thermal.Cabinets.Count)
                zone = thermal.Cabinets[bmsIndex];

            float Read(ThermalProbeKind kind, double bias)
            {
                if (zone != null && thermal.Enabled)
                    return (float)ThermalProbeSampler.ReadCelsius(zone, kind, bias);
                return (float)(outdoor + bias);
            }

            var ac = bmsData.AirConditioners[0];
            ac.CabinetTemp = Read(ThermalProbeKind.Air, biases.CabinetAirCelsius);
            ac.DefrostTemp = Read(ThermalProbeKind.Coil, biases.CoilCelsius);
            ac.CondensationTemp = Read(ThermalProbeKind.Condenser, biases.CondenserCelsius);
            bool acOn = zone?.AirConditioningOn ?? thermal.HvacEnabled;
            double setpoint = zone?.CoolingSetpointCelsius ?? thermal.CabinetHvacSetpointCelsius;
            ac.CoolingSetTemp = (float)setpoint;
            bool hvacCapable = thermal.HvacEnabled;
            bool cooling = zone?.IsHvacCooling == true;
            ac.DeviceOperationStatus = acOn;
            ac.CompressorStatus = cooling;
            ac.IndoorFanStatus = cooling || acOn;
            ac.OutdoorFanStatus = cooling;
            ac.CabinetOverheat = ac.CabinetTemp > setpoint + 8;

            var lc = bmsData.LiquidCoolingSystems[0];
            // 供液偏电池侧（偏冷），回液偏空气/电池混合（偏热）
            lc.SupplyLiquidTemp = Read(ThermalProbeKind.Battery, biases.LiquidSupplyCelsius);
            lc.ReturnLiquidTemp = Read(ThermalProbeKind.AirTop, biases.LiquidReturnCelsius);
            lc.EnvironmentTemp = Read(ThermalProbeKind.Outdoor, 0);
            lc.CondensationTemp1 = Read(ThermalProbeKind.Condenser, biases.CondenserCelsius);

            bmsData.TempHumiditySensors[0].Temperature = Read(ThermalProbeKind.AirTop, biases.DehumidifierTopCelsius);
            bmsData.TempHumiditySensors[1].Temperature = Read(ThermalProbeKind.Air, biases.DehumidifierMidCelsius);
            bmsData.TempHumiditySensors[2].Temperature = Read(ThermalProbeKind.AirBottom, biases.DehumidifierBottomCelsius);
        }

        public static void EnsureAuxiliaries(BatteryManagementSystemData bmsData)
        {
            if (bmsData.AirConditioners.Count == 0)
                bmsData.AirConditioners.Add(new AirConditionerData { UnitId = 1 });

            if (bmsData.LiquidCoolingSystems.Count == 0)
                bmsData.LiquidCoolingSystems.Add(new LiquidCoolingSystemData { UnitId = 1 });

            while (bmsData.TempHumiditySensors.Count < 3)
            {
                int id = bmsData.TempHumiditySensors.Count + 1;
                bmsData.TempHumiditySensors.Add(new TemperatureHumidityData { UnitId = id });
            }
        }
    }
}
