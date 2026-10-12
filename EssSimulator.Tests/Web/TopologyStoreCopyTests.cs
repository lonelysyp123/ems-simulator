using EssSimulator.Web.Topology;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace EssSimulator.Tests.Web;

/// <summary>
/// 组态工程复制功能：副本新 Id、默认命名、重名自动加序号、节点连线深拷贝。
/// </summary>
public class TopologyStoreCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "esssim-topo-copy-" + Guid.NewGuid().ToString("N"));
    private readonly TopologyStore _store;

    public TopologyStoreCopyTests()
    {
        Directory.CreateDirectory(_root);
        _store = new TopologyStore(new FakeEnv(_root));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private TopologyProject SeedProject(string name) => _store.SaveNamedProject(new TopologyProject
    {
        Name = name,
        Nodes =
        {
            new TopologyNode { Id = "n1", TemplateId = "pcs", X = 10, Y = 20 },
            new TopologyNode { Id = "n2", TemplateId = "pcs", X = 30, Y = 40 }
        },
        Edges =
        {
            new TopologyEdge { Id = "e1", FromNodeId = "n1", FromPortId = "ac_a", ToNodeId = "n2", ToPortId = "ac_a" }
        }
    });

    [Fact]
    public void Copy_CreatesNewId_DefaultCopyName_AndSameContent()
    {
        var src = SeedProject("10MW 示范站");

        var copy = _store.CopyNamedProject(src.Id);

        Assert.NotEqual(src.Id, copy.Id);
        Assert.Equal("10MW 示范站-副本", copy.Name);
        Assert.Equal(2, copy.Nodes.Count);
        Assert.Equal("pcs", copy.Nodes[0].TemplateId);
        Assert.Single(copy.Edges);

        // 原工程不受影响，两工程同时可见
        var list = _store.ListProjects();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, p => p.Id == src.Id && p.Name == "10MW 示范站");
        Assert.Contains(list, p => p.Id == copy.Id && p.Name == "10MW 示范站-副本");
    }

    [Fact]
    public void Copy_NameConflict_AutoSuffix()
    {
        var src = SeedProject("站点A");
        _store.CopyNamedProject(src.Id);                       // 站点A-副本
        var second = _store.CopyNamedProject(src.Id);          // 站点A-副本 2
        Assert.Equal("站点A-副本 2", second.Name);

        var custom = _store.CopyNamedProject(src.Id, "站点B");
        Assert.Equal("站点B", custom.Name);
        var customDup = _store.CopyNamedProject(src.Id, "站点B");
        Assert.Equal("站点B 2", customDup.Name);
    }

    [Fact]
    public void Copy_IsDeepCopy_MutatingCopyDoesNotAffectSource()
    {
        var src = SeedProject("深拷贝源");
        var copy = _store.CopyNamedProject(src.Id);

        copy.Nodes[0].X = 999;
        copy.Nodes.Add(new TopologyNode { Id = "n9", TemplateId = "pv_unit" });
        _store.SaveNamedProject(copy);

        var reloaded = _store.LoadNamedProject(src.Id)!;
        Assert.Equal(2, reloaded.Nodes.Count);
        Assert.Equal(10, reloaded.Nodes[0].X);
    }

    [Fact]
    public void Copy_MissingSource_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _store.CopyNamedProject("不存在"));
        Assert.Contains("不存在", ex.Message);
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
