using EssSimulator.Display;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.Web.Topology;

namespace EssSimulator.Web
{
    /// <summary>为主接线 Web 视图补充 PCS/BMS 展示字段（对齐原 TUI 主接线图信息量）。</summary>
    public static class MainLineEnricher
    {
        public static MainLineViewModel Build(TopologyStore? topologyStore = null)
        {
            int channelCount = ResolveEssChannelCount(
                GuiSimDataAccess.GetEssUnitCount(),
                GuiSimDataAccess.GetPvUnitCount());
            int unitCount = channelCount <= 0 ? 0 : GuiSimDataAccess.GetPhysicalUnitCount();
            var snap = GuiElectricalReader.ReadMainLine(0, unitCount);
            return Build(snap, channelCount, topologyStore);
        }

        /// <summary>
        /// 主接线储能支路数：纯光伏为 0（不伪造 PCS/BMS）；
        /// 全空配置回退 1，兼容旧 appsettings。
        /// </summary>
        public static int ResolveEssChannelCount(int pcsCount, int pvCount)
        {
            if (pcsCount > 0) return pcsCount;
            if (pvCount > 0) return 0;
            return 1;
        }

        public static int ResolveEssUnitCount(int channelCount) =>
            channelCount <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(channelCount / 2.0));

        public static MainLineViewModel Build(
            MainLineSnapshot snap,
            int? channelCountOverride = null,
            TopologyStore? topologyStore = null)
        {
            int channelCount = channelCountOverride ?? ResolveEssChannelCount(
                GuiSimDataAccess.GetEssUnitCount(),
                GuiSimDataAccess.GetPvUnitCount());
            var vm = new MainLineViewModel
            {
                PropagationEnabled = snap.PropagationEnabled,
                MainBreakerClosed = snap.MainBreakerClosed,
                MainBreakerTripped = snap.MainBreakerTripped,
                PccLineVoltageV = snap.PccLineVoltageV,
                StationBus35LineVoltageV = snap.StationBus35LineVoltageV,
                BusGrid = snap.BusGrid,
                Bus35Propagation = snap.Bus35Propagation,
                MeterPrimary = snap.MeterPrimary,
                MainTransformerPrimary = snap.MainTransformerPrimary,
                MainTransformerSecondary = snap.MainTransformerSecondary,
                LoadActivePowerKw = snap.LoadActivePowerKw,
                LoadReactivePowerKvar = snap.LoadReactivePowerKvar,
                LoadActivePowerSetKw = snap.LoadActivePowerSetKw,
                LoadReactivePowerSetKvar = snap.LoadReactivePowerSetKvar,
                GridNominalLineVoltageV = snap.GridNominalLineVoltageV,
                GridNominalFrequencyHz = snap.GridNominalFrequencyHz,
                SystemFrequencyHz = snap.SystemFrequencyHz,
                MeterThreePhase = snap.MeterThreePhase,
                BlackStartSummary = GuiStatusFormatters.BuildBlackStartSwitchSummary(0, channelCount),
                MainBreakerLabel = FormatBreaker(snap.MainBreakerClosed, snap.MainBreakerTripped),
                Units = snap.Units.Select(u => EnrichUnit(u, channelCount)).ToList(),
                PvUnits = ReadPvUnits()
            };

            AttachTopology(vm, topologyStore);
            return vm;
        }

        private static void AttachTopology(MainLineViewModel vm, TopologyStore? store)
        {
            if (store == null) return;
            try
            {
                var mode = store.LoadRuntimeMode();
                if (!mode.EngineeringMode) return;

                TopologyProject? project = null;
                if (!string.IsNullOrWhiteSpace(mode.ActiveProjectId))
                    project = store.LoadNamedProject(mode.ActiveProjectId!);
                project ??= store.LoadProject();
                if (project == null || project.Nodes.Count == 0) return;

                vm.EngineeringMode = true;
                vm.Topology = project;
                vm.TopologyTemplates = TopologyTemplates.All.ToList();
                vm.ActiveProjectName = mode.ActiveProjectName ?? project.Name;
                vm.HasTopologyLoad = project.Nodes.Any(n => n.TemplateId == "load");

                // 工程模式且无负载节点：概览负载冻结为 0，前端置灰不可改
                if (!vm.HasTopologyLoad)
                {
                    vm.LoadActivePowerKw = 0;
                    vm.LoadReactivePowerKvar = 0;
                    vm.LoadActivePowerSetKw = 0;
                    vm.LoadReactivePowerSetKvar = 0;
                }
            }
            catch
            {
                /* 组态附加失败不影响主接线遥测 */
            }
        }

