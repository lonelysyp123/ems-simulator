---
kind: configuration_system
name: 基于 ASP.NET Core IOptions + JSON/环境变量/工程 Overlay 的分档配置系统
category: configuration_system
scope:
    - '**'
source_files:
    - Program.cs
    - Configuration/SimulatorConfig.cs
    - Configuration/EditionConfig.cs
    - Configuration/LicenseConfig.cs
    - Configuration/WebConfig.cs
    - Core/SimulatorHost.cs
    - appsettings.json
    - appsettings.validation.json
    - configs/社区版.appsettings.json
    - configs/商业版.appsettings.json
    - configs/定制版.appsettings.json
    - configs/topology/project.json
    - configs/topology/runtime-mode.json
---

## 1. 整体方案

EssSimulator 采用 ASP.NET Core 内置的 `IConfiguration` + `IOptions<T>` 作为统一配置中心，所有运行时参数以 POCO（如 `SimulatorConfig`、`EditionConfig`、`LicenseConfig`、`WebConfig`、`PcsPhysicalConfig`、`TransformerConfig`、`UnitTransformerConfig`、`LoadConfig`、`PccConfig`）声明在 `Configuration/` 目录，并通过 `builder.Services.Configure<T>(...)` 绑定到配置源。启动时通过自定义 `LoadJsonWithCommentsAsStream` 加载支持注释与尾随逗号的 JSON 文件，再叠加环境变量与命令行参数，形成最终配置。

## 2. 配置来源与加载顺序

- **基础配置**：`appsettings.json`（必需），包含 `Simulator.Runtime/Web/Edition/Protocol`、`Pcc`、`Meter`、`Transformer`、`UnitTransformer`、`Load`、`Pcs`、`EssUnits` 等全部节。
- **环境覆盖**：`appsettings.{envName}.json`（可选，如 `Development`/`Production`），由 `builder.Configuration.AddJsonStream(..., optional: true)` 追加。
- **环境变量**：`builder.Configuration.AddEnvironmentVariables()`，例如 Web API Key 推荐通过 `Simulator__Web__ApiKey` 注入。
- **命令行**：`builder.Configuration.AddCommandLine(args)`，支持 `--machine-id` 等开关。
- **工程 Overlay**：`configs/topology/project.json` + `runtime-mode.json` 由 `TopologyOverlayLoader.TryLoad(ContentRootPath)` 加载；若启用且当前 Edition 允许组态编辑，则 overlay 中的 `EssUnits`、`PvUnits`、`Pcs`、`Transformer`、`UnitTransformer`、`Pcc`、`Meter`、`Load` 通过 `PostConfigure` 覆盖默认 appsettings 值。
- **分档模板**：`configs/社区版.appsettings.json`、`商业版.appsettings.json`、`定制版.appsettings.json`、`充值版.appsettings.json`、`演示版.appsettings.json` 由发布脚本选择并合并到部署产物中，配合 `Simulator:Edition.Name` 切换行为。

## 3. 核心模型与分层

| 配置类 | 所在文件 | 对应 JSON 节 | 作用 |
|---|---|---|---|
| `SimulatorConfig` | `Configuration/SimulatorConfig.cs` | `Simulator` | 仿真运行时（步长、传播、热模型）、协议端口、设备单元列表、光伏单元 |
| `EditionConfig` | `Configuration/EditionConfig.cs` | `Simulator:Edition` | 产品档位（Community/Commercial/Custom）及功能开关 |
| `LicenseConfig` | `Configuration/LicenseConfig.cs` | `Simulator:License` | 授权校验开关与文件名 |
| `WebConfig` | `Configuration/WebConfig.cs` | `Simulator:Web` | HTTP 监听、CORS、快照推送、API Key |
| `PcsPhysicalConfig` / `TransformerConfig` / `UnitTransformerConfig` / `LoadConfig` / `PccConfig` | `Configuration/SimulatorConfig.cs` | `Pcs` / `Transformer` / `UnitTransformer` / `Load` / `Pcc` | 物理设备与电网参数 |
| `DataExchangeOptions` | `DataExchange/Config/DataExchangeOptions.cs` | `DataExchange` | EMS/BMS 数据交换周期 |

每个 POCO 都定义 `Section` 常量（如 `SimulatorConfig.Section = "Simulator"`），使绑定路径集中可查。

## 4. 启动装配流程（Program.Main）

