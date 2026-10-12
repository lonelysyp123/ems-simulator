using System.Text.Json.Serialization;

namespace EssSimulator.Web.Topology
{
    /// <summary>组态工程：画布上的设备实例与连线。</summary>
    public sealed class TopologyProject
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string Id { get; set; } = "current";
        public string Name { get; set; } = "未命名组态";
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public List<TopologyNode> Nodes { get; set; } = new();
        public List<TopologyEdge> Edges { get; set; } = new();
    }

    /// <summary>设备库条目：单台改参设备，或由多台设备+内部连线构成的组合图元。</summary>
    public sealed class TopologyLibraryItem
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "未命名设备";
        /// <summary>device（缺省）或 composite。</summary>
        public string Kind { get; set; } = "device";
        /// <summary>单台设备的模板；组合图元用作调色板色点（通常取首个设备模板）。</summary>
        public string TemplateId { get; set; } = "";
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object?> Parameters { get; set; } = new();
        /// <summary>组合图元内的设备，坐标相对包围盒左上角。</summary>
        public List<TopologyNode> Nodes { get; set; } = new();
        /// <summary>组合图元内部连线（两端都必须在 Nodes 内）。</summary>
        public List<TopologyEdge> Edges { get; set; } = new();

        [JsonIgnore]
        public bool IsComposite =>
            string.Equals(Kind, "composite", StringComparison.OrdinalIgnoreCase)
            || (Nodes != null && Nodes.Count >= 2);
    }

    public sealed class TopologyNode
    {
        public string Id { get; set; } = "";
        public string TemplateId { get; set; } = "";
        /// <summary>若来自设备库，记录库条目 Id。</summary>
        public string? LibraryItemId { get; set; }
        public string Label { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public Dictionary<string, object?> Parameters { get; set; } = new();
    }

    public sealed class TopologyEdge
    {
        public string Id { get; set; } = "";
        public string FromNodeId { get; set; } = "";
        public string FromPortId { get; set; } = "";
        public string ToNodeId { get; set; } = "";
        public string ToPortId { get; set; } = "";
    }

    public sealed class TopologyTemplate
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsVoltageSource { get; set; }
        /// <summary>虚拟模板：无端口、不参与连线，画布不渲染（如 EMU 虚拟储能单元）。</summary>
        public bool IsVirtual { get; set; }
        public List<TopologyPortDef> Ports { get; set; } = new();
        public List<TopologyParamDef> Parameters { get; set; } = new();
        public Dictionary<string, object?> DefaultParameters { get; set; } = new();
    }

    public sealed class TopologyPortDef
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        /// <summary>ac_phase | dc_pos | dc_neg（旧版 dc 按正极兼容）</summary>
        public string Kind { get; set; } = "ac_phase";
        /// <summary>A / B / C / null</summary>
        public string? Phase { get; set; }
        /// <summary>top | bottom | left | right</summary>
        public string Side { get; set; } = "top";
        public double Offset { get; set; }
        /// <summary>该端口额定电压参数名（如 primaryVoltage / outputVoltage）。</summary>
        public string? VoltageParam { get; set; }
        public bool IsVoltageSourcePort { get; set; }
    }

    public sealed class TopologyParamDef
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "number";
        public string? Unit { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public string? Description { get; set; }
    }

    public sealed class TopologyValidationResult
    {
        public bool Ok { get; set; }
        public string? Code { get; set; }
        public string Message { get; set; } = "";
        /// <summary>校验失败时应解绑的边（通常是刚建立的那条）。</summary>
        public string? RejectEdgeId { get; set; }
        public List<string> Details { get; set; } = new();
        /// <summary>与错误相关的节点 Id，供前端高亮定位。</summary>
        public List<string> ProblemNodeIds { get; set; } = new();
    }

    public sealed class ConnectRequest
    {
        public TopologyProject Project { get; set; } = new();
        public TopologyEdge? Edge { get; set; }
        /// <summary>为 true（默认）时，交流同侧三相 / 直流正负极自动成组连接。</summary>
        public bool ExpandBundle { get; set; } = true;
    }

    public sealed class ScaffoldRequest
    {
        /// <summary>EMU 储能单元数，0–20；与 PvCount 至少一个大于 0。缺省且未给光伏时按 1。</summary>
        public int? EmuCount { get; set; }
        /// <summary>光伏单元数，0–20。</summary>
        public int? PvCount { get; set; }
        public string? Name { get; set; }
        public bool IncludeLoad { get; set; } = true;
    }

    public sealed class ConnectResponse
    {
        public TopologyValidationResult Validation { get; set; } = new();
        public TopologyProject? Project { get; set; }
    }

    /// <summary>JSON 数字可能是 JsonElement，统一转 double。</summary>
    public static class TopologyParamHelper
    {
        public static double GetDouble(IReadOnlyDictionary<string, object?> parameters, string key, double fallback = 0)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var raw) || raw == null)
                return fallback;

            if (raw is double d) return d;
            if (raw is float f) return f;
            if (raw is int i) return i;
            if (raw is long l) return l;
            if (raw is decimal m) return (double)m;
            if (raw is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.Number && je.TryGetDouble(out var jd))
                    return jd;
                if (je.ValueKind == System.Text.Json.JsonValueKind.String &&
                    double.TryParse(je.GetString(), out var js))
                    return js;
            }

            if (double.TryParse(raw.ToString(), out var parsed))
                return parsed;
            return fallback;
        }

        public static string GetString(IReadOnlyDictionary<string, object?> parameters, string key, string fallback = "")
        {
            if (parameters == null || !parameters.TryGetValue(key, out var raw) || raw == null)
                return fallback;
            if (raw is System.Text.Json.JsonElement je)
                return je.ValueKind == System.Text.Json.JsonValueKind.String ? (je.GetString() ?? fallback) : je.ToString();
            return raw.ToString() ?? fallback;
        }

        public static bool GetBool(IReadOnlyDictionary<string, object?> parameters, string key, bool fallback = false)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var raw) || raw == null)
                return fallback;
            if (raw is bool b) return b;
            if (raw is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                if (je.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                if (je.ValueKind == System.Text.Json.JsonValueKind.String &&
                    bool.TryParse(je.GetString(), out var pb)) return pb;
            }
            if (bool.TryParse(raw.ToString(), out var parsed)) return parsed;
            return fallback;
        }
    }
}
