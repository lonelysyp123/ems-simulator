using EssSimulator.EssDeviceSimModel;

namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>Logger4000 站级汇总与子阵控制（Device ID 247）。</summary>
    public sealed class PvLogger
    {
        public const int ForwardedDeviceCount = 25;

        private readonly PvUnitDevice _unit;
        private readonly object _commandGate = new();
        private DateTime _stamp;
        private double _activePercent = 100;
        private double _reactivePercent;
        private double _powerFactorSet = 1;
        private double _activeKwSet;
        private double _reactiveKvarSet;
        private ushort _onOff = 1;

        public PvLogger(PvUnitDevice unit)
        {
            _unit = unit ?? throw new ArgumentNullException(nameof(unit));
            ConnectedDeviceCount = ForwardedDeviceCount;
            UnlatchState = 1;
            _activeKwSet = unit.RatedPowerKw;
        }

        public int ConnectedDeviceCount { get; }
        public int FaultDeviceCount { get; private set; }
        public ushort RunState { get; private set; }
        public ushort UnlatchState { get; set; }
        public double TotalActivePowerW { get; private set; }
        public double TotalReactivePowerVar { get; private set; }
        public double ApparentPowerVa { get; private set; }
        public double DailyYieldKwh { get; private set; }
        public double TotalYieldKwh { get; private set; }
        public double MonthlyYieldKwh { get; private set; }
        public double AnnualYieldKwh { get; private set; }
        public double MinAdjustableActivePowerKw { get; private set; }
        public double MaxAdjustableActivePowerKw { get; private set; }
        public double MinAdjustableReactivePowerKvar { get; private set; }
        public double MaxAdjustableReactivePowerKvar { get; private set; }
        public double NominalActivePowerKw { get; private set; }
        public double NominalReactivePowerKvar { get; private set; }
        public int GridConnectedDeviceCount { get; private set; }
        public int OffGridDeviceCount { get; private set; }
        public double Pt100_1C { get; private set; }
        public double Pt100_2C { get; private set; }
        public double Adc1Voltage { get; set; }
        public double Adc1CurrentMa { get; set; }
        public double Adc2Voltage { get; set; }
        public double Adc2CurrentMa { get; set; }
        public double Adc3Voltage { get; set; }
        public double Adc3CurrentMa { get; set; }
        public double Adc4Voltage { get; set; }
        public double Adc4CurrentMa { get; set; }
        public uint DigitalInputBitmap { get; private set; }
        public bool OilTemperatureAlarm { get; private set; }
        public bool OilTemperatureTrip { get; private set; }
        public bool WindingTemperatureAlarm { get; private set; }
        public bool WindingTemperatureTrip { get; private set; }
        public ushort DoRmuTrip { get; set; }
        public ushort DoLvFan { get; set; }
        public ushort Do3 { get; set; }
        public ushort Do4 { get; set; }

        public ushort SubarrayOnOff
        {
            get { lock (_commandGate) return _onOff; }
            set
            {
                lock (_commandGate)
                {
                    _onOff = (ushort)(value != 0 ? 1 : 0);
                    _unit.SyncExternalRunCommand(_onOff != 0);
                }
            }
        }

        public double SubarrayActivePowerKw
        {
            get { lock (_commandGate) return _activeKwSet; }
            set
            {
                lock (_commandGate)
                {
                    _activeKwSet = Math.Clamp(value, 0, _unit.RatedPowerKw);
                    _activePercent = _unit.RatedPowerKw > 0 ? _activeKwSet / _unit.RatedPowerKw * 100 : 0;
                    _unit.SetActivePowerCommand(_activeKwSet);
                }
            }
        }

        public double SubarrayActivePowerPercent
        {
            get { lock (_commandGate) return _activePercent; }
            set
            {
                lock (_commandGate)
                    SubarrayActivePowerKw = _unit.RatedPowerKw * Math.Clamp(value, 0, 100) / 100.0;
            }
        }

        public double SubarrayReactivePowerKvar
        {
            get { lock (_commandGate) return _reactiveKvarSet; }
            set
            {
                lock (_commandGate)
                {
                    _reactiveKvarSet = Math.Clamp(value, -_unit.RatedPowerKw, _unit.RatedPowerKw);
                    _reactivePercent = _unit.RatedPowerKw > 0 ? _reactiveKvarSet / _unit.RatedPowerKw * 100 : 0;
                    _unit.SetReactivePowerCommand(_reactiveKvarSet);
                }
            }
        }

        public double SubarrayReactivePowerPercent
        {
            get { lock (_commandGate) return _reactivePercent; }
            set
            {
                lock (_commandGate)
                    SubarrayReactivePowerKvar = _unit.RatedPowerKw * Math.Clamp(value, -100, 100) / 100.0;
            }
        }

        public double SubarrayPowerFactor
        {
            get { lock (_commandGate) return _powerFactorSet; }
            set
            {
                lock (_commandGate)
                {
                    _powerFactorSet = Math.Clamp(value, -1, 1);
                    double mag = Math.Max(0.2, Math.Abs(_powerFactorSet));
                    double p = Math.Max(_activeKwSet, 1e-6);
                    double q = p * Math.Tan(Math.Acos(Math.Clamp(mag, 0, 1)));
                    if (_powerFactorSet < 0)
                        q = -q;
                    SubarrayReactivePowerKvar = q;
                }
            }
        }

        public void Refresh(DateTime timeStamp, TimeSpan step)
        {
            double pKw = 0, qKvar = 0, daily = 0, total = 0;
            int grid = 0;
            foreach (var inv in _unit.Inverters)
            {
                var st = inv.GetCurrentState();
                pKw += st.ActivePower;
                qKvar += st.ReactivePower;
                daily += st.DailyDischargeEnergy;
                total += st.TotalDischargeEnergy;
                if (st.Mode == OperationMode.Normal)
                    grid++;
            }

            if (_stamp != default && _stamp.Month != timeStamp.Month)
                MonthlyYieldKwh = 0;
            if (_stamp != default && _stamp.Year != timeStamp.Year)
                AnnualYieldKwh = 0;
            double dKwh = Math.Max(0, pKw) * Math.Max(0, step.TotalHours);
            MonthlyYieldKwh += dKwh;
            AnnualYieldKwh += dKwh;
            _stamp = timeStamp;

            TotalActivePowerW = pKw * 1000.0;
            TotalReactivePowerVar = qKvar * 1000.0;
            ApparentPowerVa = Math.Sqrt(TotalActivePowerW * TotalActivePowerW + TotalReactivePowerVar * TotalReactivePowerVar);
            DailyYieldKwh = daily;
            TotalYieldKwh = total;
            NominalActivePowerKw = _unit.RatedPowerKw;
            NominalReactivePowerKvar = _unit.RatedReactivePowerKvar;
            MinAdjustableActivePowerKw = 0;
            MaxAdjustableActivePowerKw = _unit.RatedPowerKw;
            MinAdjustableReactivePowerKvar = -_unit.RatedPowerKw;
            MaxAdjustableReactivePowerKvar = _unit.RatedPowerKw;
            GridConnectedDeviceCount = grid;
            OffGridDeviceCount = 0;
            FaultDeviceCount = 0;
            RunState = (ushort)(grid > 0 ? 1 : 0);

            var xf = _unit.Transformer;
            Pt100_1C = xf.OilTemperatureC;
            Pt100_2C = xf.WindingTemperatureC;
            OilTemperatureAlarm = xf.OilAlarm;
            OilTemperatureTrip = xf.OilTrip;
            WindingTemperatureAlarm = xf.WindingAlarm;
            WindingTemperatureTrip = xf.WindingTrip;
            DigitalInputBitmap = BuildDin(xf);
        }

        private static uint BuildDin(PvTransformerMonitor xf)
        {
            uint bits = 0;
            bits |= 1u << 0;  // 门关
            bits |= 1u << 1;  // 烟感正常
            bits |= 1u << 2;  // QFA 合
            bits |= 1u << 3;  // SPD1 正常
            if (xf.OilAlarm) bits |= 1u << 7;
            if (xf.OilTrip) bits |= 1u << 8;
            if (xf.WindingAlarm) bits |= 1u << 11;
            if (xf.WindingTrip) bits |= 1u << 12;
            bits |= 1u << 13; // 环网柜烟感正常
            bits |= 1u << 14; // 环网柜门关
            bits |= 1u << 15; // 所用变室门关
            return bits;
        }
    }
}