        private static MainLineUnitViewModel EnrichUnit(UnitBranchSnapshot u, int channelCount)
        {
            var layout = GuiSimDataAccess.GetPcsPerUnit();
            int baseIdx = EssDeviceSimModel.PcsUnitLayout.BaseIndexOfUnit(layout, u.UnitIndex);
            // 逐槽位构造运行时通道；越界全局通道号截断，避免实时数据 / 控制命令错位
            var channels = new List<MainLineChannelViewModel>();
            for (int s = 0; s < u.PcsChannels.Count; s++)
            {
                int idx = baseIdx + s;
                if (idx >= channelCount) break;
                channels.Add(BuildChannel(idx, u.UnitIndex, s, u.PcsChannels[s]));
            }
            var vm = new MainLineUnitViewModel
            {
                UnitIndex = u.UnitIndex,
                UnitNumber = u.UnitIndex + 1,
                UnitBreakerClosed = u.UnitBreakerClosed,
                UnitBreakerTripped = u.UnitBreakerTripped,
                UnitBreakerLabel = FormatBreaker(u.UnitBreakerClosed, u.UnitBreakerTripped),
                UnitTransformerPrimary = u.UnitTransformerPrimary,
                UnitTransformerSecondary = u.UnitTransformerSecondary,
                UnitTransformerLine = GuiStatusFormatters.FormatAcPhasorViPhi(u.UnitTransformerSecondary),
                Bus690 = u.Bus690,
                PcsChannels = u.PcsChannels,
                Channels = channels
            };
            FillUnitAuxiliaries(vm, u.UnitIndex);
            return vm;
        }

