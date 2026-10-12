using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.LocalControl;
using EssSimulator.Web.Topology;

namespace EssSimulator.Tests.Topology;

/// <summary>
/// 组态应用到仿真：ConvertForApply、多单元 LC 展开、双耳箱变跨单元映射与未接线 PCS 归耳。
/// </summary>
public class TopologyRuntimeApplyTests
{
    private static TopologyNode Node(string id, string templateId, string label,
        Dictionary<string, object?>? overrides = null, double x = 0, double y = 0)
    {
        var tpl = TopologyTemplates.Get(templateId)!;
        var p = new Dictionary<string, object?>(tpl.DefaultParameters);
        if (overrides != null)
        {
            foreach (var kv in overrides)
                p[kv.Key] = kv.Value;
        }

        return new TopologyNode { Id = id, TemplateId = templateId, Label = label, Parameters = p, X = x, Y = y };
    }

    private static TopologyEdge Edge(string id, string from, string fromPort, string to, string toPort) => new()
    {
        Id = id,
        FromNodeId = from,
        FromPortId = fromPort,
        ToNodeId = to,
        ToPortId = toPort
    };

    [Fact]
    public void Convert_rejects_null_project()
    {
        var (overlay, validation) = TopologyRuntimeConverter.Convert(null!);
        Assert.Null(overlay);
        Assert.False(validation.Ok);
        Assert.Equal("PROJECT_NULL", validation.Code);
    }

    [Fact]
    public void ConvertForApply_two_emu_scaffold_is_usable_and_expands_lc_two_groups()
    {
        var project = TopologyScaffold.BuildRadial(emuCount: 2, name: "双单元", includeLoad: true);
        var (overlay, validation) = TopologyRuntimeConverter.ConvertForApply(project);
        Assert.True(validation.Ok, validation.Message);
        Assert.NotNull(overlay);
        Assert.Equal(2, overlay!.EssUnits.Count);
        Assert.True(TopologyOverlayLoader.IsUsable(overlay));

        foreach (var unit in overlay.EssUnits)
        {
            Assert.False(unit.HasGroups);
            Assert.Equal(2, unit.PcsCount);
            Assert.Equal(1, LcLayout.GroupCount(unit));
            Assert.Equal(2, LcLayout.ExpandGroupCount(unit));
            Assert.Equal(2, LcLayout.PcsCountInGroup(unit, 0));
            Assert.Equal(0, LcLayout.PcsCountInGroup(unit, 1));
            Assert.True(LcLayout.ShouldUseExclusiveEmuMap(unit.PcsCount));
        }

        Assert.NotNull(overlay.Load);
        Assert.Equal(0, overlay.Load!.ActivePowerPlan);
        Assert.Contains(overlay.Notes, n => n.Contains("负载"));
    }

