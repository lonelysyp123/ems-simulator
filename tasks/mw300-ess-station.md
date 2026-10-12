# Implementation Plan: 300MW 纯储能测试工程

## Overview

增加一份写死的 300MW 纯储能测试工程。拓扑和 Modbus 端口都在代码常量里，启动时按工程 Id 直接生成运行时配置。现有组态工程、向导、`protocol-ports.json` 和端口车道保持原样。这份工程不能通过画布、工程库写入接口或端口页面改掉。

上一份计划（站用 SVG 恒无功）已归档到 `tasks/svg-constant-q.md` / `tasks/todo-svg-constant-q.md`。

## Architecture Decisions

- **独立工程 Id。** `mw300-ess-test`。`runtime-mode.json` 的 `activeProjectId` 等于它时，`TopologyOverlayLoader` 忽略 `runtime-overlay.json` 的内容，改为调用 `Mw300EssStation.BuildOverlay()`。其它工程仍读 JSON。
- **运行时类型不变。** 产物仍是 `TopologyRuntimeOverlay`、`EssUnitConfig`、`ProtocolPortEntry`。求解器、点表和 `ModbusPortHub` 不另写。
- **一段主变模型。** 现有 overlay 只有一个 `TransformerConfig`。三台 100 MVA 主变在本计划里是单元命名分段（每 20 台一段），电气上仍是一台 220/35 kV 主变带 60 个储能单元。不新增多主变网络。
- **规模写死。** 60 个储能单元，每单元 2 台 PCS。`PcsPhysicalConfig.RatedPower = 2500`（kW），合计 300 MW。每台 BMS 使用现有默认堆：12 簇、4 包、104 串。无光伏。主变 `RatedPower = 300000`，一、二次电压 220 kV / 35 kV。单元变 6300 kVA、35 kV / 690 V。SVG 开启，额定 10000 kvar。并网点 220 kV。
- **端口写死，且与现有车道分开。** 单元 `n`（1–60）共用 TCP `10000 + n`。`ModbusPortHub` 已支持同一端口多个从站。设备名沿用现有规则：`UnitCount` 是 PCS 通道数，故 BMS / EMU 各 120 台，LC 60 路。
  - 从站 1：`simLc{n}`
  - 从站 2、3：该单元两台 `simEmu`
  - 从站 10，簇从站 11–22：`simBms{2n-1}`
  - 从站 42，簇从站 43–54：`simBms{2n}`
  - 全站 `simEm` 1500、`simSel` 2001、`simSvg` 2201
- **不读端口覆盖。** 该工程的 `ProtocolPortPlan.Load` 直接返回 `BuildPortPlan()`，不读 `configs/protocol-ports.json`。IEC 61850 不按 PCS 启动。
- **电池固定簇等效。** 启动时 `BatteryCalcMode.CellPhysicsEnabled = false`。这份工程激活时，切换逐芯的接口拒绝写入。
- **档位限制不裁剪它。** 社区版会在 `AllowTopologyEditor == false` 时丢弃 overlay，并在 `MaxEssUnits == 2` 时裁剪单元。该工程 Id 绕过这两处，避免 300 MW 被静默变成 2 个单元。普通工程的裁剪逻辑不变。
- **磁盘上可以有一份生成结果，但不能当输入。** 应用时允许把 `BuildOverlay()` 写到 `runtime-overlay.json` 供查看。下次启动仍以代码为准。工程库不创建可编辑的 `projects/{id}.json`。
- **主接线不改。** 60 个单元仍走现有主接线渲染。本计划不做按段折叠。

## 依赖图

```
写死拓扑（Task 1）
        │
        ├── 启动分支（Task 3）── 应用与列表（Task 6）── 工程库只读（Task 8）
        │
        └── 簇等效锁定（Task 5）

写死端口（Task 2）
        │
        └── 端口 / IEC 分支（Task 4）── 端口写入拒绝（Task 7）── 端口页只读（Task 9）
```

Task 1 与 Task 2 互不依赖，可以并行。Task 3 依赖 Task 1。Task 4 依赖 Task 2，并依赖 Task 3 写入的 `StationProfileId`。Task 5 依赖 Task 3。Task 6 依赖 Task 1 和 Task 3。Task 7 依赖 Task 4。Task 8 依赖 Task 6。Task 9 依赖 Task 7。

