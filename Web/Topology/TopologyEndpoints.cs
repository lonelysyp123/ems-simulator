using EssSimulator.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssSimulator.Web.Topology
{
    public static class TopologyEndpoints
    {
        public static IEndpointRouteBuilder MapTopologyEndpoints(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/topology");
            g.AddEndpointFilter(EditionTopologyGate);

            g.MapGet("/templates", () => Results.Ok(TopologyTemplates.All));

            g.MapGet("/templates/{id}", (string id) =>
            {
                var t = TopologyTemplates.Get(id);
                return t == null ? Results.NotFound(new { message = $"模板不存在: {id}" }) : Results.Ok(t);
            });

            g.MapGet("/project", (TopologyStore store) => Results.Ok(store.LoadProject()));

            g.MapPut("/project", (TopologyProject project, TopologyStore store) =>
            {
                if (project == null)
                    return Results.BadRequest(new { message = "工程体为空" });
                if (Mw300EssStation.IsThis(project.Id))
                    return Results.BadRequest(new { message = Mw300EssStation.LockedMessage });
                var validation = TopologyValidator.ValidateProjectForSave(project);
                if (!validation.Ok)
                {
                    return Results.BadRequest(new
                    {
                        message = validation.Message,
                        validation
                    });
                }
                var saved = store.SaveProject(project);
                // 按 PCS 总数自动选型 LC 点表（随下次重启生效）
                LcPointMapAutoSelect.ApplyForProject(saved);
                return Results.Ok(saved);
            });

            g.MapPost("/validate", (TopologyProject project) =>
            {
                if (project == null)
                    return Results.BadRequest(new { message = "工程体为空" });
                return Results.Ok(TopologyValidator.ValidateProjectForSave(project));
            });

            g.MapPost("/connect", (ConnectRequest req, TopologyStore store) =>
            {
                if (req?.Project == null || req.Edge == null)
                    return Results.BadRequest(new ConnectResponse
                    {
                        Validation = new TopologyValidationResult
                        {
                            Ok = false,
                            Code = "BAD_REQUEST",
                            Message = "请求缺少 Project 或 Edge"
                        }
                    });

                if (req.ExpandBundle)
                {
                    var bundleResult = TopologyValidator.TryConnectBundle(req.Project, req.Edge, out var bundled);
                    return Results.Ok(new ConnectResponse
                    {
                        Validation = bundleResult,
                        Project = bundled ?? req.Project
                    });
                }

                var validation = TopologyValidator.TryConnect(req.Project, req.Edge);
                if (!validation.Ok)
                {
                    return Results.Ok(new ConnectResponse
                    {
                        Validation = validation,
                        Project = req.Project
                    });
                }

                var updated = TopologyValidator.ApplyConnect(req.Project, req.Edge);
                return Results.Ok(new ConnectResponse
                {
                    Validation = validation,
                    Project = updated
                });
            });

            g.MapPost("/scaffold", (ScaffoldRequest? body) =>
            {
                try
                {
                    var emuCount = body?.EmuCount ?? 0;
                    var pvCount = body?.PvCount ?? 0;
                    if (emuCount == 0 && pvCount == 0)
                        emuCount = 1;
                    var project = TopologyScaffold.BuildRadial(
                        emuCount,
                        body?.Name,
                        body?.IncludeLoad ?? true,
                        pvCount);
                    return Results.Ok(project);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

            g.MapPost("/disconnect", (DisconnectRequest req) =>
            {
                if (req?.Project == null || string.IsNullOrWhiteSpace(req.EdgeId))
                    return Results.BadRequest(new { message = "请求缺少 Project 或 EdgeId" });
                var updated = TopologyValidator.RemoveEdge(req.Project, req.EdgeId);
                return Results.Ok(updated);
            });

            g.MapGet("/library", (TopologyStore store) => Results.Ok(store.ListLibrary()));

            g.MapGet("/library/{id}", (string id, TopologyStore store) =>
            {
                var item = store.GetLibraryItem(id);
                return item == null ? Results.NotFound(new { message = "设备库条目不存在" }) : Results.Ok(item);
            });

            g.MapPut("/library", (TopologyLibraryItem item, TopologyStore store) =>
            {
                try
                {
                    return Results.Ok(store.SaveLibraryItem(item));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

            g.MapDelete("/library/{id}", (string id, TopologyStore store) =>
                store.DeleteLibraryItem(id)
                    ? Results.Ok(new { ok = true })
                    : Results.NotFound(new { message = "设备库条目不存在" }));

            g.MapGet("/paths", () => Results.Ok(new
            {
                root = "configs/topology",
                project = "configs/topology/project.json",
                library = "configs/topology/library",
                projects = "configs/topology/projects"
            }));

            g.MapGet("/projects", (TopologyStore store) =>
                Results.Ok(Mw300EssStation.WithBuiltin(store.ListProjects())));

            // 静态路径须注册在 {id} 之前，避免被当成 id
            g.MapGet("/projects/check-name", (string name, string? excludeId, TopologyStore store) =>
            {
                var hit = store.FindProjectByName(name, excludeId);
                return Results.Ok(new
                {
                    exists = hit != null,
                    project = hit
                });
            });

            g.MapPost("/projects/new", (TopologyStore store, CreateProjectRequest? body) =>
                Results.Ok(store.CreateEmptyProject(body?.Name)));

            g.MapGet("/projects/{id}", (string id, TopologyStore store) =>
            {
                if (Mw300EssStation.IsThis(id))
                    return Results.BadRequest(new { message = Mw300EssStation.LockedMessage });
                var p = store.LoadNamedProject(id);
                return p == null
                    ? Results.NotFound(new { message = "工程不存在" })
                    : Results.Ok(p);
            });

            g.MapPost("/projects/{id}/open", (string id, TopologyStore store) =>
            {
                if (Mw300EssStation.IsThis(id))
                    return Results.BadRequest(new { message = Mw300EssStation.LockedMessage });
                var p = store.OpenNamedProject(id);
                return p == null
                    ? Results.NotFound(new { message = "工程不存在" })
                    : Results.Ok(p);
            });

            g.MapPost("/projects/{id}/copy", (string id, TopologyStore store, CopyProjectRequest? body) =>
            {
                if (Mw300EssStation.IsThis(id))
                    return Results.BadRequest(new { message = Mw300EssStation.LockedMessage });
                try
                {
                    var p = store.CopyNamedProject(id, body?.Name);
                    return Results.Ok(p);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new { message = ex.Message });
                }
            });

            g.MapDelete("/projects/{id}", (string id, TopologyStore store) =>
                Mw300EssStation.IsThis(id)
                    ? Results.BadRequest(new { message = Mw300EssStation.LockedMessage })
                    : store.DeleteNamedProject(id)
                    ? Results.Ok(new { ok = true })
                    : Results.NotFound(new { message = "工程不存在" }));

            return app;
        }

        private static async ValueTask<object?> EditionTopologyGate(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
        {
            var edition = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<EditionConfig>>().Value;
            edition.ApplyPresets();
            if (!edition.AllowTopologyEditor)
            {
                return Results.Json(
                    new { message = "当前产品档位不包含组态编辑功能" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(ctx);
        }
    }

    public sealed class CreateProjectRequest
    {
        public string? Name { get; set; }
    }

    public sealed class CopyProjectRequest
    {
        public string? Name { get; set; }
    }

    public sealed class DisconnectRequest
    {
        public TopologyProject Project { get; set; } = new();
        public string EdgeId { get; set; } = "";
    }
}
