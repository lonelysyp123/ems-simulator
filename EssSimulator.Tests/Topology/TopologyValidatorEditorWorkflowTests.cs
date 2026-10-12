using EssSimulator.Web.Topology;

namespace EssSimulator.Tests.Topology;

/// <summary>
/// 组态编辑连线 vs 保存级回放：编辑只拦结构问题，电气规则留到保存/应用。
/// </summary>
public class TopologyValidatorEditorWorkflowTests
{
    private static TopologyNode Node(string id, string templateId, string label,
        Dictionary<string, object?>? parameters = null, double x = 0, double y = 0)
    {
        var tpl = TopologyTemplates.Get(templateId)!;
        var p = new Dictionary<string, object?>(tpl.DefaultParameters);
        if (parameters != null)
        {
            foreach (var kv in parameters)
                p[kv.Key] = kv.Value;
        }

        return new TopologyNode { Id = id, TemplateId = templateId, Label = label, Parameters = p, X = x, Y = y };
    }

    private static TopologyEdge Edge(string from, string fromPort, string to, string toPort) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        FromNodeId = from,
        FromPortId = fromPort,
        ToNodeId = to,
        ToPortId = toPort
    };

    private static TopologyValidationResult Connect(TopologyProject p, TopologyEdge e)
    {
        var r = TopologyValidator.TryConnect(p, e);
        if (r.Ok) TopologyValidator.ApplyConnect(p, e);
        return r;
    }

    private static TopologyProject PlantWithRoles(params TopologyNode[] extra)
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brkMain", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("mPcc", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" })
            }
        };
        foreach (var n in extra)
            p.Nodes.Add(n);
        return p;
    }

    [Fact]
    public void Edit_rejects_structural_errors_immediately()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("pcs1", "pcs", "PCS"),
                Node("bus1", "ac_bus", "母线")
            }
        };

        Assert.Equal("SELF_LINK", TopologyValidator.TryConnect(p, Edge("pcs1", "ac_a", "pcs1", "ac_b")).Code);
        Assert.Equal("PORT_MISSING", TopologyValidator.TryConnect(p, Edge("pcs1", "nope", "bus1", "a")).Code);
        Assert.Equal("NODE_MISSING", TopologyValidator.TryConnect(p, Edge("pcs1", "ac_a", "ghost", "a")).Code);

        var unknown = Node("bad", "pcs", "坏");
        unknown.TemplateId = "not-a-template";
        p.Nodes.Add(unknown);
        Assert.Equal("TEMPLATE_MISSING", TopologyValidator.TryConnect(p, Edge("bad", "ac_a", "bus1", "a")).Code);

        Assert.True(Connect(p, Edge("pcs1", "ac_a", "bus1", "a")).Ok);
        Assert.Equal("DUP_EDGE", TopologyValidator.TryConnect(p, Edge("pcs1", "ac_a", "bus1", "a")).Code);
    }

    [Fact]
    public void Edit_allows_ac_to_dc_but_save_rejects_domain_mismatch()
    {
        var p = PlantWithRoles(Node("dc1", "dc_bus", "DC"));
        var edit = TopologyValidator.TryConnect(p, Edge("pcs1", "ac_a", "dc1", "pos_t"));
        Assert.True(edit.Ok, edit.Message);
        TopologyValidator.ApplyConnect(p, Edge("pcs1", "ac_a", "dc1", "pos_t"));

        var save = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(save.Ok);
        Assert.Equal("DOMAIN_MISMATCH", save.Code);
        Assert.Contains("pcs1", save.ProblemNodeIds);
    }

    [Fact]
    public void ValidateConnectionRules_rejects_what_edit_allows()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("pcs1", "pcs", "PCS"),
                Node("dc1", "dc_bus", "DC")
            }
        };
        var edge = Edge("pcs1", "ac_a", "dc1", "pos_t");
        Assert.True(TopologyValidator.TryConnect(p, edge).Ok);
        Assert.Equal("DOMAIN_MISMATCH", TopologyValidator.ValidateConnectionRules(p, edge).Code);
    }

    [Fact]
    public void Save_requires_grid_and_exactly_one_pcc_meter()
    {
        var noGrid = new TopologyProject
        {
            Nodes =
            {
                Node("brk1", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" })
            }
        };
        var r = TopologyValidator.ValidateProjectForSave(noGrid);
        Assert.False(r.Ok);
        Assert.Equal("NEED_GRID", r.Code);

        var noPcc = PlantWithRoles();
        noPcc.Nodes.RemoveAll(n => n.Id == "mPcc");
        r = TopologyValidator.ValidateProjectForSave(noPcc);
        Assert.False(r.Ok);
        Assert.Equal("NEED_PCC_METER", r.Code);

        var multiPcc = PlantWithRoles(Node("m2", "ac_meter", "第二PCC",
            new Dictionary<string, object?> { ["isPccMeter"] = true }));
        r = TopologyValidator.ValidateProjectForSave(multiPcc);
        Assert.False(r.Ok);
        Assert.Equal("MULTI_PCC_METER", r.Code);
        Assert.Contains("mPcc", r.ProblemNodeIds);
        Assert.Contains("m2", r.ProblemNodeIds);
    }

    [Fact]
    public void Save_rejects_meter_not_connected_to_bus()
    {
        var notBus = PlantWithRoles();
        Assert.True(Connect(notBus, Edge("mPcc", "pt_a", "brkMain", "a2")).Ok);
        var save = TopologyValidator.ValidateProjectForSave(notBus);
        Assert.False(save.Ok);
        Assert.Equal("METER_NOT_BUS", save.Code);
    }

    [Fact]
    public void Save_rejects_meter_pt_voltage_mismatch()
    {
        var ptBad = Energized35kV(Node("m1", "ac_meter", "错PT",
            new Dictionary<string, object?> { ["ptPrimaryVoltage"] = 220000d }));
        Assert.True(Connect(ptBad, Edge("m1", "pt_a", "bus35", "a2")).Ok);
        var save = TopologyValidator.ValidateProjectForSave(ptBad);
        Assert.False(save.Ok);
        Assert.Equal("METER_PT_MISMATCH", save.Code);
    }

    [Fact]
    public void Save_rejects_meter_phases_on_two_buses()
    {
        var multi = Energized35kV(
            Node("bus35b", "ac_bus", "35kV-B", new Dictionary<string, object?> { ["nominalVoltage"] = 35000d }, x: 400, y: 80),
            Node("m1", "ac_meter", "跨母线", new Dictionary<string, object?> { ["ptPrimaryVoltage"] = 35000d }, y: 200));
        Assert.True(Connect(multi, Edge("m1", "pt_a", "bus35", "a2")).Ok);
        Assert.True(Connect(multi, Edge("m1", "pt_b", "bus35b", "b2")).Ok);
        var save = TopologyValidator.ValidateProjectForSave(multi);
        Assert.False(save.Ok);
        Assert.Equal("METER_MULTI_BUS", save.Code);
    }

    [Fact]
    public void Save_rejects_pcs_voltage_mismatch_with_bus()
    {
        var p = Energized35kV(Node("pLow", "pcs", "690PCS",
            new Dictionary<string, object?> { ["emuId"] = "role_emu", ["acVoltage"] = 690d }));
        Assert.True(Connect(p, Edge("pLow", "ac_a", "bus35", "a2")).Ok);
        var save = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(save.Ok);
        Assert.Equal("PCS_BUS_MISMATCH", save.Code);
    }

    [Fact]
    public void Save_rejects_split_transformer_same_ear_on_two_buses()
    {
        var p = Energized35kV(
            Node("split1", "split_transformer", "双耳1",
                new Dictionary<string, object?> { ["emuId"] = "role_emu" }),
            Node("busL", "ac_bus", "左690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
            Node("busR", "ac_bus", "右690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }));

        Assert.True(Connect(p, Edge("split1", "pri_a", "bus35", "a2")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_l_a", "busL", "a")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_l_b", "busR", "b")).Ok);

        var save = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(save.Ok);
        Assert.Equal("SPLIT_XFMR_EAR_MULTI_BUS", save.Code);
    }

    [Fact]
    public void Save_rejects_split_transformer_primary_voltage_mismatch()
    {
        var p = Energized35kV(Node("split1", "split_transformer", "双耳1",
            new Dictionary<string, object?> { ["emuId"] = "role_emu", ["primaryVoltage"] = 10000d }));
        Assert.True(Connect(p, Edge("split1", "pri_a", "bus35", "a2")).Ok);
        var save = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(save.Ok);
        Assert.Equal("XFMR_BUS_MISMATCH", save.Code);
    }

    [Fact]
    public void Save_rejects_dangling_edge_to_missing_node()
    {
        var p = PlantWithRoles();
        p.Edges.Add(Edge("pcs1", "ac_a", "deleted-bus", "a2"));
        var save = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(save.Ok);
        Assert.Equal("NODE_MISSING", save.Code);
    }

    [Fact]
    public void RemoveEdge_deenergizes_bus_after_source_disconnect()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus1", "ac_bus", "母线")
            }
        };
        var e = Edge("grid1", "a", "bus1", "a");
        Assert.True(Connect(p, e).Ok);
        TopologyValidator.RefreshAcBusEnergization(p);
        Assert.True(p.Nodes.First(n => n.Id == "bus1").Parameters["energized"] is true);

        TopologyValidator.RemoveEdge(p, e.Id);
        Assert.Empty(p.Edges);
        Assert.False(p.Nodes.First(n => n.Id == "bus1").Parameters["energized"] is true);
    }

    [Fact]
    public void TryConnectBundle_null_or_already_connected()
    {
        var nullReq = TopologyValidator.TryConnectBundle(null!, Edge("a", "a", "b", "b"), out var updated);
        Assert.False(nullReq.Ok);
        Assert.Equal("BAD_REQUEST", nullReq.Code);
        Assert.Null(updated);

        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brk1", "ac_breaker", "主断")
            }
        };
        var seed = Edge("grid1", "a", "brk1", "a");
        var first = TopologyValidator.TryConnectBundle(p, seed, out var after);
        Assert.True(first.Ok, first.Message);
        Assert.Equal(3, after!.Edges.Count);

        var again = TopologyValidator.TryConnectBundle(after, seed, out var after2);
        Assert.True(again.Ok, again.Message);
        Assert.Equal("连接已存在", again.Message);
        Assert.Equal(3, after2!.Edges.Count);
        Assert.Empty(p.Edges);
    }

    [Fact]
    public void ConvertForApply_uses_save_validation_before_overlay()
    {
        var noGrid = new TopologyProject
        {
            Nodes =
            {
                Node("brk1", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" })
            }
        };
        var (_, v) = TopologyRuntimeConverter.ConvertForApply(noGrid);
        Assert.False(v.Ok);
        Assert.Equal("NEED_GRID", v.Code);

        var noPcc = PlantWithRoles();
        noPcc.Nodes.RemoveAll(n => n.Id == "mPcc");
        (_, v) = TopologyRuntimeConverter.ConvertForApply(noPcc);
        Assert.Equal("NEED_PCC_METER", v.Code);
    }

    private static TopologyProject Energized35kV(params TopologyNode[] extra)
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus220", "ac_bus", "220kV母线"),
                Node("xfmr1", "transformer", "主变"),
                Node("bus35", "ac_bus", "35kV母线"),
                Node("role_brk", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("role_m", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("role_emu", "emu", "EMU"),
                Node("role_pcs", "pcs", "PCS", new Dictionary<string, object?> { ["emuId"] = "role_emu" })
            }
        };
        foreach (var n in extra)
            p.Nodes.Add(n);

        Assert.True(Connect(p, Edge("grid1", "a", "bus220", "a")).Ok);
        Assert.True(Connect(p, Edge("grid1", "b", "bus220", "b")).Ok);
        Assert.True(Connect(p, Edge("grid1", "c", "bus220", "c")).Ok);
        Assert.True(Connect(p, Edge("xfmr1", "pri_a", "bus220", "a2")).Ok);
        Assert.True(Connect(p, Edge("xfmr1", "pri_b", "bus220", "b2")).Ok);
        Assert.True(Connect(p, Edge("xfmr1", "pri_c", "bus220", "c2")).Ok);
        Assert.True(Connect(p, Edge("xfmr1", "sec_a", "bus35", "a")).Ok);
        Assert.True(Connect(p, Edge("xfmr1", "sec_b", "bus35", "b")).Ok);
        Assert.True(Connect(p, Edge("xfmr1", "sec_c", "bus35", "c")).Ok);
        TopologyValidator.RefreshAcBusEnergization(p);
        return p;
    }
}
