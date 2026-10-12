namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>
    /// 单元 → PCS 通道布局辅助：各单元下属 PCS 台数可配置（默认每单元 2 台）。
    /// 供电气求解器/离网逻辑在仅有设备列表、无 ESS 实例的静态上下文中推算通道范围。
    /// </summary>
    public static class PcsUnitLayout
    {
        /// <summary>指定单元第一台 PCS 的全局通道索引（0 基）。</summary>
        public static int BaseIndexOfUnit(IReadOnlyList<int>? pcsPerUnit, int unit)
        {
            if (pcsPerUnit == null)
                return unit * 2;
            int baseIdx = 0;
            for (int u = 0; u < unit && u < pcsPerUnit.Count; u++)
                baseIdx += pcsPerUnit[u];
            return baseIdx;
        }

        /// <summary>指定单元下属 PCS 台数；布局缺失时回退 2。</summary>
        public static int CountOfUnit(IReadOnlyList<int>? pcsPerUnit, int unit) =>
            pcsPerUnit != null && unit >= 0 && unit < pcsPerUnit.Count ? pcsPerUnit[unit] : 2;

        /// <summary>指定单元的 PCS 通道范围 [base, base+count)。</summary>
        public static (int BaseIndex, int Count) RangeOfUnit(IReadOnlyList<int>? pcsPerUnit, int unit) =>
            (BaseIndexOfUnit(pcsPerUnit, unit), CountOfUnit(pcsPerUnit, unit));

        /// <summary>PCS 全局通道索引（0 基）所属单元；布局缺失时回退每单元 2 台。</summary>
        public static int UnitIndexOf(IReadOnlyList<int>? pcsPerUnit, int channel)
        {
            if (pcsPerUnit == null || pcsPerUnit.Count == 0)
                return Math.Max(0, channel) / 2;
            int baseIdx = 0;
            for (int u = 0; u < pcsPerUnit.Count; u++)
            {
                if (channel < baseIdx + pcsPerUnit[u])
                    return u;
                baseIdx += pcsPerUnit[u];
            }
            return pcsPerUnit.Count - 1;
        }

        /// <summary>PCS 全局通道索引在所属单元内的槽位（0 基）。</summary>
        public static int SlotOfChannel(IReadOnlyList<int>? pcsPerUnit, int channel)
        {
            int unit = UnitIndexOf(pcsPerUnit, channel);
            return channel - BaseIndexOfUnit(pcsPerUnit, unit);
        }
    }
}
