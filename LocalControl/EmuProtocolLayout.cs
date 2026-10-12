using EssSimulator.Configuration;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// EMU 协议从站布局：一台 PCS 一路。每台 PCS 一张单槽点表，绑
    /// <c>emuN.PcsList[pcsIndex]</c>；<c>simEmu</c> 按全局 PCS 顺序编号（与 pcsN 1:1）。
    /// 断路器不进 EMU 点表，由 LC 直连储能单元模型。
    /// </summary>
    internal static class EmuProtocolLayout
    {
        public readonly record struct Endpoint(int SimIndex1Based, int UnitIndex0, int PcsIndex0)
        {
            public string ServerName => $"simEmu{SimIndex1Based}";
            public int UnitId => UnitIndex0 + 1;
        }

        public static IReadOnlyList<Endpoint> Enumerate(SimulatorConfig cfg) =>
            Enumerate(cfg.ResolveEssUnitsOrFallback());

        public static IReadOnlyList<Endpoint> Enumerate(IReadOnlyList<EssUnitConfig>? units)
        {
            if (units == null || units.Count == 0)
                return new[] { new Endpoint(1, 0, 0) };

            var list = new List<Endpoint>();
            int sim = 1;
            for (int u = 0; u < units.Count; u++)
            {
                int pcs = Math.Max(1, units[u].PcsCount);
                for (int p = 0; p < pcs; p++)
                    list.Add(new Endpoint(sim++, u, p));
            }

            return list;
        }

        public static int Count(SimulatorConfig cfg) => Enumerate(cfg).Count;

        public static bool TryFind(IReadOnlyList<EssUnitConfig>? units, int unitIndex0, int pcsIndex0, out Endpoint endpoint)
        {
            foreach (var e in Enumerate(units))
            {
                if (e.UnitIndex0 == unitIndex0 && e.PcsIndex0 == pcsIndex0)
                {
                    endpoint = e;
                    return true;
                }
            }

            endpoint = default;
            return false;
        }

        public static string ServerNameFor(IReadOnlyList<EssUnitConfig>? units, int unitIndex0, int pcsIndex0) =>
            TryFind(units, unitIndex0, pcsIndex0, out var e) ? e.ServerName : $"simEmu{unitIndex0 + 1}";

        /// <summary>单元内扁平槽位 → 组序号与组内槽位。</summary>
        public static bool TryMapUnitSlot(EssUnitConfig? unit, int slotInUnit0, out int groupIndex0, out int slotInGroup)
        {
            groupIndex0 = 0;
            slotInGroup = slotInUnit0;
            if (slotInUnit0 < 0)
                return false;

            if (unit is { HasGroups: true })
            {
                int acc = 0;
                for (int g = 0; g < unit.Groups.Count; g++)
                {
                    int n = unit.Groups[g].PcsCount;
                    if (slotInUnit0 < acc + n)
                    {
                        groupIndex0 = g;
                        slotInGroup = slotInUnit0 - acc;
                        return true;
                    }

                    acc += n;
                }

                return false;
            }

            int pcs = unit?.PcsCount ?? 2;
            return slotInUnit0 < pcs;
        }
    }
}
