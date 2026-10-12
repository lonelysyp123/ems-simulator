using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// MV 片段点名：与 <c>mv/lc.csv</c> 对齐。
    /// 并网电表遥测与高压断路器开合均走协议桥，不绑 CSV ModelSim。
    /// </summary>
    internal static class LcMvMap
    {
        public const string LineVoltageAb = "mvyc25306";
        public const string LineVoltageBc = "mvyc25308";
        public const string LineVoltageCa = "mvyc25310";
        public const string PhaseACurrent = "mvyc25312";
        public const string PhaseBCurrent = "mvyc25314";
        public const string PhaseCCurrent = "mvyc25316";
        public const string NeutralCurrent = "mvyc25318";
        public const string TotalActivePower = "mvyc25326";
        public const string TotalReactivePower = "mvyc25334";
        public const string TotalApparentPower = "mvyc25342";
        public const string PowerFactor = "mvyc25350";
        public const string Frequency = "mvyc25352";
        public const string ForwardActiveEnergySecondary = "mvyc25362";
        public const string ReverseActiveEnergySecondary = "mvyc25364";
        public const string ForwardReactiveEnergySecondary = "mvyc25368";
        public const string ReverseReactiveEnergySecondary = "mvyc25370";
        public const string HvBreakerCommand = "mv_param1";
    }

    /// <summary>并网电表（PCC）快照，平衡三相；电能已折成 PT×CT 二次值。</summary>
    internal readonly record struct LcMvMeterSnap(
        double LineVoltageAb,
        double LineVoltageBc,
        double LineVoltageCa,
        double PhaseACurrent,
        double PhaseBCurrent,
        double PhaseCCurrent,
        double NeutralCurrent,
        double TotalActivePower,
        double TotalReactivePower,
        double TotalApparentPower,
        double PowerFactor,
        double Frequency,
        double ForwardActiveEnergySecondary,
        double ReverseActiveEnergySecondary,
        double ForwardReactiveEnergySecondary,
        double ReverseReactiveEnergySecondary);

    /// <summary>把 PCC 并网电表折成 MV 电表寄存器值（瞬时一次值，电能二次值）。</summary>
    internal static class LcMvTelemetry
    {
        public static List<(string Param, object Value)> Collect(LcMvMeterSnap? meter)
        {
            var m = meter ?? default;
            return
            [
                (LcMvMap.LineVoltageAb, m.LineVoltageAb),
                (LcMvMap.LineVoltageBc, m.LineVoltageBc),
                (LcMvMap.LineVoltageCa, m.LineVoltageCa),
                (LcMvMap.PhaseACurrent, m.PhaseACurrent),
                (LcMvMap.PhaseBCurrent, m.PhaseBCurrent),
                (LcMvMap.PhaseCCurrent, m.PhaseCCurrent),
                (LcMvMap.NeutralCurrent, m.NeutralCurrent),
                (LcMvMap.TotalActivePower, m.TotalActivePower),
                (LcMvMap.TotalReactivePower, m.TotalReactivePower),
                (LcMvMap.TotalApparentPower, m.TotalApparentPower),
                (LcMvMap.PowerFactor, m.PowerFactor),
                (LcMvMap.Frequency, m.Frequency),
                (LcMvMap.ForwardActiveEnergySecondary, m.ForwardActiveEnergySecondary),
                (LcMvMap.ReverseActiveEnergySecondary, m.ReverseActiveEnergySecondary),
                (LcMvMap.ForwardReactiveEnergySecondary, m.ForwardReactiveEnergySecondary),
                (LcMvMap.ReverseReactiveEnergySecondary, m.ReverseReactiveEnergySecondary)
            ];
        }

        public static LcMvMeterSnap FromPcc(MeterSimulator meter, double systemFrequencyHz)
        {
            var tel = meter.Telemetry;
            var primary = tel.Primary;
            double lineVoltage = Math.Max(0.0, primary.LineVoltageV);
            double totalP = primary.ActivePowerKw;
            double totalQ = primary.ReactivePowerKvar;
            double totalS = primary.ApparentPowerKva;
            double i = primary.LineCurrentA;
            if (Math.Abs(i) < 1e-9 && lineVoltage > 1.0 && totalS > 1e-9)
                i = totalS * 1000.0 / lineVoltage / Math.Sqrt(3.0);

            double factor = meter.Config.Pt.Ratio * meter.Config.Ct.Ratio;
            double ToSecondary(double primaryEnergy) =>
                factor > 1e-9 ? primaryEnergy / factor : 0;

            return new LcMvMeterSnap(
                lineVoltage,
                lineVoltage,
                lineVoltage,
                i,
                i,
                i,
                NeutralCurrent: 0,
                totalP,
                totalQ,
                totalS,
                AcQuantityConverter.ComputeSignedPowerFactor(totalP, totalQ),
                systemFrequencyHz,
                ToSecondary(tel.ForwardActiveEnergyKwh),
                ToSecondary(tel.ReverseActiveEnergyKwh),
                ToSecondary(tel.ForwardReactiveEnergyKvarh),
                ToSecondary(tel.ReverseReactiveEnergyKvarh));
        }

        /// <summary>本单元电表镜像（馈线采样或 PCS 母线）；电能无 PT×CT 折算，按镜像原值抄出。</summary>
        public static LcMvMeterSnap FromUnitMeter(ElectricityMeterData meter)
        {
            return new LcMvMeterSnap(
                meter.LineVoltageAB,
                meter.LineVoltageBC,
                meter.LineVoltageCA,
                meter.PhaseACurrent,
                meter.PhaseBCurrent,
                meter.PhaseCCurrent,
                NeutralCurrent: 0,
                meter.TotalActivePower,
                meter.TotalReactivePower,
                meter.TotalApparentPower,
                meter.PowerFactor,
                meter.Frequency,
                meter.ForwardActiveEnergy,
                meter.ReverseActiveEnergy,
                meter.InductiveReactiveEnergy,
                meter.CapacitiveReactiveEnergy);
        }
    }

    /// <summary>解释并下发 <see cref="LcMvMap.HvBreakerCommand"/>。</summary>
    internal static class LcMvControl
    {
        public static double Encode(bool closed) => closed ? 0xAA : 0xEE;

        public static bool TryInterpret(double raw, out double normalized, out bool closed) =>
            ModbusValueConverter.TryNormalizeHvBreakerCommand(raw, out normalized, out closed);

        /// <summary>
        /// 合法 AA/EE 则驱动本单元高压断路器；非法命令返回 false（调用方应回退 LC 寄存器）。
        /// </summary>
        public static bool TryApply(int unitId, double raw, out double normalized, out string message)
        {
            if (!TryInterpret(raw, out normalized, out bool closed))
            {
                message = $"非法高压断路器命令 0x{(int)Math.Round(raw):X2}（仅接受 AA/EE）";
                return false;
            }

            return DeviceControlFacade.TrySetUnitBreaker(unitId, closed, out message);
        }
    }
}
