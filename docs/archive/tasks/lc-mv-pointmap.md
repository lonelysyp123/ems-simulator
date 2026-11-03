> 已归档。原 `tasks/plan.md` / `todo.md` 被电站 EMS 策略参数热更新方案覆盖。

# Implementation Plan: LC 中压点表归属与运行时继承

## Overview

5.5MW / 10MW 点表不是 EMU 单元协议，而是中压系统（LocalControl）协议：SYSTEM 段地址与基础 `lc.csv` 同源（如 107 故障总、170 黑启动状态、171 高压开关、7 黑启动写入），再按场景加模块遥测、改控制编码。本计划把这两张表迁到 `pointmaps/models/lc/`，EMU 只保留 `standard`；LC 运行时做成基类 + 子类：基础 LC 继续按点名桥接 `simEmu`，中压子类按点表 ModelSim 把仿真模型采集进本机 LC Modbus，并允许重写控制。不改径向电气、不拆 csproj。

## Architecture Decisions

- **点表身份**：`trina_5.5MW` / `trina_10MW` 的完整 CSV 放到 `pointmaps/models/lc/{id}/lc.csv`（由 `emu.csv` 改名）。`pointmaps/models/emu/` 只留 `standard`（单机组 PCS 直控 `yx`/`yt`/`yc`）。
- **不做 CSV 文件合并继承**：中压表与基础 LC 在 107/170/171/7 等地址重叠，但 PCS 控制从 `27200` 启停换成 `syst6`（3/4/5/6）等，不能按行拼表。每个 LC 型号一份完整 `lc.csv`；「继承」落在 C# 运行时，不落在 CSV 拼接。
- **采集路径跟 ModelSim，写到本机 LC Modbus**：有绑定的点走已有 DataExchange（模型 → `simLc*` 寄存器），不新做一套去轮询 `simEmu` 寄存器。无绑定的基础 LC 仍用现有桥：按 ParamName 从 `simEmu*` 抄数、回写控制。
- **运行时继承（与点表选型挂钩）**：
  ```
  LcRuntimeBase          周期、影子、解析 simEmu/simLc、写 LC 寄存器
    └── StandardLcRuntime    现 LocalControlBridgeEngine（param → yx/yt/yc）
    └── ModelBoundLcRuntime  点表含 ModelSim：跳过点名桥，交给 DataExchange
          └── Trina55MwLcRuntime   4 模块 / 2 台 PCS 块口径
          └── Trina10MwLcRuntime   8 模块 / 4 台 PCS 块口径
  ```
  子类默认**不调用**基类点名桥（点名不同，叠跑会把电压写成 0、控制静默失败）。基类提供可复用工具；子类可增加采集源、可 override 控制。
- **自动选型改打 LC**：组态 2/4/8 台 PCS → `lc` 的 `standard` / `trina_5.5MW` / `trina_10MW`。**不再改 `emu` 选型**。若 `device-models.json` 里 `emu` 仍是已搬走的 trina id，迁到 `lc` 并把 `emu` 回 `standard`。
- **现成能力复用**：`LocalControlModbusServer` 在点表有 ModelSim 时已启用 DataExchange 且桥接 `UsesDataExchange` 早退；`PointCatalogLoader` / 插件 / `EmuSystemOperationApplier` 已把 `simLc` 当 EMU 同构。迁表后中压 LC 的 syst6/syst7/syst1010 应直接生效，本计划补的是归属、工厂与子类扩展点，而不是再写一套总控语义。
- **不做**：把 5.5/10MW 继续挂在 `simEmu`；CSV `extends` 合并；改 PlantEngine；把标准 LC 的 param 全部补上 ModelSim（可后续另开）。

## 依赖图

```
trina CSV 从 emu/ 迁到 lc/（完整 lc.csv）
    │
    ├── 自动选型改写 lc；清理过期 emu=trina_* 选型
    │
    └── 测试与文档改读 lc/{id}/lc.csv
            │
            └── LcRuntimeBase + 工厂（standard 行为不变）
                    │
                    ├── ModelBound 子类：跳过点名桥，DataExchange 写 LC Modbus
                    │
                    └── Trina55 / Trina10 子类：控制/采集扩展点 + 契约测试
```

## Task List

### Phase 1: 点表归到 LC

