using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EssSimulator.Web.ThirdPartyEms
{
    public static class ThirdPartyEmsEndpoints
    {
        public static IEndpointRouteBuilder MapThirdPartyEmsEndpoints(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/third-party-ems");

            g.MapGet("/", (ThirdPartyEmsSession session) =>
            {
                var dash = session.GetDashboard();
                return Results.Ok(dash);
            });

            g.MapPost("/connect", (ThirdPartyEmsConnectRequest? req, ThirdPartyEmsSession session) =>
            {
                if (!session.TryConnect(req?.Name, out var error))
                    return Results.Json(new { ok = false, message = error }, statusCode: StatusCodes.Status409Conflict);
                return Results.Ok(new { ok = true, message = "已占用控制权，外部指令已拒绝", dashboard = session.GetDashboard() });
            });

            g.MapPost("/disconnect", (ThirdPartyEmsConnectRequest? req, ThirdPartyEmsSession session) =>
            {
                if (!session.TryDisconnect(req?.Name, out var error))
                    return Results.Json(new { ok = false, message = error }, statusCode: StatusCodes.Status409Conflict);
                return Results.Ok(new { ok = true, message = "已释放控制权", dashboard = session.GetDashboard() });
            });

            g.MapPost("/power", (ThirdPartyEmsPowerRequest req, ThirdPartyEmsSession session) =>
            {
                if (req == null)
                    return Results.BadRequest(new { ok = false, message = "请求体为空" });
                if (!session.TrySetPower(req.Name, req.ActivePowerKw, req.ReactivePowerKvar, out var error))
                    return Results.Json(new { ok = false, message = error }, statusCode: StatusCodes.Status409Conflict);
                return Results.Ok(new { ok = true, message = "已下发目标有功/无功", dashboard = session.GetDashboard() });
            });

            g.MapPost("/remote", (ThirdPartyEmsRemoteRequest req, ThirdPartyEmsSession session) =>
            {
                if (req == null)
                    return Results.BadRequest(new { ok = false, message = "请求体为空" });
                if (!session.TrySetRemote(req.Name, req.Enable, req.Mode, out var error))
                    return Results.Json(new { ok = false, message = error }, statusCode: StatusCodes.Status409Conflict);
                return Results.Ok(new { ok = true, message = "已下发远程使能/模式", dashboard = session.GetDashboard() });
            });

            g.MapPost("/operation", (ThirdPartyEmsOperationRequest req, ThirdPartyEmsSession session) =>
            {
                if (req == null)
                    return Results.BadRequest(new { ok = false, message = "请求体为空" });
                if (!session.TrySetOperation(req.Name, req.Operation, out var error))
                    return Results.Json(new { ok = false, message = error }, statusCode: StatusCodes.Status409Conflict);
                return Results.Ok(new { ok = true, message = "已下发系统操作", dashboard = session.GetDashboard() });
            });

            return app;
        }
    }
}
