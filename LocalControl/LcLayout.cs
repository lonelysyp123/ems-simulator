using EssSimulator.Configuration;

namespace EssSimulator.LocalControl
{
    /// <summary>LC 按 PCS 组展开：无 <c>emu_group</c> 时视为 1 个隐式组；点表至少展开 2 组（缺组空槽）。</summary>
    internal static class LcLayout
    {
        /// <summary>标准片段每个 PCS 组的直流通道槽位数（pcs1–pcs4，对应组内最多 2 台 PCS / 4 条支路）。</summary>
        public const int TemplateSlotsPerGroup = 4;

        /// <summary>一个 EMU 固定两个 PCS 组；缺组时点表仍展开、寄存器为 0。</summary>
        public const int PcsGroupsPerEmu = 2;

        /// <summary>LC 片段按组展开的安全上限：组 ≥21 时单元遥测与组遥测、组遥控与 mv_param1 地址冲突。</summary>
        public const int MaxGroupCount = 20;

        /// <summary>互斥 EMU 直控表只覆盖 2 路 PCS 支路；台数更多时回退拼装片段。</summary>
        public const int ExclusiveEmuPcsLimit = 2;

        public static int GroupCount(EssUnitConfig? unit) =>
            unit is { HasGroups: true } ? Math.Max(1, unit.Groups.Count) : 1;

        /// <summary>拼装/从站展开组数：至少 <see cref="PcsGroupsPerEmu"/>。</summary>
        public static int ExpandGroupCount(EssUnitConfig? unit) =>
            Math.Max(PcsGroupsPerEmu, GroupCount(unit));

        public static int GroupCountForUnit(SimulatorConfig cfg, int unitIndex)
        {
            var devices = cfg.Devices;
            if (devices == null || unitIndex < 0 || unitIndex >= devices.Count)
                return 1;
            return GroupCount(devices[unitIndex]);
        }

        public static int ExpandGroupCountForUnit(SimulatorConfig cfg, int unitIndex)
        {
            var devices = cfg.Devices;
            if (devices == null || unitIndex < 0 || unitIndex >= devices.Count)
                return PcsGroupsPerEmu;
            return ExpandGroupCount(devices[unitIndex]);
        }

        /// <summary>组内实际 PCS 支路数；超出模板槽位的通道保持默认 0。</summary>
        public static int PcsCountInGroup(EssUnitConfig? unit, int groupIndexZeroBased)
        {
            if (unit is { HasGroups: true })
            {
                if (groupIndexZeroBased < 0 || groupIndexZeroBased >= unit.Groups.Count)
                    return 0;
                return unit.Groups[groupIndexZeroBased].PcsCount;
            }

            // 扁平或无 Devices：与 emu/standard 两槽直控表对齐
            return groupIndexZeroBased == 0 ? (unit?.PcsCount ?? 2) : 0;
        }

        /// <summary>本单元各组中最大 PCS 支路数；无构成时为 0。</summary>
        public static int MaxPcsInAnyGroup(EssUnitConfig? unit)
        {
            if (unit == null)
                return 0;
            int groups = GroupCount(unit);
            int max = 0;
            for (int i = 0; i < groups; i++)
                max = Math.Max(max, PcsCountInGroup(unit, i));
            return max;
        }

        /// <summary>未知台数（0）或不超过 2 条支路时可用互斥 EMU 直控表。</summary>
        public static bool ShouldUseExclusiveEmuMap(int unitPcsCount) =>
            unitPcsCount <= ExclusiveEmuPcsLimit;
    }
}