### Task 1: 把 5.5MW / 10MW 点表迁到 LC 型号目录

**Description:** 将 `pointmaps/models/emu/trina_5.5MW/`、`trina_10MW/` 整目录迁到 `pointmaps/models/lc/`，CSV 从 `emu.csv` 改名为 `lc.csv`（内容与 ModelSim 不动）。更新两份 `model.json` 的 description：明确这是中压 LC/MV-EMS 点表，挂在 `simLc*`，绑定仍指向 `emuDeviceId.*`。EMU 侧只保留 `standard`。

**Acceptance criteria:**
- [ ] 仓库中不存在 `pointmaps/models/emu/trina_5.5MW` 与 `trina_10MW`
- [ ] `pointmaps/models/lc/trina_5.5MW/lc.csv`、`lc/trina_10MW/lc.csv` 存在且 ParamName/ModelSim 与迁前一致
- [ ] `DeviceModelRegistry.ListTypes` 下 `lc` 含 `standard`、`trina_5.5MW`、`trina_10MW`；`emu` 只含 `standard`

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~DeviceModelRegistry"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None

**Files likely touched:**
- `pointmaps/models/emu/trina_5.5MW/**`（删除）
- `pointmaps/models/emu/trina_10MW/**`（删除）
- `pointmaps/models/lc/trina_5.5MW/lc.csv`、`model.json`
- `pointmaps/models/lc/trina_10MW/lc.csv`、`model.json`

**Estimated scope:** Small: 1-2 files（实为目录搬迁）

### Task 2: 组态自动选型改打 LC，并迁移过期 emu 选型

**Description:** 将 `EmuPointMapAutoSelect` 改为（或替换为）`LcPointMapAutoSelect`：2/4/8 台 PCS 分别选 LC 的 `standard` / `trina_5.5MW` / `trina_10MW`，写入 `selections["lc"]`，不改 `selections["emu"]`。若已有 `emu=trina_5.5MW|trina_10MW`，一次迁移：`lc` 设为该 id，`emu` 改为 `standard`。工程保存/应用入口改调新 API。

**Acceptance criteria:**
- [ ] 4 台 PCS 保存工程后 `lc=trina_5.5MW`，`emu` 保持原值（测例里可预置 `standard`）
- [ ] 2 台 PCS → `lc=standard`；8 台 → `lc=trina_10MW`
- [ ] 预置 `emu=trina_10MW` 时，迁移后 `lc=trina_10MW` 且 `emu=standard`
- [ ] 其它 PCS 数量仍不改选型

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PointMapAutoSelect|FullyQualifiedName~LcPointMapAutoSelect|FullyQualifiedName~EmuPointMapAutoSelect"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1

**Files likely touched:**
- `Web/Topology/EmuPointMapAutoSelect.cs`（改名或薄封装）
- `Web/Topology/TopologyEndpoints.cs`、`SystemConfigEndpoints.cs`
- `EssSimulator.Tests/Topology/EmuPointMapAutoSelectTests.cs`

**Estimated scope:** Medium: 3-5 files

### Task 3: 测试与说明改读 LC 路径

**Description:** `TelemetryPluginTests.LoadCatalog` 改为加载 `pointmaps/models/lc/{id}/lc.csv`，目录编译用 `simLc1`（`emuDeviceId` → `emu1` 的替换结果应与现断言相同）。其它仍指向 `emu/standard/emu.csv` 的测试不动。`pointmaps/README.md` 写清：EMU 只有 standard；中压 5.5/10MW 是 LC 型号；自动选型改 `lc`。

**Acceptance criteria:**
- [ ] 插件/sum/max/syst* 绑定断言在 `simLc1` + 新路径下全部通过
- [ ] 无测试再读 `models/emu/trina_*`
- [ ] README 不再把 5.5/10MW 列为 EMU 型号

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~TelemetryPlugin|FullyQualifiedName~PointMapPathResolver|FullyQualifiedName~DeviceModelRegistry"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1

**Files likely touched:**
- `EssSimulator.Tests/DataExchange/TelemetryPluginTests.cs`
- `pointmaps/README.md`

**Estimated scope:** Small: 1-2 files

### Checkpoint: Phase 1

