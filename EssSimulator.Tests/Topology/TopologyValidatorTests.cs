using EssSimulator.Web.Topology;
using Xunit;

namespace EssSimulator.Tests.Topology;

public class TopologyValidatorTests
{
    private static TopologyNode Node(string id, string templateId, string label, Dictionary<string, object?>? parameters = null)
    {
        var tpl = TopologyTemplates.Get(templateId)!;
        var p = new Dictionary<string, object?>(tpl.DefaultParameters);
        if (parameters != null)
        {
            foreach (var kv in parameters)
                p[kv.Key] = kv.Value;
        }

        return new TopologyNode
        {
            Id = id,
            TemplateId = templateId,
            Label = label,
            Parameters = p
        };
    }

    private static void EnsureSaveRoles(TopologyProject p)
    {
        if (!p.Nodes.Any(n => n.TemplateId == "grid"))
        {
            var n = Node("role_grid", "grid", "电网");
            n.Y = -400;
            p.Nodes.Add(n);
        }

        if (!p.Nodes.Any(n => n.TemplateId == "ac_breaker" && TopologyParamHelper.GetBool(n.Parameters, "isMainBreaker")))
        {
            var n = Node("role_brk", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true });
            n.Y = -360;
            p.Nodes.Add(n);
        }

        if (!p.Nodes.Any(n => n.TemplateId == "ac_meter" && TopologyParamHelper.GetBool(n.Parameters, "isPccMeter")))
        {
            var n = Node("role_m", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true });
            n.Y = -320;
            p.Nodes.Add(n);
        }

        // 保存级校验要求至少一个含 PCS 的 EMU；补一个不参与连线的独立单元，避免干扰连线回放用例
        if (!p.Nodes.Any(n => n.TemplateId == "emu") && !p.Nodes.Any(n => n.TemplateId == "pv_unit"))
        {
            p.Nodes.Add(Node("role_emu", "emu", "EMU"));
            p.Nodes.Add(Node("role_pcs", "pcs", "PCS", new Dictionary<string, object?> { ["emuId"] = "role_emu" }));
        }
    }

    private static TopologyValidationResult Save(TopologyProject p)
    {
        EnsureSaveRoles(p);
        return TopologyValidator.ValidateProjectForSave(p);
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

    [Fact]
    public void Edit_allows_pcs_on_bus_top_save_rejects()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("bus1", "ac_bus", "35kV母线"),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" })
            }
        };

        var r = Connect(p, Edge("pcs1", "ac_a", "bus1", "a"));
        Assert.True(r.Ok, r.Message);
        Assert.NotEmpty(p.Edges);

        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("BUS_TOP_SOURCE_ONLY", save.Code);
    }

    [Fact]
    public void Save_reports_first_error_from_top_to_bottom()
    {
        var bus = Node("bus1", "ac_bus", "母线");
        bus.Y = 200;
        var grid = Node("grid1", "grid", "电网");
        grid.Y = 40;
        var pcs = Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" });
        pcs.Y = 400;
        var p = new TopologyProject { Nodes = { grid, bus, Node("emu1", "emu", "EMU-1"), pcs } };

        Assert.True(Connect(p, Edge("pcs1", "ac_a", "bus1", "a")).Ok);
        Assert.True(Connect(p, Edge("grid1", "a", "bus1", "b")).Ok);

        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("PHASE_MISMATCH", save.Code);
    }

    [Fact]
    public void Bus_bottom_allows_device_without_voltage_source()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("bus1", "ac_bus", "35kV母线"),
                Node("pcs1", "pcs", "PCS-1")
            }
        };

        var r = Connect(p, Edge("pcs1", "ac_a", "bus1", "a2"));
        Assert.True(r.Ok, r.Message);
        TopologyValidator.RefreshAcBusEnergization(p);
        var bus = p.Nodes.First(n => n.Id == "bus1");
        Assert.False(bus.Parameters.TryGetValue("energized", out var en) && en is true);
    }

    [Fact]
    public void Bus_bottom_rejects_voltage_source()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus1", "ac_bus", "母线")
            }
        };

        var r = Connect(p, Edge("grid1", "a", "bus1", "a2"));
        Assert.True(r.Ok, r.Message);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("BUS_BOTTOM_LOAD_ONLY", save.Code);
    }

    [Fact]
    public void Bus_top_port_is_exclusive()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("xfmr1", "transformer", "主变", new Dictionary<string, object?>
                {
                    ["primaryVoltage"] = 220000d,
                    ["secondaryVoltage"] = 35000d
                }),
                Node("bus1", "ac_bus", "母线")
            }
        };

        Assert.True(Connect(p, Edge("grid1", "a", "bus1", "a")).Ok);
        Assert.True(Connect(p, Edge("xfmr1", "sec_a", "bus1", "a")).Ok);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("PORT_BUSY", save.Code);
    }

    [Fact]
    public void Bus_rejects_second_voltage_source()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("grid2", "grid", "第二电网", new Dictionary<string, object?> { ["outputVoltage"] = 220000d }),
                Node("bus1", "ac_bus", "母线")
            }
        };

        Assert.True(Connect(p, Edge("grid1", "a", "bus1", "a")).Ok);
        Assert.True(Connect(p, Edge("grid2", "b", "bus1", "b")).Ok);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("BUS_MULTI_SOURCE", save.Code);
    }

    [Fact]
    public void Transformer_primary_can_attach_to_energized_bus()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus1", "ac_bus", "母线"),
                Node("xfmr1", "transformer", "主变")
            }
        };
        Assert.True(Connect(p, Edge("grid1", "a", "bus1", "a")).Ok);
        var r = Connect(p, Edge("xfmr1", "pri_a", "bus1", "a2"));
        Assert.True(r.Ok);
    }

    [Fact]
    public void Ac_bus_port_allows_multiple_downstream_devices()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "35kV电源", new Dictionary<string, object?> { ["outputVoltage"] = 35000d }),
                Node("bus1", "ac_bus", "35kV母线"),
                Node("pcs1", "pcs", "PCS-1"),
                Node("pcs2", "pcs", "PCS-2")
            }
        };
        Assert.True(Connect(p, Edge("grid1", "a", "bus1", "a")).Ok);
        Assert.True(Connect(p, Edge("grid1", "b", "bus1", "b")).Ok);
        Assert.True(Connect(p, Edge("grid1", "c", "bus1", "c")).Ok);

        // 同一母线 A' 拐角挂两台 PCS
        Assert.True(Connect(p, Edge("pcs1", "ac_a", "bus1", "a2")).Ok);
        Assert.True(Connect(p, Edge("pcs2", "ac_a", "bus1", "a2")).Ok);
        Assert.Equal(2, p.Edges.Count(e =>
            (e.FromNodeId == "bus1" && e.FromPortId == "a2") ||
            (e.ToNodeId == "bus1" && e.ToPortId == "a2")));
    }

    [Fact]
    public void Dc_polarity_must_match()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" }),
                Node("dc1", "dc_bus", "DC母线"),
                Node("bms1", "bms", "BMS-1")
            }
        };

        Assert.True(Connect(p, Edge("pcs1", "dc_pos", "dc1", "pos_t")).Ok);
        Assert.True(Connect(p, Edge("pcs1", "dc_neg", "dc1", "neg_t")).Ok);
        Assert.True(Connect(p, Edge("bms1", "dc_pos", "dc1", "neg_b")).Ok);

        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("DC_POLARITY", save.Code);
    }

    [Fact]
    public void Device_port_still_exclusive()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus1", "ac_bus", "母线"),
                Node("bus2", "ac_bus", "母线2")
            }
        };
        Assert.True(Connect(p, Edge("grid1", "a", "bus1", "a")).Ok);
        Assert.True(Connect(p, Edge("grid1", "a", "bus2", "a")).Ok);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("PORT_BUSY", save.Code);
    }

    [Fact]
    public void Transformer_requires_step_down_and_bus_voltage_match()
    {
        var bad = Node("xfmr", "transformer", "坏变", new Dictionary<string, object?>
        {
            ["primaryVoltage"] = 35000d,
            ["secondaryVoltage"] = 220000d
        });
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus1", "ac_bus", "母线"),
                bad
            }
        };
        Assert.True(Connect(p, Edge("grid1", "a", "bus1", "a")).Ok);
        Assert.True(Connect(p, Edge("xfmr", "pri_a", "bus1", "a2")).Ok);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("XFMR_RATIO", save.Code);
    }

    [Fact]
    public void Transformer_bus_voltage_mismatch_rejected()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus1", "ac_bus", "母线"),
                Node("xfmr1", "transformer", "主变", new Dictionary<string, object?>
                {
                    ["primaryVoltage"] = 110000d,
                    ["secondaryVoltage"] = 35000d
                })
            }
        };
        Assert.True(Connect(p, Edge("grid1", "a", "bus1", "a")).Ok);
        Assert.True(Connect(p, Edge("xfmr1", "pri_a", "bus1", "a2")).Ok);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("XFMR_BUS_MISMATCH", save.Code);
    }

    [Fact]
    public void Meter_must_connect_to_bus_with_matching_phase_and_pt()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus1", "ac_bus", "母线"),
                Node("m1", "ac_meter", "电表")
            }
        };
        Assert.True(Connect(p, Edge("grid1", "a", "bus1", "a")).Ok);
        Assert.True(Connect(p, Edge("grid1", "b", "bus1", "b")).Ok);
        Assert.True(Connect(p, Edge("grid1", "c", "bus1", "c")).Ok);

        var phaseBad = Connect(p, Edge("m1", "pt_a", "bus1", "b2"));
        Assert.True(phaseBad.Ok, phaseBad.Message);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("PHASE_MISMATCH", save.Code);
    }

    [Fact]
    public void Phase_mismatch_rejected()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus1", "ac_bus", "母线")
            }
        };
        var r = Connect(p, Edge("grid1", "a", "bus1", "b"));
        Assert.True(r.Ok, r.Message);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("PHASE_MISMATCH", save.Code);
    }

    [Fact]
    public void Save_validation_requires_grid_main_breaker_and_pcc_meter()
    {
        var incomplete = new TopologyProject
        {
            Nodes = { Node("grid1", "grid", "电网") }
        };
        var r0 = TopologyValidator.ValidateProjectForSave(incomplete);
        Assert.False(r0.Ok);
        Assert.Equal("NEED_MAIN_BREAKER", r0.Code);

        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brk1", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" })
            }
        };
        var ok = TopologyValidator.ValidateProjectForSave(p);
        Assert.True(ok.Ok, ok.Message);

        p.Nodes.Add(Node("brk2", "ac_breaker", "另一断", new Dictionary<string, object?> { ["isMainBreaker"] = true }));
        var multi = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(multi.Ok);
        Assert.Equal("MULTI_MAIN_BREAKER", multi.Code);
    }

    [Fact]
    public void Bus_top_accepts_ac_breaker_and_energizes_through_when_closed()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brk1", "ac_breaker", "进线断路器"),
                Node("bus1", "ac_bus", "220kV母线")
            }
        };

        Assert.True(Connect(p, Edge("grid1", "a", "brk1", "a")).Ok);
        Assert.True(Connect(p, Edge("grid1", "b", "brk1", "b")).Ok);
        Assert.True(Connect(p, Edge("grid1", "c", "brk1", "c")).Ok);

        Assert.True(Connect(p, Edge("brk1", "a2", "bus1", "a")).Ok);
        Assert.True(Connect(p, Edge("brk1", "b2", "bus1", "b")).Ok);
        Assert.True(Connect(p, Edge("brk1", "c2", "bus1", "c")).Ok);

        TopologyValidator.RefreshAcBusEnergization(p);
        var bus = p.Nodes.First(n => n.Id == "bus1");
        Assert.True(bus.Parameters.TryGetValue("energized", out var en) && en is true);
        Assert.Equal(220000d, TopologyParamHelper.GetDouble(bus.Parameters, "nominalVoltage", 0), 1);
    }

    [Fact]
    public void Bus_top_breaker_open_does_not_energize()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brk1", "ac_breaker", "进线断路器", new Dictionary<string, object?> { ["closed"] = false }),
                Node("bus1", "ac_bus", "220kV母线")
            }
        };

        Assert.True(Connect(p, Edge("grid1", "a", "brk1", "a")).Ok);
        Assert.True(Connect(p, Edge("brk1", "a2", "bus1", "a")).Ok);

        TopologyValidator.RefreshAcBusEnergization(p);
        var bus = p.Nodes.First(n => n.Id == "bus1");
        Assert.False(bus.Parameters.TryGetValue("energized", out var en) && en is true);
    }

    [Fact]
    public void Happy_path_grid_bus_transformer_energizes_secondary_bus()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus220", "ac_bus", "220kV母线"),
                Node("xfmr1", "transformer", "主变"),
                Node("bus35", "ac_bus", "35kV母线"),
                Node("pcs1", "pcs", "PCS-1")
            }
        };

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
        Assert.True(TopologyParamHelper.GetDouble(p.Nodes.First(n => n.Id == "bus35").Parameters, "nominalVoltage") > 0);

        Assert.True(Connect(p, Edge("pcs1", "ac_a", "bus35", "a2")).Ok);
    }

    [Fact]
    public void Closed_breaker_propagates_energization_to_downstream_bus()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网", new Dictionary<string, object?> { ["outputVoltage"] = 35000d }),
                Node("busUp", "ac_bus", "上段母线"),
                Node("brk1", "ac_breaker", "分段断路器", new Dictionary<string, object?>
                {
                    ["ratedVoltage"] = 35000d,
                    ["closed"] = true
                }),
                Node("busDown", "ac_bus", "下段母线")
            }
        };

        Assert.True(Connect(p, Edge("grid1", "a", "busUp", "a")).Ok);
        Assert.True(Connect(p, Edge("brk1", "a", "busUp", "a2")).Ok);
        Assert.True(Connect(p, Edge("brk1", "a2", "busDown", "a")).Ok);

        TopologyValidator.RefreshAcBusEnergization(p);
        var down = p.Nodes.First(n => n.Id == "busDown");
        Assert.True(down.Parameters.TryGetValue("energized", out var en) && en is true);
        Assert.Equal(35000d, TopologyParamHelper.GetDouble(down.Parameters, "nominalVoltage", 0), 1);
    }

    [Fact]
    public void Open_breaker_does_not_propagate_energization_to_downstream_bus()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网", new Dictionary<string, object?> { ["outputVoltage"] = 35000d }),
                Node("busUp", "ac_bus", "上段母线"),
                Node("brk1", "ac_breaker", "分段断路器", new Dictionary<string, object?>
                {
                    ["ratedVoltage"] = 35000d,
                    ["closed"] = false
                }),
                Node("busDown", "ac_bus", "下段母线")
            }
        };

        Assert.True(Connect(p, Edge("grid1", "a", "busUp", "a")).Ok);
        Assert.True(Connect(p, Edge("brk1", "a", "busUp", "a2")).Ok);
        Assert.True(Connect(p, Edge("brk1", "a2", "busDown", "a")).Ok);

        TopologyValidator.RefreshAcBusEnergization(p);
        Assert.True(p.Nodes.First(n => n.Id == "busUp").Parameters.TryGetValue("energized", out var upEn) && upEn is true);
        Assert.False(p.Nodes.First(n => n.Id == "busDown").Parameters.TryGetValue("energized", out var downEn) && downEn is true);
    }

    [Fact]
    public void Save_rejects_pcs_without_valid_emu_assignment()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brk1", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-未归属"),
                Node("pcs2", "pcs", "PCS-悬空", new Dictionary<string, object?> { ["emuId"] = "not_exist" })
            }
        };

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(r.Ok);
        Assert.Equal("PCS_EMU_UNASSIGNED", r.Code);
        Assert.Contains("pcs1", r.ProblemNodeIds);
        Assert.Contains("pcs2", r.ProblemNodeIds);
    }

    [Fact]
    public void Save_rejects_two_breakers_bound_to_the_same_emu()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brkMain", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" }),
                Node("ub1", "ac_breaker", "单元断1", new Dictionary<string, object?> { ["emuId"] = "emu1" }),
                Node("ub2", "ac_breaker", "单元断2", new Dictionary<string, object?> { ["emuId"] = "emu1" })
            }
        };

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(r.Ok);
        Assert.Equal("EMU_BREAKER_DUPLICATE", r.Code);
        Assert.Contains("ub1", r.ProblemNodeIds);
        Assert.Contains("ub2", r.ProblemNodeIds);
    }

    [Fact]
    public void Save_rejects_two_meters_bound_to_the_same_emu()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brkMain", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" }),
                Node("um1", "ac_meter", "单元表 1"),
                Node("um2", "ac_meter", "单元表 2")
            }
        };
        p.Nodes.First(n => n.Id == "um1").Parameters["emuId"] = "emu1";
        p.Nodes.First(n => n.Id == "um2").Parameters["emuId"] = "emu1";

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(r.Ok);
        Assert.Equal("EMU_METER_DUPLICATE", r.Code);
        Assert.Contains("um1", r.ProblemNodeIds);
        Assert.Contains("um2", r.ProblemNodeIds);
    }

    private static TopologyProject GroupBindingProject()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brkMain", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("e1", "emu", "EMU-1"),
                Node("g1", "emu_group", "分组-1", new Dictionary<string, object?> { ["emuId"] = "e1" })
            }
        };
        return p;
    }

    [Fact]
    public void Save_rejects_device_bound_to_missing_group()
    {
        var p = GroupBindingProject();
        p.Nodes.Add(Node("pcs1", "pcs", "PCS-1",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g-not-exist" }));

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(r.Ok);
        Assert.Equal("GROUP_UNASSIGNED", r.Code);
        Assert.Contains("pcs1", r.ProblemNodeIds);
    }

    [Fact]
    public void Save_rejects_group_emu_mismatch()
    {
        var p = GroupBindingProject();
        p.Nodes.Add(Node("e2", "emu", "EMU-2"));
        // 分组属于 e1，但设备自身 emuId 为 e2
        p.Nodes.Add(Node("pcs1", "pcs", "PCS-1",
            new Dictionary<string, object?> { ["emuId"] = "e2", ["groupId"] = "g1" }));

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(r.Ok);
        Assert.Equal("GROUP_EMU_MISMATCH", r.Code);
        Assert.Contains("pcs1", r.ProblemNodeIds);
    }

    [Fact]
    public void Save_rejects_two_breakers_in_one_group()
    {
        var p = GroupBindingProject();
        p.Nodes.Add(Node("pcs1", "pcs", "PCS-1",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1" }));
        p.Nodes.Add(Node("gb1", "ac_breaker", "组断 1",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1" }));
        p.Nodes.Add(Node("gb2", "ac_breaker", "组断 2",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1" }));

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(r.Ok);
        Assert.Equal("EMU_GROUP_BREAKER_DUPLICATE", r.Code);
        Assert.Contains("gb1", r.ProblemNodeIds);
        Assert.Contains("gb2", r.ProblemNodeIds);
    }

    [Fact]
    public void Save_accepts_two_meters_in_one_group()
    {
        // 组级电表允许多台（运行期按绑定顺序逐台建镜像）
        var p = GroupBindingProject();
        p.Nodes.Add(Node("pcs1", "pcs", "PCS-1",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1" }));
        p.Nodes.Add(Node("gm1", "ac_meter", "组表 1",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1" }));
        p.Nodes.Add(Node("gm2", "ac_meter", "组表 2",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1" }));

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.True(r.Ok, r.Message);
    }

    [Fact]
    public void Save_accepts_valid_group_bindings()
    {
        var p = GroupBindingProject();
        p.Nodes.Add(Node("pcs1", "pcs", "PCS-1",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1" }));
        p.Nodes.Add(Node("gb1", "ac_breaker", "组断",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1" }));
        p.Nodes.Add(Node("gm1", "ac_meter", "组表",
            new Dictionary<string, object?> { ["emuId"] = "e1", ["groupId"] = "g1" }));

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.True(r.Ok, r.Message);
    }

    [Fact]
    public void Save_rejects_device_bound_to_missing_emu()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brkMain", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" }),
                Node("ub1", "ac_breaker", "单元断-悬空", new Dictionary<string, object?> { ["emuId"] = "ghost" })
            }
        };

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(r.Ok);
        Assert.Equal("EMU_DEVICE_UNASSIGNED", r.Code);
        Assert.Contains("ub1", r.ProblemNodeIds);
    }

    [Fact]
    public void Save_accepts_optional_single_breaker_and_meter_binding()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brkMain", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" }),
                Node("ub1", "ac_breaker", "单元断", new Dictionary<string, object?> { ["emuId"] = "emu1" }),
                Node("um1", "ac_meter", "单元表", new Dictionary<string, object?> { ["emuId"] = "emu1" })
            }
        };

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.True(r.Ok, r.Message);
    }

    [Fact]
    public void Save_accepts_emu_without_any_bound_device()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brkMain", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-1"),
                Node("pcs1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "emu1" })
            }
        };

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.True(r.Ok, r.Message);
    }

    [Fact]
    public void Save_rejects_plant_without_generation_unit()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brk1", "ac_breaker", "主断", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC表", new Dictionary<string, object?> { ["isPccMeter"] = true }),
                Node("emu1", "emu", "EMU-无PCS")
            }
        };

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(r.Ok);
        Assert.Equal("NO_GENERATION_UNIT", r.Code);
    }

    [Fact]
    public void Pv_unit_connects_to_energized_35kv_bus()
    {
        var p = Energized35kVPlant(Node("pv1", "pv_unit", "光伏单元-1"));

        var r = Connect(p, Edge("pv1", "ac_a", "bus35", "a2"));
        Assert.True(r.Ok, r.Message);
        Assert.Contains(p.Edges, e => e.FromNodeId == "pv1" || e.ToNodeId == "pv1");
    }

    [Fact]
    public void Pv_unit_rejects_voltage_mismatch_with_bus()
    {
        var p = Energized35kVPlant(Node("pv1", "pv_unit", "光伏单元-1",
            new Dictionary<string, object?> { ["acVoltage"] = 690d }));

        var r = Connect(p, Edge("pv1", "ac_a", "bus35", "a2"));
        Assert.True(r.Ok, r.Message);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("PV_BUS_MISMATCH", save.Code);
    }

    [Fact]
    public void Pv_unit_rejects_unknown_module_model()
    {
        var p = Energized35kVPlant(Node("pv1", "pv_unit", "光伏单元-1",
            new Dictionary<string, object?> { ["moduleModel"] = "NO-SUCH" }));

        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("PV_UNKNOWN_MODULE", save.Code);
    }

    [Fact]
    public void Split_transformer_requires_emu_and_rejects_more_than_two_on_same_emu()
    {
        var p = Energized35kVPlant(
            Node("split1", "split_transformer", "双耳1"),
            Node("busL", "ac_bus", "左耳690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
            Node("busR", "ac_bus", "右耳690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }));

        Assert.True(Connect(p, Edge("split1", "pri_a", "bus35", "a2")).Ok);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("SPLIT_XFMR_EMU_REQUIRED", save.Code);

        p.Nodes.First(n => n.Id == "split1").Parameters["emuId"] = "role_emu";
        p.Nodes.Add(Node("split2", "split_transformer", "双耳2",
            new Dictionary<string, object?> { ["emuId"] = "role_emu" }));
        save = Save(p);
        Assert.True(save.Ok, save.Message);

        p.Nodes.Add(Node("split3", "split_transformer", "双耳3",
            new Dictionary<string, object?> { ["emuId"] = "role_emu" }));
        save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("SPLIT_XFMR_EMU_DUPLICATE", save.Code);
    }

    [Fact]
    public void Split_transformer_rejects_both_ears_on_same_bus()
    {
        var p = Energized35kVPlant(
            Node("split1", "split_transformer", "双耳1",
                new Dictionary<string, object?> { ["emuId"] = "role_emu" }),
            Node("bus690", "ac_bus", "690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }));

        Assert.True(Connect(p, Edge("split1", "pri_a", "bus35", "a2")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_l_a", "bus690", "a")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_r_b", "bus690", "b")).Ok);
        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("SPLIT_XFMR_EARS_SAME_BUS", save.Code);
    }

    [Fact]
    public void Split_transformer_energizes_both_ear_buses()
    {
        var p = Energized35kVPlant(
            Node("split1", "split_transformer", "双耳1",
                new Dictionary<string, object?> { ["emuId"] = "role_emu" }),
            Node("busL", "ac_bus", "左耳690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
            Node("busR", "ac_bus", "右耳690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }));

        Assert.True(Connect(p, Edge("split1", "pri_a", "bus35", "a2")).Ok);
        Assert.True(Connect(p, Edge("split1", "pri_b", "bus35", "b2")).Ok);
        Assert.True(Connect(p, Edge("split1", "pri_c", "bus35", "c2")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_l_a", "busL", "a")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_l_b", "busL", "b")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_l_c", "busL", "c")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_r_a", "busR", "a")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_r_b", "busR", "b")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_r_c", "busR", "c")).Ok);

        TopologyValidator.RefreshAcBusEnergization(p);
        Assert.True(TopologyParamHelper.GetDouble(p.Nodes.First(n => n.Id == "busL").Parameters, "nominalVoltage") > 0);
        Assert.True(TopologyParamHelper.GetDouble(p.Nodes.First(n => n.Id == "busR").Parameters, "nominalVoltage") > 0);
        var save = Save(p);
        Assert.True(save.Ok, save.Message);
    }

    [Fact]
    public void Split_transformers_reject_shared_pcs_on_same_emu()
    {
        var p = Energized35kVPlant(
            Node("split1", "split_transformer", "双耳1",
                new Dictionary<string, object?> { ["emuId"] = "role_emu" }),
            Node("split2", "split_transformer", "双耳2",
                new Dictionary<string, object?> { ["emuId"] = "role_emu" }),
            Node("busL", "ac_bus", "左耳690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
            Node("busR", "ac_bus", "右耳690", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
            Node("busR2", "ac_bus", "右耳2", new Dictionary<string, object?> { ["nominalVoltage"] = 690d }),
            Node("p1", "pcs", "PCS-1", new Dictionary<string, object?> { ["emuId"] = "role_emu", ["acVoltage"] = 690d }));

        Assert.True(Connect(p, Edge("split1", "pri_a", "bus35", "a2")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_l_a", "busL", "a")).Ok);
        Assert.True(Connect(p, Edge("split1", "ear_r_a", "busR", "a")).Ok);
        Assert.True(Connect(p, Edge("split2", "pri_a", "bus35", "b2")).Ok);
        Assert.True(Connect(p, Edge("split2", "ear_l_a", "busL", "b")).Ok);
        Assert.True(Connect(p, Edge("split2", "ear_r_a", "busR2", "a")).Ok);
        Assert.True(Connect(p, Edge("p1", "ac_a", "busL", "a2")).Ok);

        var save = Save(p);
        Assert.False(save.Ok);
        Assert.Equal("SPLIT_XFMR_PCS_OVERLAP", save.Code);
    }

    private static TopologyProject Energized35kVPlant(params TopologyNode[] extra)
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("bus220", "ac_bus", "220kV母线"),
                Node("xfmr1", "transformer", "主变"),
                Node("bus35", "ac_bus", "35kV母线")
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