    [Fact]
    public void Convert_two_grouped_emus_drive_multi_unit_lc_layout()
    {
        var project = new TopologyProject
        {
            Id = "multi-group",
            Name = "两组×两单元",
            Nodes =
            {
                Node("g1", "grid", "电网"),
                Node("e1", "emu", "EMU-1", y: 100),
                Node("e2", "emu", "EMU-2", y: 400),
                Node("g1a", "emu_group", "E1-A", new Dictionary<string, object?> { ["emuId"] = "e1" }, y: 120),
                Node("g1b", "emu_group", "E1-B", new Dictionary<string, object?> { ["emuId"] = "e1" }, y: 140),
                Node("g2a", "emu_group", "E2-A", new Dictionary<string, object?> { ["emuId"] = "e2" }, y: 420),
                Node("g2b", "emu_group", "E2-B", new Dictionary<string, object?> { ["emuId"] = "e2" }, y: 440),
                Node("p1a1", "pcs", "P1A1", new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1a" }, x: 10),
                Node("p1a2", "pcs", "P1A2", new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1a" }, x: 20),
                Node("p1b1", "pcs", "P1B1", new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1b" }, x: 30),
                Node("p1b2", "pcs", "P1B2", new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1b" }, x: 40),
                Node("p2a1", "pcs", "P2A1", new Dictionary<string, object?> { ["emuId"] = "e2", ["groupId"] = "g2a" }, x: 10),
                Node("p2a2", "pcs", "P2A2", new Dictionary<string, object?> { ["emuId"] = "e2", ["groupId"] = "g2a" }, x: 20),
                Node("p2b1", "pcs", "P2B1", new Dictionary<string, object?> { ["emuId"] = "e2", ["groupId"] = "g2b" }, x: 30),
                Node("p2b2", "pcs", "P2B2", new Dictionary<string, object?> { ["emuId"] = "e2", ["groupId"] = "g2b" }, x: 40)
            }
        };

        var (overlay, validation) = TopologyRuntimeConverter.Convert(project);
        Assert.True(validation.Ok, validation.Message);
        Assert.Equal(2, overlay!.EssUnits.Count);
        Assert.True(TopologyOverlayLoader.IsUsable(overlay));

        foreach (var unit in overlay.EssUnits)
        {
            Assert.True(unit.HasGroups);
            Assert.Equal(2, unit.Groups.Count);
            Assert.Equal(4, unit.PcsCount);
            Assert.Equal(2, LcLayout.GroupCount(unit));
            Assert.Equal(2, LcLayout.ExpandGroupCount(unit));
            Assert.Equal(2, LcLayout.PcsCountInGroup(unit, 0));
            Assert.Equal(2, LcLayout.PcsCountInGroup(unit, 1));
            Assert.Equal(2, LcLayout.MaxPcsInAnyGroup(unit));
            Assert.False(LcLayout.ShouldUseExclusiveEmuMap(unit.PcsCount));
            Assert.All(unit.Groups, g =>
            {
                Assert.Equal(2, g.Pcs.Count);
                Assert.Equal(2, g.Bms.Count);
            });
        }

        Assert.Equal(new[] { "P1A1", "P1A2" }, overlay.EssUnits[0].Groups[0].Pcs.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "P2B1", "P2B2" }, overlay.EssUnits[1].Groups[1].Pcs.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Convert_unwired_pcs_falls_onto_first_split_left_ear()
    {
        var project = new TopologyProject
        {
            Id = "orphan-ear",
            Name = "未接线PCS归左耳",
            Nodes =
            {
                Node("g1", "grid", "电网"),
                Node("e1", "emu", "EMU-1", y: 600),
                Node("split", "split_transformer", "双耳1", new Dictionary<string, object?> { ["emuId"] = "e1" }),
                Node("busL", "ac_bus", "左690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
                Node("busR", "ac_bus", "右690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
                Node("pL", "pcs", "PCS-L", new Dictionary<string, object?> { ["emuId"] = "e1" }, x: 100, y: 720),
                Node("pOrphan", "pcs", "PCS-悬空", new Dictionary<string, object?> { ["emuId"] = "e1" }, x: 150, y: 720),
                Node("pR", "pcs", "PCS-R", new Dictionary<string, object?> { ["emuId"] = "e1" }, x: 200, y: 720)
            },
            Edges =
            {
                Edge("1", "split", "ear_l_a", "busL", "a"),
                Edge("2", "split", "ear_r_a", "busR", "a"),
                Edge("3", "pL", "ac_a", "busL", "a2"),
                Edge("4", "pR", "ac_a", "busR", "a2")
            }
        };

        var (overlay, validation) = TopologyRuntimeConverter.Convert(project);
        Assert.True(validation.Ok, validation.Message);
        var unit = overlay!.EssUnits[0];
        Assert.Equal(new[] { "PCS-L", "PCS-悬空", "PCS-R" }, unit.Pcs.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { 0, 1 }, unit.SplitTransformer!.LeftEarPcsIndices.OrderBy(i => i).ToArray());
        Assert.Equal(new[] { 2 }, unit.SplitTransformer.RightEarPcsIndices.ToArray());
    }

    [Fact]
    public void Convert_clamps_positive_load_power_to_zero()
    {
        var project = new TopologyProject
        {
            Nodes =
            {
                Node("e1", "emu", "EMU-1"),
                Node("p1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "e1" }),
                Node("load1", "load", "站用", new Dictionary<string, object?>
                {
                    ["activePowerKw"] = 250d,
                    ["reactivePowerKvar"] = -30d
                })
            }
        };

        var (overlay, validation) = TopologyRuntimeConverter.Convert(project);
        Assert.True(validation.Ok, validation.Message);
        Assert.Equal(0, overlay!.Load!.ActivePowerPlan);
        Assert.Equal(-30, overlay.Load.ReactivePowerPlan, 3);
        Assert.Contains(overlay.Notes, n => n.Contains("站用") && n.Contains("有功仅消耗"));
    }

    [Fact]
    public void Convert_two_emus_each_with_split_transformer_keep_independent_ears()
    {
        var project = TwoEmuSplitPlant();
        var (overlay, validation) = TopologyRuntimeConverter.Convert(project);
        Assert.True(validation.Ok, validation.Message);
        Assert.Equal(2, overlay!.EssUnits.Count);

        Assert.Equal("箱变-1", overlay.EssUnits[0].SplitTransformer!.Name);
        Assert.Equal(new[] { 0 }, overlay.EssUnits[0].SplitTransformer!.LeftEarPcsIndices);
        Assert.Equal(new[] { 1 }, overlay.EssUnits[0].SplitTransformer!.RightEarPcsIndices);

        Assert.Equal("箱变-2", overlay.EssUnits[1].SplitTransformer!.Name);
        Assert.Equal(new[] { 0 }, overlay.EssUnits[1].SplitTransformer!.LeftEarPcsIndices);
        Assert.Equal(new[] { 1 }, overlay.EssUnits[1].SplitTransformer!.RightEarPcsIndices);

        Assert.Equal(690d, overlay.UnitTransformer!.SecondaryVoltage);
        Assert.Contains(overlay.Notes, n => n.Contains("EMU-1") && n.Contains("双耳变压器"));
        Assert.Contains(overlay.Notes, n => n.Contains("EMU-2") && n.Contains("双耳变压器"));
    }

    [Fact]
    public void Convert_without_station_transformer_notes_direct_station_bus()
    {
        var project = new TopologyProject
        {
            Nodes =
            {
                Node("g1", "grid", "电网", new Dictionary<string, object?> { ["outputVoltage"] = 35000d }),
                Node("e1", "emu", "EMU-1"),
                Node("p1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "e1" })
            }
        };

        var (overlay, validation) = TopologyRuntimeConverter.Convert(project);
        Assert.True(validation.Ok, validation.Message);
        Assert.False(overlay!.Transformer!.Present);
        Assert.Equal(35000, overlay.Pcc!.StationBusNominalLineVoltage);
        Assert.Contains(overlay.Notes, n => n.Contains("无站用主变"));
    }

    [Fact]
    public void ConvertForApply_rejects_multi_pcc_meter()
    {
        var project = TopologyScaffold.BuildRadial(1);
        var extra = Node("m2", "ac_meter", "第二PCC", new Dictionary<string, object?> { ["isPccMeter"] = true });
        project.Nodes.Add(extra);
        var (overlay, validation) = TopologyRuntimeConverter.ConvertForApply(project);
        Assert.Null(overlay);
        Assert.Equal("MULTI_PCC_METER", validation.Code);
    }

    internal static TopologyProject TwoEmuSplitPlant()
    {
        return new TopologyProject
        {
            Id = "two-emu-split",
            Name = "两单元双耳",
            Nodes =
            {
                Node("g1", "grid", "电网"),
                Node("brk", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("hv", "ac_bus", "220kV", new Dictionary<string, object?> { ["nominalVoltage"] = 220000d }),
                Node("xf", "transformer", "主变"),
                Node("lv", "ac_bus", "35kV", new Dictionary<string, object?> { ["nominalVoltage"] = 35000d }),
                Node("e1", "emu", "EMU-1", y: 600),
                Node("e2", "emu", "EMU-2", y: 800),
                Node("s1", "split_transformer", "箱变-1", new Dictionary<string, object?> { ["emuId"] = "e1" }, y: 100),
                Node("s2", "split_transformer", "箱变-2", new Dictionary<string, object?> { ["emuId"] = "e2" }, y: 200),
                Node("b1L", "ac_bus", "1左", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
                Node("b1R", "ac_bus", "1右", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
                Node("b2L", "ac_bus", "2左", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
                Node("b2R", "ac_bus", "2右", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
                Node("p1L", "pcs", "P1L", new Dictionary<string, object?> { ["emuId"] = "e1", ["acVoltage"] = 690d }, x: 100, y: 720),
                Node("p1R", "pcs", "P1R", new Dictionary<string, object?> { ["emuId"] = "e1", ["acVoltage"] = 690d }, x: 200, y: 720),
                Node("p2L", "pcs", "P2L", new Dictionary<string, object?> { ["emuId"] = "e2", ["acVoltage"] = 690d }, x: 100, y: 920),
                Node("p2R", "pcs", "P2R", new Dictionary<string, object?> { ["emuId"] = "e2", ["acVoltage"] = 690d }, x: 200, y: 920)
            },
            Edges =
            {
                Edge("e1", "g1", "a", "brk", "a"),
                Edge("e2", "brk", "a2", "hv", "a"),
                Edge("e3", "xf", "pri_a", "hv", "a2"),
                Edge("e4", "xf", "sec_a", "lv", "a"),
                Edge("e5", "s1", "pri_a", "lv", "a2"),
                Edge("e6", "s2", "pri_a", "lv", "b2"),
                Edge("e7", "s1", "ear_l_a", "b1L", "a"),
                Edge("e8", "s1", "ear_r_a", "b1R", "a"),
                Edge("e9", "s2", "ear_l_a", "b2L", "a"),
                Edge("e10", "s2", "ear_r_a", "b2R", "a"),
                Edge("e11", "p1L", "ac_a", "b1L", "a2"),
                Edge("e12", "p1R", "ac_a", "b1R", "a2"),
                Edge("e13", "p2L", "ac_a", "b2L", "a2"),
                Edge("e14", "p2R", "ac_a", "b2R", "a2")
            }
        };
    }
}
