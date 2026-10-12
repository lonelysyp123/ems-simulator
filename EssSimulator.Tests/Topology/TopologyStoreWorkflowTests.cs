using EssSimulator.Web.Topology;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace EssSimulator.Tests.Topology;

/// <summary>
/// 组态工程持久化：保存/打开、残留连线清理、旧电表口迁移、overlay 落盘。
/// </summary>
public class TopologyStoreWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "esssim-topo-wf-" + Guid.NewGuid().ToString("N"));
    private readonly TopologyStore _store;

    public TopologyStoreWorkflowTests()
    {
        Directory.CreateDirectory(_root);
        _store = new TopologyStore(new FakeEnv(_root));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Save_and_reload_scaffold_stays_saveable()
    {
        var project = TopologyScaffold.BuildRadial(emuCount: 2, name: "入库径向");
        var saved = _store.SaveNamedProject(project);
        var loaded = _store.LoadNamedProject(saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal("入库径向", loaded!.Name);
        Assert.Equal(2, loaded.Nodes.Count(n => n.TemplateId == "emu"));
        var validation = TopologyValidator.ValidateProjectForSave(loaded);
        Assert.True(validation.Ok, validation.Message);
        var (overlay, convert) = TopologyRuntimeConverter.ConvertForApply(loaded);
        Assert.True(convert.Ok, convert.Message);
        Assert.Equal(2, overlay!.EssUnits.Count);
    }

    [Fact]
    public void Save_prunes_dangling_and_unknown_port_edges()
    {
        var project = new TopologyProject
        {
            Name = "残线",
            Nodes =
            {
                new TopologyNode { Id = "pcs1", TemplateId = "pcs", Label = "PCS" },
                new TopologyNode { Id = "bus1", TemplateId = "ac_bus", Label = "母线" }
            },
            Edges =
            {
                new TopologyEdge { Id = "ok", FromNodeId = "pcs1", FromPortId = "ac_a", ToNodeId = "bus1", ToPortId = "a2" },
                new TopologyEdge { Id = "ghost", FromNodeId = "pcs1", FromPortId = "ac_b", ToNodeId = "missing", ToPortId = "a" },
                new TopologyEdge { Id = "badport", FromNodeId = "pcs1", FromPortId = "nope", ToNodeId = "bus1", ToPortId = "a" }
            }
        };

        var saved = _store.SaveNamedProject(project);
        Assert.Single(saved.Edges);
        Assert.Equal("ok", saved.Edges[0].Id);
        Assert.Equal("ac_a", saved.Edges[0].FromPortId);
    }

    [Fact]
    public void Save_migrates_legacy_meter_ct_ports_to_unified_pt()
    {
        var project = new TopologyProject
        {
            Name = "旧电表口",
            Nodes =
            {
                new TopologyNode { Id = "m1", TemplateId = "ac_meter", Label = "表" },
                new TopologyNode { Id = "bus1", TemplateId = "ac_bus", Label = "母线" }
            },
            Edges =
            {
                new TopologyEdge { Id = "pt", FromNodeId = "m1", FromPortId = "pt_a", ToNodeId = "bus1", ToPortId = "a2" },
                new TopologyEdge { Id = "ct", FromNodeId = "m1", FromPortId = "ct_a", ToNodeId = "bus1", ToPortId = "a2" }
            }
        };

        var saved = _store.SaveNamedProject(project);
        Assert.Single(saved.Edges);
        Assert.Equal("pt_a", saved.Edges[0].FromPortId);
        Assert.DoesNotContain(saved.Edges, e => e.FromPortId == "ct_a");
    }

    [Fact]
    public void CreateEmpty_open_and_find_by_name()
    {
        var blank = _store.CreateEmptyProject("草稿站");
        Assert.Equal("草稿站", blank.Name);
        Assert.Empty(blank.Nodes);
        Assert.False(File.Exists(Path.Combine(_root, "configs", "topology", "projects", blank.Id + ".json")));
        Assert.Contains(_store.ListProjects(), p => p.Id == blank.Id && p.Name == "草稿站");

        var named = _store.SaveNamedProject(TopologyScaffold.BuildRadial(1, name: "Alpha"));
        Assert.NotNull(_store.FindProjectByName("alpha"));
        Assert.Null(_store.FindProjectByName("Alpha", excludeId: named.Id));

        var opened = _store.OpenNamedProject(named.Id);
        Assert.NotNull(opened);
        var canvas = _store.LoadProject();
        Assert.Equal(named.Id, canvas.Id);
        Assert.Equal("Alpha", canvas.Name);
        Assert.NotEmpty(canvas.Nodes);
    }

    [Fact]
    public void Overlay_roundtrip_and_clear_with_runtime_mode()
    {
        var project = TopologyScaffold.BuildRadial(1, name: "应用源");
        var saved = _store.SaveNamedProject(project);
        var (overlay, validation) = TopologyRuntimeConverter.ConvertForApply(saved);
        Assert.True(validation.Ok, validation.Message);

        _store.SaveOverlay(overlay!);
        _store.SaveRuntimeMode(new TopologyRuntimeMode
        {
            EngineeringMode = true,
            ActiveProjectId = saved.Id,
            ActiveProjectName = saved.Name
        });

        var loaded = _store.LoadOverlay();
        Assert.NotNull(loaded);
        Assert.Equal("应用源", loaded!.SourceProjectName);
        Assert.Single(loaded.EssUnits);
        Assert.True(TopologyOverlayLoader.IsUsable(loaded));

        var fromDisk = TopologyOverlayLoader.TryLoad(_root);
        Assert.NotNull(fromDisk);
        Assert.Equal(saved.Id, fromDisk!.SourceProjectId);

        Assert.True(_store.DeleteNamedProject(saved.Id));
        var mode = _store.LoadRuntimeMode();
        Assert.True(mode.EngineeringMode);
        Assert.Null(mode.ActiveProjectId);

        _store.ClearOverlay();
        Assert.Null(_store.LoadOverlay());
    }

    [Fact]
    public void LoadProject_corrupt_json_returns_empty_canvas()
    {
        var path = Path.Combine(_root, "configs", "topology", "project.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not-json");
        var project = _store.LoadProject();
        Assert.Empty(project.Nodes);
        Assert.Empty(project.Edges);
    }

    [Fact]
    public void SaveProject_syncs_named_copy_for_system_config_dropdown()
    {
        var project = TopologyScaffold.BuildRadial(1, name: "画布同步");
        var saved = _store.SaveProject(project);
        var listed = _store.ListProjects();
        Assert.Contains(listed, p => p.Id == saved.Id && p.Name == "画布同步");
        Assert.Equal(1, listed.First(p => p.Id == saved.Id).EmuCount);
    }

    [Fact]
    public void ApplyFrozenStation_writes_mode_without_canvas_file()
    {
        var overlay = Mw300EssStation.ApplyToStore(_store);
        Assert.Equal(60, overlay.EssUnits.Count);

        var projectFile = Path.Combine(_root, "configs", "topology", "projects", Mw300EssStation.Id + ".json");
        Assert.False(File.Exists(projectFile));

        var mode = _store.LoadRuntimeMode();
        Assert.True(mode.EngineeringMode);
        Assert.Equal(Mw300EssStation.Id, mode.ActiveProjectId);

        var listed = Mw300EssStation.WithBuiltin(_store.ListProjects());
        var row = listed.Single(p => p.Id == Mw300EssStation.Id);
        Assert.True(row.ReadOnly);
        Assert.Equal(60, row.EmuCount);

        File.WriteAllText(
            Path.Combine(_root, "configs", "topology", "generated", "runtime-overlay.json"),
            "{}");
        var loaded = TopologyOverlayLoader.TryLoad(_root);
        Assert.Equal(60, loaded!.EssUnits.Count);
        Assert.Equal(Mw300EssStation.Id, loaded.SourceProjectId);
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
