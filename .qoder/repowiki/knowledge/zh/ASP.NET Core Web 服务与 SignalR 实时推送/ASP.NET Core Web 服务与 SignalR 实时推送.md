---
kind: external_dependency
name: ASP.NET Core Web 服务与 SignalR 实时推送
slug: aspnetcore-signalr
category: external_dependency
category_hints:
    - framework_behavior
scope:
    - '**'
---

项目基于 `Microsoft.NET.Sdk.Web`（隐式包含 ASP.NET Core）构建 Web 服务：
- 提供 HTTP API 与静态文件托管（`wwwroot/` 前端资源）。
- 通过 SignalR Hub（`Web/RealtimeHub.cs`）向前端推送实时遥测、告警与拓扑快照。
- 可选启用 API Key 鉴权中间件（`Web/ApiKeyAuthMiddleware.cs`），端口与基地址由 `Simulator.Web.*` 配置。
- 前端构建产物位于 `Web/src/`，由 Vite 构建后输出到 `wwwroot/`。