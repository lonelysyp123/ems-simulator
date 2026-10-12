# B/S 架构说明

仿真模拟器已由原 **控制台 TUI（Spectre.Console）** 改造为 **B/S 架构**：后端为 ASP.NET Core + SignalR 单进程服务，前端为 Vue 3 单页应用。仿真核心、Modbus / IEC 61850 协议、DataExchange 管道全部保留并复用。

## 一、架构总览

```
浏览器（Vue 3 SPA）
   │  HTTP/REST  +  WebSocket(SignalR)
   ▼
Kestrel（ASP.NET Core）
   ├── Minimal API  (/api/*)        状态查询 / 命令执行 / 链路控制 / IEC 61850
   ├── SignalR Hub  (/hub/realtime) 实时推送主接线/BMS/单体/连接/日志/告警/IEC61850
   └── 静态文件     (wwwroot/)      前端构建产物
   │
   ▼（同进程托管）
IHost BackgroundServices
   ├── EnergyStorageSystem    仿真主循环（含光伏 StepPvUnits）
   ├── BmsDataService / BmsLinkService / PcsDataServer / EmDataService
   ├── ModbusHostedService    Modbus TCP 从站（对外）
   ├── Iec61850HostedService  MMS IED + 入向 GOOSE 订户
   ├── SnapshotService        周期采样 → SignalR 推送
   └── LogHubDispatcher       log4net 日志 → SignalR 推送
   │
   ▼
Modbus TCP（simEm / simBms{N} / simEmu{N} / simPv{N} / simPvMeter{N}）  ← EMS/测试工具接入
IEC 61850 MMS（默认 8102 起）+ L2 GOOSE 订阅 ← IEDScout 等
```

## 二、配置

`appsettings.json` → `Simulator.Web`：

| 字段 | 默认 | 说明 |
|------|------|------|
| `HttpPort` | 5050 | Web 服务监听端口（macOS 勿用 5000，易被 AirPlay 占用） |
| `HttpBaseUrl` | `http://0.0.0.0` | 监听地址（`0.0.0.0` 全网卡） |
| `StaticFiles` | true | 是否托管 `wwwroot/` 前端文件 |
| `CorsOrigins` | `[]` | 额外允许的 CORS 来源（dev 自动放行 5173） |
| `SnapshotIntervalMs` | 200 | 实时快照推送间隔；PCS 启停等控制变更会额外立即推一帧 |
| `ApiKeyEnabled` | false | 是否保护 `/api/*`（`/api/health` 豁免）；仓外托管建议 true |
| `ApiKey` | `""` | 密钥明文；启用时必填，推荐环境变量 `Simulator__Web__ApiKey` |

命令行/环境变量覆盖示例：

```bash
dotnet run -- --Simulator:Web:HttpPort=5050
Simulator__Web__HttpPort=5050 Simulator__Web__ApiKey=secret dotnet EssSimulator
```

## 三、启动

### 生产/发布包

```bash
./start.sh        # Linux，自动打开浏览器
start.bat         # Windows
# 浏览器访问 http://localhost:5050
```

### 开发

```bash
# 推荐：一键
./scripts/dev-up.sh

# 或分终端
# 终端 1：后端
dotnet run --project EssSimulator.csproj

# 终端 2：前端 HMR（代理 /api 与 /hub 到后端端口）
cd Web && npm install && npm run dev
# 浏览器访问 http://localhost:5173
```

### 发布

```bash
./scripts/publish-windows.sh   # 自动 npm install + build + dotnet publish
./scripts/publish-linux.sh
```

发布产物包含 `wwwroot/`（前端）、`EssSimulator[.exe]`、`appsettings.json`、点表 CSV、`autotest.json` 等。