- [ ] 系统配置页 LC 能选到 5.5MW/10MW，EMU 只剩标准版
- [ ] 全量 `dotnet test EssSimulator.Tests` 绿
- [ ] 与人确认：未改运行时前，standard LC 桥接行为与迁表前一致（中压表尚未经工厂启用也可先用 DataExchange 早退路径）

### Phase 2: LC 运行时基类

### Task 4: 抽出 LcRuntimeBase 与选型工厂，standard 行为不变

**Description:** 把 `LocalControlBridgeEngine` 收成 `StandardLcRuntime : LcRuntimeBase`。`LcRuntimeBase` 持有周期入口、控制影子、读/写 LC 与解析 `simEmu*` 的工具方法；`SyncTelemetry` / `ApplyControls` 标 virtual。`LcRuntimeFactory.Create(lcModelId)`：`standard`（及未知 id）→ `StandardLcRuntime`。`LocalControlHostedService` 按当前 LC 选型为每个 `simLc*` 建 runtime。本任务不改桥接点名映射。

**Acceptance criteria:**
- [ ] LC 选型为 `standard` 时，启停/P·Q/孤岛电压/黑启动/高压开关仍按原 param→yx/yt 转发
- [ ] 工厂对未知型号回退 `StandardLcRuntime`
- [ ] 无新的对外协议行为

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~LocalControl|FullyQualifiedName~ModbusValueConverter|FullyQualifiedName~LcRuntime"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None（可与 Phase 1 并行，合入须在 Task 5 之前）

**Files likely touched:**
- `LocalControl/LocalControlBridgeEngine.cs`
- `LocalControl/LcRuntimeBase.cs`（新）
- `LocalControl/StandardLcRuntime.cs`（新，或原文件改名）
- `LocalControl/LcRuntimeFactory.cs`（新）
- `LocalControl/LocalControlHostedService.cs`
- `EssSimulator.Tests/LocalControl/`（工厂 + 行为钉）

**Estimated scope:** Medium: 3-5 files

### Task 5: ModelBoundLcRuntime — ModelSim 点表走 DataExchange，跳过点名桥

**Description:** 增加 `ModelBoundLcRuntime`：`RunCycle` 不再按 `yx3`/`yc20` 抄 `simEmu`。点表含 ModelSim 时继续用现有 `LocalControlModbusServer` DataExchange：遥测从绑定路径采集并写入本机 LC Modbus；控制由目录效果落到 `emu{n}.Emu.*` / PCS。`UsesDataExchange` 早退与子类 skip-bridge 对齐，避免双写。工厂：`trina_5.5MW` / `trina_10MW` → 对应中压子类（本任务可先都指向 `ModelBoundLcRuntime`，Task 6 再拆子类）。

**Acceptance criteria:**
- [ ] LC=trina_5.5MW 时写 `syst6=3` 使该 LC 首机组全部 PCS `pcsOnOffSwitch=true`（不要求存在 `yx3`）
- [ ] 同场景写 `syst7=1` 批量打开所属 PCS 黑启动
- [ ] LC 寄存器 `sysyc104` 等能从模型刷新（SOC 等已绑定点）
- [ ] LC=standard 时仍走点名桥，不受影响

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~LcRuntime|FullyQualifiedName~TelemetryPlugin|FullyQualifiedName~DeviceControlFacade|FullyQualifiedName~Emu"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 选型 LC=trina_5.5MW、EMU=standard、EnableLocalControl=true，重启后对 LC 端口写 syst6，主接线 PCS 启动

**Dependencies:** Task 1, Task 4

**Files likely touched:**
- `LocalControl/ModelBoundLcRuntime.cs`（新）
- `LocalControl/LcRuntimeFactory.cs`
- `LocalControl/LocalControlModbusServer.cs`（仅必要时对齐注释/早退）
- `EssSimulator.Tests/LocalControl/`（中压控制/遥测契约）

**Estimated scope:** Medium: 3-5 files

### Checkpoint: Phase 2

- [ ] standard LC：mbpoll 写 param60 仍能启动 PCS1
- [ ] 5.5MW LC：mbpoll 写 syst6=3 启动该机组全部模块；simEmu 仍是 standard 点表
- [ ] 全量测试绿

### Phase 3: 中压子类与扩展点

### Task 6: Trina55 / Trina10 子类：可增采集、可覆写控制

