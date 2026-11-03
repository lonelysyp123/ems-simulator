using EssSimulator.Configuration;
using EssSimulator.Protocol.Modbus;
using EssSimulator.Web.Topology;

namespace EssSimulator.Tests.Topology;

/// <summary>
/// 组态三相断路器 → simMv 端口计划：顺序、角色与标签必须和组态一致。
/// </summary>
public class MvBreakerProtocolLayoutTests
{
    private static TopologyNode Node(string id, string templateId, double x, double y,
        string label = "", params (string Key, object? Value)[] parameters)
    {
        var node = new TopologyNode { Id = id, TemplateId = templateId, Label = label, X = x, Y = y };
        foreach (var (key, value) in parameters)
            node.Parameters[key] = value;
        return node;
    }

    private static TopologyProject Project(params TopologyNode[] nodes)
    {
        var project = new TopologyProject { Id = "p", Name = "测试工程" };
        project.Nodes.AddRange(nodes);
        return project;
    }

    [Fact]
    public void Enumerate_OrdersByCanvasPosition_AndAssignsRoles()
    {
        // 画布上从上到下：主断、单元断、支路断；EMU 在 PCS 归属里
        var project = Project(
            Node("brk-main", "ac_breaker", 100, 50, "主断", ("isMainBreaker", true)),
            Node("emu1", "emu", 100, 100),
            Node("pcs1", "pcs", 100, 120, "", ("emuId", "emu1")),
            Node("brk-unit", "ac_breaker", 100, 150, "单元断", ("emuId", "emu1")),
            Node("brk-other", "ac_breaker", 100, 200, "支路断"));

        var list = MvBreakerProtocolLayout.Enumerate(project);

        Assert.Equal(new[] { "brk-main", "brk-unit", "brk-other" }, list.Select(b => b.NodeId));
        Assert.Equal(MvBreakerRole.Main, list[0].Role);
        Assert.Equal(MvBreakerRole.Unit, list[1].Role);
        Assert.Equal(1, list[1].UnitId);
        Assert.Equal(MvBreakerRole.Other, list[2].Role);
        Assert.Equal(0, list[2].UnitId);
        Assert.Equal("主断", list[0].Label);
    }

    [Fact]
    public void Enumerate_GroupBreaker_StaysOther()
    {
        // 带 groupId 的是分组断路器，不接管单元
        var project = Project(
            Node("emu1", "emu", 0, 0),
            Node("pcs1", "pcs", 0, 10, "", ("emuId", "emu1")),
            Node("brk-group", "ac_breaker", 0, 20, "分组断", ("emuId", "emu1"), ("groupId", "grp1")));

        var list = MvBreakerProtocolLayout.Enumerate(project);

        Assert.Single(list);
        Assert.Equal(MvBreakerRole.Other, list[0].Role);
    }

    [Fact]
    public void PortPlan_CreatesOneSimMvPerBreaker_WithPortAndLabel()
    {
        var cfg = new SimulatorConfig
        {
            Devices = new List<EssUnitConfig> { new() { Pcs = new List<PcsDeviceConfig> { new() } } },
            MvBreakers = new List<MvBreakerEndpoint>
            {
                new() { NodeId = "a", Label = "主断", Role = MvBreakerRole.Main },
                new() { NodeId = "b", Label = "单元断1", Role = MvBreakerRole.Unit, UnitId = 1 }
            }
        };

        var plan = ProtocolPortPlan.BuildDefault(cfg);
        var mv = plan.Entries.Where(e => e.Type == ProtocolDeviceType.Mv).ToList();

        Assert.Equal(2, mv.Count);
        Assert.Equal("simMv1", mv[0].Name);
        Assert.Equal(2301, mv[0].Port);
        Assert.Equal("主断", mv[0].Label);
        Assert.Equal("simMv2", mv[1].Name);
        Assert.Equal(2302, mv[1].Port);
        Assert.Equal("单元断1", mv[1].Label);
    }

    [Fact]
    public void ExpectedListenCount_IncludesBreakers()
    {
        var cfg = new SimulatorConfig
        {
            Devices = new List<EssUnitConfig> { new() { Pcs = new List<PcsDeviceConfig> { new() } } }
        };
        int without = ProtocolPortPlan.ExpectedListenCount(cfg);

        cfg.MvBreakers = new List<MvBreakerEndpoint>
        {
            new() { NodeId = "a" }, new() { NodeId = "b" }
        };

        Assert.Equal(without + 2, ProtocolPortPlan.ExpectedListenCount(cfg));
    }
}