        private static void FillUnitAuxiliaries(MainLineUnitViewModel vm, int unitIndex0)
        {
            string emu = $"emu{unitIndex0 + 1}";
            vm.UnitMeterThreePhase = new MeterThreePhaseSnapshot(
                GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.LineVoltageAB"),
                GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.LineVoltageBC"),
                GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.LineVoltageCA"),
                GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.PhaseACurrent"),
                GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.PhaseBCurrent"),
                GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.PhaseCCurrent"),
                GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.PhaseAVoltage"),
                GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.PhaseBVoltage"),
                GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.PhaseCVoltage"));
            vm.UnitMeterActivePowerKw = GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.TotalActivePower");
            vm.UnitMeterReactivePowerKvar = GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.TotalReactivePower");
            vm.UnitMeterPowerFactor = GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.PowerFactor");
            vm.UnitMeterFrequencyHz = GuiSimDataAccess.SafeGetDouble($"{emu}.ElectricityMeter.Frequency");
            vm.UnitTransformerLoadFraction = GuiSimDataAccess.SafeGetDouble($"{emu}.Transformers[0].LoadFraction");
            vm.UnitTransformerOilTemperatureC = GuiSimDataAccess.SafeGetDouble($"{emu}.Transformers[0].OilTemperatureC");
            vm.UnitTransformerActivePowerKw = GuiSimDataAccess.SafeGetDouble($"{emu}.Transformers[0].ActivePowerKw");
            vm.UnitTransformerReactivePowerKvar = GuiSimDataAccess.SafeGetDouble($"{emu}.Transformers[0].ReactivePowerKvar");
        }

        private static MainLineChannelViewModel BuildChannel(
            int channelIndex0, int unitIndex0, int slotInUnit0, PcsChannelSnapshot? pcs)
        {
            bool linked = IsBmsPcsLinked(channelIndex0);
            double packVoltage = GuiSimDataAccess.SafeGetDouble(
                $"ess._batteryRacks[{channelIndex0}]._currentState.TotalVoltage");
            double packCurrent = GuiSimDataAccess.SafeGetDouble(
                $"ess._batteryRacks[{channelIndex0}]._currentState.TotalCurrent");
            // 与电气端口一致：下电/断链后 DC 侧对外电压、电流为 0
            double dcVoltage = linked ? packVoltage : 0;
            double dcCurrent = linked ? packCurrent : 0;

            return new MainLineChannelViewModel
            {
                ChannelIndex = channelIndex0,
                ChannelNumber = channelIndex0 + 1,
                CompartmentNumber = channelIndex0 + 1,
                PcsNumber = channelIndex0 + 1,
                UnitIndex = unitIndex0,
                SlotInUnit = slotInUnit0,
                SocPercent = 100 * GuiSimDataAccess.SafeGetDouble(
                    $"ess._batteryRacks[{channelIndex0}]._currentState.MinClusterSOC"),
                DcVoltage = dcVoltage,
                DcCurrent = dcCurrent,
                GridConnect = GuiStatusFormatters.FormatGridConnectStatus(channelIndex0),
                BmsBlackStart = GuiStatusFormatters.FormatBmsMainLineBlackStart(channelIndex0),
                BmsCompact = BuildBmsCompact(channelIndex0, dcVoltage, dcCurrent),
                CumulativeChargeEnergyKwh = GuiSimDataAccess.SafeGetDouble(
                    $"ess._batteryRacks[{channelIndex0}]._currentState.TotalChargeEnergy"),
                CumulativeDischargeEnergyKwh = GuiSimDataAccess.SafeGetDouble(
                    $"ess._batteryRacks[{channelIndex0}]._currentState.TotalDischargeEnergy"),
                BmsEnergy = BuildBmsEnergy(channelIndex0),
                BmsRunStatus = BuildBmsRunStatus(channelIndex0),
                BmsAirConditioner = GuiStatusFormatters.FormatBmsMainLineAirConditioner(channelIndex0),
                PcsDeviceState = GuiStatusFormatters.FormatPcsMainLineDeviceState(unitIndex0, slotInUnit0, channelIndex0),
                PcsStartStop = GuiStatusFormatters.FormatPcsMainLineStartStop(unitIndex0, slotInUnit0),
                PcsTargetP = GuiStatusFormatters.FormatPcsMainLineTargetPower(unitIndex0, slotInUnit0),
                PcsActualP = GuiStatusFormatters.FormatPcsMainLineActualPower(
                    channelIndex0, pcs?.ActivePowerKw ?? 0),
                PcsTargetQ = GuiStatusFormatters.FormatPcsMainLineTargetReactive(unitIndex0, slotInUnit0),
                PcsActualQ = GuiStatusFormatters.FormatPcsMainLineActualReactive(
                    channelIndex0, pcs?.ReactivePowerKw ?? 0),
                PcsBlackStart = GuiStatusFormatters.FormatPcsMainLineBlackStart(unitIndex0, slotInUnit0, channelIndex0),
                PcsAcLine = pcs != null ? GuiStatusFormatters.FormatPcsAcLine(pcs.Value) : "—",
                PcsGridMode = pcs != null ? GuiStatusFormatters.FormatGridModeLabel(pcs.Value.GridMode) : "—",
                TargetActivePowerKw = GuiSimDataAccess.SafeGetDouble(
                    $"emu{unitIndex0 + 1}.PcsList[{slotInUnit0}].PCSActivePowerSetting"),
                TargetReactivePowerKvar = GuiSimDataAccess.SafeGetDouble(
                    $"emu{unitIndex0 + 1}.PcsList[{slotInUnit0}].PCSReactivePowerSetting"),
                ActualActivePowerKw = pcs?.ActivePowerKw
                    ?? GuiSimDataAccess.SafeGetDouble(
                        $"ess._pcsList[{channelIndex0}]._currentState.ActivePower"),
                ActualReactivePowerKvar = pcs?.ReactivePowerKw
                    ?? GuiSimDataAccess.SafeGetDouble(
                        $"ess._pcsList[{channelIndex0}]._currentState.ReactivePower"),
                EmuUnitNumber = channelIndex0 + 1,
                ActivePowerYtPoint = "yt0",
                ReactivePowerYtPoint = "yt1"
            };
        }

        private static List<MainLinePvUnitViewModel> ReadPvUnits()
        {
            int n = GuiSimDataAccess.GetPvUnitCount();
            var list = new List<MainLinePvUnitViewModel>(n);
            for (int i = 0; i < n; i++)
            {
                string id = $"pv{i + 1}";
                double onOff = GuiSimDataAccess.SafeGetDouble($"{id}.Logger.SubarrayOnOff");
                double runState = GuiSimDataAccess.SafeGetDouble($"{id}.Logger.RunState");
                list.Add(new MainLinePvUnitViewModel
                {
                    PvIndex = i,
                    PvNumber = i + 1,
                    DeviceId = id,
                    ActivePowerKw = GuiSimDataAccess.SafeGetDouble($"{id}.ActivePowerKw"),
                    ReactivePowerKvar = GuiSimDataAccess.SafeGetDouble($"{id}.ReactivePowerKvar"),
                    TargetActivePowerKw = GuiSimDataAccess.SafeGetDouble($"{id}.Logger.SubarrayActivePowerKw"),
                    TargetReactivePowerKvar = GuiSimDataAccess.SafeGetDouble($"{id}.Logger.SubarrayReactivePowerKvar"),
                    OnOff = onOff,
                    Running = onOff > 0.5 && runState > 0.5,
                    GridConnectedDeviceCount = (int)GuiSimDataAccess.SafeGetDouble($"{id}.Logger.GridConnectedDeviceCount"),
                    MaximumDischargePowerKw = GuiSimDataAccess.SafeGetDouble($"{id}.MaximumDischargePowerKw"),
                    ArrayA = ReadPvArray(id, "A"),
                    ArrayB = ReadPvArray(id, "B")
                });
            }
            return list;
        }

        private static MainLinePvArrayViewModel ReadPvArray(string deviceId, string side)
        {
            string prefix = $"{deviceId}.Array{side}";
            return new MainLinePvArrayViewModel
            {
                Side = side,
                AmbientTemperatureC = GuiSimDataAccess.SafeGetDouble($"{prefix}.AmbientTemperatureC"),
                IncidenceAngleDeg = GuiSimDataAccess.SafeGetDouble($"{prefix}.IncidenceAngleDeg"),
                PlaneOfArrayWm2 = GuiSimDataAccess.SafeGetDouble($"{prefix}.PlaneOfArrayWm2"),
                RearWm2 = GuiSimDataAccess.SafeGetDouble($"{prefix}.RearWm2"),
                Albedo = GuiSimDataAccess.SafeGetDouble($"{prefix}.Albedo"),
                CellTemperatureC = GuiSimDataAccess.SafeGetDouble($"{prefix}.CellTemperatureC"),
                AvailableAcPowerKw = GuiSimDataAccess.SafeGetDouble($"{prefix}.AvailableAcPowerKw"),
                ActivePowerKw = GuiSimDataAccess.SafeGetDouble($"{prefix}.ActivePowerKw"),
                DcVoltageV = GuiSimDataAccess.SafeGetDouble($"{prefix}.DcVoltageV"),
                DcCurrentA = GuiSimDataAccess.SafeGetDouble($"{prefix}.DcCurrentA"),
                LimitReason = GuiSimDataAccess.SafeGetString($"{prefix}.LimitReason")
            };
        }

        private static bool IsBmsPcsLinked(int bmsIndex0) =>
            GuiSimDataAccess.SafeGetBool($"bms{bmsIndex0 + 1}.BatteryStacks[0].IsPcsLinked")
            || GuiSimDataAccess.SafeGetBool(
                $"ess._batteryRacks[{bmsIndex0}]._currentState.IsPcsLinked");

        private static string BuildBmsCompact(int bmsIndex0, double dcVoltage, double dcCurrent)
        {
            double s = 100 * GuiSimDataAccess.SafeGetDouble(
                $"ess._batteryRacks[{bmsIndex0}]._currentState.MinClusterSOC");
            return $"SOC {s:0.0}%  Vdc {dcVoltage:0.0}  Idc {dcCurrent:0.0}";
        }

        private static string BuildBmsEnergy(int bmsIndex0)
        {
            double ch = GuiSimDataAccess.SafeGetDouble(
                $"ess._batteryRacks[{bmsIndex0}]._currentState.TotalChargeEnergy");
            double dis = GuiSimDataAccess.SafeGetDouble(
                $"ess._batteryRacks[{bmsIndex0}]._currentState.TotalDischargeEnergy");
            return $"累计充 {ch:0.0} / 放 {dis:0.0} kWh";
        }

        private static string BuildBmsRunStatus(int bmsIndex0)
        {
            int code = (int)GuiSimDataAccess.SafeGetDouble(
                $"bms{bmsIndex0 + 1}.BatteryStacks[0].OperationStatus");
            string label = BmsMapper.GetStackOperationStatusLabel(code);
            return $"运行:{label}";
        }

        private static string FormatBreaker(bool closed, bool tripped) =>
            tripped ? "跳闸" : closed ? "合" : "分";
    }