## 四、HTTP API

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/health` | 健康检查 + 仿真就绪状态 |
| GET | `/api/mainline` | 主接线全量快照（全单元） |
| GET | `/api/battery/{unit}` | 指定舱电池总览 + 簇列表 |
| GET | `/api/cells/{unit}/{cluster}` | 指定簇 4×104 单体电压 |
| GET | `/api/connections` | 网络接口/监听/客户端/链路状态 |
| GET | `/api/alert` | 严重告警状态 |
| GET | `/api/config` | 仿真与 Web/档位概要（不回传 ApiKey 明文） |
| GET | `/api/topology/*` | 组态模板/工程/设备库（档位关闭时 403） |
| GET | `/api/system/config` | 工程模式与 overlay 状态 |
| POST | `/api/system/apply` | 应用组态工程并可选重启 |
| GET | `/api/protocol` | Modbus 端口表 |
| GET/PUT | `/api/protocol-ports` | 按台端口计划；`/allowlist` 为 Modbus IP 白名单；`/apply` 热重建 |
| GET | `/api/iec61850` | IEC 61850 IED 快照与协议绑定 |
| GET | `/api/iec61850/messages` | GOOSE/系统报文环形缓冲（可按 `server` 过滤） |
| POST | `/api/iec61850/messages/clear` | 清空报文环 |
| GET/PUT | `/api/iec61850/bindings` | 按台 Modbus/IEC61850 开关（`configs/protocol-bindings.json`） |
| GET | `/api/autotest` | autotest.json 测试用例列表 |
| GET | `/api/pointmaps` | 各 sim 设备点表（DataMaps/ControlMaps） |
| POST | `/api/command` | 通用命令执行，body: `{"input":"esscmd link status"}` |
| POST | `/api/link/{target}/{state}` | 链路开关，target=`em\|bms1\|pcs1\|pv1\|pvMeter1`，state=`on\|off` |
| POST | `/api/dpctest/{name}` | 异步执行自动化测试，进度经 SignalR 推送 |

命令语义与原 TUI `esscmd / breaker / dpc / dpctest` 完全一致，详见 `docs/指令详细说明.md`。

## 五、SignalR 实时推送

端点：`/hub/realtime`

前端通过 `JoinChannel(group)` 订阅频道，后端按方法名推送：

| 频道 | 方法 | 内容 |
|------|------|------|
| `mainline` | `ReceiveMainLine` | 主接线快照（含光伏单元） |
| `battery.{unit}` | `ReceiveBattery` | 指定舱电池总览 |
| `connections` | `ReceiveConnections` | 连接/链路快照（含 IEC 61850 摘要） |
| `iec61850` | `ReceiveIec61850Message` | GOOSE 入向/系统事件报文 |
| `logs` | `ReceiveLog` | 日志条目（log4net ≥ INFO） |
| `cmdprogress` | `ReceiveCommandProgress` | dpctest 执行进度 |
| （全局） | `ReceiveAlert` | 严重告警（黑启动联锁等） |

## 六、前端页面

| 路由 | 说明 |
|------|------|
| `/mainline` | 主电气接线（SVG；工程模式可用组态单线图，含光伏支路） |
| `/mainline-3d` | 三维站场（`AllowMainline3d`） |
| `/topology` `/projects` `/system` | 组态编辑、工程管理、系统配置（`AllowTopologyEditor`） |
| `/battery` `/cells` | 电池堆簇 / 单体 |
| `/thresholds` `/alarms` | BMS 门限 / 设备告警 |
| `/command` | 命令输入 |
| `/droop-slices` | 白盒切片（`AllowDroopSlices`） |
| `/connections` | 连接与链路（IEC 61850 仅摘要，详情进 `/iec61850`） |
| `/iec61850` | IEC 61850：IED 总览、GOOSE 入向报文与解码 |
| `/protocol-ports` | 按台协议端口（Modbus / IEC 61850 开关）与 Modbus IP 白名单 |

## 七、文件结构

```
EssSimulator/
├── Program.cs                # WebApplication 入口
├── Web/                      # B/S 后端 + Vue 源码（Web/src）
│   ├── EndpointExtensions.cs
│   ├── Topology/             # 组态工程、overlay、校验
│   └── src/                  # Vue 3 SPA
├── Display/                  # 命令与状态读取（已去 Spectre）
├── wwwroot/                  # 前端构建产物（gitignore，发布时生成）
└── ...（仿真核心/协议/点表/文档 保持不变）
```

## 八、与原 TUI 的对应关系

| 原 TUI | B/S 替代 |
|--------|----------|
| `Display/GuiMain.cs` 主菜单 | Vue 路由 + Element Plus 侧栏 |
| `DrawMainElectiralToggle` | `/mainline` + `MainLineSvg.vue` |
| `DrawBatteryInfo` | `/battery` + ECharts |
| `DrawCellInfo` | `/cells` 热力色块 |
| `DrawCmd` | `/command` |
| `DrawClientConnectInfo` | `/connections` |
| `DrawLog` + `LogDisplay` | SignalR `logs` 频道（若前端接入） |
| `FatalSystemAlert` overlay | `ReceiveAlert` 推送 + 顶栏红条 |
| `Spectre.Console` | 已移除 |