## Task List

### Phase 1: 写死定义

- [x] Task 1: 300MW 运行时拓扑常量
- [x] Task 2: 300MW 端口计划常量

### Checkpoint: 常量可单测

- [x] 60 单元、120 PCS、无光伏，分段命名正确
- [x] 60 个 TCP 端口、从站号不重叠，且不占用 1501–1700 的设备车道
- [x] 普通 `ProtocolPortPlan.BuildDefault` 未被改动

### Phase 2: 启动切割

- [x] Task 3: 按工程 Id 加载代码拓扑
- [x] Task 4: 按工程 Id 使用代码端口并关闭 IEC 61850
- [x] Task 5: 锁定簇等效

### Checkpoint: 启动路径分开

- [x] 激活该 Id 时，改过的 overlay JSON 和 `protocol-ports.json` 都不生效
- [x] 其它工程仍从 JSON 和覆盖文件加载
- [x] 社区版档位不会把该工程裁成 2 个单元

### Phase 3: 禁止人为修改

- [x] Task 6: 应用入口与工程列表
- [x] Task 7: 拒绝端口覆盖写入

### Checkpoint: 写入被挡住

- [x] 应用该工程不依赖画布文件
- [x] 保存、打开、复制、删除该 Id 失败
- [x] 端口 PUT / 重置不写 `protocol-ports.json`

### Phase 4: 只读界面

- [x] Task 8: 工程库只读行
- [x] Task 9: 端口页与组态页锁定

### Checkpoint: Complete

- [x] 工程库能选用并应用，不能编辑、复制、删除
- [x] 端口页不能改号
- [x] 组态画布打不开这份工程
- [x] 相关测试与 `dotnet build ./EssSimulator.csproj` 通过

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 社区版丢弃或裁剪 overlay，300 MW 变成 2 单元 | High | Task 3 对该 Id 绕过 `AllowTopologyEditor` 和 `MaxEssUnits`；用测试锁住 |
| `protocol-ports.json` 里的同名条目在切回普通工程时仍生效 | Med | 测试工程的 Load 不读、PUT 不写该文件；普通工程行为保持现有测试 |
| 120 路 IEC 61850 把进程打满 | High | Task 4 在该工程不启动 MMS |
| 60 万节电芯逐芯积分 | High | Task 5 强制簇等效，并拒绝运行中切回逐芯 |
| 主接线一次画 60 个单元很挤 | Low | 本计划不改主接线；只读工程库仍可运行 |
| `UnitCount` 实际是 PCS 通道数，BMS 端口按单元数去配会少一半 | High | 计划按 120 个 `simBms` / `simEmu` 写死，测试断言数量 |

## Open Questions

无。分段只体现在单元名称，电气模型保持现有的单台主变。主接线布局不在本计划内。

## Task 1: 300MW 运行时拓扑常量

**Description:** 新增 `Mw300EssStation`，用常量生成一份 `TopologyRuntimeOverlay`。60 个储能单元，每单元 2 台 PCS、2 台 BMS，名称按每 20 台分为 3 段。无光伏。主变、单元变、并网点和 SVG 使用上面写死的数值。本任务不接入启动流程。

**Acceptance criteria:**
- [ ] `SourceProjectId` 为 `mw300-ess-test`，储能单元 60，光伏单元 0，`TopologyOverlayLoader.IsUsable` 为 true
- [ ] 每个单元 `PcsCount == 2`，两台 BMS 均为 12 簇、4 包、104 串；单元名能区分第 1–3 段
- [ ] `Pcs.RatedPower` 为 2500，主变 300000 kVA 量级且电压 220 kV / 35 kV，单元变 6300 kVA、35 kV / 690 V，SVG 开启且额定 10000 kvar

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Mw300EssStation"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `Web/Topology/Mw300EssStation.cs`
- `EssSimulator.Tests/Topology/Mw300EssStationTests.cs`

**Estimated scope:** Small: 1-2 files

## Task 2: 300MW 端口计划常量