    public sealed class MainLineViewModel
    {
        public bool PropagationEnabled { get; set; }
        public bool MainBreakerClosed { get; set; }
        public bool MainBreakerTripped { get; set; }
        public string MainBreakerLabel { get; set; } = "";
        public double PccLineVoltageV { get; set; }
        public double StationBus35LineVoltageV { get; set; }
        public BusNodeSnapshot? BusGrid { get; set; }
        public BusNodeSnapshot? Bus35Propagation { get; set; }
        public AcPhasorSnapshot MeterPrimary { get; set; }
        public AcPhasorSnapshot MainTransformerPrimary { get; set; }
        public AcPhasorSnapshot MainTransformerSecondary { get; set; }
        public double LoadActivePowerKw { get; set; }
        public double LoadReactivePowerKvar { get; set; }
        public double LoadActivePowerSetKw { get; set; }
        public double LoadReactivePowerSetKvar { get; set; }
        public double GridNominalLineVoltageV { get; set; }
        public double GridNominalFrequencyHz { get; set; }
        public double SystemFrequencyHz { get; set; }
        public MeterThreePhaseSnapshot MeterThreePhase { get; set; }
        public string BlackStartSummary { get; set; } = "";
        public List<MainLineUnitViewModel> Units { get; set; } = new();
        public List<MainLinePvUnitViewModel> PvUnits { get; set; } = new();

