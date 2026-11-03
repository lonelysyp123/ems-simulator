using EssSimulator.Web.Topology;
using Xunit;

namespace EssSimulator.Tests.Topology;

public class TopologyScaffoldTests
{
    [Fact]
    public void BuildRadial_creates_valid_saveable_project_with_n_emus()
    {
        var p = TopologyScaffold.BuildRadial(emuCount: 2, name: "测试径向", includeLoad: true);
        Assert.Equal("测试径向", p.Name);
        Assert.Equal(2, p.Nodes.Count(n => n.TemplateId == "emu"));
        Assert.Equal(4, p.Nodes.Count(n => n.TemplateId == "pcs"));
        Assert.Equal(4, p.Nodes.Count(n => n.TemplateId == "bms"));
        // 每台 PCS 均归属一个有效 EMU，且每个 EMU 各有 2 台 PCS
        var emuIds = p.Nodes.Where(n => n.TemplateId == "emu").Select(n => n.Id).ToHashSet();
        Assert.All(p.Nodes.Where(n => n.TemplateId == "pcs"),
            n => Assert.Contains(TopologyParamHelper.GetString(n.Parameters, "emuId"), emuIds));
        Assert.All(p.Nodes.Where(n => n.TemplateId == "emu"),
            e => Assert.Equal(2, p.Nodes.Count(n => n.TemplateId == "pcs" &&
                TopologyParamHelper.GetString(n.Parameters, "emuId") == e.Id)));
        // PCS AC 侧接 35kV 母线，DC 侧经 DC 母线接 BMS；EMU 虚拟节点无任何连线
        Assert.All(p.Nodes.Where(n => n.TemplateId == "emu"),
            e => Assert.DoesNotContain(p.Edges, x => x.FromNodeId == e.Id || x.ToNodeId == e.Id));
        Assert.Contains(p.Nodes, n => n.TemplateId == "load");
        Assert.Contains(p.Nodes, n => n.TemplateId == "ac_breaker" &&
                                      TopologyParamHelper.GetBool(n.Parameters, "isMainBreaker"));
        Assert.Contains(p.Nodes, n => n.TemplateId == "ac_meter" &&
                                      TopologyParamHelper.GetBool(n.Parameters, "isPccMeter"));

        var validation = TopologyValidator.ValidateProjectForSave(p);
        Assert.True(validation.Ok, validation.Message + " / " + string.Join("; ", validation.Details));
        Assert.True(p.Edges.Count >= 18); // 站侧三相×若干 + EMU AC/DC
    }

    [Fact]
    public void BuildRadial_rejects_invalid_emu_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TopologyScaffold.BuildRadial(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TopologyScaffold.BuildRadial(21));
    }

    [Fact]
    public void BuildRadial_without_load_omits_load_node()
    {
        var p = TopologyScaffold.BuildRadial(1, includeLoad: false);
        Assert.DoesNotContain(p.Nodes, n => n.TemplateId == "load");
        Assert.True(TopologyValidator.ValidateProjectForSave(p).Ok);
    }

    [Fact]
    public void BuildRadial_creates_pv_only_plant()
    {
        var p = TopologyScaffold.BuildRadial(emuCount: 0, name: "光伏场站", includeLoad: true, pvCount: 2);
        Assert.Equal("光伏场站", p.Name);
        Assert.Empty(p.Nodes.Where(n => n.TemplateId == "emu"));
        Assert.Equal(2, p.Nodes.Count(n => n.TemplateId == "pvu"));
        Assert.Equal(8, p.Nodes.Count(n => n.TemplateId == "pv_pcs"));
        Assert.Equal(8, p.Nodes.Count(n => n.TemplateId == "pv_array"));
        Assert.Equal(2, p.Nodes.Count(n => n.TemplateId == "transformer" && n.Label.StartsWith("光伏箱变")));
        Assert.DoesNotContain(p.Nodes, n => n.TemplateId == "pv_unit");
        Assert.DoesNotContain(p.Nodes, n => n.TemplateId == "dc_bus");
        Assert.DoesNotContain(p.Nodes, n => n.TemplateId == "bms");
        Assert.All(p.Nodes.Where(n => n.TemplateId == "pv_pcs"), pcs =>
        {
            var pvuId = TopologyParamHelper.GetString(pcs.Parameters, "pvuId");
            var xf = Assert.Single(p.Nodes.Where(n =>
                n.TemplateId == "transformer" && TopologyParamHelper.GetString(n.Parameters, "pvuId") == pvuId));
            Assert.Equal(690d, TopologyParamHelper.GetDouble(pcs.Parameters, "acVoltage"));
            Assert.Equal(35000d, TopologyParamHelper.GetDouble(xf.Parameters, "primaryVoltage"));
            Assert.Equal(690d, TopologyParamHelper.GetDouble(xf.Parameters, "secondaryVoltage"));
            var lv = TopologyValidator.DirectNeighbors(p, pcs.Id).Single(n => n.TemplateId == "ac_bus");
            Assert.Equal(690d, TopologyParamHelper.GetDouble(lv.Parameters, "nominalVoltage"));
            Assert.Contains(TopologyValidator.DirectNeighbors(p, xf.Id), n => n.Id == lv.Id);
            var arrays = TopologyValidator.DirectNeighbors(p, pcs.Id).Where(n => n.TemplateId == "pv_array").ToList();
            var array = Assert.Single(arrays);
            Assert.Equal(new[] { pcs.Id }, TopologyValidator.DirectNeighbors(p, array.Id)
                .Where(n => n.TemplateId == "pv_pcs").Select(n => n.Id));
        });
        Assert.Contains(p.Nodes, n => n.TemplateId == "load");

        var validation = TopologyValidator.ValidateProjectForSave(p);
        Assert.True(validation.Ok, validation.Message + " / " + string.Join("; ", validation.Details));

        var (overlay, convert) = TopologyRuntimeConverter.ConvertForApply(p);
        Assert.True(convert.Ok, convert.Message);
        Assert.NotNull(overlay);
        Assert.Empty(overlay!.EssUnits);
        Assert.Equal(2, overlay.PvUnits.Count);
        Assert.All(overlay.PvUnits, u => Assert.Equal(4, u.InverterCount));
    }

    [Fact]
    public void BuildRadial_creates_mixed_ess_and_pv_plant()
    {
        var p = TopologyScaffold.BuildRadial(emuCount: 1, pvCount: 2);
        Assert.Single(p.Nodes.Where(n => n.TemplateId == "emu"));
        Assert.Equal(2, p.Nodes.Count(n => n.TemplateId == "pcs"));
        Assert.Single(p.Nodes.Where(n => n.TemplateId == "dc_bus"));
        Assert.Equal(2, p.Nodes.Count(n => n.TemplateId == "bms"));
        Assert.Equal(2, p.Nodes.Count(n => n.TemplateId == "pvu"));
        Assert.True(TopologyValidator.ValidateProjectForSave(p).Ok);
    }

    [Fact]
    public void BuildRadial_rejects_zero_generation_units()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TopologyScaffold.BuildRadial(emuCount: 0, pvCount: 0));
    }

    [Fact]
    public void BuildRadial_rejects_invalid_pv_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TopologyScaffold.BuildRadial(emuCount: 0, pvCount: 21));
    }
}