**Description:** 在同一类型上生成 `ProtocolPortPlan`：120 个 `simBms`、120 个 `simEmu`、60 个 `simLc`，加上全站电表、SEL、SVG。单元 `n` 的这些设备共用端口 `10000 + n`，从站号按架构决定分配。不读取 `appsettings` 端口基数，也不读取覆盖文件。

**Acceptance criteria:**
- [ ] 设备数量为 BMS 120、EMU 120、LC 60，外加 `simEm`、`simSel`、`simSvg` 各 1
- [ ] 单元端口为 10001–10060；同一端口上的从站号（含 BMS 簇 11–22 与 43–54）互不重叠，且都在 1–247
- [ ] 除 1500、2001、2201 外，没有端口落在 1500–2299；`ValidateRanges()` 无错误

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Mw300EssStation"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `Web/Topology/Mw300EssStation.cs`
- `EssSimulator.Tests/Topology/Mw300EssStationTests.cs`

**Estimated scope:** Small: 1-2 files

## Task 3: 按工程 Id 加载代码拓扑

**Description:** `activeProjectId` 为 `mw300-ess-test` 时，加载器返回 `BuildOverlay()`，磁盘上的 overlay 内容不参与。`Program` 把该 overlay 应用到 `SimulatorConfig`，并写入 `StationProfileId`。此 Id 不因社区版关闭组态编辑而被丢弃，也不被 `MaxEssUnits` 裁剪。其它工程的加载和裁剪保持现状。

**Acceptance criteria:**
- [ ] 模式文件指向该 Id 时，即使 overlay JSON 缺失或内容是别的工程，加载结果仍是 60 个单元
- [ ] 模式文件指向其它工程时，仍按现有 JSON 加载
- [ ] 该 Id 在 `AllowTopologyEditor == false` 且 `MaxEssUnits == 2` 时仍保留 60 个单元；普通工程仍会被裁剪

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~TopologyOverlayLoader|FullyQualifiedName~Mw300EssStation"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** Task 1

**Files likely touched:**
- `Web/Topology/TopologyOverlayLoader.cs`
- `Program.cs`
- `Configuration/SimulatorConfig.cs`
- `EssSimulator.Tests/Topology/TopologyOverlayLoaderTests.cs`

**Estimated scope:** Medium: 3-5 files

## Task 4: 按工程 Id 使用代码端口并关闭 IEC 61850

**Description:** `ProtocolPortPlan.Load` 在 `StationProfileId` 为该工程时返回 `BuildPortPlan()`，跳过 `protocol-ports.json`。IEC 61850 层在同一条件下不创建 PCS 的 MMS 服务。`StationProfileId` 为空时，端口公式和覆盖文件与现在一致。

**Acceptance criteria:**
- [ ] 带该 `StationProfileId` 的配置得到 10001 起的单元端口；同目录下的覆盖文件不改变这些端口
- [ ] 该配置下 IEC 61850 启动列表为空
- [ ] 空 `StationProfileId` 的默认计划仍从 BMS 基础端口递增，并继续吃覆盖文件

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ProtocolPortPlan|FullyQualifiedName~Mw300|FullyQualifiedName~Iec61850"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** Task 2, Task 3

**Files likely touched:**
- `Protocol/Modbus/ProtocolPortPlan.cs`
- `Protocol/Iec61850/Iec61850LayerManager.cs`
- `EssSimulator.Tests/Protocol/ProtocolPortPlanTests.cs`
- `EssSimulator.Tests/Topology/Mw300EssStationTests.cs`

**Estimated scope:** Medium: 3-5 files

## Task 5: 锁定簇等效

**Description:** 该工程启动后强制 `BatteryCalcMode.CellPhysicsEnabled = false`。运行中切换逐芯的 HTTP 接口在该工程激活时拒绝，并保持簇等效。其它工程仍可切换。

**Acceptance criteria:**
- [ ] 配置带该 `StationProfileId` 时，启动路径把逐芯标志设为 false
- [ ] 该工程下切换接口返回失败，标志仍为 false
- [ ] 普通配置下切换接口仍能打开逐芯

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Mw300|FullyQualifiedName~ClusterEquivalent"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** Task 3