        /// <summary>工程模式开启且存在激活组态时为 true，前端按组态拓扑绘制主接线。</summary>
        public bool EngineeringMode { get; set; }
        /// <summary>工程模式组态中是否存在「负载」节点；无则概览负载置灰冻结。</summary>
        public bool HasTopologyLoad { get; set; }
        public string? ActiveProjectName { get; set; }
        public TopologyProject? Topology { get; set; }
        public List<TopologyTemplate>? TopologyTemplates { get; set; }
    }

    public sealed class MainLineUnitViewModel
    {
        public int UnitIndex { get; set; }
        public int UnitNumber { get; set; }
        public bool UnitBreakerClosed { get; set; }
        public bool UnitBreakerTripped { get; set; }
        public string UnitBreakerLabel { get; set; } = "";
        public AcPhasorSnapshot UnitTransformerPrimary { get; set; }
        public AcPhasorSnapshot UnitTransformerSecondary { get; set; }
        public string UnitTransformerLine { get; set; } = "";
        public BusNodeSnapshot? Bus690 { get; set; }
        /// <summary>单元内各槽位 PCS 电气快照（可含 null，与布局台数对齐）。</summary>
        public IReadOnlyList<PcsChannelSnapshot?> PcsChannels { get; set; } = Array.Empty<PcsChannelSnapshot?>();
        /// <summary>单元内各槽位运行时通道（实时数据 + 控制），按槽位顺序。</summary>
        public List<MainLineChannelViewModel> Channels { get; set; } = new();
        public MeterThreePhaseSnapshot UnitMeterThreePhase { get; set; }
        public double UnitMeterActivePowerKw { get; set; }
        public double UnitMeterReactivePowerKvar { get; set; }
        public double UnitMeterPowerFactor { get; set; }
        public double UnitMeterFrequencyHz { get; set; }
        public double UnitTransformerLoadFraction { get; set; }
        public double UnitTransformerOilTemperatureC { get; set; }
        public double UnitTransformerActivePowerKw { get; set; }
        public double UnitTransformerReactivePowerKvar { get; set; }
    }

