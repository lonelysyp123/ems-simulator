using EssSimulator.Web.Topology;
using Xunit;

namespace EssSimulator.Tests.Topology;

public class PvUnitExpansionTests
{
    [Fact]
    public void Expand_replaces_one_pv_unit_with_four_branch_pairs()
    {
        var project = LegacyPlant();
        Assert.True(PvUnitExpansion.Expand(project));

        Assert.DoesNotContain(project.Nodes, n => n.TemplateId == "pv_unit");
        var pvu = Assert.Single(project.Nodes.Where(n => n.TemplateId == "pvu"));
        var branches = project.Nodes.Where(n => n.TemplateId == "pv_pcs").OrderBy(n => n.X).ToList();
        var arrays = project.Nodes.Where(n => n.TemplateId == "pv_array").ToList();
        Assert.Equal(4, branches.Count);
        Assert.Equal(4, arrays.Count);
        Assert.All(branches, b => Assert.Equal(pvu.Id, TopologyParamHelper.GetString(b.Parameters, "pvuId")));
        var xf = Assert.Single(project.Nodes.Where(n => n.TemplateId == "transformer"));
        Assert.Equal(pvu.Id, TopologyParamHelper.GetString(xf.Parameters, "pvuId"));
        Assert.Equal(2000d, TopologyParamHelper.GetDouble(xf.Parameters, "ratedPowerKva"));
        Assert.Equal(35000d, TopologyParamHelper.GetDouble(xf.Parameters, "primaryVoltage"));
        Assert.Equal(690d, TopologyParamHelper.GetDouble(xf.Parameters, "secondaryVoltage"));
        Assert.Contains(project.Edges, e =>
            e.FromNodeId == xf.Id && e.FromPortId == "pri_a" && e.ToNodeId == "bus35");
        var lv = Assert.Single(project.Nodes.Where(n => n.TemplateId == "ac_bus" && n.Id != "bus35"));
        Assert.All(branches, b =>
        {
            Assert.Equal(690d, TopologyParamHelper.GetDouble(b.Parameters, "acVoltage"));
            Assert.Contains(project.Edges, e =>
                e.FromNodeId == b.Id && e.FromPortId == "ac_a" && e.ToNodeId == lv.Id);
            var array = Assert.Single(TopologyValidator.DirectNeighbors(project, b.Id).Where(n => n.TemplateId == "pv_array"));
            Assert.Contains(project.Edges, e =>
                e.FromNodeId == b.Id && e.FromPortId == "dc_pos" && e.ToNodeId == array.Id);
        });
        Assert.Equal("TSM-NEG21C.20Q", TopologyParamHelper.GetString(arrays[0].Parameters, "moduleModel"));
        Assert.Equal(16d, TopologyParamHelper.GetDouble(arrays[0].Parameters, "stringCount"));

        var save = TopologyValidator.ValidateProjectForSave(project);
        Assert.True(save.Ok, save.Message + " / " + string.Join("; ", save.Details));
        var (overlay, convert) = TopologyRuntimeConverter.Convert(project);
        Assert.True(convert.Ok, convert.Message);
        Assert.Equal(4, overlay!.PvUnits[0].InverterCount);
        Assert.Equal(2000, overlay.PvUnits[0].UnitXfRatedKva);
        Assert.Equal(690, overlay.PvUnits[0].InverterAcVoltageV);

        int nodes = project.Nodes.Count;
        int edges = project.Edges.Count;
        Assert.False(PvUnitExpansion.Expand(project));
        Assert.Equal(nodes, project.Nodes.Count);
        Assert.Equal(edges, project.Edges.Count);
    }

    private static TopologyProject LegacyPlant()
    {
        TopologyNode Node(string id, string templateId, string label, Dictionary<string, object?>? extra = null)
        {
            var p = new Dictionary<string, object?>(TopologyTemplates.Get(templateId)!.DefaultParameters);
            if (extra != null)
            {
                foreach (var kv in extra)
                    p[kv.Key] = kv.Value;
            }
            return new TopologyNode { Id = id, TemplateId = templateId, Label = label, Parameters = p };
        }

        var project = new TopologyProject
        {
            Id = "legacy",
            Name = "旧光伏",
            Nodes =
            {
                Node("grid", "grid", "电网"),
                Node("brk", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("meter", "ac_meter", "PCC", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("bus35", "ac_bus", "35kV母线", new Dictionary<string, object?> { ["nominalVoltage"] = 35000d }),
                Node("pv1", "pv_unit", "光伏单元-1", new Dictionary<string, object?>
                {
                    ["inverterCount"] = 4d,
                    ["unitXfRatedKva"] = 2000d
                })
            },
            Edges =
            {
                new TopologyEdge
                {
                    Id = "ac",
                    FromNodeId = "pv1",
                    FromPortId = "ac_a",
                    ToNodeId = "bus35",
                    ToPortId = "a2"
                }
            }
        };
        return project;
    }
}
