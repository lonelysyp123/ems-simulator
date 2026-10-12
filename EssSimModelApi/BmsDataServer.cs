using EssSimulator.EssDeviceSimModel;
using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssSimModelApi.Mappers;
using System.Linq;

namespace EssSimulator.EssSimModelApi
{
    /// <summary>
    /// BMS 协议镜像：构造时按单元注册 DTO，投影由 <see cref="ProtocolProjectionService"/> 在物理步进末尾调用。
    /// </summary>
    public class BmsDataService
    {
        private readonly int _unitCount;
        private readonly BatteryManagementSystemData[] _bmsDataList;
        private readonly IReadOnlyList<int> _clusterCounts;

        public BmsDataService(SimulatorConfig cfg)
        {
            var bmsCfgList = cfg.GetBmsDeviceConfigs();
            _unitCount = bmsCfgList.Count;
            _bmsDataList = new BatteryManagementSystemData[_unitCount];
            _clusterCounts = bmsCfgList.Select(x => x.ClusterCount).ToList();

            var store = SimulatorHost.Instance;
            for (int i = 0; i < _unitCount; i++)
            {
                _bmsDataList[i] = BmsDataGenerator.GenerateSampleData(1, _clusterCounts[i]);
                var bmsCfg = bmsCfgList[i];
                var stack = _bmsDataList[i].BatteryStacks[0];

                // 空调默认开启、默认制冷设定 20°C（可经 Modbus 控制点 yt1/yt2 或 dpc 修改）
                if (_bmsDataList[i].AirConditioners.Count == 0)
                    _bmsDataList[i].AirConditioners.Add(new AirConditionerData { UnitId = 1 });
                _bmsDataList[i].AirConditioners[0].OnCommand = true;
                _bmsDataList[i].AirConditioners[0].CoolingSetpointCommand = 20f;
                float clusterEnergyKWh = (float)(
                    bmsCfg.PackCount
                    * bmsCfg.CellSeriesCount
                    * bmsCfg.CellParallelCount
                    * bmsCfg.CellNominalVoltage
                    * bmsCfg.CellNominalCapacity
                    / 1000.0);
                stack.NominalEnergyKWh = clusterEnergyKWh * bmsCfg.ClusterCount;
                stack.MaxCRate = 0.5f;
                stack.ManagedClusterCount = bmsCfg.ClusterCount;
                foreach (var cluster in stack.Cluseter)
                {
                    cluster.Measurements.NominalEnergyKWh = clusterEnergyKWh;
                    cluster.Measurements.MaxCRate = 0.5f;
                }
                store.Register($"bms{i + 1}", _bmsDataList[i]);
            }
        }

        public void Project(EnergyStorageSystem ess)
        {
            for (int i = 0; i < _unitCount && i < ess._bmsRackDevices.Count; i++)
            {
                BmsMapper.SyncTelemetryAndProtection(ess._bmsRackDevices[i], _bmsDataList[i]);
                ApplyAirConditionerControl(ess, i, _bmsDataList[i]);
                BmsThermalProbeMapper.Apply(ess.Thermal, i, _bmsDataList[i]);
            }

            if (_unitCount > 0)
                _bmsDataList[0].Timestamp = DateTime.Now;
        }

        // 保留供外部（Cmd.cs 等）直接调用的告警状态机（向后兼容）
        public void UpdateStateForUnder(ref bool? l1, ref bool? l2, ref bool? l3,
            float t1, float t2, float t3, float r1, float r2, float r3, double val)
            => BmsRackProtection.UpdateUnder(ref l1, ref l2, ref l3, t1, t2, t3, r1, r2, r3, val);

        public void UpdateStateForOver(ref bool? l1, ref bool? l2, ref bool? l3,
            float t1, float t2, float t3, float r1, float r2, float r3, double val)
            => BmsRackProtection.UpdateOver(ref l1, ref l2, ref l3, t1, t2, t3, r1, r2, r3, val);

        private static void ApplyAirConditionerControl(
            EnergyStorageSystem ess,
            int bmsIndex,
            BatteryManagementSystemData bmsData)
        {
            if (bmsData.AirConditioners.Count == 0)
                return;

            var ac = bmsData.AirConditioners[0];
            if (ac.OnCommand.HasValue)
                ess.Thermal.SetCabinetAirConditioningOn(bmsIndex, ac.OnCommand.Value);
            if (ac.CoolingSetpointCommand.HasValue && ac.CoolingSetpointCommand.Value > 0)
                ess.Thermal.SetCabinetCoolingSetpointC(bmsIndex, ac.CoolingSetpointCommand.Value);
        }
    }
}
