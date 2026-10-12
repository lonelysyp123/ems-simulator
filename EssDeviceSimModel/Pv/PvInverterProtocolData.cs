namespace EssSimulator.EssDeviceSimModel.Pv
{
    public sealed class PvInverterProtocolData
    {
        private readonly PvInverterDevice _inverter;

        internal PvInverterProtocolData(PvInverterDevice inverter) => _inverter = inverter;

        public double RatedActivePowerKw => _inverter.RatedPowerKw;
        public double RatedReactivePowerKvar => _inverter.RatedReactivePowerKvar;
        public IReadOnlyList<double> MpptVoltageV => _inverter.MpptVoltageV;
        public IReadOnlyList<double> MpptCurrentA => _inverter.MpptCurrentA;
        public double LineVoltageV => _inverter.GetCurrentState().AcVoltage;
        public double PhaseCurrentAmplitudeA => Math.Abs(_inverter.GetCurrentState().AcCurrent);
        public double ActivePowerW => _inverter.GetCurrentState().ActivePower * 1000;
        public double ReactivePowerVar => _inverter.GetCurrentState().ReactivePower * 1000;
        public double FrequencyHz => _inverter.GetCurrentState().Frequency;

        public double ApparentPowerVa
        {
            get
            {
                var state = _inverter.GetCurrentState();
                double p = state.ActivePower;
                double q = state.ReactivePower;
                return Math.Sqrt(p * p + q * q) * 1000;
            }
        }

        public double PowerFactor
        {
            get
            {
                var state = _inverter.GetCurrentState();
                double p = state.ActivePower;
                double q = state.ReactivePower;
                double s = Math.Sqrt(p * p + q * q);
                return s > 0 ? p / s : 0;
            }
        }

        public ushort OperationStatus => (ushort)PcsDisplayLabels.ToOperationStatusCode(
            _inverter.GetCurrentState(), _inverter.IsExternalRunCommand);

        /// <summary>阳光电源逆变器保持寄存器 5006：207 开机。</summary>
        public const ushort RunCommandStart = 207;

        /// <summary>阳光电源逆变器保持寄存器 5006：206 关机。</summary>
        public const ushort RunCommandStop = 206;

        public ushort RunCommand
        {
            get => _inverter.IsExternalRunCommand ? RunCommandStart : RunCommandStop;
            set
            {
                if (value != RunCommandStart && value != RunCommandStop)
                    throw new ArgumentOutOfRangeException(nameof(value), "启停命令只接受 207（开机）或 206（关机）");
                _inverter.SyncExternalRunCommand(value == RunCommandStart);
            }
        }

        public double ActivePowerSettingKw
        {
            get => _inverter.ActivePowerSettingKw;
            set => _inverter.ActivePowerSettingKw = value;
        }

        public double ReactivePowerSettingKvar
        {
            get => _inverter.ReactivePowerSettingKvar;
            set => _inverter.ReactivePowerSettingKvar = value;
        }

        public double ActivePowerSettingW => ActivePowerSettingKw * 1000;
        public double ReactivePowerSettingVar => ReactivePowerSettingKvar * 1000;
    }
}
