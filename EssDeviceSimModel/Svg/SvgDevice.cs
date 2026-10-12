using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Svg
{
    /// <summary>
    /// 站用 SVG：并联电流源，恒无功。电压和频率由外部测得，不担当电压源。
    /// 母线收集功率时调用 <see cref="ApplyMeasurement"/>；<see cref="Step"/> 不重复计算。
    /// </summary>
    public sealed class SvgDevice : ISinglePortDevice
    {
        public const double LagSeconds = 0.02;

        /// <summary>满载损耗占额定无功的比例。空载加上随电流平方增加的部分，在额定无功时合计为此值。</summary>
        public const double LossFraction = 0.008;

        /// <summary>开机后的空载损耗（控制与冷却）占额定无功的比例，与实际无功无关。</summary>
        public const double NoLoadLossFraction = 0.0015;

        public const double VoltageMinPu = 0.85;
        public const double VoltageMaxPu = 1.15;

        private readonly double _ratedKvar;
        private readonly double _nominalVoltageV;
        private double _qKvar;

        public SvgDevice(string deviceId, SvgConfig? config = null)
        {
            config ??= new SvgConfig();
            DeviceId = deviceId;
            _ratedKvar = Math.Max(0, config.RatedCapacityKvar);
            _nominalVoltageV = config.NominalLineVoltageV > 1 ? config.NominalLineVoltageV : 35000;
            Port = new ElectricalPort
            {
                PortId = "ac",
                Kind = PortKind.BusConnected,
                Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities { Connection = ThreePhaseConnection.Star }),
                Output = ElectricalPortSnapshot.FromAc(new AcInternalQuantities { Connection = ThreePhaseConnection.Star })
            };
            PowerFactor = 1;
        }

        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.Svg;
        public ElectricalPort Port { get; }
        public IReadOnlyList<ElectricalPort> Ports => new[] { Port };

        /// <summary>1 开机，其他值关机。</summary>
        public ushort RunCommand { get; set; }

        /// <summary>主站写入的无功设定（kvar），不随输出夹限改写。</summary>
        public double ReactiveSetpointKvar { get; set; }

        public ushort RunState { get; private set; }
        public double LineVoltageAbV { get; private set; }
        public double LineVoltageBcV { get; private set; }
        public double LineVoltageCaV { get; private set; }
        public double PhaseACurrentA { get; private set; }
        public double PhaseBCurrentA { get; private set; }
        public double PhaseCCurrentA { get; private set; }
        public double ReactivePowerKvar { get; private set; }
        public double PowerFactor { get; private set; }
        public double FrequencyHz { get; private set; }
        public double RatedCapacityKvar => _ratedKvar;
        public double AvailableReactiveUpperKvar { get; private set; }

        /// <summary>运行损耗，从电网取电时为负（kW）。开机且母线有电压时至少为空载损耗，随无功平方增加，满载达到额定的 <see cref="LossFraction"/>。</summary>
        public double ActivePowerKw { get; private set; }

        public void ApplyMeasurement(double lineVoltageV, double frequencyHz, TimeSpan step)
        {
            double voltage = double.IsFinite(lineVoltageV) ? lineVoltageV : 0;
            double frequency = double.IsFinite(frequencyHz) ? frequencyHz : 0;
            LineVoltageAbV = LineVoltageBcV = LineVoltageCaV = voltage;
            FrequencyHz = frequency;

            bool on = RunCommand == 1;
            bool voltageOk = voltage >= VoltageMinPu * _nominalVoltageV
                && voltage <= VoltageMaxPu * _nominalVoltageV;
            bool frequencyOk = frequency > 1;
            if (!on || !voltageOk || !frequencyOk)
            {
                RunState = on ? (ushort)2 : (ushort)0;
                // 闭锁且母线仍有电压时，辅助电源继续取空载损耗；关机或失压则无法交换功率。
                double activeKw = on && voltage > 1 ? NoLoadActivePowerKw() : 0;
                ClearReactive(activeKw, voltage);
                WritePort(voltage, frequency);
                return;
            }

            RunState = 1;
            AvailableReactiveUpperKvar = _ratedKvar;
            double target = Math.Clamp(ReactiveSetpointKvar, -_ratedKvar, _ratedKvar);
            double blend = LagBlend(step);
            _qKvar += (target - _qKvar) * blend;
            ReactivePowerKvar = _qKvar;
            ActivePowerKw = LoadActivePowerKw(_qKvar);
            ApplyPhaseCurrent(voltage, ActivePowerKw, _qKvar);
            PowerFactor = ComputePowerFactor(ActivePowerKw, _qKvar);
            WritePort(voltage, frequency);
        }

        public void Step(DeviceStepContext context, TimeSpan step)
        {
        }

        private void ClearReactive(double activeKw, double voltage)
        {
            _qKvar = 0;
            ReactivePowerKvar = 0;
            ActivePowerKw = activeKw;
            AvailableReactiveUpperKvar = 0;
            ApplyPhaseCurrent(voltage, activeKw, 0);
            PowerFactor = ComputePowerFactor(activeKw, 0);
        }

        /// <summary>P = -[P0 + (Pfull − P0)(Q/Qrated)²]，取电为负。</summary>
        private double LoadActivePowerKw(double reactiveKvar)
        {
            if (_ratedKvar <= 0)
                return 0;
            double ratio = reactiveKvar / _ratedKvar;
            double p0 = NoLoadLossFraction * _ratedKvar;
            double pFull = LossFraction * _ratedKvar;
            return -(p0 + (pFull - p0) * ratio * ratio);
        }

        private double NoLoadActivePowerKw() => -NoLoadLossFraction * _ratedKvar;

        private void ApplyPhaseCurrent(double voltage, double activeKw, double reactiveKvar)
        {
            double apparent = Math.Sqrt(activeKw * activeKw + reactiveKvar * reactiveKvar);
            double current = voltage > 1
                ? apparent * 1000.0 / (ElectricalConventions.Sqrt3 * voltage)
                : 0;
            PhaseACurrentA = PhaseBCurrentA = PhaseCCurrentA = current;
        }

        private void WritePort(double voltage, double frequency)
        {
            var output = AcQuantityConverter.FromLineVoltageAndPower(
                voltage,
                ActivePowerKw,
                ReactivePowerKvar,
                ThreePhaseConnection.Star,
                frequency);
            AcPortHelper.WriteAcOutput(Port, output);
        }

        private static double LagBlend(TimeSpan step)
        {
            double dt = step.TotalSeconds;
            if (dt <= 0)
                return 0;
            return 1 - Math.Exp(-dt / LagSeconds);
        }

        private static double ComputePowerFactor(double activeKw, double reactiveKvar)
        {
            if (Math.Abs(reactiveKvar) < 1e-6)
                return 1;
            double apparent = Math.Sqrt(activeKw * activeKw + reactiveKvar * reactiveKvar);
            if (apparent < 1e-9)
                return 1;
            return Math.Sign(reactiveKvar) * Math.Abs(activeKw) / apparent;
        }
    }
}
