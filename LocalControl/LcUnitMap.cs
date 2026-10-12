namespace EssSimulator.LocalControl
{
    /// <summary>
    /// unit 片段布局：<c>n</c> 是 EMU 内 PCS 组号；模块 1/2 是该组第一台 PCS 的两条支路。
    /// </summary>
    internal readonly record struct LcUnitFragmentLayout(
        string Id,
        string ParamPrefix,
        int AddressBase,
        int AddressStride,
        int ParamStride,
        int PairCount)
    {
        public int Address(int n, int offset) => AddressBase + AddressStride * (n - 1) + offset;

        public string Param(int n, int offset) => $"{ParamPrefix}{offset + ParamStride * (n - 1)}";

        public string BatteryVoltage(int n, int module) => Param(n, module == 0 ? 0 : 6);
        public string BatteryCurrent(int n, int module) => Param(n, module == 0 ? 1 : 7);
        public string BusVoltage(int n, int module) => Param(n, module == 0 ? 2 : 8);
        public string InductorR(int n, int module) => Param(n, module == 0 ? 3 : 9);
        public string InductorS(int n, int module) => Param(n, module == 0 ? 4 : 10);
        public string InductorT(int n, int module) => Param(n, module == 0 ? 5 : 11);
        public string BatteryPower(int n, int module) => Param(n, module == 0 ? 16 : 17);
        public string WarningWord1(int n, int module) => Param(n, module == 0 ? 231 : 245);
        public string WarningWord2(int n, int module) => Param(n, module == 0 ? 232 : 246);
        public string AcCurrentR(int n) => Param(n, 12);
        public string AcCurrentS(int n) => Param(n, 13);
        public string AcCurrentT(int n) => Param(n, 14);
        public string BatteryPowerTotal(int n) => Param(n, 15);
        public string GridActivePower(int n) => Param(n, 18);
        public string GridReactivePower(int n) => Param(n, 19);
        public string GridVoltageRs(int n) => Param(n, 20);
        public string GridVoltageSt(int n) => Param(n, 21);
        public string GridVoltageTr(int n) => Param(n, 22);
        public string OverTempNtc(int n) => Param(n, 23);
        public string AvailableCapacity(int n) => Param(n, 24);
        public string RatedCapacity(int n) => Param(n, 25);
    }

    /// <summary>
    /// 10MW / 5.5MW 单元片段点名。两段始终共存：10MW 写 n=1,2；5.5MW 只写 n=1。
    /// </summary>
    internal static class LcUnitMap
    {
        public static readonly LcUnitFragmentLayout TenMw = new(
            "unit_10MW", "unit_param", AddressBase: 2600, AddressStride: 300, ParamStride: 600, PairCount: 2);

        public static readonly LcUnitFragmentLayout FiveFiveMw = new(
            "unit_5.5MW", "unit1_param", AddressBase: 5000, AddressStride: 600, ParamStride: 600, PairCount: 1);

        public static readonly LcUnitFragmentLayout[] Fragments = { TenMw, FiveFiveMw };

        public const int AddressBase = 2600;
        public const int AddressStride = 300;
        public const int ParamStride = 600;

        public static int Address(int n, int offset) => TenMw.Address(n, offset);
        public static string Param(int n, int offset) => TenMw.Param(n, offset);
        public static string BatteryVoltage(int n, int module) => TenMw.BatteryVoltage(n, module);
        public static string BatteryCurrent(int n, int module) => TenMw.BatteryCurrent(n, module);
        public static string BusVoltage(int n, int module) => TenMw.BusVoltage(n, module);
        public static string InductorR(int n, int module) => TenMw.InductorR(n, module);
        public static string InductorS(int n, int module) => TenMw.InductorS(n, module);
        public static string InductorT(int n, int module) => TenMw.InductorT(n, module);
        public static string BatteryPower(int n, int module) => TenMw.BatteryPower(n, module);
        public static string WarningWord1(int n, int module) => TenMw.WarningWord1(n, module);
        public static string WarningWord2(int n, int module) => TenMw.WarningWord2(n, module);
        public static string AcCurrentR(int n) => TenMw.AcCurrentR(n);
        public static string AcCurrentS(int n) => TenMw.AcCurrentS(n);
        public static string AcCurrentT(int n) => TenMw.AcCurrentT(n);
        public static string BatteryPowerTotal(int n) => TenMw.BatteryPowerTotal(n);
        public static string GridActivePower(int n) => TenMw.GridActivePower(n);
        public static string GridReactivePower(int n) => TenMw.GridReactivePower(n);
        public static string GridVoltageRs(int n) => TenMw.GridVoltageRs(n);
        public static string GridVoltageSt(int n) => TenMw.GridVoltageSt(n);
        public static string GridVoltageTr(int n) => TenMw.GridVoltageTr(n);
        public static string OverTempNtc(int n) => TenMw.OverTempNtc(n);
        public static string AvailableCapacity(int n) => TenMw.AvailableCapacity(n);
        public static string RatedCapacity(int n) => TenMw.RatedCapacity(n);
    }

    /// <summary>组内一条 PCS 支路（点表「模块1/2」）快照。</summary>
    internal readonly record struct LcUnitModuleSnap(
        object BatteryVoltage,
        object BatteryCurrent,
        object LineVoltageAb,
        object LineVoltageBc,
        object LineVoltageCa,
        object InductorR,
        object InductorS,
        object InductorT,
        object BatteryPower,
        object IgbtMaxTemp,
        object AvailableCapacity,
        object RatedPower,
        object WarningWord1,
        object WarningWord2,
        object ActivePower,
        object ReactivePower);

    /// <summary>把 PCS 组内前两条支路（一台 PCS）折成 unit 片段寄存器值。</summary>
    internal static class LcUnitTelemetry
    {
        public static List<(string Param, object Value)> Collect(
            int n,
            LcUnitModuleSnap? module1,
            LcUnitModuleSnap? module2) =>
            Collect(LcUnitMap.TenMw, n, module1, module2);

        public static List<(string Param, object Value)> Collect(
            LcUnitFragmentLayout layout,
            int n,
            LcUnitModuleSnap? module1,
            LcUnitModuleSnap? module2)
        {
            var pts = new List<(string, object)>(32);
            WriteModule(pts, layout, n, 0, module1);
            WriteModule(pts, layout, n, 1, module2);

            double p1 = Num(module1?.BatteryPower);
            double p2 = Num(module2?.BatteryPower);
            pts.Add((layout.BatteryPowerTotal(n), p1 + p2));
            pts.Add((layout.OverTempNtc(n), MaxPresent(module1?.IgbtMaxTemp, module2?.IgbtMaxTemp)));
            pts.Add((layout.AvailableCapacity(n), Num(module1?.AvailableCapacity) + Num(module2?.AvailableCapacity)));
            pts.Add((layout.RatedCapacity(n), Num(module1?.RatedPower) + Num(module2?.RatedPower)));

            pts.Add((layout.AcCurrentR(n), Abs(module1?.InductorR) + Abs(module2?.InductorR)));
            pts.Add((layout.AcCurrentS(n), Abs(module1?.InductorS) + Abs(module2?.InductorS)));
            pts.Add((layout.AcCurrentT(n), Abs(module1?.InductorT) + Abs(module2?.InductorT)));
            pts.Add((layout.GridActivePower(n), Num(module1?.ActivePower) + Num(module2?.ActivePower)));
            pts.Add((layout.GridReactivePower(n), Num(module1?.ReactivePower) + Num(module2?.ReactivePower)));
            pts.Add((layout.GridVoltageRs(n), MeanPresent(module1?.LineVoltageAb, module2?.LineVoltageAb)));
            pts.Add((layout.GridVoltageSt(n), MeanPresent(module1?.LineVoltageBc, module2?.LineVoltageBc)));
            pts.Add((layout.GridVoltageTr(n), MeanPresent(module1?.LineVoltageCa, module2?.LineVoltageCa)));
            return pts;
        }

        private static void WriteModule(
            List<(string, object)> pts,
            LcUnitFragmentLayout layout,
            int n,
            int module,
            LcUnitModuleSnap? snap)
        {
            pts.Add((layout.BatteryVoltage(n, module), snap?.BatteryVoltage ?? 0));
            pts.Add((layout.BatteryCurrent(n, module), snap?.BatteryCurrent ?? 0));
            pts.Add((layout.BusVoltage(n, module), snap == null ? 0 : EquivalentLineVoltage(snap.Value)));
            pts.Add((layout.InductorR(n, module), Abs(snap?.InductorR)));
            pts.Add((layout.InductorS(n, module), Abs(snap?.InductorS)));
            pts.Add((layout.InductorT(n, module), Abs(snap?.InductorT)));
            pts.Add((layout.BatteryPower(n, module), snap?.BatteryPower ?? 0));
            pts.Add((layout.WarningWord1(n, module), snap?.WarningWord1 ?? 0));
            pts.Add((layout.WarningWord2(n, module), snap?.WarningWord2 ?? 0));
        }

        /// <summary>三线电压等效线电压 √((Uab²+Ubc²+Uca²)/3)；平衡时等于任一线电压。</summary>
        internal static double EquivalentLineVoltage(LcUnitModuleSnap snap)
        {
            double ab = Num(snap.LineVoltageAb);
            double bc = Num(snap.LineVoltageBc);
            double ca = Num(snap.LineVoltageCa);
            return Math.Sqrt((ab * ab + bc * bc + ca * ca) / 3.0);
        }

        private static double Abs(object? value) => Math.Abs(Num(value));

        private static double Num(object? value) =>
            value == null ? 0 : ModbusValueConverter.ToDouble(value);

        private static double MaxPresent(object? a, object? b)
        {
            bool hasA = a != null;
            bool hasB = b != null;
            if (!hasA && !hasB)
                return 0;
            if (!hasA)
                return Num(b);
            if (!hasB)
                return Num(a);
            return Math.Max(Num(a), Num(b));
        }

        private static double MeanPresent(object? a, object? b)
        {
            bool hasA = a != null;
            bool hasB = b != null;
            if (!hasA && !hasB)
                return 0;
            if (!hasA)
                return Num(b);
            if (!hasB)
                return Num(a);
            return (Num(a) + Num(b)) / 2.0;
        }
    }
}
