using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Web.Topology
{
    /// <summary>
    /// 把组态工程的母线/电表连线映射到运行时母线 Id，供电表采样与串联设备交换使用。
    /// </summary>
    public static class TopologyElectricalMapper
    {
        public sealed class Mapping
        {
            public IReadOnlyDictionary<string, string> BusRuntimeIds { get; init; } =
                new Dictionary<string, string>();
            public bool HasStationTransformer { get; init; }
        }

        public static Mapping Map(TopologyProject project)
        {
            var busIds = new Dictionary<string, string>(StringComparer.Ordinal);
            bool hasStationXfmr = project.Nodes.Any(IsStationTransformer);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            int unit690 = 0;

            foreach (var grid in project.Nodes.Where(n => n.TemplateId == "grid"))
                Walk(project, grid.Id, cameFrom: null, xfmrCrossed: 0, passedMainBreaker: false,
                    incomingSplitEar: null, incomingSplitUnit: 0, incomingSplitXfmr: 0);

            return new Mapping
            {
                BusRuntimeIds = busIds,
                HasStationTransformer = hasStationXfmr
            };

            void Walk(
                TopologyProject p,
                string nodeId,
                string? cameFrom,
                int xfmrCrossed,
                bool passedMainBreaker,
                string? incomingSplitEar,
                int incomingSplitUnit,
                int incomingSplitXfmr)
            {
                if (!visited.Add(nodeId))
                    return;

                var node = p.Nodes.FirstOrDefault(n => n.Id == nodeId);
                if (node == null)
                    return;

                if (node.TemplateId == "ac_bus")
                {
                    if (!busIds.ContainsKey(node.Id))
                    {
                        busIds[node.Id] = incomingSplitEar == "L"
                            ? RuntimeBusIds.Unit690Ear(incomingSplitUnit, incomingSplitXfmr, right: false)
                            : incomingSplitEar == "R"
                                ? RuntimeBusIds.Unit690Ear(incomingSplitUnit, incomingSplitXfmr, right: true)
                                : AssignBusId(xfmrCrossed, passedMainBreaker, hasStationXfmr, ref unit690);
                    }

                    foreach (var nb in Neighbors(p, node.Id))
                    {
                        if (nb == cameFrom) continue;
                        Walk(p, nb, node.Id, xfmrCrossed, passedMainBreaker, null, 0, 0);
                    }
                    return;
                }

                if (node.TemplateId is "ac_meter" or "load" or "pcs" or "pv_unit"
                    or "bms" or "dc_bus" or "emu" or "emu_group")
                    return;

                bool nextMain = passedMainBreaker
                    || (node.TemplateId == "ac_breaker"
                        && TopologyParamHelper.GetBool(node.Parameters, "isMainBreaker"));
                int nextXfmr = xfmrCrossed + (TopologyTemplates.IsTransformerLike(node.TemplateId) ? 1 : 0);
                int splitUnit = TopologyTemplates.IsSplitTransformer(node.TemplateId)
                    ? IndexOfEmuWithPcs(p, TopologyParamHelper.GetString(node.Parameters, "emuId"))
                    : 0;
                int splitXfmr = TopologyTemplates.IsSplitTransformer(node.TemplateId)
                    ? IndexOfSplitOnEmu(p, TopologyParamHelper.GetString(node.Parameters, "emuId"), node.Id)
                    : 0;

                foreach (var (nb, localPort) in NeighborPorts(p, node.Id))
                {
                    if (nb == cameFrom) continue;
                    string? ear = null;
                    if (TopologyTemplates.IsSplitTransformer(node.TemplateId))
                    {
                        if (TopologyTemplates.IsSplitLeftEarPort(localPort)) ear = "L";
                        else if (TopologyTemplates.IsSplitRightEarPort(localPort)) ear = "R";
                    }
                    Walk(p, nb, node.Id, nextXfmr, nextMain, ear, splitUnit, splitXfmr);
                }
            }
        }

        /// <summary>含 PCS 的 EMU 按画布 Y/X 排序后的序号；找不到时返回 0。</summary>
        public static int IndexOfEmuWithPcs(TopologyProject project, string? emuId)
        {
            if (string.IsNullOrWhiteSpace(emuId))
                return 0;
            int idx = 0;
            foreach (var emu in project.Nodes.Where(n => n.TemplateId == "emu").OrderBy(n => n.Y).ThenBy(n => n.X))
            {
                bool hasPcs = project.Nodes.Any(p =>
                    p.TemplateId == "pcs" && TopologyParamHelper.GetString(p.Parameters, "emuId") == emu.Id);
                if (!hasPcs)
                    continue;
                if (string.Equals(emu.Id, emuId, StringComparison.Ordinal))
                    return idx;
                idx++;
            }
            return 0;
        }

        /// <summary>同一 EMU 下双耳变压器按画布 Y/X 的序号；找不到时返回 0。</summary>
        public static int IndexOfSplitOnEmu(TopologyProject project, string? emuId, string xfmrId)
        {
            int idx = 0;
            foreach (var node in project.Nodes
                .Where(n => TopologyTemplates.IsSplitTransformer(n.TemplateId)
                    && string.Equals(TopologyParamHelper.GetString(n.Parameters, "emuId"), emuId, StringComparison.Ordinal))
                .OrderBy(n => n.Y).ThenBy(n => n.X))
            {
                if (string.Equals(node.Id, xfmrId, StringComparison.Ordinal))
                    return idx;
                idx++;
            }

            return 0;
        }

        public static string? ResolveMeterSourceBusId(TopologyProject project, TopologyNode meter)
        {
            var mapping = Map(project);
            var bus = FindConnectedAcBus(project, meter.Id);
            if (bus == null)
                return null;
            return mapping.BusRuntimeIds.TryGetValue(bus.Id, out var runtimeId) ? runtimeId : null;
        }

        public static TopologyNode? FindConnectedAcBus(TopologyProject project, string nodeId)
        {
            foreach (var nbId in Neighbors(project, nodeId))
            {
                var nb = project.Nodes.FirstOrDefault(n => n.Id == nbId);
                if (nb?.TemplateId == "ac_bus")
                    return nb;
            }
            return null;
        }

        public static bool HasStationTransformer(TopologyProject project) =>
            project.Nodes.Any(IsStationTransformer);

        private static bool IsStationTransformer(TopologyNode n) =>
            n.TemplateId == "transformer"
            && string.IsNullOrWhiteSpace(TopologyParamHelper.GetString(n.Parameters, "emuId"));

        private static string AssignBusId(
            int xfmrCrossed,
            bool passedMainBreaker,
            bool hasStationXfmr,
            ref int unit690)
        {
            if (xfmrCrossed <= 0)
            {
                if (!passedMainBreaker)
                    return RuntimeBusIds.Grid;
                return hasStationXfmr ? RuntimeBusIds.AfterMainBreaker : RuntimeBusIds.Station35;
            }

            if (xfmrCrossed == 1)
                return RuntimeBusIds.Station35;

            int idx = unit690++;
            return RuntimeBusIds.Unit690(idx);
        }

        private static IEnumerable<string> Neighbors(TopologyProject project, string nodeId)
        {
            foreach (var e in project.Edges)
            {
                if (e.FromNodeId == nodeId) yield return e.ToNodeId;
                else if (e.ToNodeId == nodeId) yield return e.FromNodeId;
            }
        }

        private static IEnumerable<(string NodeId, string LocalPortId)> NeighborPorts(
            TopologyProject project, string nodeId)
        {
            foreach (var e in project.Edges)
            {
                if (e.FromNodeId == nodeId) yield return (e.ToNodeId, e.FromPortId);
                else if (e.ToNodeId == nodeId) yield return (e.FromNodeId, e.ToPortId);
            }
        }
    }
}
