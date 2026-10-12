using System.Text.Json;

namespace EssSimulator.Web.Topology
{
    /// <summary>启动时加载组态 overlay：工程模式开启且内容合法才生效。</summary>
    public static class TopologyOverlayLoader
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static bool IsUsable(TopologyRuntimeOverlay? overlay)
        {
            if (overlay == null)
                return false;

            bool hasEss = overlay.EssUnits is { Count: > 0 };
            bool hasPv = overlay.PvUnits is { Count: > 0 };
            if (!hasEss && !hasPv)
                return false;

            if (hasEss)
            {
                foreach (var unit in overlay.EssUnits)
                {
                    if (unit == null)
                        return false;
                    if (unit.HasGroups)
                    {
                        // 分组构成：运行时按 group 顺序展平，要求各组 PCS/BMS 完整
                        if (unit.Groups.Any(g => g == null || g.Pcs is not { Count: > 0 } || g.Bms is not { Count: > 0 }))
                            return false;
                    }
                    else if (unit.Pcs is not { Count: > 0 } || unit.Bms is not { Count: > 0 })
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public static TopologyRuntimeOverlay? TryLoad(string contentRoot)
        {
            var modePath = Path.Combine(contentRoot, "configs", "topology", "runtime-mode.json");
            var overlayPath = Path.Combine(contentRoot, "configs", "topology", "generated", "runtime-overlay.json");
            try
            {
                if (!File.Exists(modePath))
                    return null;

                var mode = JsonSerializer.Deserialize<TopologyRuntimeMode>(File.ReadAllText(modePath), JsonOpts);
                if (mode?.EngineeringMode != true)
                    return null;

                if (Mw300EssStation.IsThis(mode.ActiveProjectId))
                    return Mw300EssStation.BuildOverlay();

                if (!File.Exists(overlayPath))
                    return null;

                var overlay = JsonSerializer.Deserialize<TopologyRuntimeOverlay>(File.ReadAllText(overlayPath), JsonOpts);
                if (!IsUsable(overlay))
                {
                    Console.WriteLine(
                        $"[Topology] 工程 overlay 无效（缺少储能或光伏发电单元），已忽略，改用 appsettings：{overlayPath}");
                    return null;
                }

                return overlay;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Topology] 加载工程 overlay 失败：{overlayPath} ({ex.GetType().Name}): {ex.Message}");
                return null;
            }
        }
    }
}
