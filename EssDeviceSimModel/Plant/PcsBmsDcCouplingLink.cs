using System;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Thermal;

namespace EssSimulator.EssDeviceSimModel.Plant
{
    /// <summary>
    /// PCS↔BMS 直流耦合边：环境温、V/I 交换、损耗登记，以及电池节点温度同步。
    /// 功率降额不做温度反馈：是否降功率只由 PCS / 对应 BMS 的告警与故障信息决定。
    /// </summary>
    public sealed class PcsBmsDcCouplingLink
    {
        private readonly Func<string, double?>? _settledDcIntoDeviceResolver;

        public PcsBmsDcCouplingLink(
            PcsDevice pcs,
            BmsRackDevice bms,
            int thermalCabinetIndex,
            Func<string, double?>? settledDcIntoDeviceResolver = null)
        {
            Pcs = pcs ?? throw new ArgumentNullException(nameof(pcs));
            Bms = bms ?? throw new ArgumentNullException(nameof(bms));
            ThermalCabinetIndex = thermalCabinetIndex;
            _settledDcIntoDeviceResolver = settledDcIntoDeviceResolver;
        }

        public PcsDevice Pcs { get; }
        public BmsRackDevice Bms { get; }
        public int ThermalCabinetIndex { get; }

        public void Step(
            PlantThermalSystem thermal,
            DateTime simTime,
            TimeSpan elapsed,
            TimeSpan integrationElapsed)
        {
            ArgumentNullException.ThrowIfNull(thermal);

            double cabinetAir = thermal.GetBmsAmbientCelsius(ThermalCabinetIndex);
            double pcsAmbient = thermal.Enabled ? cabinetAir : thermal.OutdoorCelsius;

            // 电池节点温度同步到 BMS 设备：作为电芯热环境（节点温度越高，电芯散热效率越低）。
            Bms.ApplyBatteryNodeTemperature(
                ThermalCabinetIndex >= 0 && ThermalCabinetIndex < thermal.Cabinets.Count
                    ? thermal.Cabinets[ThermalCabinetIndex].BatteryNodeCelsius
                    : cabinetAir);

            ((ITemperatureAware)Pcs).ApplyAmbientTemperature(pcsAmbient);
            ((ITemperatureAware)Bms).ApplyAmbientTemperature(cabinetAir);

            if (Bms.IsLinked)
            {
                var rackState = Bms.Rack.GetRackState();
                if (rackState == null)
                    return;

                Pcs.Update(rackState.TotalVoltage, rackState.IsFault, simTime, elapsed, integrationElapsed);
                double rackCurrent = ResolveRackCurrentA();
                Bms.UpdatePhysics(rackCurrent, cabinetAir, simTime, integrationElapsed);
            }
            else
            {
                Pcs.Update(0, 0, simTime, elapsed, integrationElapsed);
                Bms.UpdatePhysics(0, cabinetAir, simTime, integrationElapsed);
            }

            thermal.RecordCabinetHeatWatts(
                ThermalCabinetIndex,
                ((IElectricalLossSource)Bms).GetElectricalLossWatts());
        }

        /// <summary>
        /// 硬规则4：直流单一真相来自相量内核结算的 I_dc（= P_ac/V_dc + 损耗），SOC 不由 PCS 控制自算的
        /// DcCurrent 推进。结算值「流入设备为正」（放电为正），BMS 堆电流「充电为正」，故取负。
        /// 已接入内核但本拍未结算该 PCS（无网络功率交换）时不推进 SOC（0）；未接入内核（独立测试）时退回控制值。
        /// </summary>
        private double ResolveRackCurrentA()
        {
            if (_settledDcIntoDeviceResolver == null)
                return -Pcs.GetCurrentState().DcCurrent;
            return _settledDcIntoDeviceResolver(Pcs.DeviceId) is { } idcIntoDeviceA
                ? -idcIntoDeviceA
                : 0;
        }
    }
}