**Files likely touched:**
- `Program.cs`
- `Web/EndpointExtensions.cs`
- `EssSimulator.Tests/Topology/Mw300EssStationTests.cs`

**Estimated scope:** Small: 1-2 files

## Task 6: 应用入口与工程列表

**Description:** 系统应用接口接受 `mw300-ess-test`，直接采用代码生成的 overlay，写入运行模式。不要求 `configs/topology/projects` 里存在画布文件，也不调用画布工程的保存。工程列表在两个列表接口里都多出这一条，并标为只读。保存、打开、复制、删除这个 Id 都拒绝。

**Acceptance criteria:**
- [ ] 应用该 Id 后，`runtime-mode.json` 的激活工程是它，且不产生可编辑的 `projects/{id}.json`
- [ ] `/api/topology/projects` 与系统配置里的工程列表都包含这条只读工程
- [ ] 对该 Id 的保存、打开、复制、删除返回失败，磁盘上的其它工程不变

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~TopologyStore|FullyQualifiedName~Mw300"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** Task 1, Task 3

**Files likely touched:**
- `Web/Topology/SystemConfigEndpoints.cs`
- `Web/Topology/TopologyEndpoints.cs`
- `Web/Topology/TopologyRuntimeModels.cs`
- `EssSimulator.Tests/Topology/TopologyStoreWorkflowTests.cs`

**Estimated scope:** Medium: 3-5 files

## Task 7: 拒绝端口覆盖写入

**Description:** 该工程处于激活配置时，协议端口的保存和恢复默认都拒绝，并且不创建、不修改 `configs/protocol-ports.json`。查询接口仍返回代码里的端口计划。

**Acceptance criteria:**
- [ ] PUT 与重置在该工程下返回失败
- [ ] 失败后覆盖文件保持调用前的内容（原本没有则仍然没有）
- [ ] GET 能看到 10001 起的单元端口

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ProtocolPort"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** Task 4

**Files likely touched:**
- `Web/ProtocolPortEndpoints.cs`
- `Protocol/Modbus/ProtocolLayerManager.cs`
- `EssSimulator.Tests/Protocol/ProtocolPortPlanTests.cs`

**Estimated scope:** Small: 1-2 files

## Task 8: 工程库只读行

**Description:** 工程配置页列出这条测试工程。可以选择并「确认并重新初始化」。编辑、复制、删除入口对该行不可用。普通工程的这些操作保持可用。

**Acceptance criteria:**
- [ ] 列表中出现「300MW纯储能测试工程」，且没有编辑、复制、删除
- [ ] 选中后可以应用，请求体里的工程 Id 为 `mw300-ess-test`
- [ ] 普通工程行仍能编辑、复制、删除

**Verification:**
- [ ] Tests pass: 若该页已有组件测试则跑对应 `node --test`；否则以浏览器手工验证为准
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 打开工程配置页，确认只读行可以应用，普通工程操作仍在

**Dependencies:** Task 6

**Files likely touched:**
- `Web/src/views/ProjectHubView.vue`
- `Web/src/services/api.js`

**Estimated scope:** Small: 1-2 files

## Task 9: 端口页与组态页锁定

**Description:** 协议端口页在该工程运行时展示代码中的端口，保存和恢复默认不可用。组态编辑页不能打开 `mw300-ess-test` 作为可编辑画布。

**Acceptance criteria:**
- [ ] 端口页显示 10001 段端口，保存与恢复默认不可提交
- [ ] 直接打开该工程的组态地址时停留在只读说明或回到工程库，不出现可拖拽画布
- [ ] 普通工程的端口页和组态页仍可编辑

**Verification:**
- [ ] Tests pass: `node --test Web/src/views/ProtocolPortsView.test.mjs`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 应用测试工程后看端口页；再打开一个普通工程，确认端口和组态仍可改

**Dependencies:** Task 7, Task 6

**Files likely touched:**
- `Web/src/views/ProtocolPortsView.vue`
- `Web/src/views/ProtocolPortsView.test.mjs`
- `Web/src/views/TopologyView.vue`

**Estimated scope:** Medium: 3-5 files
