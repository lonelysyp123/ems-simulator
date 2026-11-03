using EssSimulator.Configuration;

namespace EssSimulator.Web.Topology
{
    /// <summary>
    /// 组态里每台三相断路器对应一路中压从站。顺序按画布 Y、X、节点 id。
    /// 主断、无分组且归属含 PCS 的 EMU 的断路器标出角色；其余为其它。
    /// </summary>
    public static class MvBreakerProtocolLayout
    {
        public static List<MvBreakerEndpoint> Enumerate(TopologyProject? project)
        {
            var list = Enumerate(project?.Nodes);
            if (project == null)
                return list;

            var mapping = TopologyElectricalMapper.Map(project);
            var byId = list.ToDictionary(b => b.NodeId, StringComparer.Ordinal);
            foreach (var brk in project.Nodes.Where(n => n.TemplateId == "ac_breaker"))
            {
                if (!byId.TryGetValue(brk.Id, out var ep))
                    continue;
                var bus = TopologyElectricalMapper.FindConnectedAcBus(project, brk.Id);
                if (bus != null && mapping.BusRuntimeIds.TryGetValue(bus.Id, out var runtimeId))
                    ep.SourceBusId = runtimeId;
            }

            return list;
        }

        public static List<MvBreakerEndpoint> Enumerate(IEnumerable<TopologyNode>? nodes)
        {
            var list = nodes?.ToList() ?? new List<TopologyNode>();
            var unitByEmu = new Dictionary<string, int>(StringComparer.Ordinal);
            int nextUnit = 0;
            foreach (var emu in list
                         .Where(n => n.TemplateId == "emu")
                         .OrderBy(n => n.Y).ThenBy(n => n.X).ThenBy(n => n.Id, StringComparer.Ordinal))
            {
                bool hasPcs = list.Any(p =>
                    p.TemplateId == "pcs" &&
                    TopologyParamHelper.GetString(p.Parameters, "emuId") == emu.Id);
                if (!hasPcs)
                    continue;
                nextUnit++;
                unitByEmu[emu.Id] = nextUnit;
            }

            var result = new List<MvBreakerEndpoint>();
            foreach (var brk in list
                         .Where(n => n.TemplateId == "ac_breaker")
                         .OrderBy(n => n.Y).ThenBy(n => n.X).ThenBy(n => n.Id, StringComparer.Ordinal))
            {
                string emuId = TopologyParamHelper.GetString(brk.Parameters, "emuId");
                string groupId = TopologyParamHelper.GetString(brk.Parameters, "groupId");
                var role = MvBreakerRole.Other;
                int unitId = 0;
                if (TopologyParamHelper.GetBool(brk.Parameters, "isMainBreaker"))
                {
                    role = MvBreakerRole.Main;
                }
                else if (!string.IsNullOrWhiteSpace(emuId)
                         && string.IsNullOrWhiteSpace(groupId)
                         && unitByEmu.TryGetValue(emuId, out unitId))
                {
                    role = MvBreakerRole.Unit;
                }
                else
                {
                    unitId = 0;
                }

                result.Add(new MvBreakerEndpoint
                {
                    NodeId = brk.Id,
                    Label = string.IsNullOrWhiteSpace(brk.Label) ? brk.Id : brk.Label,
                    Role = role,
                    UnitId = role == MvBreakerRole.Unit ? unitId : 0
                });
            }

            return result;
        }
    }
}
