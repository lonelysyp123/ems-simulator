# 仿真模拟器 EssSimulator

> **一台电脑，一座虚拟电站。**
> 储能与光伏端到端仿真，对外提供 Modbus TCP 与 IEC 61850，
> 让 EMS / PCS / BMS 在真机到货之前就完成闭环联调。

[![CI](https://github.com/lonelysyp123/ems-simulator/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/lonelysyp123/ems-simulator/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Vue](https://img.shields.io/badge/Vue-3-42b883)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6)
![Modbus](https://img.shields.io/badge/Modbus-TCP-1f6feb)
![IEC 61850](https://img.shields.io/badge/IEC-61850%20MMS-1f6feb)

<!-- 首页视觉：这是本页最值得补的一块。录一段 10 秒 GIF（主接线在动 + mbpoll 读到电压），
     放到 docs/images/mainline.gif 后，取消下面这行的注释：
![主接线](https://raw.githubusercontent.com/lonelysyp123/ems-simulator/master/docs/images/mainline.gif)
-->

## 它是什么

一个**单进程、自包含**的储能 / 光储电站仿真程序。启动后它对外就是一座电站的设备侧：

- 电池堆 → 簇 → Pack → 电芯逐级建模，SOC 随充放电积分，簇间均衡；
- PCS 有功/无功爬坡、并网/离网、黑启动 V/f 建压、交直流耦合；
- 主变 / 单元变（损耗、阻抗、励磁涌流）、主断与单元断、站用负载、并网点电表；
- 光伏单元：组串逆变器 + MVS 箱变、组件 IV / 辐照 / 双面反照率 / 年限衰减、限发与无功；
- 电热耦合：环境温度、柜体热网络、空调闭环、温度降额、电池老化。

对外通过 **Modbus TCP 从站**（EMU / BMS / LC / 光伏 / 电表）和 **IEC 61850 MMS + GOOSE 订户**暴露数据与控制，**外部 EMS 完全按连接真设备的方式连它，通常不改一行代码**。

配套一个浏览器界面（默认 `http://127.0.0.1:5050`）：主接线 2D/3D、电池下钻、告警门限、命令台、组态编辑器。

## 和"假从站"、和 HIL 的区别

| | 手写 Mock 从站 | 半实物仿真（HIL） | 本仿真器 |
|---|---|---|---|
| 数据因果 | 数据是写死的，bug 到现场才暴露 | 有 | **有**：PCS 爬坡、SOC 积分、断路器分合真的改变潮流、电热耦合 |
| 硬件 | 无 | 实时仿真机，数十万起 | **普通笔记本**，解压即用（自包含，无需装 .NET） |
| 适合什么 | 只验证"能不能连上" | 保护/装置级微秒实时性 | **控制与通讯联调、策略验证、回归测试** |

> 定位说明：本程序不做保护装置的微秒级实时测试（那是 HIL 的领域），解决的是 **EMS 90% 的联调工作量**——接口、点表、控制逻辑、工况复现。

## 5 分钟上手

### 方式 A：运行发布包（联调、演示）

每个版本在 **[Releases](https://github.com/lonelysyp123/ems-simulator/releases)** 页提供 **Windows x64** 自包含包（zip），
**无需安装 .NET 运行时**。

```powershell
# 解压后双击 start.bat，或直接运行 EssSimulator.exe
```

启动后打开 **http://127.0.0.1:5050** 即可看到主接线。

### 方式 B：从源码运行（开发、二次开发）

需要 **.NET 8 SDK** + **Node 18+**。开发机不限系统（macOS / Linux 均可编译与调试），
**发布目标只有 Windows x64**。

```bash
git clone https://github.com/lonelysyp123/ems-simulator.git
cd ems-simulator
./scripts/dev-up.sh          # 自动装前端依赖，同时起后端(5050)与前端(5173)
```

浏览器访问 `http://localhost:5173`（Vite 会把 `/api` 与 `/hub` 代理到后端）。
Windows 上可分两个终端手动执行：`dotnet run --project EssSimulator.csproj` 与 `cd Web && npm install && npm run dev`。

### 方式 C：只跑后端（无界面、CI、纯协议）

```bash
dotnet run --project EssSimulator.csproj     # 只监听 API 与协议端口
curl -s http://127.0.0.1:5050/api/health     # {"status":"ok","ready":true,...}
```

此时没有 Web 界面（仓库不含前端构建产物），但 **Modbus / IEC 61850 端口全部可用**，适合放进自动化回归流水线。

## 能做什么

| 场景 | 说明 |
|---|---|
| **EMS 出厂回归** | 每次发版先对仿真器跑一遍点位全量读写 + 启停 + 功率设定 + 黑启动，不用等真机排期 |
| **点表对拍** | 换一份 CSV 点表即可复现另一家 PCS/BMS 的接口，10 分钟看出两家差异 |
| **工况复现** | 通讯中断（`link off`）、断路器分合、限发、离网黑启动——同一场景可无限次重放 |
| **培训与教学** | 新人不用碰真机就能理解"主断分闸后 690V 为什么还有残压" |
| **投标演示** | 现场连客户自己的 EMS，10 分钟跑通一条完整链路 |

技术特性：

- **点表驱动**：`FunctionCode / Address / Type / Scale / ParamName / ModelSim` 六列 CSV，`ModelSim` 支持 `model=4|arg1=<对象路径>` 直接绑定 DTO，另支持 `sum` / `max` 聚合；
- **设备型号可选**：同一设备类型下多套点表（如 BMS 的 `standard` / `g2_pro` / `g2_4mwh`），界面切换、重启生效；
- **组态编辑器**：拖拽搭拓扑、一键生成 1–20 单元径向电站、多工程管理，保存后生成运行时 overlay 直接驱动仿真；
- **开源可改**：GPL-3.0，点表、设备模型、协议映射都可以按现场需求自己扩。

## 对外接口

端口集中在 `appsettings.json` → `Simulator.Protocol`，也可在「协议端口」页面修改并热重建。

| 虚拟设备 | 服务名 | 默认端口 | 点表 |
|---|---|---|---|
| 并网点电表 | `simEm` | 1500 | `em.csv` |
| BMS 第 N 路 | `simBmsN` | 1501 + (N-1) | `bms_bank.csv`（簇表从站 = 1 + 簇序号） |
| EMU 第 N 路（含 PCS） | `simEmuN` | 1601 + (N-1) | `emu.csv`，**一台 PCS 一路从站** |
| LocalControl 第 N 路 | `simLcN` | 1701 + (N-1) | `lc.csv`（`EnableLocalControl=true`） |
| 光伏 Logger | `simPvN` | 1801 + (N-1) | `pv_logger.csv` |
| 光伏低压电表 | `simPvMeterN` | 1901 + (N-1) | `pv_apm810.csv` |
| 站点级 SEL 装置 | `simSel` | 2001 | `sel.csv`（`EnableSel=true`） |
| 光伏逆变器 | `simPvInvN` | 2101 + (N-1) | `pv_inverter.csv` |
| 中压断路器 | `simMvN` | 2301 + (N-1) | `mv.csv`（组态里每台三相断路器一路） |
| IEC 61850 MMS（IED） | 按 EMU | 8102 + (N-1) | `pointmaps/models/emu/iec61850/mapping.csv` |
| Web / REST / SignalR | — | 5050 | — |

- 默认监听 `127.0.0.1`；跨机联调需改 `HttpBaseUrl` 并放行防火墙。
- **接入口令**：`configs/modbus-allowlist.json` 按来源 IP 过滤（空名单不限制，改完需重启）。
- **GOOSE 订阅**：Windows 发布包暂不启用（随包原生库未编译该能力），MMS 服务端不受影响；详见 [项目编译说明](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/项目编译说明.md) 的「已知限制」。
- 只读监控可直接用 `mbpoll`：**按 CSV 的 FunctionCode 选 `-t`**（`4`=输入寄存器，`3`=保持寄存器，`1`/`5`=线圈），勿一律用 `-t 4`。

## Web 界面

| 页面 | 内容 |
|---|---|
| 电站概览 | 主电气接线（2D 单线图）、P设/P实、电压电流、断路器操作 |
| 数字孪生 | 3D 场景，设备详情下钻 |
| 组态编辑 | 拖拽建模、拓扑校验、保存为工程并生成运行时配置 |
| 工程配置 | 设备型号与点表选型、应用/回滚 |
| 电池堆簇 / 电池单体 | SOC、电压、温度逐级下钻 |
| BMS 告警门限 | 簇级三级门限在线配置 |
| 设备告警 / 连接信息 | 告警位、Modbus 客户端连接状态 |
| 命令输入 | `esscmd` / `dpc` / `breaker` / `dpctest` 控制台 |
| 协议端口 / IEC 61850 | 端口热重建、MMS 与 GOOSE 报文查看 |
| EMS 策略 / 第三方 EMS | 内置 EMS 策略闭环、外部 EMS 直连数据模型 |
| 白盒切片 | Droop 过程切片采样与回放 |

## 适用对象

- **EMS / 主站研发**：策略开发、闭环验证、发版回归；
- **PCS / BMS / 光伏设备厂商**：与 EMS 的协议一致性联调；
- **系统集成商 / EPC**：投标演示、项目培训、交付前预演；
- **高校 / 培训机构**：储能与光伏系统教学实验。

## 目录结构

```
EssDeviceSimModel/   物理模型：主循环、径向潮流、热网络、设备（PCS/变压器/断路器/BMS/Battery/Pv）
EssSimModelApi/      对外 DTO 与 Mapper（EMU/BMS/电表）、对象路径解析
DataExchange/        点目录 + 遥测/控制/反馈三管道（Shadow 去重）
Protocol/            Modbus 从站与端口调度；Iec61850/（MMS IED、入向 GOOSE）；Ptp/
Web/                 ASP.NET 端点、SignalR、组态（Topology/）、快照；前端源码 Web/src/
LocalControl/        LC 聚合协议与点表片段拼装
Configuration/       配置类型定义（Runtime / Protocol / Web）
pointmaps/           点表 CSV（models/{设备类型}/{型号}/）
configs/             EMS 策略、组态工程、协议绑定、Modbus IP 白名单
scripts/             开发与发布脚本（publish-windows.sh、dev-up.sh、test/）
EssSimulator.Tests/  xUnit 测试
```

## 文档

**上手与联调**

| 文档 | 说明 |
|---|---|
| [用户手册](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/用户手册.md) | 安装启动、浏览器界面、组态/3D、Modbus（含 IP 白名单）、IEC 61850 GOOSE、光伏单元、联调流程 |
| [指令详细说明](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/指令详细说明.md) | `esscmd` / `dpc` / `breaker` / `dpctest`，含光伏直控 `setpv` |
| [appsettings 字段说明](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/appsettings.explained.md) | 各配置段：协议端口、IEC 61850 / GOOSE、`PvUnits`、Modbus IP 白名单 |
| [点位表版本说明](https://github.com/lonelysyp123/ems-simulator/blob/master/pointmaps/README.md) | `models` 选型规则，含 `emu/iec61850` 与 `pv` |

**开发与架构**

| 文档 | 说明 |
|---|---|
| [系统设计说明](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/系统设计说明.md) | 技术栈、分层、数据流、储能/光伏设备模型、设计原则 |
| [B/S 架构说明](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/B-S架构说明.md) | Web 后端、Vue 前端、HTTP / SignalR 通道 |
| [项目编译说明](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/项目编译说明.md) | 编译、打包脚本、dist 产物布局、自动发布流程 |

**测试与运维**

| 文档 | 说明 |
|---|---|
| [测试报告（归档）](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/archive/测试报告.md) | 性能与 DataExchange 手动测试记录（2026-06，历史）|

**产品与延伸阅读**

| 文档 | 说明 |
|---|---|
| [产品介绍](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/marketing/产品介绍.md) | 对外短文案与能力清单 |
| [演示录屏脚本](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/marketing/演示录屏脚本.md) | 演示录屏分镜 |
| [为什么需要仿真](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/blog/储能仿真系统-困境和思考.md) | 联调阶段的现实困境与仿真难在哪里 |
| [真实与虚拟](https://github.com/lonelysyp123/ems-simulator/blob/master/docs/blog/储能仿真系统-真实和虚拟.md) | 哪些是"像真电站一样动"，哪些是虚拟环境才有的便利 |

> 文档约定：**点表、端口、单元数以仓库根目录的 `appsettings.json` 与 `*.csv` 为准**，文档中的数字仅为示例；操作界面为浏览器 Web，不是控制台 TUI。

## 构建与测试

```bash
dotnet build ./EssSimulator.csproj
dotnet test                                   # xUnit，覆盖潮流、控制桥、点表管道、BMS 保护、组态、IEC 61850 映射等
cd Web && npm install && npm run build        # Vite 直出仓库根 wwwroot/，dotnet publish 自动带上
cd Web && npm test                             # 前端单元测试（node --test，纯逻辑模块）
```

发布脚本：`scripts/publish-windows.sh`（自包含单文件，出 `EssSimulator-win-x64.zip`）；打 `v*` tag 由 GitHub Actions 自动发布到 Releases。

## 许可证

本项目以 **[GPL-3.0](https://github.com/lonelysyp123/ems-simulator/blob/master/LICENSE)** 发布：可自由使用、修改、分发，分发衍生作品时需同样以 GPL-3.0 开源。

- IEC 61850 功能依赖 **libiec61850**（GPLv3）及其 .NET 封装，Windows 原生库随包分发；macOS / Linux 原生库仅供开发机本地调试，不随包；
- 其余依赖见 [EssSimulator.csproj](https://github.com/lonelysyp123/ems-simulator/blob/master/EssSimulator.csproj)（NModbus、CsvHelper、log4net 等），均为宽松许可。

## 反馈与贡献

- 问题与建议请开 [Issue](https://github.com/lonelysyp123/ems-simulator/issues)，**点表相关问题请附 CSV 片段 + 端口 + 日志**，定位会快很多；
- 欢迎 PR：新设备点表、新协议适配、文档修正都很有价值，分工与提交约定见 [CONTRIBUTING](https://github.com/lonelysyp123/ems-simulator/blob/develop/CONTRIBUTING.md)；
- 分支约定：`master` 为发布分支，`develop` 为集成分支，功能分支用 `feature_*` / `fix_*`，合入后删除；
