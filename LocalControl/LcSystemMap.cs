namespace EssSimulator.LocalControl
{
    /// <summary>
    /// 系统片段点名：与 <c>system/lc.csv</c> 的 <c>sysyc*</c> / <c>syst*</c> 对齐。
    /// 生产 LC 不走 CSV ModelSim，由 <see cref="LcSystemTelemetry"/> 按本单元仿真对象抄数/回写。
    /// </summary>
    internal static class LcSystemMap
    {
        public const string NominalPower = "sysyc100";
        public const string NominalEnergy = "sysyc102";
        public const string Soc = "sysyc104";
        public const string Soh = "sysyc105";
        public const string DetailedStatus = "sysyc106";
        public const string FaultSummary = "sysyc107";
        public const string RunStateSummary = "sysyc108";
        public const string ChargeEnergy = "sysyc109";
        public const string DischargeEnergy = "sysyc111";
        public const string MaxChargePower = "sysyc113";
        public const string MaxDischargePower = "sysyc115";
        public const string PcsSummary = "sysyc121";
        public const string PowerFactor = "sysyc122";
        public const string ApparentPower = "sysyc123";
        public const string ActivePower = "sysyc125";
        public const string ReactivePower = "sysyc127";
        public const string Frequency = "sysyc129";
        public const string DcPower = "sysyc131";
        public const string DcCurrent = "sysyc133";
        public const string DcVoltage = "sysyc135";
        public const string BlackStart = "sysyc170";
        public const string HvBreaker = "sysyc171";
        public const string PcsTotal = "sysyc200";
        public const string PcsRunning = "sysyc201";
        public const string PcsAlarm = "sysyc202";
        public const string PcsFault = "sysyc203";
        public const string BmsTotal = "sysyc204";
        public const string BmsRunning = "sysyc205";
        public const string BmsAlarm = "sysyc206";
        public const string BmsFault = "sysyc207";
        public const string Word218 = "sysyc218";
        public const string Word219 = "sysyc219";
        public const string Word220 = "sysyc220";
        public const string Word221 = "sysyc221";
        public const string Word222 = "sysyc222";
        public const string Word223 = "sysyc223";
        public const string Word224 = "sysyc224";
        public const string Word225 = "sysyc225";
        public const string Word226 = "sysyc226";
        public const string Word227 = "sysyc227";
        public const string Word228 = "sysyc228";

        public const string RemoteEnable = "syst4";
        public const string RemoteMode = "syst5";
        public const string SystemOp = "syst6";
        public const string BlackStartWrite = "syst7";
        public const string TargetP = "syst1010";
        public const string TargetQ = "syst1011";
        public const string Restart = "syst1399";

        public const int RestartPulse = 0xAA;
        public const int HvBreakerClosed = 0xAA;
        public const int HvBreakerOpen = 0xEE;
        public const int Word219MonitorBit = 1 << 8;
        public const int Word219EmsBit = 1 << 10;

        /// <summary>CSV ModelSim=0 会把寄存器初值写成 0；高压开关合法值只有 AA/EE，启动按缺省合闸写成 AA。</summary>
        public static void ApplyStartupDefaults(IDictionary<string, object> defaults)
        {
            defaults[HvBreaker] = (ushort)HvBreakerClosed;
        }

        /// <summary>LC 写点 → emuN.Emu.*；syst7 另有 PCS 批量下发。</summary>
        public static readonly LcSystemControlBinding[] EmuControls =
        {
            new(RemoteEnable, "RemoteControlEnable", false),
            new(RemoteMode, "RemoteControlMode", false),
            new(SystemOp, "SystemOperation", false),
            new(BlackStartWrite, "BlackStartModeWrite", true),
            new(TargetP, "TargetActivePower", false),
            new(TargetQ, "TargetReactivePower", false)
        };
    }

    internal readonly record struct LcSystemControlBinding(string Param, string EmuField, bool AsBool);

    /// <summary>本单元 EMU 虚拟模型快照。</summary>
    internal readonly record struct LcSystemEmuSnap(
        object Soc,
        object MaxChargePower,
        object MaxDischargePower,
        object OutputActivePower,
        object OutputReactivePower,
        object? HvBreakerAaEe);

    /// <summary>本单元一台 PCS。</summary>
    internal readonly record struct LcSystemPcsSnap(
        object PowerFactor,
        object ApparentPower,
        object BatteryPower,
        object BatteryCurrent,
        object Frequency,
        object BatteryVoltage,
        object? RatedPower = null,
        object? OperationStatus = null,
        object? AlarmSummary = null);

    /// <summary>本单元一台 BMS 堆（与 PCS 通道 1:1，全局编号 bms(base+i)）。</summary>
    internal readonly record struct LcSystemBmsSnap(
        object NominalEnergyKWh,
        object Soh,
        object AvailableChargeKWh,
        object AvailableDischargeKWh,
        object FaultSummary,
        object AlarmSummary,
        object OperationStatus);

    /// <summary>本单元一个组：Present=组内有 PCS；Faulted=组内任一台 OperationStatus=6。</summary>
    internal readonly record struct LcSystemGroupSnap(bool Present, bool Faulted);

    /// <summary>原 CSV plugin 点：故障总/精简状态/黑启动/PCS 台数统计。</summary>
    internal readonly record struct LcSystemPluginSnap(
        object FaultSummary,
        object RunStateSummary,
        object BlackStart,
        object PcsTotal,
        object PcsRunning,
        object PcsAlarm,
        object PcsFault);

    /// <summary>把本单元 EMU + 实际 PcsList/BMS 折成 system 片段寄存器值。</summary>
    internal static class LcSystemTelemetry
    {
        public static bool IsRestartPulse(double value) =>
            (int)Math.Round(value) == LcSystemMap.RestartPulse;

        /// <summary>
        /// sysyc104 Scale=1000 且为 u16：工程值必须是 0–1。
        /// 大于 1 的输入视为百分数（PcsMapper 曾写 0–100）。
        /// </summary>
        public static double NormalizeSoc(double soc)
        {
            if (double.IsNaN(soc) || double.IsInfinity(soc))
                return 0;
            double x = soc;
            if (Math.Abs(x) > 1.0 + 1e-9)
                x /= 100.0;
            return Math.Clamp(x, 0, 1);
        }

        /// <summary>
        /// 高压开关状态：合闸/动作=0xAA，分闸/复归=0xEE。0 不是协议合法值。
        /// 接受 Closed(0/1) 或已编码的 AA/EE。
        /// </summary>
        public static int EncodeHvStatus(object? closed) =>
            Num(closed) is 0xAA or 1 ? LcSystemMap.HvBreakerClosed : LcSystemMap.HvBreakerOpen;

        public static List<(string Param, object Value)> Collect(
            LcSystemEmuSnap emu,
            IReadOnlyList<LcSystemPcsSnap> pcs,
            LcSystemPluginSnap plugins) =>
            Collect(emu, pcs, plugins, Array.Empty<LcSystemBmsSnap>(), Array.Empty<LcSystemGroupSnap>(), live: false);

        public static List<(string Param, object Value)> Collect(
            LcSystemEmuSnap emu,
            IReadOnlyList<LcSystemPcsSnap> pcs,
            LcSystemPluginSnap plugins,
            IReadOnlyList<LcSystemBmsSnap> bms,
            IReadOnlyList<LcSystemGroupSnap> groups,
            bool live)
        {
            var first = pcs.Count > 0 ? pcs[0] : (LcSystemPcsSnap?)null;
            double apparent = 0, dcPower = 0, dcCurrent = 0, rated = 0;
            foreach (var m in pcs)
            {
                apparent += Num(m.ApparentPower);
                dcPower += Num(m.BatteryPower);
                dcCurrent += Num(m.BatteryCurrent);
                rated += Num(m.RatedPower);
            }

            double energy = 0, sohSum = 0, charge = 0, discharge = 0;
            int bmsRunning = 0, bmsAlarm = 0, bmsFault = 0;
            foreach (var stack in bms)
            {
                energy += Num(stack.NominalEnergyKWh);
                sohSum += Num(stack.Soh);
                charge += Num(stack.AvailableChargeKWh);
                discharge += Num(stack.AvailableDischargeKWh);
                bool fault = Num(stack.FaultSummary) != 0;
                bool alarm = !fault && Num(stack.AlarmSummary) != 0;
                bool shutdown = (int)Math.Round(Num(stack.OperationStatus)) == 4;
                if (fault) bmsFault++;
                else if (alarm) bmsAlarm++;
                if (!fault && !shutdown) bmsRunning++;
            }

            return new List<(string, object)>
            {
                (LcSystemMap.NominalPower, rated),
                (LcSystemMap.NominalEnergy, energy),
                (LcSystemMap.Soc, NormalizeSoc(Num(emu.Soc))),
                (LcSystemMap.Soh, bms.Count == 0 ? 0 : sohSum / bms.Count),
                (LcSystemMap.DetailedStatus, EncodeDetailedStatus(pcs)),
                (LcSystemMap.FaultSummary, plugins.FaultSummary ?? 0),
                (LcSystemMap.RunStateSummary, plugins.RunStateSummary ?? 0),
                (LcSystemMap.ChargeEnergy, charge),
                (LcSystemMap.DischargeEnergy, discharge),
                (LcSystemMap.MaxChargePower, emu.MaxChargePower ?? 0),
                (LcSystemMap.MaxDischargePower, emu.MaxDischargePower ?? 0),
                (LcSystemMap.PcsSummary, EncodePcsSummary(pcs)),
                (LcSystemMap.PowerFactor, first?.PowerFactor ?? 0),
                (LcSystemMap.ApparentPower, apparent),
                (LcSystemMap.ActivePower, emu.OutputActivePower ?? 0),
                (LcSystemMap.ReactivePower, emu.OutputReactivePower ?? 0),
                (LcSystemMap.Frequency, first?.Frequency ?? 0),
                (LcSystemMap.DcPower, dcPower),
                (LcSystemMap.DcCurrent, dcCurrent),
                (LcSystemMap.DcVoltage, first?.BatteryVoltage ?? 0),
                (LcSystemMap.BlackStart, plugins.BlackStart ?? 0),
                (LcSystemMap.HvBreaker, EncodeHvStatus(emu.HvBreakerAaEe)),
                (LcSystemMap.PcsTotal, plugins.PcsTotal ?? 0),
                (LcSystemMap.PcsRunning, plugins.PcsRunning ?? 0),
                (LcSystemMap.PcsAlarm, plugins.PcsAlarm ?? 0),
                (LcSystemMap.PcsFault, plugins.PcsFault ?? 0),
                (LcSystemMap.BmsTotal, bms.Count),
                (LcSystemMap.BmsRunning, bmsRunning),
                (LcSystemMap.BmsAlarm, bmsAlarm),
                (LcSystemMap.BmsFault, bmsFault),
                (LcSystemMap.Word218, CommsBits(pcs.Count, 16)),
                (LcSystemMap.Word219, live ? LcSystemMap.Word219MonitorBit | LcSystemMap.Word219EmsBit : 0),
                (LcSystemMap.Word220, CommsBits(bms.Count, 10)),
                (LcSystemMap.Word221, GroupBits(groups, 0, 16)),
                (LcSystemMap.Word222, GroupBits(groups, 16, 8)),
                (LcSystemMap.Word223, 0),
                (LcSystemMap.Word224, 0),
                (LcSystemMap.Word225, 0),
                (LcSystemMap.Word226, 0),
                (LcSystemMap.Word227, 0),
                (LcSystemMap.Word228, pcs.Count <= 2 ? CommsBits(pcs.Count, 2) : 0)
            };
        }

        /// <summary>
        /// 详细版：仿真无启停暂态。故障→5；告警→6；充/放电运行→3；全待机→4；全停机→1；无 PCS→0。
        /// </summary>
        internal static int EncodeDetailedStatus(IReadOnlyList<LcSystemPcsSnap> pcs)
        {
            if (pcs.Count == 0)
                return 0;
            bool anyFault = false, anyAlarm = false, anyPq = false, anyStandby = false, anyStop = false;
            foreach (var m in pcs)
            {
                int st = Status(m);
                if (st == 6) anyFault = true;
                else if (Num(m.AlarmSummary) != 0) anyAlarm = true;
                if (st is 4 or 5) anyPq = true;
                else if (st == 2) anyStandby = true;
                else if (st == 1) anyStop = true;
            }

            if (anyFault) return 5;
            if (anyAlarm) return 6;
            if (anyPq) return 3;
            if (anyStandby) return 4;
            if (anyStop) return 1;
            return 0;
        }

        /// <summary>0 初始；4 任一故障；3 任一充/放电；2 全部待机；1 全部关闭。</summary>
        internal static int EncodePcsSummary(IReadOnlyList<LcSystemPcsSnap> pcs)
        {
            if (pcs.Count == 0)
                return 0;
            bool anyFault = false, anyPq = false, allStandby = true, allOff = true;
            foreach (var m in pcs)
            {
                int st = Status(m);
                if (st == 6) anyFault = true;
                if (st is 4 or 5) anyPq = true;
                if (st != 2) allStandby = false;
                if (st != 1) allOff = false;
            }

            if (anyFault) return 4;
            if (anyPq) return 3;
            if (allStandby) return 2;
            if (allOff) return 1;
            return anyPq ? 3 : 2;
        }

        internal static int CommsBits(int count, int maxBits)
        {
            int n = Math.Clamp(count, 0, maxBits);
            return n == 0 ? 0 : (1 << n) - 1;
        }

        internal static int GroupBits(IReadOnlyList<LcSystemGroupSnap> groups, int offset, int bitCount)
        {
            int word = 0;
            for (int i = 0; i < bitCount; i++)
            {
                int g = offset + i;
                if (g >= groups.Count)
                    break;
                if (groups[g].Present && !groups[g].Faulted)
                    word |= 1 << i;
            }

            return word;
        }

        private static int Status(LcSystemPcsSnap m) => (int)Math.Round(Num(m.OperationStatus));

        private static double Num(object? value) =>
            value == null ? 0 : ModbusValueConverter.ToDouble(value);
    }
}
