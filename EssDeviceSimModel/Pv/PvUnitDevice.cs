using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;

namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>
    /// 光伏单元（MVS 箱变站）：16 台组串逆变器 + Logger/电表/箱变温度/气象/DIDO/PID。
    /// 对外量对齐站级点表，供后续 ModelSim 绑定。
    /// </summary>
    public sealed class PvUnitDevice
    {
        private readonly PvUnitConfig _config;
        private readonly PvInverterDevice[] _inverters;
        private readonly PvModuleSimulator _module;
        private double _gridLineVoltage = 690;
        private double _gridFrequency = 50;
        private bool _gridAvailable;

        public PvUnitDevice(string deviceId, PvUnitConfig config, PvModuleSimulator? module = null)
        {
            DeviceId = deviceId;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (_config.InverterCount < 1)
                throw new ArgumentOutOfRangeException(nameof(config), "逆变器数量至少为 1");

            _module = module ?? PvModuleSimulator.CreateNeg21c20q();
            _inverters = new PvInverterDevice[_config.InverterCount];
            for (int i = 0; i < _inverters.Length; i++)
            {
                _inverters[i] = new PvInverterDevice($"{deviceId}.inv{i + 1}", _config.Inverter, _module)
                {
                    DisplayLabel = $"{deviceId}-INV{i + 1}"
                };
            }

            ArrayA = new PvArrayClimate();
            ArrayB = new PvArrayClimate();
            Weather = new PvWeatherStation();
            Transformer = new PvTransformerMonitor();
            MeterLv = new PvApm810Meter($"{deviceId}.meterLv", _config.UnitXfSecondaryV);
            MeterHv = new PvApm810Meter($"{deviceId}.meterHv", _config.UnitXfPrimaryV);
            MvIo1 = PvMvIoModule.CreateModule1($"{deviceId}.mvdido1");
            MvIo2 = PvMvIoModule.CreateModule2($"{deviceId}.mvdido2");
            Pid1 = new PvPidDevice($"{deviceId}.pid1");
            Pid2 = new PvPidDevice($"{deviceId}.pid2");
            Logger = new PvLogger(this);
        }

        public static PvUnitDevice CreateDefault(string deviceId) =>
            new(deviceId, new PvUnitConfig());

        public static PvUnitDevice FromRuntime(string deviceId, PvUnitRuntimeConfig runtime)
        {
            runtime ??= new PvUnitRuntimeConfig();
            var spec = TrinaPvModuleCatalog.GetOrThrow(runtime.ModuleModel);
            double deg = PvModuleSimulator.DegradationFactorFromYears(spec, runtime.OperatingYears);
            var module = new PvModuleSimulator(spec, deg);
            var unit = new PvUnitDevice(deviceId, ToConfig(runtime), module);
            unit.Logger.SubarrayOnOff = 1;
            unit.ArrayA.SetAlbedo(runtime.GroundAlbedo);
            unit.ArrayB.SetAlbedo(runtime.GroundAlbedo);
            return unit;
        }

        public static PvUnitConfig ToConfig(PvUnitRuntimeConfig runtime)
        {
            runtime ??= new PvUnitRuntimeConfig();
            int invCount = Math.Max(1, runtime.InverterCount);
            return new PvUnitConfig
            {
                InverterCount = invCount,
                Inverter = new PvInverterConfig
                {
                    ModulesPerString = Math.Max(1, runtime.ModulesPerString),
                    StringCount = Math.Max(1, runtime.StringCount),
                    RatedPowerKw = runtime.InverterRatedPowerKw,
                    MaxPowerKw = runtime.InverterMaxPowerKw,
                    Efficiency = runtime.InverterEfficiency,
                    DcVoltageRangeMinV = runtime.DcVoltageMin,
                    DcVoltageRangeMaxV = runtime.DcVoltageMax,
                    AcNominalLineVoltageV = runtime.InverterAcVoltageV
                },
                UnitXfPrimaryV = runtime.UnitXfPrimaryV,
                UnitXfSecondaryV = runtime.UnitXfSecondaryV,
                UnitXfRatedKva = runtime.UnitXfRatedKva > 0
                    ? runtime.UnitXfRatedKva
                    : invCount * runtime.InverterRatedPowerKw
            };
        }

        public string DeviceId { get; }
        public double AcNominalLineVoltageV => _config.UnitXfSecondaryV;
        public int InverterCount => _inverters.Length;
        public IReadOnlyList<PvInverterDevice> Inverters => _inverters;
        public double RatedPowerKw => _inverters.Sum(inv => inv.RatedPowerKw);
        public int TotalModuleCount => _inverters.Sum(inv => inv.TotalModuleCount);
        public double AvailableDcPowerKw => _inverters.Sum(inv => inv.AvailableDcPowerKw);
        public double ActivePowerKw => _inverters.Sum(inv => inv.GetCurrentState().ActivePower);
        public double ReactivePowerKvar => _inverters.Sum(inv => inv.GetCurrentState().ReactivePower);

        public int ArrayAInverterCount => (_inverters.Length + 1) / 2;
        public int ArrayBInverterCount => _inverters.Length - ArrayAInverterCount;
        public PvArrayClimate ArrayA { get; }
        public PvArrayClimate ArrayB { get; }
        public double MaximumDischargePowerKw =>
            ArrayA.AvailableAcPowerKw + ArrayB.AvailableAcPowerKw;

        public PvLogger Logger { get; }
        public PvTransformerMonitor Transformer { get; }
        public PvApm810Meter MeterLv { get; }
        public PvApm810Meter MeterHv { get; }
        public PvMvIoModule MvIo1 { get; }
        public PvMvIoModule MvIo2 { get; }
        public PvPidDevice Pid1 { get; }
        public PvPidDevice Pid2 { get; }
        public PvWeatherStation Weather { get; }

        public void SyncExternalRunCommand(bool run)
        {
            foreach (var inv in _inverters)
                inv.SyncExternalRunCommand(run);
        }

        public void TransitionToMode(OperationMode newMode)
        {
            foreach (var inv in _inverters)
                inv.TransitionToMode(newMode);
        }

        public void UpdateGridState(double voltage, double frequency, bool isUtilityGridAvailable)
        {
            _gridLineVoltage = voltage;
            _gridFrequency = frequency;
            _gridAvailable = isUtilityGridAvailable;
            foreach (var inv in _inverters)
                inv.UpdateGridState(voltage, frequency, isUtilityGridAvailable);
        }

        public void SetPowerCommand(double activePowerKw, double reactivePowerKvar = 0)
        {
            int n = _inverters.Length;
            double pEach = activePowerKw / n;
            double qEach = reactivePowerKvar / n;
            foreach (var inv in _inverters)
                inv.SetPowerCommand(pEach, qEach);
        }

        public PvArrayClimate ArrayClimate(string side) =>
            string.Equals(side, "B", StringComparison.OrdinalIgnoreCase) ? ArrayB : ArrayA;

        /// <summary>按方阵 A/B 的环境温度、入射角与反照率计算 MPPT 出力（场况，非 IEC STC）。</summary>
        public void Update(DateTime timeStamp, TimeSpan timeStep, double gRearWm2 = 0)
        {
            int nA = ArrayAInverterCount;
            UpdateArray(ArrayA, 0, nA, timeStamp, timeStep, gRearWm2);
            UpdateArray(ArrayB, nA, _inverters.Length, timeStamp, timeStep, gRearWm2);

            double gAvg = 0.5 * (ArrayA.PlaneOfArrayWm2 + ArrayB.PlaneOfArrayWm2);
            double tAmb = 0.5 * (ArrayA.AmbientTemperatureC + ArrayB.AmbientTemperatureC);
            double tMod = 0.5 * (ArrayA.CellTemperatureC + ArrayB.CellTemperatureC);
            Weather.Update(tAmb, tMod, gAvg, gAvg * 0.9, timeStamp, timeStep);
            FinishStation(tAmb, timeStamp, timeStep);
        }

        /// <summary>
        /// 场况入口：gFront 为平面辐照，ambientC 为环境温度（电池温度由 NOCT 估算）。
        /// gRearWm2&gt;0 时覆盖两侧反照率计算，便于单测打 BNPI。
        /// </summary>
        public void Update(double gFrontWm2, double ambientC, DateTime timeStamp, TimeSpan timeStep, double gRearWm2 = 0)
        {
            ArrayA.SetAmbientTemperatureC(ambientC);
            ArrayB.SetAmbientTemperatureC(ambientC);
            double angle = InverseIncidenceForIrradiance(gFrontWm2);
            ArrayA.SetIncidenceAngleDeg(angle);
            ArrayB.SetIncidenceAngleDeg(angle);
            Update(timeStamp, timeStep, gRearWm2);
        }

        private void UpdateArray(
            PvArrayClimate climate, int fromInclusive, int toExclusive,
            DateTime timeStamp, TimeSpan timeStep, double gRearWm2)
        {
            double g = climate.PlaneOfArrayWm2;
            double gRear = gRearWm2 > 0 ? gRearWm2 : climate.Albedo * g;
            climate.RearWm2 = gRear;
            double cellTempC = PvModuleSimulator.EstimateCellTempC(_module.Spec, climate.AmbientTemperatureC, g);
            climate.CellTemperatureC = cellTempC;
            double availableAc = 0;
            double activeAc = 0;
            double vSum = 0;
            double iSum = 0;
            int vLive = 0;
            var reasons = new List<string>();
            for (int i = fromInclusive; i < toExclusive; i++)
            {
                _inverters[i].Update(g, cellTempC, timeStamp, timeStep, gRear);
                availableAc += _inverters[i].AvailableDcPowerKw * _config.Inverter.Efficiency;
                var st = _inverters[i].GetCurrentState();
                activeAc += st.ActivePower;
                iSum += st.DcCurrent;
                if (st.DcVoltage > 1)
                {
                    vSum += st.DcVoltage;
                    vLive++;
                }
                reasons.Add(_inverters[i].LimitReason);
            }

            climate.AvailableAcPowerKw = availableAc;
            climate.ActivePowerKw = activeAc;
            climate.DcVoltageV = vLive > 0 ? vSum / vLive : 0;
            climate.DcCurrentA = iSum;
            climate.LimitReason = reasons.Count == 0
                ? "停机"
                : PvInverterDevice.WorstLimitReason(reasons);
        }

        private void FinishStation(double ambientC, DateTime timeStamp, TimeSpan timeStep)
        {
            double loadPu = RatedPowerKw > 0 ? Math.Clamp(ActivePowerKw / RatedPowerKw, 0, 1.2) : 0;
            Transformer.Update(ambientC, loadPu);

            double lvV = _gridAvailable ? _config.UnitXfSecondaryV : 0;
            double hvV = _gridAvailable ? _config.UnitXfPrimaryV : 0;
            MeterLv.Sample(lvV, ActivePowerKw, ReactivePowerKvar, _gridFrequency, timeStep);
            MeterHv.Sample(hvV, ActivePowerKw, ReactivePowerKvar, _gridFrequency, timeStep);

            Pid1.Update(ambientC);
            Pid2.Update(ambientC);
            Logger.Refresh(timeStamp, timeStep);
        }

        internal static double InverseIncidenceForIrradiance(double gPoaWm2, double beamWm2 = PvIrradianceModel.PeakWm2)
        {
            if (gPoaWm2 <= 0 || beamWm2 <= 0)
                return 0;
            double g = Math.Min(gPoaWm2, beamWm2);
            double s = Math.Clamp(g / beamWm2, 0, 1);
            return Math.Asin(s) * 180.0 / Math.PI;
        }
    }
}
