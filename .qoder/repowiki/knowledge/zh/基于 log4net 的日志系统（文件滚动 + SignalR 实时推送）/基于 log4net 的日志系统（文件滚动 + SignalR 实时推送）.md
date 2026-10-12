---
kind: logging_system
name: 基于 log4net 的日志系统（文件滚动 + SignalR 实时推送）
category: logging_system
scope:
    - '**'
source_files:
    - log4net.config
    - Program.cs
    - Web/LogHubAppender.cs
    - Web/RealtimeHub.cs
    - EssDeviceSimModel/Diagnostics/SimStateChangeLogger.cs
---

## 1. 使用的框架与工具
- 日志框架：log4net（通过 `log4net.config` XML 配置，并在 `Program.Main` 中通过 `XmlConfigurator.Configure` 加载）。
- 输出目标（sink）有两个：
  - `RollingFileAppender`：按天滚动写入 `Logs/` 目录下的 `yyyy-MM-dd.log` 文件，最多保留 5 份备份，使用 `MinimalLock` 锁模式，格式为 `%n%d [%t] %-5p %c [%L] - %m %n`。
  - `LogHubAppender`：自定义 `AppenderSkeleton`，将每条 `LoggingEvent` 转换为 `LogEntryDto` 后入队到 `ConcurrentQueue`，由 `BackgroundService` 形式的 `LogHubDispatcher` 消费并通过 SignalR `RealtimeHub` 的 `logs` 频道推送到前端。
- 应用启动时还会把 `LogHubAppender` 编程式挂到 log4net 根 logger 上（阈值 `Info`），无需修改配置文件即可启用实时日志推送。

## 2. 关键文件
- `log4net.config`：定义 RollingFileAppender、日期滚动策略、PatternLayout 转换模式及 root level=ALL。
- `Program.cs`：应用入口，负责加载 `log4net.config`、注册 `LogHubAppender`、记录应用启动/协议创建/就绪等生命周期日志。
- `Web/LogHubAppender.cs`：自定义 log4net Appender + `LogHubBridge` 队列 + `LogHubDispatcher` BackgroundService，实现日志→SignalR 推送。
- `Web/RealtimeHub.cs`：SignalR Hub，定义 `RealtimeChannels.Logs`、`RealtimeMethods.ReceiveLog` 以及 `LogEntryDto`（Timestamp/Level/Logger/Message/Thread/Exception）。
- 业务模块中的 ILog 使用点（示例）：`DataExchangeSession`、`ControlPipeline`、`TelemetryPipeline`、`RackControlPipeline`、`RackTelemetryPipeline`、`ControlFeedbackPipeline`、`EnergyStorageSystem`、`ElectricalSignalRouter`、`SimStateChangeLogger`、`FatalSystemAlert`、`GuiSimDataAccess`、`GuiStatusFormatters` 等，均通过 `LogManager.GetLogger(typeof(...))` 获取命名 logger。

## 3. 架构与约定
- **初始化顺序**：在 `WebApplication.CreateBuilder` 之前完成 log4net 配置加载；若配置文件不存在或解析失败，回退到 `BasicConfigurator.Configure()`，保证即使配置异常也不阻塞启动。
- **双 sink 并行**：文件日志由 log4net 自身维护；实时日志通过额外挂载的 `LogHubAppender` 异步推送到 SignalR，两者互不影响。`LogHubBridge` 使用固定容量 `MaxQueue = 2000` 的 `ConcurrentQueue` 做简单背压，超出时丢弃最旧条目，避免高吞吐下阻塞主流程。
- **命名 logger**：所有业务代码统一通过 `LogManager.GetLogger(typeof(SomeClass))` 获取 logger，因此日志中的 `%c` 会输出完整类型名（如 `EssSimulator.DataExchange.Pipeline.ControlPipeline`），便于按模块过滤。
- **结构化字段**：日志事件本身是 log4net 的 `LoggingEvent`，包含 TimeStamp、Level、LoggerName、ThreadName、ExceptionObject；`LogHubAppender` 将其映射为 `LogEntryDto` 的强类型字段（Timestamp/Level/Logger/Message/Thread/Exception），供前端展示。
- **日志级别**：默认 root level=ALL，文件全部写入；SignalR 推送仅 Info 及以上（`Threshold = log4net.Core.Level.Info`），调试级日志不会占用 SignalR 通道。
- **异常隔离**：`LogHubAppender.Append` 和 `LogHubDispatcher` 发送路径都包裹 try/catch，确保“日志桥接失败不可影响主流程”，符合生产环境对日志子系统的高可用要求。

## 4. 约定与约束
- **日志位置与轮转**：日志文件固定输出到运行目录下的 `Logs/` 子目录，文件名按 `yyyy-MM-dd.log` 生成，最多保留 5 个历史文件（来自 `log4net.config` 的 `MaxSizeRollBackups` 与 `DatePattern`）。
- **日志格式**：采用 PatternLayout 的 `%n%d [%t] %-5p %c [%L] - %m %n`，即换行+时间+线程+级别+logger名+行号+消息+换行，便于 grep 与外部日志采集。
- **运行时级别控制**：当前根级别设为 ALL，未提供动态调整级别的机制；如需在生产环境降低噪音，应修改 `log4net.config` 中 `<root><level value="..."/></root>`。
- **SignalR 日志推送范围**：仅 Info 及以上级别通过 SignalR 推送；Debug/Trace 仅落盘，不进入实时通道。
- **错误处理原则**：任何日志相关异常（配置加载失败、Appender 注册失败、SignalR 推送失败）均被捕获并降级处理，不得导致主业务流程中断。
- **业务日志风格**：诊断类日志集中在 `EssDeviceSimModel/Diagnostics/SimStateChangeLogger.cs`，以 `[PCS状态]`、`[BMS状态]` 等前缀标记状态变化，便于快速定位仿真器内部状态机变更。