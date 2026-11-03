namespace EssSimulator.Web.Topology
{
    /// <summary>
    /// 把旧的整台 <c>pv_unit</c> 展开成虚拟光伏单元、光伏 PCS 支路和方阵。
    /// 只改内存中的工程；调用方决定何时写回磁盘。
    /// </summary>
    public static class PvUnitExpansion
    {
        public static bool Expand(TopologyProject project)
        {
            if (project?.Nodes == null) return false;
            var legacy = project.Nodes.Where(n => n.TemplateId == "pv_unit").ToList();
            if (legacy.Count == 0) return false;
            foreach (var unit in legacy)
                ExpandOne(project, unit);
            return true;
        }

        private static void ExpandOne(TopologyProject project, TopologyNode unit)
        {
            int count = Math.Max(1, (int)TopologyParamHelper.GetDouble(unit.Parameters, "inverterCount", 4));
            var pvuTpl = TopologyTemplates.Get("pvu")!;
            var pcsTpl = TopologyTemplates.Get("pv_pcs")!;
            var arrayTpl = TopologyTemplates.Get("pv_array")!;

            var pvu = new TopologyNode
            {
                Id = unit.Id + "_pvu",
                TemplateId = "pvu",
                Label = string.IsNullOrWhiteSpace(unit.Label) ? "光伏单元" : unit.Label,
                X = unit.X,
                Y = unit.Y - 160,
                Parameters = new Dictionary<string, object?>(pvuTpl.DefaultParameters)
            };
            Copy(pvu.Parameters, unit.Parameters, "name");

            double primary = TopologyParamHelper.GetDouble(unit.Parameters, "unitXfPrimaryV", 35000);
            double secondary = TopologyParamHelper.GetDouble(unit.Parameters, "unitXfSecondaryV", 690);
            var xfTpl = TopologyTemplates.Get("transformer")!;
            var xf = new TopologyNode
            {
                Id = unit.Id + "_xf",
                TemplateId = "transformer",
                Label = pvu.Label + "箱变",
                X = unit.X,
                Y = unit.Y - 80,
                Parameters = new Dictionary<string, object?>(xfTpl.DefaultParameters)
            };
            xf.Parameters["pvuId"] = pvu.Id;
            xf.Parameters["primaryVoltage"] = primary;
            xf.Parameters["secondaryVoltage"] = secondary;
            xf.Parameters["ratedPowerKva"] = TopologyParamHelper.GetDouble(
                unit.Parameters, "unitXfRatedKva", count * 320);
            xf.Parameters["noLoadLossW"] = TopologyParamHelper.GetDouble(unit.Parameters, "unitXfNoLoadLossW", 100);
            xf.Parameters["loadLossW"] = TopologyParamHelper.GetDouble(unit.Parameters, "unitXfLoadLossW", 200);
            xf.Parameters["impedancePercent"] = TopologyParamHelper.GetDouble(unit.Parameters, "unitXfImpedancePercent", 5);

            var busTpl = TopologyTemplates.Get("ac_bus")!;
            var lv = new TopologyNode
            {
                Id = unit.Id + "_lv",
                TemplateId = "ac_bus",
                Label = pvu.Label + " 690V",
                X = unit.X,
                Y = unit.Y - 20,
                Parameters = new Dictionary<string, object?>(busTpl.DefaultParameters)
            };
            lv.Parameters["nominalVoltage"] = secondary;
            lv.Parameters["name"] = lv.Label;

            var touching = project.Edges
                .Where(e => e.FromNodeId == unit.Id || e.ToNodeId == unit.Id)
                .ToList();
            project.Edges.RemoveAll(e => e.FromNodeId == unit.Id || e.ToNodeId == unit.Id);
            foreach (var edge in touching)
            {
                bool fromUnit = edge.FromNodeId == unit.Id;
                string? pri = PrimaryPort(fromUnit ? edge.FromPortId : edge.ToPortId);
                if (pri == null) continue;
                project.Edges.Add(new TopologyEdge
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FromNodeId = fromUnit ? xf.Id : edge.FromNodeId,
                    FromPortId = fromUnit ? pri : edge.FromPortId,
                    ToNodeId = fromUnit ? edge.ToNodeId : xf.Id,
                    ToPortId = fromUnit ? edge.ToPortId : pri
                });
            }
            AddPhases(project, xf.Id, "sec_a", "sec_b", "sec_c", lv.Id, "a", "b", "c");

            double pitch = 80;
            double origin = unit.X - (count - 1) * pitch / 2.0;
            for (int i = 0; i < count; i++)
            {
                var pcs = new TopologyNode
                {
                    Id = $"{unit.Id}_pcs{i + 1}",
                    TemplateId = "pv_pcs",
                    Label = $"{pvu.Label}-{i + 1}",
                    X = origin + i * pitch,
                    Y = unit.Y,
                    Parameters = new Dictionary<string, object?>(pcsTpl.DefaultParameters)
                };
                pcs.Parameters["pvuId"] = pvu.Id;
                pcs.Parameters["acVoltage"] = secondary;
                Copy(pcs.Parameters, unit.Parameters, "dcVoltageMin", "dcVoltageMax");
                Map(pcs.Parameters, unit.Parameters, "pcsRatedPowerKw", "inverterRatedPowerKw");
                Map(pcs.Parameters, unit.Parameters, "pcsMaxPowerKw", "inverterMaxPowerKw");
                Map(pcs.Parameters, unit.Parameters, "pcsEfficiency", "inverterEfficiency");

                var array = new TopologyNode
                {
                    Id = $"{unit.Id}_array{i + 1}",
                    TemplateId = "pv_array",
                    Label = $"{pvu.Label}-方阵{i + 1}",
                    X = pcs.X,
                    Y = unit.Y + 120,
                    Parameters = new Dictionary<string, object?>(arrayTpl.DefaultParameters)
                };
                Copy(array.Parameters, unit.Parameters,
                    "moduleModel", "stringCount", "modulesPerString", "operatingYears", "groundAlbedo", "name");

                project.Nodes.Add(pcs);
                project.Nodes.Add(array);
                AddPhases(project, pcs.Id, "ac_a", "ac_b", "ac_c", lv.Id, "a2", "b2", "c2");
                project.Edges.Add(new TopologyEdge
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FromNodeId = pcs.Id,
                    FromPortId = "dc_pos",
                    ToNodeId = array.Id,
                    ToPortId = "dc_pos"
                });
                project.Edges.Add(new TopologyEdge
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FromNodeId = pcs.Id,
                    FromPortId = "dc_neg",
                    ToNodeId = array.Id,
                    ToPortId = "dc_neg"
                });
            }

            project.Nodes.Add(pvu);
            project.Nodes.Add(xf);
            project.Nodes.Add(lv);
            project.Nodes.Remove(unit);
        }

        private static void AddPhases(
            TopologyProject project,
            string fromId, string fromA, string fromB, string fromC,
            string toId, string toA, string toB, string toC)
        {
            AddEdge(project, fromId, fromA, toId, toA);
            AddEdge(project, fromId, fromB, toId, toB);
            AddEdge(project, fromId, fromC, toId, toC);
        }

        private static void AddEdge(TopologyProject project, string fromId, string fromPort, string toId, string toPort)
        {
            project.Edges.Add(new TopologyEdge
            {
                Id = Guid.NewGuid().ToString("N"),
                FromNodeId = fromId,
                FromPortId = fromPort,
                ToNodeId = toId,
                ToPortId = toPort
            });
        }

        private static string? PrimaryPort(string portId) => portId switch
        {
            "ac_a" => "pri_a",
            "ac_b" => "pri_b",
            "ac_c" => "pri_c",
            _ => null
        };

        private static void Copy(Dictionary<string, object?> dest, Dictionary<string, object?> src, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (src != null && src.TryGetValue(key, out var value) && value != null)
                    dest[key] = value;
            }
        }

        private static void Map(Dictionary<string, object?> dest, Dictionary<string, object?> src, string destKey, string srcKey)
        {
            if (src != null && src.TryGetValue(srcKey, out var value) && value != null)
                dest[destKey] = value;
        }
    }
}
