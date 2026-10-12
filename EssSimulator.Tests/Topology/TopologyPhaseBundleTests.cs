using EssSimulator.Web.Topology;
using Xunit;

namespace EssSimulator.Tests.Topology;

public class TopologyPhaseBundleTests
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

    private static TopologyEdge Edge(string from, string fromPort, string to, string toPort) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        FromNodeId = from,
        FromPortId = fromPort,
        ToNodeId = to,
        ToPortId = toPort
    };

    [Fact]
    public void ExpandBundle_ac_phases_same_side_yields_three_edges()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brk1", "ac_breaker", "主断")
            }
        };

        var edges = TopologyValidator.ExpandBundle(p, Edge("grid1", "a", "brk1", "a"));
        Assert.Equal(3, edges.Count);
        Assert.Contains(edges, e => e.FromPortId == "a" && e.ToPortId == "a");
        Assert.Contains(edges, e => e.FromPortId == "b" && e.ToPortId == "b");
        Assert.Contains(edges, e => e.FromPortId == "c" && e.ToPortId == "c");
    }

    [Fact]
    public void ExpandBundle_breaker_bottom_to_bus_top_pairs_by_phase()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("brk1", "ac_breaker", "主断"),
                Node("bus1", "ac_bus", "HV")
            }
        };

        var edges = TopologyValidator.ExpandBundle(p, Edge("brk1", "a2", "bus1", "a"));
        Assert.Equal(3, edges.Count);
        Assert.Contains(edges, e => e.FromPortId == "a2" && e.ToPortId == "a");
        Assert.Contains(edges, e => e.FromPortId == "b2" && e.ToPortId == "b");
        Assert.Contains(edges, e => e.FromPortId == "c2" && e.ToPortId == "c");
    }

    [Fact]
    public void ExpandBundle_dc_polarity_yields_pos_and_neg()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("pcs1", "pcs", "PCS"),
                Node("dc1", "dc_bus", "DC")
            }
        };

        var edges = TopologyValidator.ExpandBundle(p, Edge("pcs1", "dc_pos", "dc1", "pos_t"));
        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, e => e.FromPortId == "dc_pos" && e.ToPortId == "pos_t");
        Assert.Contains(edges, e => e.FromPortId == "dc_neg" && e.ToPortId == "neg_t");
    }

    [Fact]
    public void ExpandBundle_split_transformer_left_ear_does_not_include_right_ear()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("split1", "split_transformer", "双耳"),
                Node("busL", "ac_bus", "左690")
            }
        };

        var edges = TopologyValidator.ExpandBundle(p, Edge("split1", "ear_l_a", "busL", "a"));
        Assert.Equal(3, edges.Count);
        Assert.Contains(edges, e => e.FromPortId == "ear_l_a" && e.ToPortId == "a");
        Assert.Contains(edges, e => e.FromPortId == "ear_l_b" && e.ToPortId == "b");
        Assert.Contains(edges, e => e.FromPortId == "ear_l_c" && e.ToPortId == "c");
        Assert.DoesNotContain(edges, e => e.FromPortId.StartsWith("ear_r_"));
    }

    [Fact]
    public void ExpandBundle_split_transformer_right_ear_pairs_independently()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("split1", "split_transformer", "双耳"),
                Node("busR", "ac_bus", "右690")
            }
        };

        var fromEar = TopologyValidator.ExpandBundle(p, Edge("split1", "ear_r_b", "busR", "b"));
        Assert.Equal(3, fromEar.Count);
        Assert.Contains(fromEar, e => e.FromPortId == "ear_r_a" && e.ToPortId == "a");
        Assert.Contains(fromEar, e => e.FromPortId == "ear_r_b" && e.ToPortId == "b");
        Assert.Contains(fromEar, e => e.FromPortId == "ear_r_c" && e.ToPortId == "c");
        Assert.DoesNotContain(fromEar, e => e.FromPortId.StartsWith("ear_l_"));

        var fromBus = TopologyValidator.ExpandBundle(p, Edge("busR", "a", "split1", "ear_r_a"));
        Assert.Equal(3, fromBus.Count);
        Assert.Contains(fromBus, e => e.FromPortId == "a" && e.ToPortId == "ear_r_a");
        Assert.Contains(fromBus, e => e.FromPortId == "b" && e.ToPortId == "ear_r_b");
        Assert.Contains(fromBus, e => e.FromPortId == "c" && e.ToPortId == "ear_r_c");
        Assert.DoesNotContain(fromBus, e => e.ToPortId.StartsWith("ear_l_"));
    }

    [Fact]
    public void TryConnectBundle_split_transformer_left_ear_does_not_wire_right_ear()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("split1", "split_transformer", "双耳"),
                Node("busL", "ac_bus", "左690"),
                Node("busR", "ac_bus", "右690")
            }
        };

        var r = TopologyValidator.TryConnectBundle(p, Edge("split1", "ear_l_a", "busL", "a"), out var afterLeft);
        Assert.True(r.Ok, r.Message);
        Assert.NotNull(afterLeft);
        Assert.Equal(3, afterLeft!.Edges.Count);
        Assert.All(afterLeft.Edges, e => Assert.StartsWith("ear_l_", e.FromPortId));
        Assert.DoesNotContain(afterLeft.Edges, e => e.FromPortId.StartsWith("ear_r_"));

        r = TopologyValidator.TryConnectBundle(afterLeft, Edge("split1", "ear_r_a", "busR", "a"), out var afterRight);
        Assert.True(r.Ok, r.Message);
        Assert.NotNull(afterRight);
        Assert.Equal(6, afterRight!.Edges.Count);
        Assert.Equal(3, afterRight.Edges.Count(e => e.FromPortId.StartsWith("ear_l_")));
        Assert.Equal(3, afterRight.Edges.Count(e => e.FromPortId.StartsWith("ear_r_")));
    }

    [Fact]
    public void TryConnectBundle_connects_three_phases_in_one_call()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brk1", "ac_breaker", "主断"),
                Node("bus1", "ac_bus", "HV")
            }
        };

        var r = TopologyValidator.TryConnectBundle(p, Edge("grid1", "a", "brk1", "a"), out var afterGrid);
        Assert.True(r.Ok);
        Assert.NotNull(afterGrid);
        Assert.Equal(3, afterGrid!.Edges.Count);

        r = TopologyValidator.TryConnectBundle(afterGrid, Edge("brk1", "a2", "bus1", "a"), out var afterBus);
        Assert.True(r.Ok);
        Assert.NotNull(afterBus);
        Assert.Equal(6, afterBus!.Edges.Count);

        TopologyValidator.RefreshAcBusEnergization(afterBus);
        var bus = afterBus.Nodes.First(n => n.Id == "bus1");
        Assert.True(bus.Parameters.TryGetValue("energized", out var en) && en is true);
    }

    [Fact]
    public void TryConnectBundle_allows_electrical_invalid_during_edit()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("pcs1", "pcs", "PCS"),
                Node("bus1", "ac_bus", "未带电母线")
            }
        };

        var r = TopologyValidator.TryConnectBundle(p, Edge("pcs1", "ac_a", "bus1", "a"), out var updated);
        Assert.True(r.Ok, r.Message);
        Assert.NotNull(updated);
        Assert.NotEmpty(updated!.Edges);
        Assert.Empty(p.Edges);
    }

    [Fact]
    public void Save_validation_exposes_problem_node_ids_for_multi_main_breaker()
    {
        var p = new TopologyProject
        {
            Nodes =
            {
                Node("grid1", "grid", "电网"),
                Node("brk1", "ac_breaker", "主断A", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("brk2", "ac_breaker", "主断B", new Dictionary<string, object?> { ["isMainBreaker"] = true }),
                Node("m1", "ac_meter", "PCC", new Dictionary<string, object?> { ["isPccMeter"] = true })
            }
        };

        var r = TopologyValidator.ValidateProjectForSave(p);
        Assert.False(r.Ok);
        Assert.Equal("MULTI_MAIN_BREAKER", r.Code);
        Assert.Contains("brk1", r.ProblemNodeIds);
        Assert.Contains("brk2", r.ProblemNodeIds);
    }
}
