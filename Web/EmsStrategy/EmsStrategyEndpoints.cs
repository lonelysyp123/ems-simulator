using EssSimulator.Core;
using EssSimulator.EmsStrategy.Adapter;
using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EssSimulator.Web.EmsStrategy;

public sealed class EmsStrategyStatusDto
{
    public bool Enabled { get; set; }
    public string GateOwner { get; set; } = "";
    public EmsStrategyConfig Config { get; set; } = EmsStrategyConfig.CreateDefault();
    public EmsStrategySnapshot Snapshot { get; set; } = new();
}

public static class EmsStrategyEndpoints
{
    public static IEndpointRouteBuilder MapEmsStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ems-strategy");

        g.MapGet("/", (EmsStrategyRuntime runtime) => Results.Ok(ToStatus(runtime)));

        g.MapPost("/enable", (EmsStrategyEnableRequest? req, EmsStrategyRuntime runtime) =>
        {
            bool enabled = req?.Enabled ?? true;
            if (!runtime.TrySetEnabled(enabled, out var error))
                return Results.Json(new { ok = false, message = error }, statusCode: StatusCodes.Status409Conflict);
            return Results.Ok(new
            {
                ok = true,
                message = enabled ? "EMS 策略已启用，外部指令已拒绝" : "EMS 策略已关闭",
                status = ToStatus(runtime)
            });
        });

        g.MapPost("/", (EmsStrategyPatchRequest? req, EmsStrategyRuntime runtime) =>
        {
            var next = runtime.Config.Clone();
            EmsStrategyConfigPatcher.Apply(next, req ?? new EmsStrategyPatchRequest());
            if (!runtime.TryReplace(next, out var error))
                return Results.Json(new { ok = false, message = error }, statusCode: StatusCodes.Status409Conflict);
            return Results.Ok(new { ok = true, message = "已更新 EMS 策略", status = ToStatus(runtime) });
        });

        return app;
    }

    private static EmsStrategyStatusDto ToStatus(EmsStrategyRuntime runtime) => new()
    {
        Enabled = runtime.Config.Enabled,
        GateOwner = ExternalControlGate.Owner.ToString(),
        Config = runtime.Config,
        Snapshot = runtime.Snapshot()
    };
}

public sealed class EmsStrategyEnableRequest
{
    public bool Enabled { get; set; } = true;
}
