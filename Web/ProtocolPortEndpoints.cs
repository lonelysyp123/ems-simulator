using EssSimulator.Protocol.Modbus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EssSimulator.Web
{
    /// <summary>协议端口配置请求条目。</summary>
    public sealed class ProtocolPortChangeRequest
    {
        public List<ProtocolPortChangeEntry> Entries { get; set; } = new();
    }

    public sealed class ProtocolPortChangeEntry
    {
        public string Name { get; set; } = string.Empty;
        public int Port { get; set; }
        public byte SlaveId { get; set; } = 1;
    }

    public sealed class ProtocolPortResetRequest
    {
        /// <summary>恢复默认后是否立即热重建。</summary>
        public bool Rebuild { get; set; }
    }

    /// <summary>Modbus TCP 来源 IP 白名单保存请求。</summary>
    public sealed class ModbusAllowListRequest
    {
        public bool AllowLoopback { get; set; } = true;
        public List<string> Addresses { get; set; } = new();
    }

    /// <summary>
    /// 协议端口配置端点：查询设备端口计划、保存手动覆盖（protocol-ports.json）、
    /// Modbus IP 白名单、立即热重建协议层、恢复默认。
    /// </summary>
    public static class ProtocolPortEndpoints
    {
        public static IEndpointRouteBuilder MapProtocolPortEndpoints(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/protocol-ports");

            // 查询：全部设备的默认/当前端口与从站号、在线状态、错误信息
            g.MapGet("/", () =>
            {
                var manager = ProtocolLayerManager.Instance;
                return Results.Ok(new
                {
                    devices = manager.GetSnapshot(),
                    overridesError = manager.OverridesError,
                    groups = BuildPortGroups(manager)
                });
            });

            // 保存：校验通过后写入 protocol-ports.json（重启后生效；也可随后调用 apply 立即生效）
            g.MapPut("/", (ProtocolPortChangeRequest req) =>
            {
                var manager = ProtocolLayerManager.Instance;
                if (req?.Entries == null || req.Entries.Count == 0)
                    return Results.BadRequest(new { ok = false, message = "修改内容为空" });

                var snapshot = manager.GetSnapshot();
                if (snapshot.Count == 0)
                    return Results.BadRequest(new { ok = false, message = "协议层尚未初始化，稍后重试" });

                // 在当前计划快照基础上套用修改，生成待保存计划
                var plan = BuildPlanFromSnapshot(snapshot);
                foreach (var change in req.Entries)
                {
                    var entry = plan.Find(change.Name);
                    if (entry == null)
                        return Results.BadRequest(new { ok = false, message = $"未知设备：{change.Name}" });
                    entry.Port = change.Port;
                    entry.SlaveId = change.SlaveId;
                }

                var errors = manager.ValidatePlan(plan);
                if (errors.Count > 0)
                    return Results.BadRequest(new { ok = false, message = "端口计划校验失败", errors });

                ProtocolPortPlan.SaveOverrides(plan.Entries);
                return Results.Ok(new
                {
                    ok = true,
                    message = "已保存端口配置（重启后生效；可点击「立即生效」热重建协议层）",
                    entries = plan.Entries.Select(e => new { e.Name, e.Port, e.SlaveId, e.IsDefault })
                });
            });

            // 立即生效：热重建协议层（断开现有 Modbus 连接后按计划重新监听）
            g.MapPost("/apply", () =>
            {
                var result = ProtocolLayerManager.Instance.Rebuild();
                return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
            });

            g.MapGet("/allowlist", () =>
            {
                var file = ModbusIpAllowList.Load(out var loadError);
                var active = ModbusPortHub.Instance.ActiveAllowList;
                return Results.Ok(new
                {
                    file = new { allowLoopback = file.AllowLoopback, addresses = file.Addresses },
                    active = new
                    {
                        allowLoopback = active.AllowLoopback,
                        addresses = active.Addresses,
                        unrestricted = active.IsUnrestricted
                    },
                    pendingRestart = !file.SamePolicy(active),
                    loadError
                });
            });

            g.MapPut("/allowlist", (ModbusAllowListRequest req) =>
            {
                if (!ModbusIpAllowList.TryCreate(req?.AllowLoopback ?? true, req?.Addresses, out var list, out var errors))
                    return Results.BadRequest(new { ok = false, message = "白名单校验失败", errors });

                ModbusIpAllowList.Save(list);
                var active = ModbusPortHub.Instance.ActiveAllowList;
                return Results.Ok(new
                {
                    ok = true,
                    message = "已保存，重启进程后生效",
                    pendingRestart = !list.SamePolicy(active),
                    allowLoopback = list.AllowLoopback,
                    addresses = list.Addresses
                });
            });

            // 恢复默认：删除覆盖文件，可选同时热重建
            g.MapPost("/reset", (ProtocolPortResetRequest? req) =>
            {
                ProtocolPortPlan.ClearOverrides();
                if (req?.Rebuild == true)
                {
                    var result = ProtocolLayerManager.Instance.Rebuild();
                    return result.Ok
                        ? Results.Ok(new { ok = true, message = "已恢复默认端口并完成热重建", rebuild = result })
                        : Results.BadRequest(new { ok = false, message = "已恢复默认端口，但热重建存在失败设备", rebuild = result });
                }
                return Results.Ok(new { ok = true, message = "已恢复默认端口配置（重启后生效）" });
            });

            return app;
        }

        /// <summary>按端口聚合共享组信息（同端口多设备时界面高亮提示）。</summary>
        private static List<object> BuildPortGroups(ProtocolLayerManager manager)
        {
            return manager.GetSnapshot()
                .GroupBy(d => d.Port)
                .Where(g => g.Count() > 1)
                .Select(g => (object)new
                {
                    port = g.Key,
                    devices = g.Select(d => new { d.Name, d.SlaveId }).ToArray()
                })
                .ToList();
        }

        /// <summary>由快照重建计划对象（保留默认值与 rack 数量，用于保存前校验）。</summary>
        private static ProtocolPortPlan BuildPlanFromSnapshot(List<ProtocolDeviceSnapshot> snapshot)
        {
            var plan = new ProtocolPortPlan();
            foreach (var d in snapshot)
            {
                plan.Entries.Add(new ProtocolPortEntry
                {
                    Name = d.Name,
                    Type = d.Type,
                    PointMapFile = d.PointMapFile,
                    RackCount = d.RackCount,
                    DefaultPort = d.DefaultPort,
                    DefaultSlaveId = d.DefaultSlaveId,
                    Port = d.Port,
                    SlaveId = d.SlaveId
                });
            }
            return plan;
        }
    }
}