**Description:** `Trina55MwLcRuntime` / `Trina10MwLcRuntime` 继承 `ModelBoundLcRuntime`。基类提供 `CollectExtra` / `ApplyControls` 虚方法：子类可增加采集设备（例如额外电表、以后的 BMS），也可覆写总控（相对标准 LC 的逐 PCS 启停）。本任务用测试钉住差异：5.5MW 目录含 PcsList[0..3] 绑定、10MW 含 PcsList[0..7]；工厂按型号返回对应子类。不在本任务里新加真实设备点（CSV 已含模块遥测）。

**Acceptance criteria:**
- [ ] `LcRuntimeFactory.Create("trina_5.5MW")` 的运行时类型为 `Trina55MwLcRuntime`
- [ ] `Create("trina_10MW")` → `Trina10MwLcRuntime`
- [ ] 虚方法存在且中压子类不调用 `StandardLcRuntime` 的点名桥
- [ ] 5.5MW 目录编译在 PcsCount=2 的机组上按现有 `EmuDeviceCatalogFilter` 剔除 PcsList[2]/[3]（门控行为保持）

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~LcRuntime|FullyQualifiedName~EmuDeviceCatalogFilter|FullyQualifiedName~TelemetryPlugin"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 5

**Files likely touched:**
- `LocalControl/Trina55MwLcRuntime.cs`、`Trina10MwLcRuntime.cs`（新）
- `LocalControl/LcRuntimeFactory.cs`
- `EssSimulator.Tests/LocalControl/`

**Estimated scope:** Small: 1-2 files（加测试则为 Medium）

### Task 7: 文档与系统配置文案

**Description:** 同步 `pointmaps/README.md`、`docs/系统设计说明.md`（及用户手册里把 5.5/10MW 写成 EMU 的句子）：LC 是中压系统点表，可按场景换型号；EMU 是单元直控；自动选型改 LC。系统配置页说明不强制改 Vue 结构，只改会误导的文案（若有「EMU 5.5MW」字样）。

**Acceptance criteria:**
- [ ] 文档不再把 trina_5.5MW/10MW 称为 EMU 点表
- [ ] 写明采集：ModelSim → 模型 → 写入 simLc Modbus；标准 LC 仍点名桥接 simEmu

**Verification:**
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: README 与系统配置页描述一致

**Dependencies:** Task 2, Task 6

**Files likely touched:**
- `pointmaps/README.md`
- `docs/系统设计说明.md`
- `docs/用户手册.md`（仅相关句）
- `Web/src/views/SystemConfigView.vue`（仅误导文案时）

**Estimated scope:** Small: 1-2 files

### Checkpoint: Complete

- [ ] Phase 1–3 验收标准均满足
- [ ] `dotnet test EssSimulator.Tests` 与 `dotnet build ./EssSimulator.csproj` 通过
- [ ] 未做项已明确：CSV extends 合并、标准 LC 全面 ModelSim 化、PlantEngine、多 csproj

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 迁表后仍有代码/脚本按 `emu/trina_*/emu.csv` 加载 | 启动失败或测红 | Task 3 先全库搜路径；发布脚本只认 models 目录扫描 |
| ModelBound 与点名桥双写 | 中压 LC 遥测被桥接写成 0 | 子类不调用基类 SyncTelemetry；沿用 UsesDataExchange 早退 |
| 现场 `emu=trina_*` 选型残留 | simEmu 找不到点表 | Task 2 启动/保存时迁移到 lc |
| 多机组 + 中压单机点表 | 第二台 emu 的模块不在 emu1 绑定里 | 保持现有 firstEmuId；多中压系统仍是多 simLc，每路绑本组首机组 |
| 标准 LC 与中压 LC 控制语义不同（0/1 vs 3/4/5/6） | 联调脚本写错点 | 文档 + Task 5 契约测试钉住 syst6 |

## Open Questions

- 多储能单元（例如 2×5.5MW）时，是否仍按 `LocalControlEmuPerGroup` 切多路 `simLc`（每路一张中压表绑本组 `emu{n}`）？默认保持现状。
- 以后若只要「在标准 LC 上加几个电表点」，再考虑 `model.json` 的 `extends` + 增量 CSV；本期 5.5/10MW 用完整表。
- 插件类名 `TrinaEmuFaultWordPlugin` 是否改名：本期不动，避免无行为 diff。
