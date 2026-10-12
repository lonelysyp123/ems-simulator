using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Web.Topology
{
    /// <summary>
    /// 组态保存时不再改 LC 选型（LC 由片段拼装）。
    /// 仅把过期的 emu=trina_* 回退为 standard。
    /// </summary>
    public static class LcPointMapAutoSelect
    {
        public const string LcTypeId = "lc";
        public const string EmuTypeId = "emu";
        public const string EmuStandardModelId = "standard";

        private static readonly HashSet<string> MovedFromEmuModelIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "trina_5.5MW",
            "trina_10MW"
        };

        /// <summary>LC 不再按 PCS 台数选型。</summary>
        public static string? ResolveModelId(int pcsCount)
        {
            _ = pcsCount;
            return null;
        }

        public static int CountPcs(TopologyProject project) =>
            project?.Nodes.Count(n => n.TemplateId == "pcs") ?? 0;

        /// <summary>
        /// 不写 lc 选型。若 emu 仍指向已迁走的中压型号，则把 emu 回 standard。
        /// 返回被纠正的 emu 型号 id；无改动时返回 null。
        /// </summary>
        public static string? ApplyForProject(TopologyProject project, string? rootOverride = null)
        {
            _ = project;
            var selection = DeviceModelRegistry.LoadSelection(rootOverride);
            if (!TryMigrateStaleEmuSelection(selection))
                return null;

            DeviceModelRegistry.SaveSelection(selection, rootOverride);
            return EmuStandardModelId;
        }

        /// <summary>将已迁到 LC 的中压型号从 emu 选型清掉，emu 回 standard。不写 lc 键。</summary>
        internal static bool TryMigrateStaleEmuSelection(DeviceModelSelection selection)
        {
            if (!selection.Selections.TryGetValue(EmuTypeId, out var emuModel) ||
                string.IsNullOrWhiteSpace(emuModel) ||
                !MovedFromEmuModelIds.Contains(emuModel))
                return false;

            selection.Selections[EmuTypeId] = EmuStandardModelId;
            return true;
        }
    }
}
