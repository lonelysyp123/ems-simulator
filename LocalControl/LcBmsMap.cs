using EssSimulator.Configuration;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// BMS 片段点名：与 <c>bms/lc.csv</c> 的 <c>bmsyc{38000 + 200*(n-1) + offset}</c> 对齐。
    /// <c>n</c> 为组序号，对应该组全部 PCS 对应的 <c>simBms*</c>（遥测按堆聚合）。
    /// </summary>
    internal static class LcBmsMap
    {
        public const int BaseAddress = 38000;
        public const int Stride = 200;

        public static string Point(int n, int offset) =>
            $"bmsyc{BaseAddress + Stride * (n - 1) + offset}";

        public static string TotalVoltage(int n) => Point(n, 0);
        public static string TotalCurrent(int n) => Point(n, 1);
        public static string Soc(int n) => Point(n, 3);
        public static string Soh(int n) => Point(n, 4);
        public static string Insulation(int n) => Point(n, 5);
        public static string ChargeRemain(int n) => Point(n, 6);
        public static string DischargeRemain(int n) => Point(n, 7);
        public static string MaxChargeCurrent(int n) => Point(n, 8);
        public static string MaxDischargeCurrent(int n) => Point(n, 9);
        public static string ClusterCurrentDiff(int n) => Point(n, 10);
        public static string ClusterVoltageDiff(int n) => Point(n, 11);
        public static string MaxCellVoltageRack(int n) => Point(n, 12);
        public static string MaxCellVoltagePack(int n) => Point(n, 13);
        public static string MaxCellVoltageCell(int n) => Point(n, 14);
        public static string MaxCellVoltage(int n) => Point(n, 15);
        public static string MinCellVoltageRack(int n) => Point(n, 16);
        public static string MinCellVoltagePack(int n) => Point(n, 17);
        public static string MinCellVoltageCell(int n) => Point(n, 18);
        public static string MinCellVoltage(int n) => Point(n, 19);
        public static string AvgCellVoltage(int n) => Point(n, 20);
        public static string MaxCellTempRack(int n) => Point(n, 21);
        public static string MaxCellTempPack(int n) => Point(n, 22);
        public static string MaxCellTempCell(int n) => Point(n, 23);
        public static string MaxCellTemp(int n) => Point(n, 24);
        public static string MinCellTempRack(int n) => Point(n, 25);
        public static string MinCellTempPack(int n) => Point(n, 26);
        public static string MinCellTempCell(int n) => Point(n, 27);
        public static string MinCellTemp(int n) => Point(n, 28);
        public static string AvgCellTemp(int n) => Point(n, 29);
        public static string ChargeEnergyHigh(int n) => Point(n, 30);
        public static string ChargeEnergyLow(int n) => Point(n, 31);
        public static string DischargeEnergyHigh(int n) => Point(n, 32);
        public static string DischargeEnergyLow(int n) => Point(n, 33);
        public static string OnlineClusters(int n) => Point(n, 34);
        public static string TotalClusters(int n) => Point(n, 35);
        public static string MinParallelClusters(int n) => Point(n, 36);
        public static string DischargePower(int n) => Point(n, 37);
        public static string ChargePower(int n) => Point(n, 38);
        public static string FaultSummary(int n) => Point(n, 39);
        public static string BlackStartMode(int n) => Point(n, 60);
        public static string BlackStartStatus(int n) => Point(n, 61);
        public static string GridConnect(int n) => Point(n, 62);
        public static string RunMode(int n) => Point(n, 71);
        public static string ChargeDischarge(int n) => Point(n, 72);
        public static string DryContact(int n) => Point(n, 73);
        public static string Heartbeat(int n) => Point(n, 74);
    }

    /// <summary>BMS 堆协议点名：与 <c>bms_bank.csv</c> 对齐，LC 只通过这些点抄 <c>simBms*</c>。</summary>
    internal static class LcBmsBankMap
    {
        public const string PcsFaultStatus = "param1";
        public const string RunMode = "param2";
        public const string ChargeDischarge = "param3";
        public const string GridConnect = "param4";
        public const string BlackStartEnter = "param15";
        public const string BlackStartStatus = "param16";
        public const string FaultSummary = "param19";
        public const string AlarmSummary = "param23";
        public const string ProtectionSummary = "param27";
        public const string ClusterDropFault = "param31";
        public const string Voltage = "param39";
        public const string Current = "param40";
        public const string Insulation = "param42";
        public const string OnlineClusters = "param43";
        public const string TotalClusters = "param44";
        public const string MinParallelClusters = "param45";
        public const string Heartbeat = "param46";
        public const string Soc = "param47";
        public const string Soh = "param49";
        public const string ChargeRemain = "param50";
        public const string DischargeRemain = "param51";
        public const string MaxChargeCurrent = "param52";
        public const string MaxDischargeCurrent = "param53";
        public const string DischargePower = "param54";
        public const string ChargePower = "param55";
        public const string ClusterCurrentDiff = "param60";
        public const string ClusterVoltageDiff = "param65";
        public const string MaxCellVoltageRack = "param66";
        public const string MaxCellVoltagePack = "param67";
        public const string MaxCellVoltageCell = "param68";
        public const string MaxCellVoltage = "param69";
        public const string MinCellVoltageRack = "param70";
        public const string MinCellVoltagePack = "param71";
        public const string MinCellVoltageCell = "param72";
        public const string MinCellVoltage = "param73";
        public const string AvgCellVoltage = "param74";
        public const string MaxCellTempRack = "param76";
        public const string MaxCellTempPack = "param77";
        public const string MaxCellTempCell = "param78";
        public const string MaxCellTemp = "param79";
        public const string MinCellTempRack = "param80";
        public const string MinCellTempPack = "param81";
        public const string MinCellTempCell = "param82";
        public const string MinCellTemp = "param83";
        public const string AvgCellTemp = "param84";
        public const string ChargeEnergy = "param116";
        public const string DischargeEnergy = "param117";

        public static readonly string[] All =
        [
            PcsFaultStatus, RunMode, ChargeDischarge, GridConnect,
            BlackStartEnter, BlackStartStatus, FaultSummary, AlarmSummary, ProtectionSummary,
            ClusterDropFault, Voltage, Current, Insulation, OnlineClusters, TotalClusters,
            MinParallelClusters, Heartbeat, Soc, Soh, ChargeRemain, DischargeRemain,
            MaxChargeCurrent, MaxDischargeCurrent, DischargePower, ChargePower,
            ClusterCurrentDiff, ClusterVoltageDiff,
            MaxCellVoltageRack, MaxCellVoltagePack, MaxCellVoltageCell, MaxCellVoltage,
            MinCellVoltageRack, MinCellVoltagePack, MinCellVoltageCell, MinCellVoltage, AvgCellVoltage,
            MaxCellTempRack, MaxCellTempPack, MaxCellTempCell, MaxCellTemp,
            MinCellTempRack, MinCellTempPack, MinCellTempCell, MinCellTemp, AvgCellTemp,
            ChargeEnergy, DischargeEnergy
        ];

        /// <summary>按当前选型的 <c>bms_bank.csv</c> 解析逻辑点名；失败时回退 G2 Pro。</summary>
        public static LcBmsBankParams FromSelected()
        {
            try
            {
                return FromCsv(PointMapPathResolver.Resolve("bms_bank.csv"));
            }
            catch
            {
                return G2Pro();
            }
        }

        /// <summary>按 bank CSV 的 ModelSim 属性名（及少量描述兜底）解析逻辑点名。</summary>
        public static LcBmsBankParams FromCsv(string csvPath)
        {
            var rows = CSVUtil.CSV2Class<MapEntry>(csvPath) ?? new List<MapEntry>();
            var byProp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                string? prop = LastModelProperty(row.ModelSim);
                if (prop == null || string.IsNullOrWhiteSpace(row.ParamName))
                    continue;
                if (!byProp.ContainsKey(prop))
                    byProp[prop] = row.ParamName;
            }

            string Prop(string name) =>
                byProp.TryGetValue(name, out var param) ? param : "";

            string First(params string[] candidates)
            {
                foreach (var c in candidates)
                {
                    if (!string.IsNullOrWhiteSpace(c))
                        return c;
                }

                return "";
            }

            string Desc(Func<string, bool> match) =>
                rows.FirstOrDefault(r => match(r.Description ?? "") && !string.IsNullOrWhiteSpace(r.ParamName))
                    ?.ParamName ?? "";

            return new LcBmsBankParams
            {
                PcsFaultStatus = First(Prop("PcsFaultStatus"), Desc(d => d.Contains("对外故障"))),
                RunMode = Prop("OperationStatus"),
                ChargeDischarge = Prop("ChargeDischargeStatus"),
                GridConnect = Prop("GridConnectStatus"),
                BlackStartEnter = Prop("BlackStartEnterSuccess"),
                BlackStartStatus = Prop("BlackStartStatus"),
                FaultSummary = Prop("BMSFaultSummary"),
                AlarmSummary = Prop("BMSAlarmSummary"),
                ProtectionSummary = Prop("BMSProtectionSummary"),
                ClusterDropFault = First(Prop("ClusterDropFault"), Desc(d => d.Contains("掉簇"))),
                Voltage = Prop("TotalVoltage"),
                Current = Prop("Current"),
                Insulation = First(Prop("InsulationPlus"), Desc(d =>
                    d.Contains("绝缘电阻", StringComparison.Ordinal) ||
                    d.Contains("系统绝缘", StringComparison.Ordinal))),
                OnlineClusters = Prop("OnlineClusterCount"),
                TotalClusters = First(Prop("TotalClusterCount"), Desc(d =>
                    d.Contains("堆内总簇") || d.Contains("系统总簇"))),
                MinParallelClusters = First(Prop("MinParallelClusterCount"), Desc(d =>
                    d.Contains("最小并机") || d.Contains("最小并网"))),
                Heartbeat = Desc(d =>
                    d.Contains("心跳") && !d.Contains("40108")),
                Soc = Prop("SOC"),
                Soh = Prop("SOH"),
                ChargeRemain = Prop("AvailableChargeCapacity"),
                DischargeRemain = Prop("AvailableDischargeCapacity"),
                MaxChargeCurrent = Prop("MaxChargeCurrent"),
                MaxDischargeCurrent = Prop("MaxDischargeCurrent"),
                DischargePower = Prop("MaxDischargePower"),
                ChargePower = Prop("MaxChargePower"),
                ClusterCurrentDiff = Prop("ClusterCurrentDiff"),
                ClusterVoltageDiff = Prop("ClusterVoltageDiff"),
                MaxCellVoltageRack = Prop("MaxCellVoltageClusterId"),
                MaxCellVoltagePack = Prop("MaxCellVoltagePackId"),
                MaxCellVoltageCell = Prop("MaxCellVoltageCellId"),
                MaxCellVoltage = Prop("MaxCellVoltage"),
                MinCellVoltageRack = Prop("MinCellVoltageClusterId"),
                MinCellVoltagePack = Prop("MinCellVoltagePackId"),
                MinCellVoltageCell = Prop("MinCellVoltageCellId"),
                MinCellVoltage = Prop("MinCellVoltage"),
                AvgCellVoltage = Prop("AvgCellVoltage"),
                MaxCellTempRack = Prop("MaxCellTempClusterId"),
                MaxCellTempPack = Prop("MaxCellTempPackId"),
                MaxCellTempCell = Prop("MaxCellTempCellId"),
                MaxCellTemp = Prop("MaxCellTemp"),
                MinCellTempRack = Prop("MinCellTempClusterId"),
                MinCellTempPack = Prop("MinCellTempPackId"),
                MinCellTempCell = Prop("MinCellTempCellId"),
                MinCellTemp = Prop("MinCellTemp"),
                AvgCellTemp = Prop("AvgCellTemp"),
                ChargeEnergy = Prop("CumulativeChargeEnergy"),
                DischargeEnergy = Prop("CumulativeDischargeEnergy")
            };
        }

        private static string? LastModelProperty(string? modelSim)
        {
            var model = ModbusSimServer.GetModelParam(modelSim ?? "");
            var path = model?.Arg1;
            if (string.IsNullOrWhiteSpace(path))
                return null;
            int dot = path.LastIndexOf('.');
            return dot < 0 ? path.Trim() : path[(dot + 1)..].Trim();
        }

        public static LcBmsBankParams G2Pro() => new()
        {
            PcsFaultStatus = PcsFaultStatus,
            RunMode = RunMode,
            ChargeDischarge = ChargeDischarge,
            GridConnect = GridConnect,
            BlackStartEnter = BlackStartEnter,
            BlackStartStatus = BlackStartStatus,
            FaultSummary = FaultSummary,
            AlarmSummary = AlarmSummary,
            ProtectionSummary = ProtectionSummary,
            ClusterDropFault = ClusterDropFault,
            Voltage = Voltage,
            Current = Current,
            Insulation = Insulation,
            OnlineClusters = OnlineClusters,
            TotalClusters = TotalClusters,
            MinParallelClusters = MinParallelClusters,
            Heartbeat = Heartbeat,
            Soc = Soc,
            Soh = Soh,
            ChargeRemain = ChargeRemain,
            DischargeRemain = DischargeRemain,
            MaxChargeCurrent = MaxChargeCurrent,
            MaxDischargeCurrent = MaxDischargeCurrent,
            DischargePower = DischargePower,
            ChargePower = ChargePower,
            ClusterCurrentDiff = ClusterCurrentDiff,
            ClusterVoltageDiff = ClusterVoltageDiff,
            MaxCellVoltageRack = MaxCellVoltageRack,
            MaxCellVoltagePack = MaxCellVoltagePack,
            MaxCellVoltageCell = MaxCellVoltageCell,
            MaxCellVoltage = MaxCellVoltage,
            MinCellVoltageRack = MinCellVoltageRack,
            MinCellVoltagePack = MinCellVoltagePack,
            MinCellVoltageCell = MinCellVoltageCell,
            MinCellVoltage = MinCellVoltage,
            AvgCellVoltage = AvgCellVoltage,
            MaxCellTempRack = MaxCellTempRack,
            MaxCellTempPack = MaxCellTempPack,
            MaxCellTempCell = MaxCellTempCell,
            MaxCellTemp = MaxCellTemp,
            MinCellTempRack = MinCellTempRack,
            MinCellTempPack = MinCellTempPack,
            MinCellTempCell = MinCellTempCell,
            MinCellTemp = MinCellTemp,
            AvgCellTemp = AvgCellTemp,
            ChargeEnergy = ChargeEnergy,
            DischargeEnergy = DischargeEnergy
        };
    }

    /// <summary>BMS bank 逻辑量对应的协议 ParamName（随选型变化）。</summary>
    internal sealed class LcBmsBankParams
    {
        public string PcsFaultStatus { get; init; } = "";
        public string RunMode { get; init; } = "";
        public string ChargeDischarge { get; init; } = "";
        public string GridConnect { get; init; } = "";
        public string BlackStartEnter { get; init; } = "";
        public string BlackStartStatus { get; init; } = "";
        public string FaultSummary { get; init; } = "";
        public string AlarmSummary { get; init; } = "";
        public string ProtectionSummary { get; init; } = "";
        public string ClusterDropFault { get; init; } = "";
        public string Voltage { get; init; } = "";
        public string Current { get; init; } = "";
        public string Insulation { get; init; } = "";
        public string OnlineClusters { get; init; } = "";
        public string TotalClusters { get; init; } = "";
        public string MinParallelClusters { get; init; } = "";
        public string Heartbeat { get; init; } = "";
        public string Soc { get; init; } = "";
        public string Soh { get; init; } = "";
        public string ChargeRemain { get; init; } = "";
        public string DischargeRemain { get; init; } = "";
        public string MaxChargeCurrent { get; init; } = "";
        public string MaxDischargeCurrent { get; init; } = "";
        public string DischargePower { get; init; } = "";
        public string ChargePower { get; init; } = "";
        public string ClusterCurrentDiff { get; init; } = "";
        public string ClusterVoltageDiff { get; init; } = "";
        public string MaxCellVoltageRack { get; init; } = "";
        public string MaxCellVoltagePack { get; init; } = "";
        public string MaxCellVoltageCell { get; init; } = "";
        public string MaxCellVoltage { get; init; } = "";
        public string MinCellVoltageRack { get; init; } = "";
        public string MinCellVoltagePack { get; init; } = "";
        public string MinCellVoltageCell { get; init; } = "";
        public string MinCellVoltage { get; init; } = "";
        public string AvgCellVoltage { get; init; } = "";
        public string MaxCellTempRack { get; init; } = "";
        public string MaxCellTempPack { get; init; } = "";
        public string MaxCellTempCell { get; init; } = "";
        public string MaxCellTemp { get; init; } = "";
        public string MinCellTempRack { get; init; } = "";
        public string MinCellTempPack { get; init; } = "";
        public string MinCellTempCell { get; init; } = "";
        public string MinCellTemp { get; init; } = "";
        public string AvgCellTemp { get; init; } = "";
        public string ChargeEnergy { get; init; } = "";
        public string DischargeEnergy { get; init; } = "";
    }

    /// <summary>从 <c>simBms*</c> 解析后的工程值快照（已除以 bank 点表 Scale）。</summary>
    internal sealed class LcBmsBankSnap
    {
        public object Voltage { get; init; } = 0;
        public object Current { get; init; } = 0;
        public object Soc { get; init; } = 0;
        public object Soh { get; init; } = 0;
        public object Insulation { get; init; } = 0;
        public object ChargeRemainKwh { get; init; } = 0;
        public object DischargeRemainKwh { get; init; } = 0;
        public object MaxChargeCurrent { get; init; } = 0;
        public object MaxDischargeCurrent { get; init; } = 0;
        public object ClusterCurrentDiff { get; init; } = 0;
        public object ClusterVoltageDiff { get; init; } = 0;
        public object MaxCellVoltageRack { get; init; } = 0;
        public object MaxCellVoltagePack { get; init; } = 0;
        public object MaxCellVoltageCell { get; init; } = 0;
        public object MaxCellVoltage { get; init; } = 0;
        public object MinCellVoltageRack { get; init; } = 0;
        public object MinCellVoltagePack { get; init; } = 0;
        public object MinCellVoltageCell { get; init; } = 0;
        public object MinCellVoltage { get; init; } = 0;
        public object AvgCellVoltage { get; init; } = 0;
        public object MaxCellTempRack { get; init; } = 0;
        public object MaxCellTempPack { get; init; } = 0;
        public object MaxCellTempCell { get; init; } = 0;
        public object MaxCellTemp { get; init; } = 0;
        public object MinCellTempRack { get; init; } = 0;
        public object MinCellTempPack { get; init; } = 0;
        public object MinCellTempCell { get; init; } = 0;
        public object MinCellTemp { get; init; } = 0;
        public object AvgCellTemp { get; init; } = 0;
        public object ChargeEnergyKwh { get; init; } = 0;
        public object DischargeEnergyKwh { get; init; } = 0;
        public object OnlineClusters { get; init; } = 0;
        public object TotalClusters { get; init; } = 0;
        public object MinParallelClusters { get; init; } = 0;
        public object DischargePowerKw { get; init; } = 0;
        public object ChargePowerKw { get; init; } = 0;
        public object ProtectionSummary { get; init; } = 0;
        public object AlarmSummary { get; init; } = 0;
        public object FaultSummary { get; init; } = 0;
        public object PcsFaultStatus { get; init; } = 0;
        public object ClusterDropFault { get; init; } = 0;
        public object BlackStartEnter { get; init; } = 0;
        public object BlackStartStatus { get; init; } = 0;
        public object GridConnectStatus { get; init; } = 0;
        public object RunMode { get; init; } = 0;
        public object ChargeDischarge { get; init; } = 0;
        public object Heartbeat { get; init; } = 0;
    }

    /// <summary>组 n 对应的 BMS 从站：与该组各台 PCS 共用全局序号（simEmuK ↔ simBmsK）。</summary>
    internal static class LcBmsLayout
    {
        public static IReadOnlyList<string> ServerNamesForGroup(
            IReadOnlyList<EssUnitConfig>? units,
            int unitIndex0,
            EssUnitConfig? unit,
            int groupN)
        {
            if (groupN < 1)
                return Array.Empty<string>();
            int pcs = LcLayout.PcsCountInGroup(unit, groupN - 1);
            if (pcs <= 0)
                return Array.Empty<string>();

            var names = new List<string>(pcs);
            for (int k = 0; k < pcs; k++)
            {
                int flat = LcPcsIndex.Flat(unit, groupN - 1, k);
                if (!EmuProtocolLayout.TryFind(units, unitIndex0, flat, out var ep))
                    continue;
                names.Add($"simBms{ep.SimIndex1Based}");
            }

            return names;
        }

        public static string? ServerNameForGroup(
            IReadOnlyList<EssUnitConfig>? units,
            int unitIndex0,
            EssUnitConfig? unit,
            int groupN)
        {
            var names = ServerNamesForGroup(units, unitIndex0, unit, groupN);
            return names.Count == 0 ? null : names[0];
        }
    }

    /// <summary>把 BMS bank 工程值折成 LC Battery System 寄存器值。</summary>
    internal static class LcBmsTelemetry
    {
        public const int FaultWarnBit = 1 << 0;
        public const int FaultAlarmBit = 1 << 1;
        public const int FaultFaultBit = 1 << 2;
        public const int FaultNoChargeBit = 1 << 3;
        public const int FaultNoDischargeBit = 1 << 4;
        public const int FaultPcsBit = 1 << 5;
        public const int FaultCircuitBit = 1 << 6;

        public const int DryFaultBit = 1 << 0;
        public const int DryAlarmBit = 1 << 1;
        public const int DryWarnBit = 1 << 2;
        public const int DryOnlineBit = 1 << 3;

        public static int EncodeFaultWord(
            double protection,
            double alarm,
            double fault,
            int runMode,
            double pcsFault,
            double dropFault)
        {
            int word = 0;
            if (protection != 0) word |= FaultWarnBit;
            if (alarm != 0) word |= FaultAlarmBit;
            if (fault != 0) word |= FaultFaultBit;
            if (runMode == 1) word |= FaultNoChargeBit;
            if (runMode == 2) word |= FaultNoDischargeBit;
            if (pcsFault != 0) word |= FaultPcsBit;
            if (dropFault != 0) word |= FaultCircuitBit;
            return word;
        }

        public static int EncodeDryContact(double fault, double alarm, double protection, double onlineClusters)
        {
            int word = 0;
            if (fault != 0) word |= DryFaultBit;
            if (alarm != 0) word |= DryAlarmBit;
            if (protection != 0) word |= DryWarnBit;
            if (onlineClusters > 0) word |= DryOnlineBit;
            return word;
        }

        /// <summary>
        /// 组内多堆：电压/SOC/SOH/均温均压取平均，电流/能量/功率/簇数求和，故障字按位或，
        /// 极值与定位取最差堆。
        /// </summary>
        public static LcBmsBankSnap Aggregate(IReadOnlyList<LcBmsBankSnap> snaps)
        {
            if (snaps == null || snaps.Count == 0)
                return new LcBmsBankSnap();
            if (snaps.Count == 1)
                return snaps[0];

            int n = snaps.Count;
            int maxVIdx = 0;
            int minVIdx = 0;
            double maxV = Num(snaps[0].MaxCellVoltage);
            double minV = Num(snaps[0].MinCellVoltage);
            for (int i = 1; i < n; i++)
            {
                double vMax = Num(snaps[i].MaxCellVoltage);
                if (vMax > maxV)
                {
                    maxV = vMax;
                    maxVIdx = i;
                }

                double vMin = Num(snaps[i].MinCellVoltage);
                if (minV <= 0 || (vMin > 0 && vMin < minV))
                {
                    minV = vMin;
                    minVIdx = i;
                }
            }

            var hi = snaps[maxVIdx];
            var lo = snaps[minVIdx];
            return new LcBmsBankSnap
            {
                Voltage = Mean(snaps, s => s.Voltage),
                Current = Sum(snaps, s => s.Current),
                Soc = Mean(snaps, s => s.Soc),
                Soh = Mean(snaps, s => s.Soh),
                Insulation = MinPositive(snaps, s => s.Insulation),
                ChargeRemainKwh = Sum(snaps, s => s.ChargeRemainKwh),
                DischargeRemainKwh = Sum(snaps, s => s.DischargeRemainKwh),
                MaxChargeCurrent = Sum(snaps, s => s.MaxChargeCurrent),
                MaxDischargeCurrent = Sum(snaps, s => s.MaxDischargeCurrent),
                ClusterCurrentDiff = Max(snaps, s => s.ClusterCurrentDiff),
                ClusterVoltageDiff = Max(snaps, s => s.ClusterVoltageDiff),
                MaxCellVoltageRack = hi.MaxCellVoltageRack,
                MaxCellVoltagePack = hi.MaxCellVoltagePack,
                MaxCellVoltageCell = hi.MaxCellVoltageCell,
                MaxCellVoltage = hi.MaxCellVoltage,
                MinCellVoltageRack = lo.MinCellVoltageRack,
                MinCellVoltagePack = lo.MinCellVoltagePack,
                MinCellVoltageCell = lo.MinCellVoltageCell,
                MinCellVoltage = lo.MinCellVoltage,
                AvgCellVoltage = Mean(snaps, s => s.AvgCellVoltage),
                MaxCellTempRack = ArgMax(snaps, s => s.MaxCellTemp).MaxCellTempRack,
                MaxCellTempPack = ArgMax(snaps, s => s.MaxCellTemp).MaxCellTempPack,
                MaxCellTempCell = ArgMax(snaps, s => s.MaxCellTemp).MaxCellTempCell,
                MaxCellTemp = Max(snaps, s => s.MaxCellTemp),
                MinCellTempRack = ArgMinPositive(snaps, s => s.MinCellTemp).MinCellTempRack,
                MinCellTempPack = ArgMinPositive(snaps, s => s.MinCellTemp).MinCellTempPack,
                MinCellTempCell = ArgMinPositive(snaps, s => s.MinCellTemp).MinCellTempCell,
                MinCellTemp = MinPositive(snaps, s => s.MinCellTemp),
                AvgCellTemp = Mean(snaps, s => s.AvgCellTemp),
                ChargeEnergyKwh = Sum(snaps, s => s.ChargeEnergyKwh),
                DischargeEnergyKwh = Sum(snaps, s => s.DischargeEnergyKwh),
                OnlineClusters = Sum(snaps, s => s.OnlineClusters),
                TotalClusters = Sum(snaps, s => s.TotalClusters),
                MinParallelClusters = MinPositive(snaps, s => s.MinParallelClusters),
                DischargePowerKw = Sum(snaps, s => s.DischargePowerKw),
                ChargePowerKw = Sum(snaps, s => s.ChargePowerKw),
                ProtectionSummary = BitOr(snaps, s => s.ProtectionSummary),
                AlarmSummary = BitOr(snaps, s => s.AlarmSummary),
                FaultSummary = BitOr(snaps, s => s.FaultSummary),
                PcsFaultStatus = BitOr(snaps, s => s.PcsFaultStatus),
                ClusterDropFault = BitOr(snaps, s => s.ClusterDropFault),
                BlackStartEnter = Max(snaps, s => s.BlackStartEnter),
                BlackStartStatus = Max(snaps, s => s.BlackStartStatus),
                GridConnectStatus = Max(snaps, s => s.GridConnectStatus),
                RunMode = Max(snaps, s => s.RunMode),
                ChargeDischarge = Max(snaps, s => s.ChargeDischarge),
                Heartbeat = Max(snaps, s => s.Heartbeat)
            };
        }

        public static List<(string Param, object Value)> Collect(int n, LcBmsBankSnap? bank)
        {
            var b = bank ?? new LcBmsBankSnap();
            SplitU32(b.ChargeEnergyKwh, out int chgHi, out int chgLo);
            SplitU32(b.DischargeEnergyKwh, out int dchgHi, out int dchgLo);
            int runMode = (int)Math.Round(Num(b.RunMode));
            double protection = Num(b.ProtectionSummary);
            double alarm = Num(b.AlarmSummary);
            double fault = Num(b.FaultSummary);
            double online = Num(b.OnlineClusters);

            return
            [
                (LcBmsMap.TotalVoltage(n), b.Voltage),
                (LcBmsMap.TotalCurrent(n), b.Current),
                (LcBmsMap.Soc(n), b.Soc),
                (LcBmsMap.Soh(n), b.Soh),
                (LcBmsMap.Insulation(n), b.Insulation),
                (LcBmsMap.ChargeRemain(n), b.ChargeRemainKwh),
                (LcBmsMap.DischargeRemain(n), b.DischargeRemainKwh),
                (LcBmsMap.MaxChargeCurrent(n), b.MaxChargeCurrent),
                (LcBmsMap.MaxDischargeCurrent(n), b.MaxDischargeCurrent),
                (LcBmsMap.ClusterCurrentDiff(n), b.ClusterCurrentDiff),
                (LcBmsMap.ClusterVoltageDiff(n), b.ClusterVoltageDiff),
                (LcBmsMap.MaxCellVoltageRack(n), b.MaxCellVoltageRack),
                (LcBmsMap.MaxCellVoltagePack(n), b.MaxCellVoltagePack),
                (LcBmsMap.MaxCellVoltageCell(n), b.MaxCellVoltageCell),
                (LcBmsMap.MaxCellVoltage(n), VoltsToMilli(b.MaxCellVoltage)),
                (LcBmsMap.MinCellVoltageRack(n), b.MinCellVoltageRack),
                (LcBmsMap.MinCellVoltagePack(n), b.MinCellVoltagePack),
                (LcBmsMap.MinCellVoltageCell(n), b.MinCellVoltageCell),
                (LcBmsMap.MinCellVoltage(n), VoltsToMilli(b.MinCellVoltage)),
                (LcBmsMap.AvgCellVoltage(n), VoltsToMilli(b.AvgCellVoltage)),
                (LcBmsMap.MaxCellTempRack(n), b.MaxCellTempRack),
                (LcBmsMap.MaxCellTempPack(n), b.MaxCellTempPack),
                (LcBmsMap.MaxCellTempCell(n), b.MaxCellTempCell),
                (LcBmsMap.MaxCellTemp(n), b.MaxCellTemp),
                (LcBmsMap.MinCellTempRack(n), b.MinCellTempRack),
                (LcBmsMap.MinCellTempPack(n), b.MinCellTempPack),
                (LcBmsMap.MinCellTempCell(n), b.MinCellTempCell),
                (LcBmsMap.MinCellTemp(n), b.MinCellTemp),
                (LcBmsMap.AvgCellTemp(n), b.AvgCellTemp),
                (LcBmsMap.ChargeEnergyHigh(n), chgHi),
                (LcBmsMap.ChargeEnergyLow(n), chgLo),
                (LcBmsMap.DischargeEnergyHigh(n), dchgHi),
                (LcBmsMap.DischargeEnergyLow(n), dchgLo),
                (LcBmsMap.OnlineClusters(n), b.OnlineClusters),
                (LcBmsMap.TotalClusters(n), b.TotalClusters),
                (LcBmsMap.MinParallelClusters(n), b.MinParallelClusters),
                (LcBmsMap.DischargePower(n), b.DischargePowerKw),
                (LcBmsMap.ChargePower(n), b.ChargePowerKw),
                (LcBmsMap.FaultSummary(n), EncodeFaultWord(
                    protection, alarm, fault, runMode, Num(b.PcsFaultStatus), Num(b.ClusterDropFault))),
                (LcBmsMap.BlackStartMode(n), b.BlackStartEnter),
                (LcBmsMap.BlackStartStatus(n), b.BlackStartStatus),
                (LcBmsMap.GridConnect(n), b.GridConnectStatus),
                (LcBmsMap.RunMode(n), b.RunMode),
                (LcBmsMap.ChargeDischarge(n), b.ChargeDischarge),
                (LcBmsMap.DryContact(n), EncodeDryContact(fault, alarm, protection, online)),
                (LcBmsMap.Heartbeat(n), b.Heartbeat)
            ];
        }

        private static double Num(object? value) =>
            value == null ? 0 : ModbusValueConverter.ToDouble(value);

        private static double Mean(IReadOnlyList<LcBmsBankSnap> snaps, Func<LcBmsBankSnap, object> sel)
        {
            double sum = 0;
            int n = 0;
            foreach (var s in snaps)
            {
                double v = Num(sel(s));
                sum += v;
                n++;
            }

            return n == 0 ? 0 : sum / n;
        }

        private static double Sum(IReadOnlyList<LcBmsBankSnap> snaps, Func<LcBmsBankSnap, object> sel)
        {
            double sum = 0;
            foreach (var s in snaps)
                sum += Num(sel(s));
            return sum;
        }

        private static double Max(IReadOnlyList<LcBmsBankSnap> snaps, Func<LcBmsBankSnap, object> sel)
        {
            double max = double.NegativeInfinity;
            foreach (var s in snaps)
                max = Math.Max(max, Num(sel(s)));
            return double.IsNegativeInfinity(max) ? 0 : max;
        }

        private static double MinPositive(IReadOnlyList<LcBmsBankSnap> snaps, Func<LcBmsBankSnap, object> sel)
        {
            double min = double.PositiveInfinity;
            foreach (var s in snaps)
            {
                double v = Num(sel(s));
                if (v > 0)
                    min = Math.Min(min, v);
            }

            return double.IsPositiveInfinity(min) ? 0 : min;
        }

        private static double BitOr(IReadOnlyList<LcBmsBankSnap> snaps, Func<LcBmsBankSnap, object> sel)
        {
            long word = 0;
            foreach (var s in snaps)
                word |= (long)Math.Round(Num(sel(s)));
            return word;
        }

        private static LcBmsBankSnap ArgMax(IReadOnlyList<LcBmsBankSnap> snaps, Func<LcBmsBankSnap, object> sel)
        {
            int idx = 0;
            double max = Num(sel(snaps[0]));
            for (int i = 1; i < snaps.Count; i++)
            {
                double v = Num(sel(snaps[i]));
                if (v > max)
                {
                    max = v;
                    idx = i;
                }
            }

            return snaps[idx];
        }

        private static LcBmsBankSnap ArgMinPositive(IReadOnlyList<LcBmsBankSnap> snaps, Func<LcBmsBankSnap, object> sel)
        {
            int idx = 0;
            double min = Num(sel(snaps[0]));
            for (int i = 1; i < snaps.Count; i++)
            {
                double v = Num(sel(snaps[i]));
                if (min <= 0 || (v > 0 && v < min))
                {
                    min = v;
                    idx = i;
                }
            }

            return snaps[idx];
        }

        private static int VoltsToMilli(object? volts) =>
            (int)Math.Round(Num(volts) * 1000.0);

        private static void SplitU32(object? energy, out int high, out int low)
        {
            double raw = Num(energy);
            if (raw < 0)
                raw = 0;
            ulong u = (ulong)Math.Round(raw);
            high = (int)((u >> 16) & 0xFFFF);
            low = (int)(u & 0xFFFF);
        }
    }
}
