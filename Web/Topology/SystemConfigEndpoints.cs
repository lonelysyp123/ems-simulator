using EssSimulator.Configuration;
using EssSimulator.Protocol.Modbus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EssSimulator.Web.Topology
{
    public static class SystemConfigEndpoints
    {
        private static IResult ApplyFrozenStation(
            SystemApplyRequest req,
            TopologyStore store,
            IHostApplicationLifetime lifetime)
        {
            var overlay = Mw300EssStation.ApplyToStore(store);
            if (req.ConfirmRestart)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    WriteRestartSentinel();
                    lifetime.StopApplication();
                });
            }

            return Results.Ok(new SystemApplyResponse
            {
                Ok = true,
                Restarting = req.ConfirmRestart,
                Overlay = overlay,
                Message = req.ConfirmRestart
                    ? $"已应用工程「{Mw300EssStation.Name}」（储能 {overlay.EssUnits.Count}），模拟器即将重启"
                    : "已写入 300MW 测试工程（需重启后生效）",
                Details = overlay.Notes
            });
        }

        /// <summary>
        /// 在程序目录下创建 .restart 哨兵文件，通知启动脚本（start.bat）本次退出属于
        /// "重新初始化"而非手动关闭，脚本检测到后会自动拉起后端。
        /// </summary>
        private static void WriteRestartSentinel()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, ".restart");
                File.WriteAllText(path, DateTime.UtcNow.ToString("O"));
            }
            catch { /* 哨兵写入失败不影响关闭流程 */ }
        }

        public static IEndpointRouteBuilder MapSystemConfigEndpoints(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/system");

            // 设备型号/点表选择独立于组态编辑功能，不受档位门拦截
            var dm = app.MapGroup("/api/system");

            dm.MapGet("/device-models", () =>
            {
                var selection = DeviceModelRegistry.LoadSelection();
                var types = DeviceModelRegistry.ListTypes();
                foreach (var t in types)
                {
                    if (string.Equals(t.Id, "lc", StringComparison.OrdinalIgnoreCase))
                        t.Models = t.Models.Where(DeviceModelRegistry.IsExclusiveModel).ToList();
                }
                return Results.Ok(new
                {
                    Types = types,
                    Selection = selection.Selections,
                    HasSelection = selection.Selections.Count > 0,
                    Pointmaps = BuildPointmapSummary()
                });
            });

            dm.MapPost("/device-models/apply", (
                DeviceModelsApplyRequest req,
                IHostApplicationLifetime lifetime) =>
            {
                if (req == null || req.Selections == null || req.Selections.Count == 0)
                    return Results.BadRequest(new SystemApplyResponse { Ok = false, Message = "选型内容为空" });

                var cleaned = new Dictionary<string, string>(req.Selections, StringComparer.OrdinalIgnoreCase);

                var errors = DeviceModelRegistry.ValidateSelection(cleaned);
                if (errors.Count > 0)
                    return Results.BadRequest(new SystemApplyResponse
                    {
                        Ok = false,
                        Message = "选型校验失败",
                        Details = errors
                    });

                DeviceModelRegistry.SaveSelection(new DeviceModelSelection
                {
                    Selections = cleaned
                });

                if (req.ConfirmRestart)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(400);
                        WriteRestartSentinel();
                        lifetime.StopApplication();
                    });
                }

                var names = BuildPointmapSummary()
                    .Select(p => $"{p.TypeName}: {(p.ModelName ?? p.ModelId ?? "legacy 兜底")}");

                return Results.Ok(new SystemApplyResponse
                {
                    Ok = true,
                    Restarting = req.ConfirmRestart,
                    Message = req.ConfirmRestart
                        ? "已保存设备型号选型，模拟器即将重启"
                        : "已保存设备型号选型（需重启后生效）",
                    Details = names.ToList()
                });
            });

            g.MapGet("/config", (
                TopologyStore store,
                IOptions<SimulatorConfig> simCfg) =>
            {
                var mode = store.LoadRuntimeMode();
                var overlay = store.LoadOverlay();
                var projects = store.ListProjects();
                bool eng = mode.EngineeringMode && overlay != null;
                return Results.Ok(new SystemConfigState
                {
                    EngineeringMode = mode.EngineeringMode,
                    ActiveProjectId = mode.ActiveProjectId,
                    ActiveProjectName = mode.ActiveProjectName,
                    OverlayPresent = overlay != null,
                    Source = eng ? "topology" : "appsettings",
                    RuntimeUnitCount = simCfg.Value.EffectiveEssUnitCount,
                    RuntimePvUnitCount = simCfg.Value.PvUnitCount,
                    Projects = Mw300EssStation.WithBuiltin(projects).ToList(),
                    Pointmaps = BuildPointmapSummary(),
                    OverlaySummary = overlay == null ? null : new TopologyRuntimeOverlay
                    {
                        SourceProjectId = overlay.SourceProjectId,
                        SourceProjectName = overlay.SourceProjectName,
                        GeneratedAtUtc = overlay.GeneratedAtUtc,
                        EssUnits = overlay.EssUnits,
                        PvUnits = overlay.PvUnits,
                        EnableSvg = overlay.EnableSvg,
                        SvgRatedCapacityKvar = overlay.SvgRatedCapacityKvar,
                        Notes = overlay.Notes
                    }
                });
            });

            g.MapGet("/projects", (TopologyStore store) =>
                Results.Ok(Mw300EssStation.WithBuiltin(store.ListProjects())));

            g.MapPost("/apply", (
                SystemApplyRequest req,
                TopologyStore store,
                IHostApplicationLifetime lifetime) =>
            {
                if (req == null)
                    return Results.BadRequest(new SystemApplyResponse { Ok = false, Message = "请求体为空" });

                if (!req.EngineeringMode)
                {
                    // 关闭工程模式：清 overlay，下次启动用 appsettings
                    store.ClearOverlay();
                    store.SaveRuntimeMode(new TopologyRuntimeMode
                    {
                        EngineeringMode = false,
                        ActiveProjectId = null,
                        ActiveProjectName = null
                    });

                    if (req.ConfirmRestart)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(400);
                            WriteRestartSentinel();
                            lifetime.StopApplication();
                        });
                    }

                    return Results.Ok(new SystemApplyResponse
                    {
                        Ok = true,
                        Restarting = req.ConfirmRestart,
                        Message = req.ConfirmRestart
                            ? "已关闭工程模式，模拟器即将重启并恢复 appsettings.json 配置"
                            : "已关闭工程模式（需重启后生效）",
                        Details = { "来源: appsettings.json" }
                    });
                }

                if (string.IsNullOrWhiteSpace(req.ProjectId))
                    return Results.BadRequest(new SystemApplyResponse
                    {
                        Ok = false,
                        Message = "工程模式下必须选择一个组态工程"
                    });

                if (Mw300EssStation.IsThis(req.ProjectId))
                    return ApplyFrozenStation(req, store, lifetime);

                var project = store.LoadNamedProject(req.ProjectId!);
                if (project == null)
                {
                    var cur = store.LoadProject();
                    if (cur.Id == req.ProjectId || string.Equals(cur.Name, req.ProjectId, StringComparison.OrdinalIgnoreCase))
                        project = cur;
                }

                if (project == null || project.Nodes.Count == 0)
                    return Results.BadRequest(new SystemApplyResponse
                    {
                        Ok = false,
                        Message = $"未找到工程或工程为空: {req.ProjectId}"
                    });

                // 确保工程已入库
                if (string.IsNullOrWhiteSpace(project.Id))
                    project.Id = req.ProjectId!;
                store.SaveNamedProject(project);
                store.SaveProject(project);

                // 按 PCS 总数自动选型 LC 点表（本次重启即生效）
                LcPointMapAutoSelect.ApplyForProject(project);

                var (overlay, validation) = TopologyRuntimeConverter.ConvertForApply(project);
                if (!validation.Ok || overlay == null)
                    return Results.BadRequest(new SystemApplyResponse
                    {
                        Ok = false,
                        Message = validation.Message,
                        Details = validation.Details
                    });

                store.SaveOverlay(overlay);
                store.SaveRuntimeMode(new TopologyRuntimeMode
                {
                    EngineeringMode = true,
                    ActiveProjectId = project.Id,
                    ActiveProjectName = project.Name
                });

                if (req.ConfirmRestart)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        WriteRestartSentinel();
                        lifetime.StopApplication();
                    });
                }

                return Results.Ok(new SystemApplyResponse
                {
                    Ok = true,
                    Restarting = req.ConfirmRestart,
                    Overlay = overlay,
                    Message = req.ConfirmRestart
                            ? $"已应用工程「{project.Name}」（储能 {overlay.EssUnits.Count} / 光伏 {overlay.PvUnits.Count}），模拟器即将重启"
                        : $"已写入工程配置（需重启后生效）",
                    Details = overlay.Notes
                });
            });

            return app;
        }

        /// <summary>汇总各设备类型当前生效的点表型号（selection / legacy）。</summary>
        private static List<PointmapRuntimeEntry> BuildPointmapSummary()
        {
            var selection = DeviceModelRegistry.LoadSelection();
            var summary = new List<PointmapRuntimeEntry>();
            foreach (var type in DeviceModelRegistry.ListTypes())
            {
                if (string.Equals(type.Id, "lc", StringComparison.OrdinalIgnoreCase))
                {
                    if (selection.Selections.TryGetValue(type.Id, out var lcModelId)
                        && !string.IsNullOrWhiteSpace(lcModelId))
                    {
                        var exclusive = type.Models.FirstOrDefault(m =>
                            string.Equals(m.Id, lcModelId, StringComparison.OrdinalIgnoreCase)
                            && DeviceModelRegistry.IsExclusiveModel(m));
                        if (exclusive != null)
                        {
                            summary.Add(new PointmapRuntimeEntry
                            {
                                TypeId = type.Id,
                                TypeName = type.Name,
                                ModelId = exclusive.Id,
                                ModelName = exclusive.Name,
                                Source = "selection"
                            });
                            continue;
                        }
                    }

                    var fragments = type.Models
                        .Where(m => !DeviceModelRegistry.IsExclusiveModel(m))
                        .Select(m => Path.GetFileName(m.Directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToArray();
                    summary.Add(new PointmapRuntimeEntry
                    {
                        TypeId = type.Id,
                        TypeName = type.Name,
                        ModelId = null,
                        ModelName = fragments.Length > 0
                            ? string.Join(" / ", fragments) + " 按组展开拼装"
                            : "按组展开拼装",
                        Source = "compose"
                    });
                    continue;
                }
                var entry = new PointmapRuntimeEntry
                {
                    TypeId = type.Id,
                    TypeName = type.Name,
                    Source = "legacy"
                };

                if (selection.Selections.TryGetValue(type.Id, out var modelId) && !string.IsNullOrWhiteSpace(modelId))
                {
                    entry.ModelId = modelId;
                    entry.ModelName = type.Models
                        .FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase))
                        ?.Name;
                    entry.Source = "selection";
                }

                summary.Add(entry);
            }
            return summary;
        }
    }
}