1. 解析 `--machine-id` 直接输出机器码后退出。
2. 初始化 log4net，注册 SignalR LogHub Appender。
3. 构建 `WebApplication`，清空默认配置源，按顺序添加：`appsettings.json` → `appsettings.{env}.json` → 环境变量 → 命令行。
4. 调用 `EnforceLicenseOrExit`：读取 `EditionConfig` 并 `ApplyPresets()`；若未显式设置 `License.Required`，则非社区版强制要求运行目录或程序目录下存在 `license.txt`，校验失败输出机器码并 `Environment.ExitCode = 2`。
5. 尝试加载工程 overlay，若 Edition 不允许编辑则丢弃。
6. 使用 `Services.Configure` 绑定各 POCO，再用 `PostConfigure` 执行：
   - `EditionConfig.ApplyPresets()`（社区版关闭高级能力、锁定拓扑、限制 `MaxEssUnits`）。
   - `LicenseConfig.Required` 回退逻辑（未显式配置时按 Edition 决定）。
   - 将顶层 `EssUnits` 合并进 `SimulatorConfig.Devices`，并用 overlay 覆盖。
   - 对社区版 `LockTopology && MaxEssUnits > 0` 的情况裁剪 `Devices`。
   - 禁止非开放档位开启 `DroopSliceCaptureEnabled`。
7. 根据 `WebConfig.HttpBaseUrl` 与 `HttpPort` 设置 Kestrel 监听地址。
8. 注册 SignalR、JSON 序列化策略、核心仿真模型单例（`EnergyStorageSystem` 及其 PV 单元）到 `SimulatorHost.Instance` 全局字典。
9. 注册各 HostedService（BMS/PCS/EMU DataServer、Modbus、LocalControl、Snapshot、LogHub）。
10. 构建应用、挂载中间件（CORS、ApiKeyAuthMiddleware、静态文件）、映射端点与 SPA 回退，启动后打印协议端口日志并等待仿真就绪。

## 5. 分档与特性开关机制

`EditionConfig.ApplyPresets()` 是唯一的“档位预设”入口：当 `Name` 为 Community（或中文“社区版”）时，强制关闭 `AllowDroopSlices`、`AllowMainline3d`、`AllowTopologyEditor`，设置 `LockTopology = true`，并将 `MaxEssUnits` 默认设为 2。该逻辑在 Program 中至少被调用三次（早期读取、`PostConfigure`、overlay 判断前），确保无论从哪里读取配置，社区版行为一致。

## 6. 约定与约束

- **配置文件格式**：必须为 UTF-8 JSON，允许注释与尾随逗号（由 `JsonCommentHandling.Skip` + `AllowTrailingCommas = true` 保证）。
- **节命名**：所有配置 POCO 必须提供 `Section` 常量，并通过 `GetSection(...).Get<T>()` 绑定；新增配置需同时更新 `Program` 中的 `Configure`/`PostConfigure` 调用。
- **环境变量注入**：推荐使用 ASP.NET Core 配置约定的双下划线路径（如 `Simulator__Web__ApiKey`），避免将密钥写入仓库。
- **工程模式优先级**：当 `configs/topology/runtime-mode.json` 的 `engineeringMode=true` 且 Edition 允许编辑时，overlay 完全覆盖 appsettings 中的设备与物理参数；否则仅使用 appsettings。
- **授权强制**：非社区版在未显式设置 `Simulator:License:Required=false` 时必须存在有效 `license.txt`，否则进程以退出码 2 终止。
- **端口分配**：BMS/EMU/LocalControl/PV Logger/Meter 端口均基于 `Base*Port` + `*PortStep` 计算，启动时通过 `LogProtocolCreateInfo` 统一打印，便于运维核对。
- **向后兼容**：`SimulatorConfig.EffectiveEssUnitCount` 在无 `EssUnits` 且无 PvUnits 时回退为 1，保持旧 appsettings 可用；`UnitCount` 固定为 `EffectiveEssUnitCount * 2`（每单元 2 路 PCS/BMS）。

## 7. 关键文件

- `Program.cs`：配置加载、叠加、授权校验、服务装配、Kestrel 监听、启动编排。
- `Configuration/SimulatorConfig.cs`：仿真器、协议、设备、PCS、变压器、负载、PCC 等全部 POCO。
- `Configuration/EditionConfig.cs`：产品档位与功能开关。
- `Configuration/LicenseConfig.cs`：授权校验配置。
- `Configuration/WebConfig.cs`：Web 服务与 API Key 配置。
- `Core/SimulatorHost.cs`：全局对象存储（ess/pv/bms/emu 等实例注册表）。
- `appsettings.json`：默认配置基线。
- `appsettings.validation.json`：验证用最小配置集。
- `configs/*.appsettings.json`：各分档模板。
- `configs/topology/project.json`、`runtime-mode.json`：工程组态 overlay。
- `docs/appsettings.explained.md`：配置字段说明文档。