using EssSimulator.Core;
using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Plugins;
using log4net;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// 基础 LC 运行时：在 simLc* 与 simEmu* 寄存器之间按组×槽位抄数/回写。
    /// </summary>
    internal class StandardLcRuntime : LcRuntimeBase
    {
        private readonly Dictionary<string, double> _controlShadow = new();
        private readonly TrinaEmuFaultWordPlugin _warningWords = new();
        private readonly ReflectionSimulationAdapter _sim = new();
        private LcBmsBankParams? _bmsBank;

        public StandardLcRuntime(ILog log) : base(log) { }

        public override void RunCycle(
            Func<string, ModbusSimServer?> resolveEmu,
            LocalControlModbusServer lc,
            int lcIdx,
            int emuPerGroup,
            int emuCount)
        {
            if (!lc.IsOnline)
                return;

            // 含模型绑定的点表（如 trina 系统级点表）由 DataExchange 管道驱动，无需桥接转发
            if (lc.UsesDataExchange)
                return;

            string lcName = lc.ServerName;
            SyncTelemetry(resolveEmu, lc, lcIdx);
            ApplyControls(resolveEmu, lc, lcName, lcIdx);
            CollectExtra(resolveEmu, lc, lcIdx, emuPerGroup, emuCount);
        }

        private void SyncTelemetry(
            Func<string, ModbusSimServer?> resolveEmu,
            LocalControlModbusServer lc,
            int lcIdx)
        {
            int groups = Math.Max(1, lc.LcGroupCount);
            var unit = lc.EssUnit;
            int unitId = lcIdx + 1;

            for (int n = 1; n <= groups; n++)
            {
                int pcsInGroup = LcLayout.PcsCountInGroup(unit, n - 1);
                for (int k = 0; k < LcLayout.TemplateSlotsPerGroup; k++)
                {
                    if (k >= pcsInGroup)
                    {
                        WritePcsTelemetryDefaults(lc, n, k);
                        continue;
                    }

                    int flat = LcPcsIndex.Flat(unit, n - 1, k);
                    var emu = resolveEmu(lc.EmuServerNameForPcs(flat));
                    if (emu == null || !emu.IsDataPathReady)
                    {
                        WritePcsTelemetryDefaults(lc, n, k);
                        continue;
                    }

                    var snap = ReadChannel(emu);

                    bool isOn = ModbusValueConverter.ToDouble(snap.StartStop) != 0;
                    var mappedStatus = MapRunStatus(
                        snap.Status,
                        isOn,
                        ModbusValueConverter.ToDouble(snap.MeasP),
                        ModbusValueConverter.ToDouble(snap.MeasQ));

                    lc.SetDataStoreByMesurePointName(LcChannelMap.Fault(n, k), snap.Fault);
                    lc.SetDataStoreByMesurePointName(LcChannelMap.Alarm(n, k), snap.Alarm);
                    lc.SetDataStoreByMesurePointName(LcChannelMap.Status(n, k), mappedStatus);
                    lc.SetDataStoreByMesurePointName(LcChannelMap.Freq(n, k), snap.Freq);
                    lc.SetDataStoreByMesurePointName(LcChannelMap.Vab(n, k), snap.Vab);
                    lc.SetDataStoreByMesurePointName(LcChannelMap.Vbc(n, k), snap.Vbc);
                    lc.SetDataStoreByMesurePointName(LcChannelMap.Vca(n, k), snap.Vca);
                    WriteExtraChannelTelemetry(lc, n, k, snap.MeasP, snap.MeasQ);
                    lc.SetDataStoreByMesurePointName(LcChannelMap.Ready(n, k), IsPcsReady(snap) ? 1 : 0);
                }
            }

            SyncUnitFragments(lc, unit, unitId);
            SyncSystemFragment(lc, unitId);
            SyncMvFragment(lc, unitId);
            SyncBmsFragment(resolveEmu, lc, lcIdx);
        }

        /// <summary>
        /// 系统片段：本单元 emuN 虚拟模型 + 实际 PcsList 台数求和；
        /// 插件点复用 <see cref="TrinaEmuFaultWordPlugin"/>（deviceRoot = emuN）。
        /// </summary>
        private void SyncSystemFragment(LocalControlModbusServer lc, int unitId)
        {
            LcSystemEmuSnap emuSnap = default;
            IReadOnlyList<LcSystemPcsSnap> pcs = Array.Empty<LcSystemPcsSnap>();
            LcSystemPluginSnap plugins = default;
            {
                string root = $"emu{unitId}";
                object hvBreaker = ReadPath($"{root}.Breaker.Closed");
                var ess = SimulatorHost.Instance.TryGetEss();
                if (ess != null)
                    hvBreaker = ess.IsUnitBreakerClosed(unitId - 1)
                        ? LcSystemMap.HvBreakerClosed
                        : LcSystemMap.HvBreakerOpen;
                emuSnap = new LcSystemEmuSnap(
                    ReadPath($"{root}.Emu.AverageBatterySoc"),
                    ReadPath($"{root}.Emu.MaxChargePower"),
                    ReadPath($"{root}.Emu.MaxDischargePower"),
                    ReadPath($"{root}.Emu.OutputActivePower"),
                    ReadPath($"{root}.Emu.OutputReactivePower"),
                    hvBreaker);
                pcs = ReadUnitPcs(unitId);
                plugins = new LcSystemPluginSnap(
                    ComputePlugin(TrinaEmuFaultWordPlugin.SystemFaultSummaryKey, root),
                    ComputePlugin(TrinaEmuFaultWordPlugin.SystemRunStateSummaryKey, root),
                    ComputePlugin(TrinaEmuFaultWordPlugin.UnitBlackStartStatusKey, root),
                    ComputePlugin(TrinaEmuFaultWordPlugin.UnitPcsTotalCountKey, root),
                    ComputePlugin(TrinaEmuFaultWordPlugin.UnitPcsRunningCountKey, root),
                    ComputePlugin(TrinaEmuFaultWordPlugin.UnitPcsAlarmCountKey, root),
                    ComputePlugin(TrinaEmuFaultWordPlugin.UnitPcsFaultCountKey, root));
            }

            var bms = ReadUnitBms(unitId, pcs.Count);
            var groups = ReadUnitGroups(lc.EssUnit, pcs);
            foreach (var (param, value) in LcSystemTelemetry.Collect(emuSnap, pcs, plugins, bms, groups, live: true))
                lc.SetDataStoreByMesurePointName(param, value);
        }

        /// <summary>
        /// BMS 段：按组抄对应 <c>simBms*</c> 的 bank 寄存器（bms_bank.csv），再折成 LC bmsyc*。
        /// 不读仿真对象，BMS 离线时该组写 0。组内多堆按电压/SOC 平均、电流功率求和、故障字或运算。
        /// </summary>
        private void SyncBmsFragment(
            Func<string, ModbusSimServer?> resolveBms,
            LocalControlModbusServer lc,
            int lcIdx)
        {
            int groups = Math.Max(1, lc.LcGroupCount);
            for (int n = 1; n <= groups; n++)
            {
                var names = LcBmsLayout.ServerNamesForGroup(lc.EssUnits, lcIdx, lc.EssUnit, n);
                var snaps = new List<LcBmsBankSnap>(names.Count);
                foreach (var name in names)
                {
                    var bms = resolveBms(name);
                    if (bms is { IsOnline: true })
                        snaps.Add(ReadBmsBank(bms));
                }

                LcBmsBankSnap? snap = snaps.Count == 0 ? null : LcBmsTelemetry.Aggregate(snaps);
                foreach (var (param, value) in LcBmsTelemetry.Collect(n, snap))
                    lc.SetDataStoreByMesurePointName(param, value);
            }
        }

        private LcBmsBankParams BmsBank => _bmsBank ??= LcBmsBankMap.FromSelected();

        private LcBmsBankSnap ReadBmsBank(ModbusSimServer bms) => new()
        {
            Voltage = ReadParamOrDefault(bms, BmsBank.Voltage),
            Current = ReadParamOrDefault(bms, BmsBank.Current),
            Soc = ReadParamOrDefault(bms, BmsBank.Soc),
            Soh = ReadParamOrDefault(bms, BmsBank.Soh),
            Insulation = ReadParamOrDefault(bms, BmsBank.Insulation),
            ChargeRemainKwh = ReadParamOrDefault(bms, BmsBank.ChargeRemain),
            DischargeRemainKwh = ReadParamOrDefault(bms, BmsBank.DischargeRemain),
            MaxChargeCurrent = ReadParamOrDefault(bms, BmsBank.MaxChargeCurrent),
            MaxDischargeCurrent = ReadParamOrDefault(bms, BmsBank.MaxDischargeCurrent),
            ClusterCurrentDiff = ReadParamOrDefault(bms, BmsBank.ClusterCurrentDiff),
            ClusterVoltageDiff = ReadParamOrDefault(bms, BmsBank.ClusterVoltageDiff),
            MaxCellVoltageRack = ReadParamOrDefault(bms, BmsBank.MaxCellVoltageRack),
            MaxCellVoltagePack = ReadParamOrDefault(bms, BmsBank.MaxCellVoltagePack),
            MaxCellVoltageCell = ReadParamOrDefault(bms, BmsBank.MaxCellVoltageCell),
            MaxCellVoltage = ReadParamOrDefault(bms, BmsBank.MaxCellVoltage),
            MinCellVoltageRack = ReadParamOrDefault(bms, BmsBank.MinCellVoltageRack),
            MinCellVoltagePack = ReadParamOrDefault(bms, BmsBank.MinCellVoltagePack),
            MinCellVoltageCell = ReadParamOrDefault(bms, BmsBank.MinCellVoltageCell),
            MinCellVoltage = ReadParamOrDefault(bms, BmsBank.MinCellVoltage),
            AvgCellVoltage = ReadParamOrDefault(bms, BmsBank.AvgCellVoltage),
            MaxCellTempRack = ReadParamOrDefault(bms, BmsBank.MaxCellTempRack),
            MaxCellTempPack = ReadParamOrDefault(bms, BmsBank.MaxCellTempPack),
            MaxCellTempCell = ReadParamOrDefault(bms, BmsBank.MaxCellTempCell),
            MaxCellTemp = ReadParamOrDefault(bms, BmsBank.MaxCellTemp),
            MinCellTempRack = ReadParamOrDefault(bms, BmsBank.MinCellTempRack),
            MinCellTempPack = ReadParamOrDefault(bms, BmsBank.MinCellTempPack),
            MinCellTempCell = ReadParamOrDefault(bms, BmsBank.MinCellTempCell),
            MinCellTemp = ReadParamOrDefault(bms, BmsBank.MinCellTemp),
            AvgCellTemp = ReadParamOrDefault(bms, BmsBank.AvgCellTemp),
            ChargeEnergyKwh = ReadParamOrDefault(bms, BmsBank.ChargeEnergy),
            DischargeEnergyKwh = ReadParamOrDefault(bms, BmsBank.DischargeEnergy),
            OnlineClusters = ReadParamOrDefault(bms, BmsBank.OnlineClusters),
            TotalClusters = ReadParamOrDefault(bms, BmsBank.TotalClusters),
            MinParallelClusters = ReadParamOrDefault(bms, BmsBank.MinParallelClusters),
            DischargePowerKw = ReadParamOrDefault(bms, BmsBank.DischargePower),
            ChargePowerKw = ReadParamOrDefault(bms, BmsBank.ChargePower),
            ProtectionSummary = ReadParamOrDefault(bms, BmsBank.ProtectionSummary),
            AlarmSummary = ReadParamOrDefault(bms, BmsBank.AlarmSummary),
            FaultSummary = ReadParamOrDefault(bms, BmsBank.FaultSummary),
            PcsFaultStatus = ReadParamOrDefault(bms, BmsBank.PcsFaultStatus),
            ClusterDropFault = ReadParamOrDefault(bms, BmsBank.ClusterDropFault),
            BlackStartEnter = ReadParamOrDefault(bms, BmsBank.BlackStartEnter),
            BlackStartStatus = ReadParamOrDefault(bms, BmsBank.BlackStartStatus),
            GridConnectStatus = ReadParamOrDefault(bms, BmsBank.GridConnect),
            RunMode = ReadParamOrDefault(bms, BmsBank.RunMode),
            ChargeDischarge = ReadParamOrDefault(bms, BmsBank.ChargeDischarge),
            Heartbeat = ReadParamOrDefault(bms, BmsBank.Heartbeat)
        };

        /// <summary>MV 电表段：优先抄本单元电表（馈线/PCS 母线），没有 EMU 镜像时回退 PCC。</summary>
        private void SyncMvFragment(LocalControlModbusServer lc, int unitId)
        {
            LcMvMeterSnap? snap = null;
            var emu = SimulatorHost.Instance.TryGetEmu(unitId);
            if (emu != null)
            {
                snap = LcMvTelemetry.FromUnitMeter(emu.ElectricityMeter);
            }
            else
            {
                var ess = SimulatorHost.Instance.TryGetEss();
                if (ess != null)
                    snap = LcMvTelemetry.FromPcc(
                        ess.ElectricalNetwork.PccMeter,
                        ess.ElectricalNetwork.SystemFrequencyHz);
            }

            foreach (var (param, value) in LcMvTelemetry.Collect(snap))
                lc.SetDataStoreByMesurePointName(param, value);
        }

        private static LcSystemPcsSnap[] ReadUnitPcs(int unitId)
        {
            int count = (int)Math.Max(0, Math.Round(ModbusValueConverter.ToDouble(ReadPath($"emu{unitId}.PcsList.Count"))));
            var list = new LcSystemPcsSnap[count];
            for (int i = 0; i < count; i++)
            {
                list[i] = new LcSystemPcsSnap(
                    ReadDto(unitId, i, "PowerFactor"),
                    ReadDto(unitId, i, "ApparentPower"),
                    ReadDto(unitId, i, "BatteryPower"),
                    ReadDto(unitId, i, "BatteryCurrent"),
                    ReadDto(unitId, i, "Frequency"),
                    ReadDto(unitId, i, "BatteryVoltage"),
                    ReadDto(unitId, i, "PCSRatePower"),
                    ReadDto(unitId, i, "OperationStatus"),
                    ReadDto(unitId, i, "AlarmSummary1"));
            }

            return list;
        }

        private static LcSystemBmsSnap[] ReadUnitBms(int unitId, int pcsCount)
        {
            int baseIdx = 0;
            for (int u = 1; u < unitId; u++)
                baseIdx += (int)Math.Max(0, Math.Round(ModbusValueConverter.ToDouble(ReadPath($"emu{u}.PcsList.Count"))));

            var store = SimulatorHost.Instance;
            var list = new List<LcSystemBmsSnap>(pcsCount);
            for (int i = 0; i < pcsCount; i++)
            {
                int id = baseIdx + i + 1;
                if (!store.Contains($"bms{id}"))
                    continue;
                string stack = $"bms{id}.BatteryStacks[0]";
                list.Add(new LcSystemBmsSnap(
                    ReadPath($"{stack}.NominalEnergyKWh"),
                    ReadPath($"{stack}.SOH"),
                    ReadPath($"{stack}.AvailableChargeCapacity"),
                    ReadPath($"{stack}.AvailableDischargeCapacity"),
                    ReadPath($"{stack}.BMSFaultSummary"),
                    ReadPath($"{stack}.BMSAlarmSummary"),
                    ReadPath($"{stack}.OperationStatus")));
            }

            return list.ToArray();
        }

        private static LcSystemGroupSnap[] ReadUnitGroups(
            EssSimulator.Configuration.EssUnitConfig? unit,
            IReadOnlyList<LcSystemPcsSnap> pcs)
        {
            int groups = Math.Max(1, LcLayout.GroupCount(unit));
            var snaps = new LcSystemGroupSnap[groups];
            int offset = 0;
            for (int g = 0; g < groups; g++)
            {
                int nPcs = LcLayout.PcsCountInGroup(unit, g);
                bool faulted = false;
                for (int k = 0; k < nPcs; k++)
                {
                    int idx = offset + k;
                    if (idx < pcs.Count &&
                        (int)Math.Round(ModbusValueConverter.ToDouble(pcs[idx].OperationStatus ?? 0)) == 6)
                        faulted = true;
                }

                snaps[g] = new LcSystemGroupSnap(nPcs > 0, faulted);
                offset += nPcs;
            }

            return snaps;
        }

        /// <summary>
        /// 10MW / 5.5MW 单元片段：n 为 PCS 组号；模块1/2 为该组第一台 PCS 的两条支路。
        /// 10MW 写 n=1,2；5.5MW 只写 n=1。缺支路写 0。
        /// </summary>
        private void SyncUnitFragments(
            LocalControlModbusServer lc,
            EssSimulator.Configuration.EssUnitConfig? unit,
            int unitId)
        {
            foreach (var layout in LcUnitMap.Fragments)
            {
                for (int n = 1; n <= layout.PairCount; n++)
                {
                    int pcsInGroup = LcLayout.PcsCountInGroup(unit, n - 1);
                    LcUnitModuleSnap? m1 = pcsInGroup > 0
                        ? ReadUnitModule(unitId, LcPcsIndex.Flat(unit, n - 1, 0))
                        : null;
                    LcUnitModuleSnap? m2 = pcsInGroup > 1
                        ? ReadUnitModule(unitId, LcPcsIndex.Flat(unit, n - 1, 1))
                        : null;

                    foreach (var (param, value) in LcUnitTelemetry.Collect(layout, n, m1, m2))
                        lc.SetDataStoreByMesurePointName(param, value);
                }
            }
        }

        private LcUnitModuleSnap ReadUnitModule(int unitId, int flatPcsIndex) =>
            new(
                ReadDto(unitId, flatPcsIndex, "BatteryVoltage"),
                ReadDto(unitId, flatPcsIndex, "BatteryCurrent"),
                ReadDto(unitId, flatPcsIndex, "LineVoltageAB"),
                ReadDto(unitId, flatPcsIndex, "LineVoltageBC"),
                ReadDto(unitId, flatPcsIndex, "LineVoltageCA"),
                ReadDto(unitId, flatPcsIndex, "PhaseACurrent"),
                ReadDto(unitId, flatPcsIndex, "PhaseBCurrent"),
                ReadDto(unitId, flatPcsIndex, "PhaseCCurrent"),
                ReadDto(unitId, flatPcsIndex, "BatteryPower"),
                ReadDto(unitId, flatPcsIndex, "IGBTMaxTemp"),
                ReadDto(unitId, flatPcsIndex, "AvailableCapacity"),
                ReadDto(unitId, flatPcsIndex, "PCSRatePower"),
                ComputeModuleWarning(unitId, flatPcsIndex, "ModuleWarningWord1"),
                ComputeModuleWarning(unitId, flatPcsIndex, "ModuleWarningWord2"),
                ReadDto(unitId, flatPcsIndex, "ActivePower"),
                ReadDto(unitId, flatPcsIndex, "ReactivePower"));

        private object ComputeModuleWarning(int unitId, int flatPcsIndex, string wordKey) =>
            ComputePlugin(wordKey, $"emu{unitId}.PcsList[{flatPcsIndex}]");

        private object ComputePlugin(string wordKey, string deviceRoot)
        {
            try
            {
                return _warningWords.Compute(wordKey, deviceRoot, _sim) ?? 0;
            }
            catch (Exception ex)
            {
                Log.Debug($"ComputePlugin {wordKey} {deviceRoot} 失败，回退 0", ex);
                return 0;
            }
        }

        private static object ReadPath(string path)
        {
            try
            {
                return SimServer.GetExtIfVariableVal(path) ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private ChannelSnapshot ReadChannel(ModbusSimServer emu)
        {
            var proto = EmuPcsChannel.Slot0;
            return new ChannelSnapshot(
                ReadParamOrDefault(emu, proto.Fault),
                ReadParamOrDefault(emu, proto.Alarm),
                ReadParamOrDefault(emu, proto.Status),
                ReadParamOrDefault(emu, proto.Freq),
                ReadParamOrDefault(emu, proto.Vab),
                ReadParamOrDefault(emu, proto.Vbc),
                ReadParamOrDefault(emu, proto.Vca),
                ReadParamOrDefault(emu, proto.BlackStart),
                ReadParamOrDefault(emu, proto.StartStop),
                ReadParamOrDefault(emu, proto.MeasActivePower),
                ReadParamOrDefault(emu, proto.MeasReactivePower),
                ReadParamOrDefault(emu, proto.IslandV));
        }

        private readonly record struct ChannelSnapshot(
            object Fault, object Alarm, object Status, object Freq,
            object Vab, object Vbc, object Vca,
            object BlackStart, object StartStop, object MeasP, object MeasQ,
            object IslandVSet);

        private static bool IsPcsReady(in ChannelSnapshot snap) =>
            LcPcsReady.IsReady(
                ModbusValueConverter.ToDouble(snap.BlackStart) != 0,
                ModbusValueConverter.ToDouble(snap.IslandVSet),
                ModbusValueConverter.ToDouble(snap.Vab),
                ModbusValueConverter.ToDouble(snap.Vbc),
                ModbusValueConverter.ToDouble(snap.Vca));

        private void ApplyControls(
            Func<string, ModbusSimServer?> resolveEmu,
            LocalControlModbusServer lc,
            string lcName,
            int lcIdx)
        {
            if (EssSimulator.Core.ExternalControlGate.IsBlocked)
                return;
            int groups = Math.Max(1, lc.LcGroupCount);
            var unit = lc.EssUnit;
            int unitId = lcIdx + 1;

            for (int n = 1; n <= groups; n++)
            {
                int pcsInGroup = LcLayout.PcsCountInGroup(unit, n - 1);
                for (int k = 0; k < LcLayout.TemplateSlotsPerGroup && k < pcsInGroup; k++)
                {
                    int flat = LcPcsIndex.Flat(unit, n - 1, k);
                    var emu = resolveEmu(lc.EmuServerNameForPcs(flat));
                    if (emu != null && emu.IsDataPathReady)
                    {
                        var proto = EmuPcsChannel.Slot0;
                        ForwardWhenChanged(lc, lcName, LcChannelMap.StartStop(n, k), emu, proto.StartStop, asBool: true);
                        ForwardWhenChanged(lc, lcName, LcChannelMap.ActivePowerSet(n, k), emu, proto.ActivePowerSet, asBool: false);
                        ForwardWhenChanged(lc, lcName, LcChannelMap.ReactivePowerSet(n, k), emu, proto.ReactivePowerSet, asBool: false);
                        ForwardWhenChanged(lc, lcName, LcChannelMap.IslandV(n, k), emu, proto.IslandV, asBool: false);
                        ForwardWhenChanged(lc, lcName, LcChannelMap.IslandF(n, k), emu, proto.IslandF, asBool: false);
                    }
                    else
                    {
                        ForwardWhenChangedDto(lc, lcName, LcChannelMap.StartStop(n, k), unitId, flat, "pcsOnOffSwitch", asBool: true);
                        ForwardWhenChangedDto(lc, lcName, LcChannelMap.ActivePowerSet(n, k), unitId, flat, "PCSActivePowerSetting", asBool: false);
                        ForwardWhenChangedDto(lc, lcName, LcChannelMap.ReactivePowerSet(n, k), unitId, flat, "PCSReactivePowerSetting", asBool: false);
                        ForwardWhenChangedDto(lc, lcName, LcChannelMap.IslandV(n, k), unitId, flat, "IslandVoltageSetting", asBool: false);
                        ForwardWhenChangedDto(lc, lcName, LcChannelMap.IslandF(n, k), unitId, flat, "IslandFrequencySetting", asBool: false);
                    }
                }
            }

            ApplyGlobalBlackStartControl(resolveEmu, lc, lcName, lcIdx);
            ApplySystemEmuControls(lc, lcName, unitId);
            ApplySystemRestartControl(lc, lcName, unitId);
            ApplyMvHvBreakerControl(lc, lcName, unitId);
        }

        /// <summary>syst4/5/6/1010/1011 写入 emuN.Emu.*；syst7 由黑启动批量路径同时写 BlackStartModeWrite。</summary>
        private void ApplySystemEmuControls(LocalControlModbusServer lc, string lcName, int unitId)
        {
            foreach (var binding in LcSystemMap.EmuControls)
            {
                if (binding.Param == LcSystemMap.BlackStartWrite)
                    continue;
                ForwardWhenChangedEmu(lc, lcName, binding.Param, unitId, binding.EmuField, binding.AsBool);
            }
        }

        /// <summary>syst1399 写 0xAA 视为单元复位脉冲：等同 syst6=6（全部停机清故障），写完回 0。</summary>
        private void ApplySystemRestartControl(LocalControlModbusServer lc, string lcName, int unitId)
        {
            const string lcParam = LcSystemMap.Restart;
            string key = $"{lcName}:{lcParam}";
            if (!_controlShadow.ContainsKey(key))
                _controlShadow[key] = 0;

            if (!TryReadChangedControl(lc, lcName, lcParam, asBool: false, out _, out var current))
                return;

            if (!LcSystemTelemetry.IsRestartPulse(current))
            {
                UpdateControlShadow(lcName, lcParam, current);
                return;
            }

            Log.Info($"[LC-Change] {lcName}.{lcParam}: restart pulse 0xAA");
            WriteEmu(unitId, "SystemOperation", 6);
            try { lc.SetDataStoreByMesurePointName(lcParam, 0); }
            catch { /* 脉冲回零失败可忽略 */ }
            UpdateControlShadow(lcName, lcParam, 0);
        }

        /// <summary>mv_param1 写 AA/EE 驱动本单元高压断路器；非法值回退 LC 寄存器。</summary>
        private void ApplyMvHvBreakerControl(LocalControlModbusServer lc, string lcName, int unitId)
        {
            const string lcParam = LcMvMap.HvBreakerCommand;
            PrimeMvHvBreakerShadow(lc, lcName, unitId);
            if (!TryReadChangedControl(lc, lcName, lcParam, asBool: false, out var prev, out var current))
                return;

            if (!LcMvControl.TryApply(unitId, current, out var normalized, out var message))
            {
                double fallback = double.IsNaN(prev) ? LcMvControl.Encode(true) : prev;
                Log.Warn($"[LC-Change] {lcName}.{lcParam}: 拒绝 {ModbusValueConverter.FormatControlValue(current)}，回退 {ModbusValueConverter.FormatControlValue(fallback)} ({message})");
                try { lc.SetDataStoreByMesurePointName(lcParam, fallback); }
                catch { /* 回退失败可忽略 */ }
                UpdateControlShadow(lcName, lcParam, fallback);
                return;
            }

            Log.Info(
                $"[LC-Change] {lcName}.{lcParam}: {ModbusValueConverter.FormatControlValue(prev)} -> {ModbusValueConverter.FormatControlValue(normalized)} ({message})");
            try { lc.SetDataStoreByMesurePointName(lcParam, normalized); }
            catch { /* 规范化写回失败可忽略 */ }
            UpdateControlShadow(lcName, lcParam, normalized);
        }

        private void PrimeMvHvBreakerShadow(LocalControlModbusServer lc, string lcName, int unitId)
        {
            const string lcParam = LcMvMap.HvBreakerCommand;
            string key = $"{lcName}:{lcParam}";
            if (_controlShadow.ContainsKey(key))
                return;

            bool closed = ModbusValueConverter.ToDouble(ReadPath($"emu{unitId}.Breaker.Closed")) != 0;
            double val = LcMvControl.Encode(closed);
            _controlShadow[key] = val;
            try { lc.SetDataStoreByMesurePointName(lcParam, val); }
            catch { /* 首轮对齐失败可忽略 */ }
        }

        private void ApplyGlobalBlackStartControl(
            Func<string, ModbusSimServer?> resolveEmu,
            LocalControlModbusServer lc,
            string lcName,
            int lcIdx)
        {
            const string lcParam = LcSystemMap.BlackStartWrite;
            PrimeGlobalBlackStartShadow(resolveEmu, lc, lcName, lcIdx);
            if (!TryReadChangedControl(lc, lcName, lcParam, asBool: true, out var prevBlackStart, out var blackStartGlobal))
                return;

            Log.Info(
                $"[LC-Change] {lcName}.{lcParam}: {ModbusValueConverter.FormatControlValue(prevBlackStart)} -> {ModbusValueConverter.FormatControlValue(blackStartGlobal)}");

            bool success = WriteEmu(lcIdx + 1, "BlackStartModeWrite", blackStartGlobal);
            var unit = lc.EssUnit;
            int groups = Math.Max(1, lc.LcGroupCount);
            for (int n = 1; n <= groups; n++)
            {
                int pcsInGroup = LcLayout.PcsCountInGroup(unit, n - 1);
                for (int k = 0; k < pcsInGroup; k++)
                {
                    int flat = LcPcsIndex.Flat(unit, n - 1, k);
                    var emu = resolveEmu(lc.EmuServerNameForPcs(flat));
                    if (emu != null && emu.IsDataPathReady)
                    {
                        success &= TryPublishControlWithLog(
                            emu, EmuPcsChannel.Slot0.BlackStart, blackStartGlobal, asBool: true, $"{lcName}.{lcParam}");
                    }
                    else
                    {
                        success &= LcPcsDtoCommand.WriteAndApply(lcIdx + 1, flat, "BlackStartEnabled", blackStartGlobal != 0);
                    }
                }
            }

            if (success)
                UpdateControlShadow(lcName, lcParam, blackStartGlobal);
        }

        private void WritePcsTelemetryDefaults(LocalControlModbusServer lc, int n, int k)
        {
            lc.SetDataStoreByMesurePointName(LcChannelMap.Fault(n, k), 0);
            lc.SetDataStoreByMesurePointName(LcChannelMap.Alarm(n, k), 0);
            lc.SetDataStoreByMesurePointName(LcChannelMap.Status(n, k), 0);
            lc.SetDataStoreByMesurePointName(LcChannelMap.Freq(n, k), 0);
            lc.SetDataStoreByMesurePointName(LcChannelMap.Vab(n, k), 0);
            lc.SetDataStoreByMesurePointName(LcChannelMap.Vbc(n, k), 0);
            lc.SetDataStoreByMesurePointName(LcChannelMap.Vca(n, k), 0);
            WriteExtraChannelTelemetry(lc, n, k, 0, 0);
            lc.SetDataStoreByMesurePointName(LcChannelMap.Ready(n, k), 0);
        }

        /// <summary>
        /// group/lc.csv 的 pcs 运行状态透传 emu yc44（1停机 2待机 4充电 5放电 6故障）。
        /// 黑启动直流通道的 0/1/2 编码见 <see cref="DcChannelRunStatus"/>，不用于本组点。
        /// </summary>
        protected virtual object MapRunStatus(object emuStatus, bool isOn, double activePower, double reactivePower) =>
            emuStatus;

        /// <summary>把 EMU 实测有功/无功抄到 group 片段点 group_param102+。</summary>
        protected virtual void WriteExtraChannelTelemetry(
            LocalControlModbusServer lc,
            int n,
            int k,
            object activePower,
            object reactivePower)
        {
            lc.SetDataStoreByMesurePointName(LcChannelMap.ExtraActivePower(n, k), activePower);
            lc.SetDataStoreByMesurePointName(LcChannelMap.ExtraReactivePower(n, k), reactivePower);
        }

        private static object ReadDto(int unitId, int flatPcsIndex, string field)
        {
            try
            {
                return SimServer.GetExtIfVariableVal($"emu{unitId}.PcsList[{flatPcsIndex}].{field}") ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private object ReadParamOrDefault(ModbusSimServer server, string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName))
                return 0;
            try
            {
                return server.GetDataObjectByMesurePointName(paramName) ?? 0;
            }
            catch (Exception ex)
            {
                Log.Debug($"ReadParamOrDefault 读取 {paramName} 失败，回退 0", ex);
                return 0;
            }
        }

        private void ForwardWhenChangedDto(
            LocalControlModbusServer lc,
            string lcName,
            string lcParam,
            int unitId,
            int flatPcsIndex,
            string dtoField,
            bool asBool)
        {
            if (!TryReadChangedControl(lc, lcName, lcParam, asBool, out var prevValue, out var currentValue))
                return;

            Log.Info(
                $"[LC-Change] {lcName}.{lcParam}: {ModbusValueConverter.FormatControlValue(prevValue)} -> {ModbusValueConverter.FormatControlValue(currentValue)}");

            object published = asBool ? currentValue != 0 : currentValue;
            bool success;
            if (double.IsNaN(prevValue))
            {
                var targetValue = ModbusValueConverter.ToDouble(ReadDto(unitId, flatPcsIndex, dtoField));
                targetValue = asBool ? (targetValue != 0 ? 1 : 0) : targetValue;
                success = Math.Abs(targetValue - currentValue) < 1e-9 ||
                          LcPcsDtoCommand.WriteAndApply(unitId, flatPcsIndex, dtoField, published);
            }
            else
            {
                success = LcPcsDtoCommand.WriteAndApply(unitId, flatPcsIndex, dtoField, published);
            }

            if (success)
                UpdateControlShadow(lcName, lcParam, currentValue);
        }

        private void ForwardWhenChangedEmu(
            LocalControlModbusServer lc,
            string lcName,
            string lcParam,
            int unitId,
            string emuField,
            bool asBool)
        {
            if (!TryReadChangedControl(lc, lcName, lcParam, asBool, out var prevValue, out var currentValue))
                return;

            Log.Info(
                $"[LC-Change] {lcName}.{lcParam}: {ModbusValueConverter.FormatControlValue(prevValue)} -> {ModbusValueConverter.FormatControlValue(currentValue)}");

            object published = asBool ? currentValue != 0 : currentValue;
            bool success;
            if (double.IsNaN(prevValue))
            {
                var targetValue = ModbusValueConverter.ToDouble(ReadPath($"emu{unitId}.Emu.{emuField}"));
                targetValue = asBool ? (targetValue != 0 ? 1 : 0) : targetValue;
                success = Math.Abs(targetValue - currentValue) < 1e-9 ||
                          WriteEmu(unitId, emuField, published);
            }
            else
            {
                success = WriteEmu(unitId, emuField, published);
            }

            if (success)
                UpdateControlShadow(lcName, lcParam, currentValue);
        }

        private static bool WriteEmu(int unitId, string field, object value)
        {
            try
            {
                return SimServer.SetExtIfVariableVal($"emu{unitId}.Emu.{field}", value);
            }
            catch
            {
                return false;
            }
        }

        private void ForwardWhenChanged(
            LocalControlModbusServer lc,
            string lcName,
            string lcParam,
            ModbusSimServer targetEmu,
            string targetParam,
            bool asBool)
        {
            if (!TryReadChangedControl(lc, lcName, lcParam, asBool, out var prevValue, out var currentValue))
                return;

            Log.Info(
                $"[LC-Change] {lcName}.{lcParam}: {ModbusValueConverter.FormatControlValue(prevValue)} -> {ModbusValueConverter.FormatControlValue(currentValue)}");

            bool success;
            if (double.IsNaN(prevValue))
            {
                var targetValue = ModbusValueConverter.ToDouble(ReadParamOrDefault(targetEmu, targetParam));
                targetValue = asBool ? (targetValue != 0 ? 1 : 0) : targetValue;
                success = Math.Abs(targetValue - currentValue) < 1e-9 ||
                          TryPublishControlWithLog(targetEmu, targetParam, currentValue, asBool, $"{lcName}.{lcParam}");
            }
            else
            {
                success = TryPublishControlWithLog(targetEmu, targetParam, currentValue, asBool, $"{lcName}.{lcParam}");
            }

            if (success)
                UpdateControlShadow(lcName, lcParam, currentValue);
        }

        private void PrimeGlobalBlackStartShadow(
            Func<string, ModbusSimServer?> resolveEmu,
            LocalControlModbusServer lc,
            string lcName,
            int lcIdx)
        {
            const string lcParam = LcSystemMap.BlackStartWrite;
            string key = $"{lcName}:{lcParam}";
            if (_controlShadow.ContainsKey(key))
                return;

            bool enabled = true;
            bool any = false;
            var unit = lc.EssUnit;
            int groups = Math.Max(1, lc.LcGroupCount);
            for (int n = 1; n <= groups && enabled; n++)
            {
                int pcsInGroup = LcLayout.PcsCountInGroup(unit, n - 1);
                for (int k = 0; k < pcsInGroup && enabled; k++)
                {
                    int flat = LcPcsIndex.Flat(unit, n - 1, k);
                    var pcsEmu = resolveEmu(lc.EmuServerNameForPcs(flat));
                    if (pcsEmu == null || !pcsEmu.IsDataPathReady)
                    {
                        enabled = false;
                        break;
                    }

                    any = true;
                    enabled = ModbusValueConverter.ToDouble(
                        ReadParamOrDefault(pcsEmu, EmuPcsChannel.Slot0.BlackStart)) != 0;
                }
            }

            enabled = any && enabled;

            double val = enabled ? 1 : 0;
            _controlShadow[key] = val;
            try { lc.SetDataStoreByMesurePointName(lcParam, val); }
            catch { /* 首轮对齐失败可忽略 */ }
        }

        private bool TryReadChangedControl(
            LocalControlModbusServer lc,
            string lcName,
            string param,
            bool asBool,
            out double previousValue,
            out double currentValue)
        {
            previousValue = 0;
            currentValue = 0;

            object? raw;
            try { raw = lc.GetDataObjectByMesurePointName(param); }
            catch { return false; }

            if (raw == null)
                return false;

            currentValue = ModbusValueConverter.ToDouble(raw);
            currentValue = asBool ? (currentValue != 0 ? 1 : 0) : currentValue;

            string key = $"{lcName}:{param}";
            if (!_controlShadow.TryGetValue(key, out var prev))
            {
                previousValue = double.NaN;
                return true;
            }

            previousValue = prev;
            return Math.Abs(prev - currentValue) >= 1e-9;
        }

        private void UpdateControlShadow(string lcName, string lcParam, double value) =>
            _controlShadow[$"{lcName}:{lcParam}"] = value;

        private bool TryPublishControlWithLog(
            ModbusSimServer targetEmu,
            string targetParam,
            double value,
            bool asBool,
            string sourceLabel)
        {
            try
            {
                object published = asBool
                    ? value != 0
                    : LcEmuRegisterCodec.ToControlRegisterRaw(targetEmu.ControlMaps, targetParam, value);
                targetEmu.SetDataObjectByMesurePointName(targetParam, published);
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[LC-Publish:failed] {sourceLabel} -> {targetEmu}.{targetParam} value={value} error={ex.Message}",
                    ex);
                return false;
            }

            Log.Debug($"[LC-Publish:success] {sourceLabel} -> {targetEmu}.{targetParam} value={value}");
            return true;
        }
    }
}
