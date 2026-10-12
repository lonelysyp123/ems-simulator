using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>Acrel APM810 三相电表。发电为正有功并记入反向电能；受电为负并记入正向电能。</summary>
    public sealed class PvApm810Meter
    {
        public string DeviceId { get; }
        public double NominalLineVoltageV { get; }

        public PvApm810Meter(string deviceId, double nominalLineVoltageV)
        {
            DeviceId = deviceId;
            NominalLineVoltageV = nominalLineVoltageV;
        }

        public double PhaseAVoltage { get; private set; }
        public double PhaseBVoltage { get; private set; }
        public double PhaseCVoltage { get; private set; }
        public double LineVoltageAb { get; private set; }
        public double LineVoltageBc { get; private set; }
        public double LineVoltageCa { get; private set; }
        public double PhaseACurrent { get; private set; }
        public double PhaseBCurrent { get; private set; }
        public double PhaseCCurrent { get; private set; }
        public double PhaseAActivePowerW { get; private set; }
        public double PhaseBActivePowerW { get; private set; }
        public double PhaseCActivePowerW { get; private set; }
        public double TotalActivePowerW { get; private set; }
        public double FeedInPowerM1W { get; private set; }
        public double FeedInPowerM2W { get; private set; }
        public double PhaseAReactivePowerVar { get; private set; }
        public double PhaseBReactivePowerVar { get; private set; }
        public double PhaseCReactivePowerVar { get; private set; }
        public double TotalReactivePowerVar { get; private set; }
        public double PhaseAApparentPowerVa { get; private set; }
        public double PhaseBApparentPowerVa { get; private set; }
        public double PhaseCApparentPowerVa { get; private set; }
        public double TotalApparentPowerVa { get; private set; }
        public double TotalPowerFactor { get; private set; }
        public double PhaseAPowerFactor { get; private set; }
        public double PhaseBPowerFactor { get; private set; }
        public double PhaseCPowerFactor { get; private set; }
        public double FrequencyHz { get; private set; }
        public double ForwardActiveEnergyKwh { get; private set; }
        public double ReverseActiveEnergyKwh { get; private set; }
        public double ForwardReactiveEnergyKvarh { get; private set; }
        public double ReverseReactiveEnergyKvarh { get; private set; }

        public void Sample(double lineVoltageV, double activePowerKw, double reactivePowerKvar, double frequencyHz, TimeSpan step)
        {
            double pKw = activePowerKw;
            double exportKw = Math.Max(0, pKw);
            double importKw = Math.Max(0, -pKw);
            double qKvar = reactivePowerKvar;
            double sKva = Math.Sqrt(pKw * pKw + qKvar * qKvar);
            double vLine = Math.Max(0, lineVoltageV);
            double vPhase = vLine / Math.Sqrt(3.0);
            double i = vLine > 1 && sKva > 1e-9 ? sKva * 1000.0 / (vLine * Math.Sqrt(3.0)) : 0;
            double pf = AcQuantityConverter.ComputeSignedPowerFactor(pKw, qKvar);
            double pW = pKw * 1000.0;
            double exportW = exportKw * 1000.0;
            double qVar = qKvar * 1000.0;
            double sVa = sKva * 1000.0;

            PhaseAVoltage = PhaseBVoltage = PhaseCVoltage = vPhase;
            LineVoltageAb = LineVoltageBc = LineVoltageCa = vLine;
            PhaseACurrent = PhaseBCurrent = PhaseCCurrent = i;
            PhaseAActivePowerW = PhaseBActivePowerW = PhaseCActivePowerW = pW / 3.0;
            TotalActivePowerW = pW;
            FeedInPowerM1W = exportW;
            FeedInPowerM2W = exportW;
            PhaseAReactivePowerVar = PhaseBReactivePowerVar = PhaseCReactivePowerVar = qVar / 3.0;
            TotalReactivePowerVar = qVar;
            PhaseAApparentPowerVa = PhaseBApparentPowerVa = PhaseCApparentPowerVa = sVa / 3.0;
            TotalApparentPowerVa = sVa;
            TotalPowerFactor = PhaseAPowerFactor = PhaseBPowerFactor = PhaseCPowerFactor = pf;
            FrequencyHz = frequencyHz;

            double hours = Math.Max(0, step.TotalHours);
            ReverseActiveEnergyKwh += exportKw * hours;
            ForwardActiveEnergyKwh += importKw * hours;
            if (qKvar >= 0)
                ForwardReactiveEnergyKvarh += qKvar * hours;
            else
                ReverseReactiveEnergyKvarh += -qKvar * hours;
        }
    }
}