    public sealed class MainLineChannelViewModel
    {
        public int ChannelIndex { get; set; }
        public int ChannelNumber { get; set; }
        public int CompartmentNumber { get; set; }
        public int PcsNumber { get; set; }
        public int UnitIndex { get; set; }
        public int SlotInUnit { get; set; }
        public double SocPercent { get; set; }
        public double DcVoltage { get; set; }
        public double DcCurrent { get; set; }
        public string GridConnect { get; set; } = "";
        public string BmsBlackStart { get; set; } = "";
        public string BmsCompact { get; set; } = "";
        public double CumulativeChargeEnergyKwh { get; set; }
        public double CumulativeDischargeEnergyKwh { get; set; }
        public string BmsEnergy { get; set; } = "";
        public string BmsRunStatus { get; set; } = "";
                /// <summary>BMS 舱空调状态与柜内温度（如「空调:制冷 25.3℃ 设20.0℃」）。</summary>
                public string BmsAirConditioner { get; set; } = "";
        public string PcsDeviceState { get; set; } = "";
        public string PcsStartStop { get; set; } = "";
        public string PcsTargetP { get; set; } = "";
        public string PcsActualP { get; set; } = "";
        public string PcsTargetQ { get; set; } = "";
        public string PcsActualQ { get; set; } = "";
        public string PcsBlackStart { get; set; } = "";
        public string PcsAcLine { get; set; } = "";
        public string PcsGridMode { get; set; } = "";
        public double TargetActivePowerKw { get; set; }
        public double TargetReactivePowerKvar { get; set; }
        /// <summary>PCS 实时有功 kW：&gt;0 放电，&lt;0 充电。</summary>
        public double ActualActivePowerKw { get; set; }
        public double ActualReactivePowerKvar { get; set; }
        /// <summary>对应 simEmu 编号（与全局 PCS 编号 1:1）。</summary>
        public int EmuUnitNumber { get; set; }
        public string ActivePowerYtPoint { get; set; } = "yt0";
        public string ReactivePowerYtPoint { get; set; } = "yt1";
    }

    public sealed class MainLinePvUnitViewModel
    {
        public int PvIndex { get; set; }
        public int PvNumber { get; set; }
        public string DeviceId { get; set; } = "";
        public double ActivePowerKw { get; set; }
        public double ReactivePowerKvar { get; set; }
        public double TargetActivePowerKw { get; set; }
        public double TargetReactivePowerKvar { get; set; }
        public double OnOff { get; set; }
        public bool Running { get; set; }
        public int GridConnectedDeviceCount { get; set; }
        public double MaximumDischargePowerKw { get; set; }
        public MainLinePvArrayViewModel ArrayA { get; set; } = new();
        public MainLinePvArrayViewModel ArrayB { get; set; } = new();
    }

    public sealed class MainLinePvArrayViewModel
    {
        public string Side { get; set; } = "A";
        public double AmbientTemperatureC { get; set; } = 25;
        public double IncidenceAngleDeg { get; set; } = 90;
        public double PlaneOfArrayWm2 { get; set; }
        public double RearWm2 { get; set; }
        public double Albedo { get; set; }
        public double CellTemperatureC { get; set; } = 25;
        public double AvailableAcPowerKw { get; set; }
        public double ActivePowerKw { get; set; }
        public double DcVoltageV { get; set; }
        public double DcCurrentA { get; set; }
        public string LimitReason { get; set; } = "";
    }
}
