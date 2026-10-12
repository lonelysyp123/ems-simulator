namespace EssSimulator.Display
{
    /// <summary>主接线视图用的 AC 相量快照（V/I/φ 为主，P/Q 按需）。</summary>
    public readonly record struct AcPhasorSnapshot(
        double LineVoltageV,
        double LineCurrentA,
        double PhaseAngleDeg,
        double FrequencyHz)
    {
        public double ActivePowerKw =>
            EssSimulator.EssDeviceSimModel.Model.AcQuantityConverter.ComputeActivePowerKw(
                LineVoltageV, LineCurrentA, PhaseAngleDeg);

        public double ReactivePowerKvar =>
            EssSimulator.EssDeviceSimModel.Model.AcQuantityConverter.ComputeReactivePowerKvar(
                LineVoltageV, LineCurrentA, PhaseAngleDeg);

        public double PowerFactor =>
            EssSimulator.EssDeviceSimModel.Model.AcQuantityConverter.ComputePowerFactor(
                LineVoltageV, LineCurrentA, PhaseAngleDeg);
    }

    public readonly record struct BusNodeSnapshot(
        string BusId,
        double LineVoltageV,
        double LineCurrentA,
        double PhaseAngleDeg,
        double FrequencyHz);

    public readonly record struct PcsChannelSnapshot(
        int Index,
        int UnitIndex,
        int SlotInUnit,
        AcPhasorSnapshot AcOutput,
        double ActivePowerKw,
        double ReactivePowerKw,
        double AcVoltageState,
        double AcCurrentState,
        double FrequencyHz,
        string GridMode,
        string BlackStartPhase,
        bool BlackStartEnabled);

    public readonly record struct UnitBranchSnapshot(
        int UnitIndex,
        bool UnitBreakerClosed,
        bool UnitBreakerTripped,
        AcPhasorSnapshot UnitTransformerPrimary,
        AcPhasorSnapshot UnitTransformerSecondary,
        BusNodeSnapshot? Bus690,
        IReadOnlyList<PcsChannelSnapshot?> PcsChannels);

    public sealed class MainLineSnapshot
    {
        public bool PropagationEnabled { get; init; }
        public bool MainBreakerClosed { get; init; }
        public bool MainBreakerTripped { get; init; }
        public double PccLineVoltageV { get; init; }
        public double StationBus35LineVoltageV { get; init; }
        public BusNodeSnapshot? BusGrid { get; init; }
        public BusNodeSnapshot? Bus35Propagation { get; init; }
        public AcPhasorSnapshot MeterPrimary { get; init; }
        public AcPhasorSnapshot MainTransformerPrimary { get; init; }
        public AcPhasorSnapshot MainTransformerSecondary { get; init; }
        public double LoadActivePowerKw { get; init; }
        public double LoadReactivePowerKvar { get; init; }
        public double LoadActivePowerSetKw { get; init; }
        public double LoadReactivePowerSetKvar { get; init; }
        public bool SvgPresent { get; init; }
        public int SvgRunState { get; init; }
        public int SvgRunCommand { get; init; }
        public double SvgReactivePowerKvar { get; init; }
        public double SvgReactiveSetpointKvar { get; init; }
        public double SvgAvailableReactiveUpperKvar { get; init; }
        public double SvgRatedCapacityKvar { get; init; }
        /// <summary>仿真电网额定线电压设定（V）。</summary>
        public double GridNominalLineVoltageV { get; init; }
        /// <summary>仿真电网额定频率设定（Hz）。</summary>
        public double GridNominalFrequencyHz { get; init; }
        /// <summary>当前系统频率（Hz，主断分闸时可为 0）。</summary>
        public double SystemFrequencyHz { get; init; }
        /// <summary>PCC 电表三相线电压/相电流（与 em DTO / 点表一致）。</summary>
        public MeterThreePhaseSnapshot MeterThreePhase { get; init; }
        public IReadOnlyList<UnitBranchSnapshot> Units { get; init; } = Array.Empty<UnitBranchSnapshot>();
    }

    /// <summary>电表三相量（平衡系统下三相等值，仍分字段上报便于界面与点表对齐）。</summary>
    public readonly record struct MeterThreePhaseSnapshot(
        double LineVoltageAB,
        double LineVoltageBC,
        double LineVoltageCA,
        double PhaseACurrent,
        double PhaseBCurrent,
        double PhaseCCurrent,
        double PhaseAVoltage,
        double PhaseBVoltage,
        double PhaseCVoltage);

    /// <summary>从 ess/em 路径读取主接线电气量（对齐传播求解 V-I-φ 架构）。</summary>
    public static class GuiElectricalReader
    {
        public static MainLineSnapshot ReadMainLine(int unitStart, int unitEndExclusive)
        {
            bool propagation = GuiSimDataAccess.TryGetObject("ess.RadialGraph") != null;

            bool mainClosed = GuiSimDataAccess.SafeGetBool("ess._breaker.IsClosed");
            bool mainTripped = GuiSimDataAccess.SafeGetBool(
                "ess.ElectricalNetwork.MainBreaker.SwitchState.IsTripped");

            var meter = ReadAcPhasor("ess.ElectricalNetwork.PccMeter.Telemetry.Primary");
            double systemF = GuiSimDataAccess.SafeGetDouble("ess.ElectricalNetwork.SystemFrequencyHz");
            if (systemF > 0.05)
                meter = meter with { FrequencyHz = systemF };
            else if (meter.FrequencyHz <= 0.05)
                meter = meter with { FrequencyHz = GuiSimDataAccess.SafeGetDouble("em.Frequency") };

            if (meter.LineVoltageV <= 0 && meter.LineCurrentA <= 0)
            {
                meter = new AcPhasorSnapshot(
                    GuiSimDataAccess.SafeGetDouble("em.LineVoltageAB"),
                    Math.Max(
                        GuiSimDataAccess.SafeGetDouble("em.PhaseACurrent"),
                        Math.Max(
                            GuiSimDataAccess.SafeGetDouble("em.PhaseBCurrent"),
                            GuiSimDataAccess.SafeGetDouble("em.PhaseCCurrent"))),
                    0,
                    GuiSimDataAccess.SafeGetDouble("em.Frequency"));
                if (meter.LineCurrentA > 0)
                {
                    double p = GuiSimDataAccess.SafeGetDouble("em.TotalActivePower");
                    double q = GuiSimDataAccess.SafeGetDouble("em.TotalReactivePower");
                    var ph = EssSimulator.EssDeviceSimModel.Model.AcQuantityConverter.FromPowerToPhasor(
                        meter.LineVoltageV, p, q);
                    meter = meter with { PhaseAngleDeg = ph.PhaseAngleDeg, LineCurrentA = ph.LineCurrentA };
                }
            }

            var mainPri = ReadTransformerSide(
                "ess._mainTransformer._currentState.PrimaryVoltage",
                "ess._mainTransformer._currentState.PrimaryCurrent",
                "ess._mainTransformer.Primary.Output.Ac.Internal");
            var mainSec = ReadTransformerSide(
                "ess._mainTransformer._currentState.SecondaryVoltage",
                "ess._mainTransformer._currentState.SecondaryCurrent",
                "ess._mainTransformer.Secondary.Output.Ac.Internal");

            var layout = GuiSimDataAccess.GetPcsPerUnit();
            var units = new List<UnitBranchSnapshot>();
            for (int u = unitStart; u < unitEndExclusive; u++)
            {
                // 单元内 PCS 台数由布局决定，逐槽位读取，不再限定 A/B 双通道
                int baseIdx = EssDeviceSimModel.PcsUnitLayout.BaseIndexOfUnit(layout, u);
                int slotCount = Math.Max(1, EssDeviceSimModel.PcsUnitLayout.CountOfUnit(layout, u));
                var channels = new List<PcsChannelSnapshot?>(slotCount);
                for (int s = 0; s < slotCount; s++)
                    channels.Add(ReadPcsChannel(baseIdx + s, u, s));

                units.Add(new UnitBranchSnapshot(
                    u,
                    GuiSimDataAccess.SafeGetBool($"ess.ElectricalNetwork.UnitBreakers[{u}].SwitchState.IsClosed",
                        GuiSimDataAccess.SafeGetBool($"ess._unitBreakers[{u}].IsClosed")),
                    GuiSimDataAccess.SafeGetBool($"ess.ElectricalNetwork.UnitBreakers[{u}].SwitchState.IsTripped"),
                    ReadTransformerSide(
                        $"ess._unitTransformers[{u}]._currentState.PrimaryVoltage",
                        $"ess._unitTransformers[{u}]._currentState.PrimaryCurrent",
                        $"ess._unitTransformers[{u}].Primary.Output.Ac.Internal"),
                    ReadTransformerSide(
                        $"ess._unitTransformers[{u}]._currentState.SecondaryVoltage",
                        $"ess._unitTransformers[{u}]._currentState.SecondaryCurrent",
                        $"ess._unitTransformers[{u}].Secondary.Output.Ac.Internal"),
                    ReadBusNode($"ess.RadialGraph.UnitBuses690[{u}]"),
                    channels));
            }

            return new MainLineSnapshot
            {
                PropagationEnabled = propagation,
                MainBreakerClosed = mainClosed,
                MainBreakerTripped = mainTripped,
                PccLineVoltageV = GuiSimDataAccess.SafeGetDouble("ess.PccLineVoltageV"),
                StationBus35LineVoltageV = GuiSimDataAccess.SafeGetDouble("ess.StationBus35LineVoltageV"),
                BusGrid = ReadBusNode("ess.RadialGraph.BusGrid"),
                Bus35Propagation = ReadBusNode("ess.RadialGraph.Bus35"),
                MeterPrimary = meter,
                MainTransformerPrimary = mainPri,
                MainTransformerSecondary = mainSec,
                LoadActivePowerKw = GuiSimDataAccess.SafeGetDouble("ess._loadSimulator.ActivePower"),
                LoadReactivePowerKvar = GuiSimDataAccess.SafeGetDouble("ess._loadSimulator.ReactivePower"),
                LoadActivePowerSetKw = GuiSimDataAccess.SafeGetDouble("ess._loadSimulator.ActivePowerSetpointKw"),
                LoadReactivePowerSetKvar = GuiSimDataAccess.SafeGetDouble("ess._loadSimulator.ReactivePowerSetpointKvar"),
                SvgPresent = GuiSimDataAccess.TryGetObject("ess.Svg") != null,
                SvgRunState = (int)GuiSimDataAccess.SafeGetDouble("ess.Svg.RunState"),
                SvgRunCommand = (int)GuiSimDataAccess.SafeGetDouble("ess.Svg.RunCommand"),
                SvgReactivePowerKvar = GuiSimDataAccess.SafeGetDouble("ess.Svg.ReactivePowerKvar"),
                SvgReactiveSetpointKvar = GuiSimDataAccess.SafeGetDouble("ess.Svg.ReactiveSetpointKvar"),
                SvgAvailableReactiveUpperKvar = GuiSimDataAccess.SafeGetDouble("ess.Svg.AvailableReactiveUpperKvar"),
                SvgRatedCapacityKvar = GuiSimDataAccess.SafeGetDouble("ess.Svg.RatedCapacityKvar"),
                GridNominalLineVoltageV = GuiSimDataAccess.SafeGetDouble(
                    "ess.ElectricalNetwork.Grid.NominalLineVoltageV", 220000),
                GridNominalFrequencyHz = GuiSimDataAccess.SafeGetDouble(
                    "ess.ElectricalNetwork.Grid.NominalFrequencyHz", 50),
                SystemFrequencyHz = systemF > 0.05
                    ? systemF
                    : GuiSimDataAccess.SafeGetDouble("ess.ElectricalNetwork.Grid.NominalFrequencyHz", 50),
                MeterThreePhase = ReadMeterThreePhase(meter),
                Units = units
            };
        }

        private static MeterThreePhaseSnapshot ReadMeterThreePhase(AcPhasorSnapshot meterFallback)
        {
            double uab = GuiSimDataAccess.SafeGetDouble("em.LineVoltageAB");
            double ubc = GuiSimDataAccess.SafeGetDouble("em.LineVoltageBC");
            double uca = GuiSimDataAccess.SafeGetDouble("em.LineVoltageCA");
            double ia = GuiSimDataAccess.SafeGetDouble("em.PhaseACurrent");
            double ib = GuiSimDataAccess.SafeGetDouble("em.PhaseBCurrent");
            double ic = GuiSimDataAccess.SafeGetDouble("em.PhaseCCurrent");
            double va = GuiSimDataAccess.SafeGetDouble("em.PhaseAVoltage");
            double vb = GuiSimDataAccess.SafeGetDouble("em.PhaseBVoltage");
            double vc = GuiSimDataAccess.SafeGetDouble("em.PhaseCVoltage");

            // em 尚未写入时，用平衡相量回填，保证界面始终有线电压/相电流
            if (uab <= 1 && ubc <= 1 && uca <= 1 && meterFallback.LineVoltageV > 1)
                uab = ubc = uca = meterFallback.LineVoltageV;
            if (Math.Abs(ia) < 1e-9 && Math.Abs(ib) < 1e-9 && Math.Abs(ic) < 1e-9
                && Math.Abs(meterFallback.LineCurrentA) > 1e-9)
                ia = ib = ic = meterFallback.LineCurrentA;
            if (va <= 1 && vb <= 1 && vc <= 1 && meterFallback.LineVoltageV > 1)
                va = vb = vc = meterFallback.LineVoltageV / Math.Sqrt(3.0);

            return new MeterThreePhaseSnapshot(uab, ubc, uca, ia, ib, ic, va, vb, vc);
        }

        private static PcsChannelSnapshot? ReadPcsChannel(int pcsIndex, int unitIndex, int slot)
        {
            int channelCount = GuiSimDataAccess.GetEssUnitCount();
            if (pcsIndex < 0 || pcsIndex >= channelCount)
                return null;

            var acOut = ReadAcPhasor($"ess._pcsList[{pcsIndex}].Ac.Output.Ac.Internal");
            return new PcsChannelSnapshot(
                pcsIndex,
                unitIndex,
                slot,
                acOut,
                GuiSimDataAccess.SafeGetDouble($"ess._pcsList[{pcsIndex}]._currentState.ActivePower"),
                GuiSimDataAccess.SafeGetDouble($"ess._pcsList[{pcsIndex}]._currentState.ReactivePower"),
                GuiSimDataAccess.SafeGetDouble($"ess._pcsList[{pcsIndex}]._currentState.AcVoltage"),
                GuiSimDataAccess.SafeGetDouble($"ess._pcsList[{pcsIndex}]._currentState.AcCurrent"),
                GuiSimDataAccess.SafeGetDouble($"ess._pcsList[{pcsIndex}]._currentState.Frequency", 50),
                GuiSimDataAccess.SafeGetString($"ess._pcsList[{pcsIndex}]._currentState.GMode", "?"),
                GuiSimDataAccess.SafeGetString($"ess._pcsList[{pcsIndex}]._currentState.BlackStartPhase", "Inactive"),
                GuiSimDataAccess.SafeGetBool($"ess._pcsList[{pcsIndex}]._currentState.BlackStartEnabled"));
        }

        private static AcPhasorSnapshot ReadTransformerSide(
            string stateVoltagePath,
            string stateCurrentPath,
            string portInternalPath)
        {
            var port = ReadAcPhasor(portInternalPath);
            double v = GuiSimDataAccess.SafeGetDouble(stateVoltagePath);
            double i = Math.Abs(GuiSimDataAccess.SafeGetDouble(stateCurrentPath));
            if (port.LineVoltageV > 1)
                return port with { LineVoltageV = v > 1 ? v : port.LineVoltageV };

            double phi = port.PhaseAngleDeg;
            if (i > 0 && phi == 0 && port.LineCurrentA <= 0)
                return new AcPhasorSnapshot(v, i, phi, 0);
            return new AcPhasorSnapshot(v, port.LineCurrentA > 0 ? port.LineCurrentA : i, phi, port.FrequencyHz);
        }

        private static AcPhasorSnapshot ReadAcPhasor(string internalPathPrefix)
        {
            double v = GuiSimDataAccess.SafeGetDouble($"{internalPathPrefix}.LineVoltageV");
            double i = GuiSimDataAccess.SafeGetDouble($"{internalPathPrefix}.LineCurrentA");
            double phi = GuiSimDataAccess.SafeGetDouble($"{internalPathPrefix}.PhaseAngleDeg");
            double f = GuiSimDataAccess.SafeGetDouble($"{internalPathPrefix}.FrequencyHz", 50);
            return new AcPhasorSnapshot(v, i, phi, f);
        }

        private static BusNodeSnapshot? ReadBusNode(string objectPath)
        {
            if (GuiSimDataAccess.TryGetObject(objectPath) == null)
                return null;

            string id = GuiSimDataAccess.SafeGetString($"{objectPath}.BusId", objectPath);
            return new BusNodeSnapshot(
                id,
                GuiSimDataAccess.SafeGetDouble($"{objectPath}.LineVoltageV"),
                GuiSimDataAccess.SafeGetDouble($"{objectPath}.TotalLineCurrentA"),
                GuiSimDataAccess.SafeGetDouble($"{objectPath}.TotalPhaseAngleDeg"),
                GuiSimDataAccess.SafeGetDouble($"{objectPath}.FrequencyHz", 50));
        }
    }
}
