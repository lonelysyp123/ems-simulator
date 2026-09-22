namespace EssSimulator.LocalControl
{
    /// <summary>组级通道点名：与 <c>group/lc.csv</c> 的 <c>group_param{...}</c> 公式对齐。</summary>
    internal static class LcChannelMap
    {
        public static string Fault(int n, int k) => $"group_param{4 + 28 * (n - 1) + k}";
        public static string Alarm(int n, int k) => $"group_param{8 + 28 * (n - 1) + k}";
        public static string Status(int n, int k) => $"group_param{12 + 28 * (n - 1) + k}";
        public static string Freq(int n, int k) => $"group_param{16 + 28 * (n - 1) + k}";
        public static string Vab(int n, int k) => $"group_param{20 + 28 * (n - 1) + 3 * k}";
        public static string Vbc(int n, int k) => $"group_param{21 + 28 * (n - 1) + 3 * k}";
        public static string Vca(int n, int k) => $"group_param{22 + 28 * (n - 1) + 3 * k}";
        public static string StartStop(int n, int k) => $"group_param{60 + 20 * (n - 1) + k}";
        public static string ActivePowerSet(int n, int k) => $"group_param{64 + 20 * (n - 1) + k}";
        public static string ReactivePowerSet(int n, int k) => $"group_param{68 + 20 * (n - 1) + k}";
        public static string IslandV(int n, int k) => $"group_param{72 + 20 * (n - 1) + k}";
        public static string IslandF(int n, int k) => $"group_param{76 + 20 * (n - 1) + k}";
        public static string ExtraActivePower(int n, int k) => $"group_param{102 + 8 * (n - 1) + k}";
        public static string ExtraReactivePower(int n, int k) => $"group_param{106 + 8 * (n - 1) + k}";
        public static string Ready(int n, int k) => $"group_param{118 + 4 * (n - 1) + k}";
    }

    /// <summary>组内槽位到本单元扁平 PcsList 下标。</summary>
    internal static class LcPcsIndex
    {
        public static int Flat(EssSimulator.Configuration.EssUnitConfig? unit, int groupIndexZeroBased, int slot)
        {
            if (unit is { HasGroups: true })
            {
                int acc = 0;
                for (int g = 0; g < groupIndexZeroBased && g < unit.Groups.Count; g++)
                    acc += unit.Groups[g].PcsCount;
                return acc + slot;
            }

            return slot;
        }
    }
}
