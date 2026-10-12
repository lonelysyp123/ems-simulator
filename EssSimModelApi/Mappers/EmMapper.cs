using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi.ElectricMeter;

namespace EssSimulator.EssSimModelApi.Mappers
{
    /// <summary>
    /// 将电气网络 PCC 电表采样映射到电表接口 DTO（EmData）。
    /// </summary>
    public static class EmMapper
    {
        public static void MapEssToEmData(EnergyStorageSystem ess, EmData emData, TimeSpan dt,
            ref double forwardKWh, ref double reverseKWh)
        {
            var meter = ess.ElectricalNetwork.PccMeter;
            var tel = meter.Telemetry;
            var primary = tel.Primary;

            double lineVoltage = Math.Max(0.0, primary.LineVoltageV);
            double totalP = primary.ActivePowerKw;
            double totalQ = primary.ReactivePowerKvar;
            double totalS = Math.Sqrt(totalP * totalP + totalQ * totalQ);
            double pf = AcQuantityConverter.ComputeSignedPowerFactor(totalP, totalQ);
            double I = primary.LineCurrentA;

            if (Math.Abs(I) < 1e-9 && lineVoltage > 1.0 && totalS > 1e-9)
                I = totalS * 1000.0 / lineVoltage / Math.Sqrt(3.0);

            double phaseV = lineVoltage / Math.Sqrt(3.0);

            emData.PhaseAVoltage = emData.PhaseBVoltage = emData.PhaseCVoltage = (float)phaseV;
            emData.LineVoltageAB = emData.LineVoltageBC = emData.LineVoltageCA = (float)lineVoltage;
            emData.PhaseACurrent = emData.PhaseBCurrent = emData.PhaseCCurrent = (float)I;

            double perP = totalP / 3.0;
            double perQ = totalQ / 3.0;
            emData.PhaseAActivePower = emData.PhaseBActivePower = emData.PhaseCActivePower = (float)perP;
            emData.PhaseAReactivePower = emData.PhaseBReactivePower = emData.PhaseCReactivePower = (float)perQ;
            emData.TotalActivePower = (float)totalP;
            emData.TotalReactivePower = (float)totalQ;
            emData.TotalApparentPower = (float)totalS;
            emData.PowerFactor = (float)pf;

            emData.Frequency = (float)ess.ElectricalNetwork.SystemFrequencyHz;

            forwardKWh = tel.ForwardActiveEnergyKwh;
            reverseKWh = tel.ReverseActiveEnergyKwh;
            emData.ForwardActiveEnergy = (float)forwardKWh;
            emData.ReverseActiveEnergy = (float)reverseKWh;
        }
    }
}
