using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Web.Topology
{
    /// <summary>系统运行模式：appsettings 默认拓扑，或组态工程驱动。</summary>
    public sealed class TopologyRuntimeMode
    {
        public bool EngineeringMode { get; set; }
        public string? ActiveProjectId { get; set; }
        public string? ActiveProjectName { get; set; }
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class TopologyProjectSummary
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime UpdatedAtUtc { get; set; }
        public int NodeCount { get; set; }
        public int EmuCount { get; set; }
        public int PvCount { get; set; }
        /// <summary>写死测试工程为 true，工程库不能编辑、复制或删除。</summary>
        public bool ReadOnly { get; set; }
    }

    /// <summary>由组态工程生成、启动时覆盖 appsettings 对应节的运行时补丁。</summary>
    public sealed class TopologyRuntimeOverlay
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string SourceProjectId { get; set; } = "";
        public string SourceProjectName { get; set; } = "";
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        public List<EssUnitConfig> EssUnits { get; set; } = new();
        public List<PvUnitRuntimeConfig> PvUnits { get; set; } = new();
        public PccConfig? Pcc { get; set; }
        public TransformerConfig? Transformer { get; set; }
        public UnitTransformerConfig? UnitTransformer { get; set; }
        public MeterConfig? Meter { get; set; }
        public PcsPhysicalConfig? Pcs { get; set; }
        /// <summary>组态「负载」节点映射的站用负载计划（有功仅允许 ≤0）。</summary>
        public LoadConfig? Load { get; set; }
        /// <summary>工程里有 SVG 节点时为真。应用后覆盖 appsettings 的 EnableSvg。</summary>
        public bool EnableSvg { get; set; }
        /// <summary>SVG 额定容量（kvar）。未放置节点时为 0。</summary>
        public double SvgRatedCapacityKvar { get; set; }
        /// <summary>工程内全部三相断路器，顺序即 simMv 序号。</summary>
        public List<MvBreakerEndpoint> MvBreakers { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    public sealed class SystemConfigState
    {
        public bool EngineeringMode { get; set; }
        public string? ActiveProjectId { get; set; }
        public string? ActiveProjectName { get; set; }
        public bool OverlayPresent { get; set; }
        public string Source { get; set; } = "appsettings"; // appsettings | topology
        public int RuntimeUnitCount { get; set; }
        public int RuntimePvUnitCount { get; set; }
        public List<TopologyProjectSummary> Projects { get; set; } = new();
        public TopologyRuntimeOverlay? OverlaySummary { get; set; }
        /// <summary>各设备类型当前生效的点表型号摘要。</summary>
        public List<PointmapRuntimeEntry> Pointmaps { get; set; } = new();
    }

    /// <summary>单个设备类型的点表生效状态（系统配置界面展示用）。</summary>
    public sealed class PointmapRuntimeEntry
    {
        public string TypeId { get; set; } = "";
        public string TypeName { get; set; } = "";
        /// <summary>选中型号 id；未选型（legacy 兜底）时为 null。</summary>
        public string? ModelId { get; set; }
        public string? ModelName { get; set; }
        /// <summary>selection=型号选型生效；legacy=未选型兜底；compose=LC 片段拼装。</summary>
        public string Source { get; set; } = "legacy";
    }

    /// <summary>设备型号选型应用请求（POST /api/system/device-models/apply）。</summary>
    public sealed class DeviceModelsApplyRequest
    {
        /// <summary>设备类型 id → 型号 id。</summary>
        public Dictionary<string, string> Selections { get; set; } = new();
        public bool ConfirmRestart { get; set; } = true;
    }

    public sealed class SystemApplyRequest
    {
        public bool EngineeringMode { get; set; }
        public string? ProjectId { get; set; }
        public bool ConfirmRestart { get; set; } = true;
    }

    public sealed class SystemApplyResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
        public bool Restarting { get; set; }
        public TopologyRuntimeOverlay? Overlay { get; set; }
        public List<string> Details { get; set; } = new();
    }
}
