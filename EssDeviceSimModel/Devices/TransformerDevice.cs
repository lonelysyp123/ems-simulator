using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    public sealed class TransformerDevice : ITransformerDevice
    {
        private readonly TransformerDeviceConfig _config;
        private readonly double _ambientTemperature;
        private double _prevPrimaryVoltagePu = -1.0;
        private double _inrushExtraPrimaryA;

        public TransformerDevice(string deviceId, TransformerDeviceConfig config, double ambientTemp = 25.0)
        {
            DeviceId = deviceId;
            _config = config;
            _ambientTemperature = ambientTemp;
            _currentState = new TransformerState
            {
                PrimaryCurrent = 0,
                Temperature = ambientTemp,
                Timestamp = DateTime.Now
            };
            Primary = CreatePort("primary", config.PrimaryConnection, PortKind.SeriesUpstream);
            Secondary = CreatePort("secondary", config.SecondaryConnection, PortKind.SeriesDownstream);
        }

        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.Transformer;
        public TransformerDeviceConfig Config => _config;
        public TransformerState _currentState { get; private set; }
        public ElectricalPort Primary { get; }
        public ElectricalPort Secondary { get; }
        public IReadOnlyList<ElectricalPort> Ports => new[] { Primary, Secondary };

        public double TurnsRatio => _config.SecondaryNominalLineVoltageV > 1e-9
            ? _config.PrimaryNominalLineVoltageV / _config.SecondaryNominalLineVoltageV
            : 1.0;

        public double MagnetizingReactiveKvar => GetSecondaryMagnetizingReactiveKvar();
        public double NoLoadActivePowerKw => GetSecondaryNoLoadActivePowerKw();

        public TransformerState GetCurrentState() => _currentState;

        public double GetSecondaryMagnetizingReactiveKvar()
        {
            double v = _currentState.SecondaryVoltage;
            double iMag = _currentState.MagnetizingCurrentSecondary;
            if (v < 1.0 || iMag < 1e-6)
                return 0;
            return Math.Sqrt(3.0) * v * iMag / 1000.0;
        }

        public double GetSecondaryNoLoadActivePowerKw() => Math.Max(0, _currentState.IronLoss / 1000.0);

        /// <summary>励磁涌流对应的站用电需求（供黑启动 PCS 承担）。</summary>
        public (double ActiveKw, double ReactiveKvar) GetInrushDemandKwKvar()
        {
            double v = _currentState.SecondaryVoltage;
            double i = _currentState.MagnetizingInrushCurrentSecondary;
            if (v < 1.0 || i < 1e-6)
                return (0, 0);

            double skva = Math.Sqrt(3.0) * v * i / 1000.0;
            return (skva * 0.12, skva * 0.92);
        }

        public void Step(DeviceStepContext context, TimeSpan step)
        {
            var priIn = AcPortHelper.ReadAcInput(Primary);
            var secIn = AcPortHelper.ReadAcInput(Secondary);

            double apparentKva = Math.Sqrt(
                secIn.ActivePowerKw * secIn.ActivePowerKw + secIn.ReactivePowerKvar * secIn.ReactivePowerKvar);
            double powerFactor = apparentKva > 0 ? secIn.ActivePowerKw / apparentKva : 1.0;

            RunPhysics(
                priIn.LineVoltageV,
                secIn.LineCurrentA,
                powerFactor,
                apparentKva,
                secIn.ReactivePowerKvar,
                context.SimulationTime,
                step,
                applyReactiveVoltageShift: true);

            PublishPorts(priIn.FrequencyHz, secIn, context);
        }

        public void Update(
            double primaryVoltage,
            double secondaryCurrent,
            double powerFactor,
            double totalApparentPowerKva,
            double totalReactivePowerKvar,
            DateTime timeStamp,
            TimeSpan simulationStep,
            bool applyReactiveVoltageShift = true)
        {
            RunPhysics(
                primaryVoltage,
                secondaryCurrent,
                powerFactor,
                totalApparentPowerKva,
                totalReactivePowerKvar,
                timeStamp,
                simulationStep,
                applyReactiveVoltageShift);
        }

        public void OverrideSecondaryVoltage(double secondaryLineVoltageV) =>
            _currentState.SecondaryVoltage = secondaryLineVoltageV;

        /// <summary>主断分闸且无反送时，状态与端口一并清零，避免界面读到上一拍并网电压。</summary>
        public void WriteDeenergizedPorts()
        {
            var emptyPri = new AcInternalQuantities { Connection = _config.PrimaryConnection };
            var emptySec = new AcInternalQuantities { Connection = _config.SecondaryConnection };
            AcPortHelper.WriteAcOutput(Primary, emptyPri);
            AcPortHelper.WriteAcOutput(Secondary, emptySec);
        }

        public void RefreshIslandReverseExcitation(
            double stationBus35LineVoltageV,
            double secondaryCurrent,
            double powerFactor,
            double apparentKva,
            double reactiveKvar,
            DateTime timeStamp,
            TimeSpan step)
        {
            double primaryEqV = stationBus35LineVoltageV * TurnsRatio;
            RunPhysics(primaryEqV, secondaryCurrent, powerFactor, apparentKva, reactiveKvar, timeStamp, step, false);
            _currentState.SecondaryVoltage = stationBus35LineVoltageV;
            _currentState.PrimaryCurrent = 0;
        }

        private void RunPhysics(
            double primaryVoltage,
            double secondaryCurrent,
            double powerFactor,
            double totalApparentPowerKva,
            double totalReactivePowerKvar,
            DateTime timeStamp,
            TimeSpan simulationStep,
            bool applyReactiveVoltageShift)
        {
            _currentState.PrimaryVoltage = primaryVoltage;
            double loadSecondaryCurrent = secondaryCurrent;
            _currentState.PowerFactor = powerFactor;
            _currentState.Timestamp = timeStamp;

            double dt = Math.Max(simulationStep.TotalSeconds, 1e-6);
            double turnsRatio = TurnsRatio;
            double vN = _config.PrimaryNominalLineVoltageV;
            double vPuNow = vN > 1e-9 ? Math.Clamp(primaryVoltage / vN, 0.0, 1.5) : 0.0;

            if (_config.MagnetizingInrushEnabled)
            {
                double tau = Math.Max(0.05, _config.MagnetizingInrushDecayTimeConstantSec);
                _inrushExtraPrimaryA *= Math.Exp(-dt / tau);

                if (_prevPrimaryVoltagePu >= 0.0)
                {
                    double dvPu = vPuNow - _prevPrimaryVoltagePu;
                    if (dvPu > 1e-9)
                    {
                        double ratePuPerSec = dvPu / dt;
                        double th = _config.MagnetizingInrushDvDtThresholdPuPerSec;
                        if (ratePuPerSec > th)
                        {
                            double iRatedPri = _config.RatedPowerKva * 1000 / (vN * Math.Sqrt(3));
                            double denom = Math.Max(th * 4.0, 1e-9);
                            double intensity = Math.Clamp((ratePuPerSec - th) / denom, 0.0, 1.0);
                            double add = _config.MagnetizingInrushPeakExtraMultipleOfRatedPrimary * iRatedPri * intensity;
                            _inrushExtraPrimaryA += add;
                            double iMax = _config.MagnetizingInrushMaxExtraMultipleOfRatedPrimary * iRatedPri;
                            if (_inrushExtraPrimaryA > iMax)
                                _inrushExtraPrimaryA = iMax;
                        }
                    }
                }

                _prevPrimaryVoltagePu = vPuNow;
            }
            else
            {
                _inrushExtraPrimaryA = 0;
                _prevPrimaryVoltagePu = vPuNow;
            }

            double ratedSecondaryCurrent = RatedLineCurrent(
                _config.RatedPowerKva, _config.SecondaryNominalLineVoltageV);

            double netVoltageFactor = 1.0;
            if (applyReactiveVoltageShift)
            {
                double zPu = _config.ImpedancePercent / 100.0;
                double reactiveShiftPu = GridFeedbackConventions.CalculatePccReactiveVoltageShiftPu(
                    totalReactivePowerKvar,
                    _config.RatedPowerKva,
                    zPu,
                    _config.ReactiveVoltageInfluenceCoefficient);
                netVoltageFactor = 1 + reactiveShiftPu;
            }

            double secondaryVoltage = DivideByTurns(primaryVoltage, turnsRatio) * netVoltageFactor;
            _currentState.SecondaryVoltage = double.IsFinite(secondaryVoltage) ? secondaryVoltage : 0;

            double ratedPrimaryCurrent = RatedLineCurrent(
                _config.RatedPowerKva, _config.PrimaryNominalLineVoltageV);
            double vRatio = double.IsFinite(_config.PrimaryNominalLineVoltageV)
                && _config.PrimaryNominalLineVoltageV > 1e-9
                && double.IsFinite(primaryVoltage)
                ? primaryVoltage / _config.PrimaryNominalLineVoltageV
                : 0.0;
            double noLoadPrimaryA = (_config.NoLoadCurrentPercent / 100.0) * ratedPrimaryCurrent * Math.Abs(vRatio);
            double inrushPrimaryA = _config.MagnetizingInrushEnabled ? _inrushExtraPrimaryA : 0.0;
            _currentState.MagnetizingInrushCurrentPrimary = inrushPrimaryA;

            double noLoadSecondaryA = noLoadPrimaryA * turnsRatio;
            double inrushSecondaryA = inrushPrimaryA * turnsRatio;
            double magnetizingSecondaryA = noLoadSecondaryA + inrushSecondaryA;
            _currentState.MagnetizingNoLoadCurrentSecondary = noLoadSecondaryA;
            _currentState.MagnetizingInrushCurrentSecondary = inrushSecondaryA;
            _currentState.MagnetizingCurrentSecondary = magnetizingSecondaryA;

            double pfAbs = Math.Clamp(Math.Abs(powerFactor), 0.0, 1.0);
            double sinPhi = Math.Sqrt(Math.Max(0.0, 1.0 - pfAbs * pfAbs));
            double signQ = totalReactivePowerKvar >= 0 ? 1.0 : -1.0;

            double i2Mag = Math.Abs(loadSecondaryCurrent);
            double i2W = i2Mag * pfAbs;
            double i2Q = i2Mag * sinPhi * signQ;
            double i2QTotal = i2Q + magnetizingSecondaryA;

            _currentState.SecondaryCurrent = double.IsFinite(loadSecondaryCurrent) ? loadSecondaryCurrent : 0;
            double loadRatio = ratedSecondaryCurrent > 0 && double.IsFinite(loadSecondaryCurrent)
                ? Math.Abs(loadSecondaryCurrent) / ratedSecondaryCurrent
                : 0;
            _currentState.LoadRatio = double.IsFinite(loadRatio) ? loadRatio : 0;

            double i1W = DivideByTurns(i2W, turnsRatio);
            double i1Q = DivideByTurns(i2QTotal, turnsRatio);
            double primaryCurrent = Math.Sqrt(i1W * i1W + i1Q * i1Q);
            _currentState.PrimaryCurrent = double.IsFinite(primaryCurrent) ? primaryCurrent : 0;
            if (loadSecondaryCurrent < 0 && Math.Abs(i2W) >= 1e-6)
                _currentState.PrimaryCurrent = -_currentState.PrimaryCurrent;
            else if (loadSecondaryCurrent < 0 && i2QTotal < 0)
                _currentState.PrimaryCurrent = -Math.Abs(_currentState.PrimaryCurrent);

            var loss = TransformerLoss.Compute(
                _config.NoLoadLossKw * 1000.0,
                _config.LoadLossKw * 1000.0,
                primaryVoltage,
                _config.PrimaryNominalLineVoltageV,
                _currentState.LoadRatio);
            _currentState.IronLoss = loss.IronLossW;
            _currentState.CopperLoss = loss.CopperLossW;
            _currentState.TotalLoss = loss.TotalLossW;

            double outputPower = Math.Sqrt(3.0) * _currentState.SecondaryVoltage * loadSecondaryCurrent * powerFactor;
            double pAbs = Math.Abs(outputPower);
            double efficiency = (pAbs + _currentState.TotalLoss) > 0
                ? pAbs / (pAbs + _currentState.TotalLoss)
                : 0;
            _currentState.Efficiency = double.IsFinite(efficiency) ? efficiency : 0;
            _currentState.Power = double.IsFinite(outputPower) ? outputPower : 0;
        }

        private static double RatedLineCurrent(double ratedKva, double nominalVoltageV)
        {
            if (!double.IsFinite(ratedKva) || !double.IsFinite(nominalVoltageV) || nominalVoltageV <= 1e-9)
                return 0;
            return ratedKva * 1000.0 / (nominalVoltageV * Math.Sqrt(3));
        }

        private static double DivideByTurns(double value, double turnsRatio)
        {
            if (!double.IsFinite(value) || !double.IsFinite(turnsRatio) || Math.Abs(turnsRatio) <= 1e-9)
                return 0;
            return value / turnsRatio;
        }

        /// <summary>
        /// 并网且电网可用时，励磁/铁损无功由无穷大电网在 PCC 侧吸收，端口只传递穿越功率（secIn）；
        /// 离网或励磁涌流期间仍输出完整励磁分量。
        /// </summary>
        private void PublishPorts(double frequencyHz, AcInternalQuantities secIn, DeviceStepContext context)
        {
            bool gridSlackAbsorbsMagnetizing = context.MainBreakerClosed
                && context.UtilityGridAvailable
                && !IsMagnetizingInrushActive();

            double magQ = gridSlackAbsorbsMagnetizing ? 0 : GetSecondaryMagnetizingReactiveKvar();
            double ironKw = gridSlackAbsorbsMagnetizing ? 0 : GetSecondaryNoLoadActivePowerKw();

            AcPortHelper.WriteAcOutput(Primary, AcQuantityConverter.FromLineVoltageAndPower(
                _currentState.PrimaryVoltage,
                secIn.ActivePowerKw + ironKw,
                secIn.ReactivePowerKvar + magQ,
                _config.PrimaryConnection,
                frequencyHz));

            AcPortHelper.WriteAcOutput(Secondary, AcQuantityConverter.FromLineVoltageAndPower(
                _currentState.SecondaryVoltage,
                secIn.ActivePowerKw,
                secIn.ReactivePowerKvar,
                _config.SecondaryConnection,
                frequencyHz));
        }

        private bool IsMagnetizingInrushActive() =>
            _config.MagnetizingInrushEnabled
            && (_inrushExtraPrimaryA > 1e-3
                || _currentState.MagnetizingInrushCurrentSecondary > 1e-3);

        private static ElectricalPort CreatePort(string portId, ThreePhaseConnection connection, PortKind kind)
        {
            var empty = new AcInternalQuantities { Connection = connection };
            return new ElectricalPort
            {
                PortId = portId,
                Kind = kind,
                Input = ElectricalPortSnapshot.FromAc(empty),
                Output = ElectricalPortSnapshot.FromAc(empty)
            };
        }
    }
}
