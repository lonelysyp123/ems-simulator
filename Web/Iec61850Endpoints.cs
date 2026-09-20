using EssSimulator.Configuration;
using EssSimulator.Display;
using EssSimulator.Protocol.Iec61850;
using EssSimulator.Protocol.Modbus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace EssSimulator.Web
{
    public sealed class ProtocolBindingsChangeRequest
    {
        public List<ProtocolBindingEntry> Entries { get; set; } = new();
        public bool Rebuild { get; set; } = true;
    }

    public static class Iec61850Endpoints
    {
        public static IEndpointRouteBuilder MapIec61850Endpoints(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/iec61850");

            g.MapGet("/", () =>
            {
                var manager = Iec61850LayerManager.Instance;
                return Results.Ok(new
                {
                    devices = manager.GetSnapshot(),
                    bindings = manager.Bindings.Entries,
                    bindingsError = manager.BindingsError
                });
            });

            g.MapGet("/bindings", () =>
            {
                var bindings = ProtocolBindings.Load();
                return Results.Ok(new
                {
                    entries = bindings.Entries,
                    error = bindings.LoadError
                });
            });

            g.MapGet("/messages", (string? server, int? limit) =>
            {
                var list = Iec61850TrafficLog.Snapshot(server, limit ?? 200);
                return Results.Ok(new
                {
                    capacity = Iec61850TrafficLog.Capacity,
                    count = list.Count,
                    messages = list.Select(ToDto)
                });
            });

            g.MapPost("/messages/clear", () =>
            {
                Iec61850TrafficLog.Clear();
                return Results.Ok(new { ok = true });
            });

            g.MapPut("/bindings", (ProtocolBindingsChangeRequest req, IOptions<SimulatorConfig> cfg) =>
            {
                if (req?.Entries == null)
                    return Results.BadRequest(new { ok = false, message = "修改内容为空" });

                foreach (var entry in req.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                        return Results.BadRequest(new { ok = false, message = "设备名不能为空" });
                    if (entry.Protocols == null || entry.Protocols.Count == 0)
                        return Results.BadRequest(new { ok = false, message = $"{entry.Name} 至少选择一种协议" });
                }

                ProtocolBindings.Save(req.Entries);
                if (req.Rebuild)
                {
                    ProtocolLayerManager.Instance.Rebuild();
                    Iec61850LayerManager.Instance.ReloadBindingsAndRestart(cfg.Value);
                }

                return Results.Ok(new
                {
                    ok = true,
                    message = req.Rebuild ? "已保存并重建 Modbus / IEC 61850 协议层" : "已保存（重启后生效）",
                    devices = Iec61850LayerManager.Instance.GetSnapshot()
                });
            });

            g.MapPost("/link/{target}/{state}", (string target, string state) =>
            {
                if (!Display.EssCommand.TryParseLinkState(state, out var enable, out var msg))
                    return Results.BadRequest(new { success = false, message = msg });

                string? name = ResolveEmuName(target);
                if (name == null)
                    return Results.BadRequest(new { success = false, message = "请使用 iec61850-pcsN 或 simEmuN" });

                bool ok = Iec61850LayerManager.Instance.TrySetOnline(name, enable);
                if (!ok)
                    return Results.Ok(new { success = false, message = $"找不到 {name} 的 IEC 61850 IED" });

                return Results.Ok(new
                {
                    success = true,
                    message = enable ? $"{name} IEC 61850 已上线" : $"{name} IEC 61850 已离线"
                });
            });

            return app;
        }

        internal static string? ResolveEmuName(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return null;
            if (target.StartsWith("simEmu", StringComparison.OrdinalIgnoreCase))
                return target;
            if (target.StartsWith("iec61850-pcs", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(target.AsSpan("iec61850-pcs".Length), out var n)
                && n >= 1)
            {
                return $"simEmu{n}";
            }

            if (target.StartsWith("pcs", StringComparison.OrdinalIgnoreCase)
                && target.EndsWith("-iec61850", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(target.AsSpan(3, target.Length - 3 - "-iec61850".Length), out n)
                && n >= 1)
            {
                return $"simEmu{n}";
            }

            return null;
        }

        private static object ToDto(Iec61850TrafficMessage m) => new
        {
            m.Id,
            utc = m.Utc.ToString("o"),
            localTime = m.Utc.ToLocalTime().ToString("HH:mm:ss.fff"),
            m.Direction,
            m.Protocol,
            service = m.Service,
            server = m.ServerName,
            iedName = m.IedName,
            clientPeer = m.ClientPeer,
            paramName = m.ParamName,
            objectRef = m.ObjectRef,
            orCat = m.OrCat,
            ctlNum = m.CtlNum,
            appId = m.AppId,
            goCbRef = m.GoCbRef,
            goId = m.GoId,
            datSet = m.DatSet,
            stNum = m.StNum,
            sqNum = m.SqNum,
            isTest = m.IsTest,
            simulation = m.IsTest,
            ndsCom = m.NeedsCommission,
            confRev = m.ConfRev,
            timeAllowedToLive = m.TimeAllowedToLive,
            t = m.GooseTimestampUtc,
            numDatSetEntries = m.NumDatSetEntries,
            m.Result,
            m.Summary,
            writes = m.Writes,
            values = m.Values,
            allData = m.AllData?.Select(e => new
            {
                e.Index,
                paramName = e.ParamName,
                type = e.Type,
                value = e.Value,
                description = e.Description
            })
        };
    }
}
