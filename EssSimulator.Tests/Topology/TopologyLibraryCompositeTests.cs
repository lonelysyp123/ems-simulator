using EssSimulator.Web.Topology;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace EssSimulator.Tests.Topology;

public class TopologyLibraryCompositeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "esssim-topo-lib-" + Guid.NewGuid().ToString("N"));
    private readonly TopologyStore _store;

    public TopologyLibraryCompositeTests()
    {
        Directory.CreateDirectory(_root);
        _store = new TopologyStore(new FakeEnv(_root));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Save_Composite_RoundtripsRelativeNodesAndInternalEdges()
    {
        var saved = _store.SaveLibraryItem(new TopologyLibraryItem
        {
            Kind = "composite",
            Name = "5.5MW 馈线",
            Nodes =
            {
                new TopologyNode
                {
                    Id = "pA",
                    TemplateId = "pcs",
                    Label = "PCS-A",
                    X = 0,
                    Y = 0,
                    Parameters = new Dictionary<string, object?> { ["emuId"] = "stale", ["pcsRatedPowerKw"] = 1725d }
                },
                new TopologyNode
                {
                    Id = "bmsA",
                    TemplateId = "bms",
                    Label = "BMS-A",
                    X = 0,
                    Y = 160
                }
            },
            Edges =
            {
                new TopologyEdge { Id = "e1", FromNodeId = "pA", FromPortId = "dc_pos", ToNodeId = "bmsA", ToPortId = "dc_pos" },
                new TopologyEdge { Id = "dangling", FromNodeId = "pA", FromPortId = "ac_a", ToNodeId = "bus", ToPortId = "a2" }
            }
        });

        Assert.Equal("composite", saved.Kind);
        Assert.Equal("pcs", saved.TemplateId);
        Assert.Equal(2, saved.Nodes.Count);
        Assert.Single(saved.Edges);
        Assert.Equal("bmsA", saved.Edges[0].ToNodeId);
        Assert.False(saved.Nodes[0].Parameters.ContainsKey("emuId"));

        var reloaded = _store.GetLibraryItem(saved.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.IsComposite);
        Assert.Equal("5.5MW 馈线", reloaded.Name);
        Assert.Equal(2, reloaded.Nodes.Count);
        Assert.Equal("BMS-A", reloaded.Nodes[1].Label);
        Assert.Equal(160, reloaded.Nodes[1].Y);
        Assert.Single(reloaded.Edges);
    }

    [Fact]
    public void Save_Composite_RejectsFewerThanTwoNodes()
    {
        var ex = Assert.Throws<ArgumentException>(() => _store.SaveLibraryItem(new TopologyLibraryItem
        {
            Kind = "composite",
            Name = "不够",
            Nodes = { new TopologyNode { Id = "p1", TemplateId = "pcs" } }
        }));
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void Save_Composite_RejectsUnknownNodeTemplate()
    {
        Assert.Throws<ArgumentException>(() => _store.SaveLibraryItem(new TopologyLibraryItem
        {
            Kind = "composite",
            Nodes =
            {
                new TopologyNode { Id = "a", TemplateId = "pcs" },
                new TopologyNode { Id = "b", TemplateId = "not-a-template" }
            }
        }));
    }

    [Fact]
    public void Save_Device_StillRequiresKnownTemplate()
    {
        var saved = _store.SaveLibraryItem(new TopologyLibraryItem
        {
            Name = "单台 PCS",
            TemplateId = "pcs",
            Parameters = new Dictionary<string, object?> { ["pcsRatedPowerKw"] = 1000d }
        });
        Assert.Equal("device", saved.Kind);
        Assert.Empty(saved.Nodes);

        Assert.Throws<ArgumentException>(() => _store.SaveLibraryItem(new TopologyLibraryItem
        {
            Name = "坏",
            TemplateId = "nope"
        }));
    }

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public FakeEnv(string contentRoot) => ContentRootPath = contentRoot;
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "EssSimulator.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = "";
    }
}
