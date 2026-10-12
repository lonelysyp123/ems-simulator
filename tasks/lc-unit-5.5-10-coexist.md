# Implementation Plan: LC 5.5MW / 10MW 单元片段共存

## Overview

`unit_5.5MW` 与 `unit_10MW` 是拼装 LC 里**始终同时存在**的两段窗口，不是互斥选型。任意工程都拼、都写；现场采哪一段是习惯，仿真器不按工程类型藏表。

组态画布上的 `pcs` 节点是 **PCS 变流器支路**（一台 PCS 由两条支路组成），不是整台 PCS。本期在协议绑定之外，增加一次展示改名，避免继续把支路叫成「PCS 变流器」。

不改电气求解、不改 `templateId`、不把两段做成 exclusive 整表。

## 对象（唯一用语）

没有「PCS 单元」。四级如下：

```
EMU 储能单元                    组态：emu          固定 2 个 PCS 组
 └── PCS 组 / PCS group         组态：emu_group    每组 1 或 2 台 PCS
      └── PCS                   协议对象，不单独占画布节点
           └── PCS 支路 × 2     组态：pcs          两条支路组成一台 PCS
```

| 名称 | 含义 | 组态 |
|------|------|------|
| PCS 支路 | PCS 的组成部分；画布上现有「PCS 变流器」节点就是它 | `templateId=pcs`（id 不改，只改显示名） |
| PCS | 由 **两条** PCS 支路组成 | 无独立模板；= 某 PCS 组内一对支路（模块1+模块2） |
| PCS 组 | 含 1 或 2 台 PCS（即 2 或 4 条支路） | `emu_group` |
| EMU | 固定 **两个** PCS 组 | `emu` |

双耳箱变：一耳 = 一个 PCS 组，不另开协议对象。

典型构成：

- **5.5MW EMU**：两组各 1 台 PCS（每组 2 条支路，共 4 条支路 / 2 台 PCS）
- **10MW EMU**：两组各 2 台 PCS（每组 4 条支路，共 8 条支路 / 4 台 PCS）——unit 片段每个 `n` 仍只暴露该组 **第一台 PCS** 的两条支路；组内第二台 PCS 的两条支路只在 `group` 段槽位里

组内只有 1 条支路时，该 PCS 的模块 2 为 0。

## Architecture Decisions

- **两段共存，不选型。** 拼装 LC = `system` + `group` + `bms` + `mv` + `unit_5.5MW` + `unit_10MW`。无 `lc_unit` 下拉。选 `lc=emu` 整表时仍整表替换。
- **`n` = 该 EMU 内的 PCS 组号，固定 1 和 2。** 与「画了几个 `emu_group`」脱钩：缺组当 0 台 / 0 条支路。
- **unit 片段的模块1/2 = 该 PCS 组内第一台 PCS 的两条支路**（组态里该组顺序前两个 `pcs` 节点）。
- **5.5MW 段只写 `n=1`（第一个 PCS 组）。** 第二个 PCS 组不进 `unit1_param`。这就是「一个 EMU 里的前一台 PCS（两条支路）看得见，后一台 PCS 不管」。
- **10MW 段写 `n=1,2`。** 所以表上是 `n=2`：两个 PCS 组各一台 PCS 的两条支路。
- **`group` 段 `n` 同样是 PCS 组号。** 组内最多 4 条支路槽（pcs1–pcs4）对应组内最多 2 台 PCS；现 CSV 槽位保留，空槽写 0。
- **不改 `templateId`。** 工程 JSON、连线、`PcsList`、ModelSim 路径仍用 `pcs`。只改模板显示名、默认实例名、校验文案、手册、EMU 树计数标签。
- **`maxPcsPerGroup` 不用于在两张 unit 表之间二选一。** `model.json` 的 `id` 改为目录名 `unit_5.5MW` / `unit_10MW`（现在都叫 `unit`）。

交叉读（两段都在）：

| 工程 | 5.5MW 段（仅 PCS 组 1） | 10MW 段 |
|------|-------------------------|---------|
| 5.5MW（两组各 1 台 PCS） | 组 1 那台 PCS 的两条支路 | `n=1` 同上；`n=2` = 组 2 那台 PCS |
| 10MW（两组各 2 台 PCS） | 只含组 1 **第一台** PCS 的两条支路 | `n=1` 组 1 第一台；`n=2` 组 2 第一台；各组第二台 PCS 只在 `group` 段 |

地址（CSV 不动）：

| 片段 | 基址 | `n` 步长 | 点名 | 采集 |
|------|------|----------|------|------|
| `unit_10MW` | 2600 | 300 | `unit_param{offset+600*(n-1)}` | 每周期写 `n=1,2` |
| `unit_5.5MW` | 5000 | 600 | `unit1_param{offset+600*(n-1)}` | 只写 `n=1` |

## 依赖图

```
对象用语 + model.json id
    │
    ├── 拼装：两段始终并入；unit 按 pairCount 展开（5.5→1，10→2）
    │
    ├── 采集：同一套「PCS 组 + 组内前两条支路」双写 unit_param / unit1_param
    │
    └── 组态展示：pcs 显示名改为「PCS 变流器支路」（templateId 不变）
            │
            └── 文档 / EMU 树计数 / 校验文案
```

## Task List

### Task 1: 元数据与拼装（两段始终共存）

**Description:** 修正 `unit_5.5MW` / `unit_10MW` 的 `model.json`：`id` 与目录名一致；说明写成 PCS 组窗口，不要 `maxPcsPerGroup` 互踢。`LcPointMapComposer` 两段都收。单元片段展开次数与 `groupCount` 脱钩：5.5MW 只物化 `n=1`，10MW 即使只有 1 个 `emu_group` 也物化 `n=1,2`（地址 2900）。其它片段仍按 PCS 组数展开，但组数按 **固定 2**（缺组空槽）。冲突检测应通过（地址 2600 vs 5000，点名 `unit_param` vs `unit1_param`）。

**Acceptance criteria:**
- [ ] `groupCount=1` 的拼装结果同时含 `unit_param0@2600`、`unit_param600@2900`、`unit1_param0@5000`
- [ ] 无 `unit1_param` 的 `n=2` 点
- [ ] 两段 `id` 不再都是 `unit`
- [ ] 无「只拼其中一张」的配置项

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~LcPointMapComposer|FullyQualifiedName~LcUnitMap"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None

**Files likely touched:**
- `pointmaps/models/lc/unit_5.5MW/model.json`
- `pointmaps/models/lc/unit_10MW/model.json`
- `LocalControl/LcPointMapComposer.cs`
- `LocalControl/LcPointMapExpander.cs`（若需按片段不同展开次数）
- `EssSimulator.Tests/LocalControl/LcPointMapComposerTests.cs`

**Estimated scope:** Medium

### Task 2: 按 PCS 组双写 unit 遥测

**Description:** `LcUnitMap` 收成两份布局（前缀/基址/步长/`pairCount`）。`SyncUnitFragment` 不再跟「每组只采画布前两台、且只写 `unit_param`」。对每个 PCS 组：模块1/2 = 该组扁平顺序前两条 **支路**（`pcs` 节点）。5.5MW 布局只写组 1；10MW 写组 1 和组 2。缺支路写 0。聚合（交流电流、电网 P/Q 等）仍是这一对支路（一台 PCS）的合计。

**Acceptance criteria:**
- [ ] EMU=两组×各 2 条支路（各 1 台 PCS）：5.5MW 段只有组 1；10MW `n=2` 是组 2
- [ ] EMU=两组×各 1 条支路：两段模块 2 均为 0；10MW `n=2` 模块 1 = 组 2 那条支路
- [ ] EMU=两组×各 4 条支路（各 2 台 PCS）：unit 段仍只绑每组前两条支路；`group` 段 pcs3/pcs4 仍有后两条

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~LcUnit"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1

**Files likely touched:**
- `LocalControl/LcUnitMap.cs`
- `LocalControl/StandardLcRuntime.cs`
- `LocalControl/LcLayout.cs`
- `EssSimulator.Tests/LocalControl/LcUnitMapTests.cs`

**Estimated scope:** Medium

### Task 3: 组态 `pcs` 显示名改为「PCS 变流器支路」

**Description:** 画布模板 `pcs` 当前 `Name=PCS 变流器`、默认实例名 `PCS变流器`，语义上是一条 PCS 支路，不是整台 PCS。只改**展示名与说明**，`templateId` 保持 `pcs`，已存工程节点 id / 参数键 / 连线不迁移。EMU 树、校验、手册里把「一台 PCS 变流器」改成支路口径；计数「PCS×N」改为「支路×N」（N = `pcs` 节点数）。不要改 `PcsList`、电气设备类名、Modbus 点名。

**Acceptance criteria:**
- [ ] `TopologyTemplates.Get("pcs").Name == "PCS 变流器支路"`
- [ ] 默认 `parameters.name` 为 `PCS变流器支路`
- [ ] 模板 Description、`emu` / `emu_group` 描述、校验 `PCS_EMU_UNASSIGNED` 不再把该节点称作整台「PCS 变流器」
- [ ] 左侧 EMU 树计数为「支路×N」
- [ ] `templateId` 仍为 `pcs`；旧工程打开无需迁移
- [ ] 用户手册组态设备列表与模板名一致

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~TopologyTemplates"`
- [ ] 组态编辑器：设备栏名称、拖入默认名、EMU 树计数（浏览器点选）
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None（可与 Task 1 并行）

**Files likely touched:**
- `Web/Topology/TopologyTemplates.cs`
- `Web/Topology/TopologyValidator.cs`
- `Web/src/components/topology/EmuTree.vue`
- `EssSimulator.Tests/Topology/TopologyTemplatesTests.cs`
- `docs/用户手册.md`
- `Web/Topology/TopologyRuntimeConverter.cs`（Notes 里「PCS×」改为「支路×」时一并改，避免保存摘要继续说台）

**Estimated scope:** Small

### Task 4: 文档

**Description:** `pointmaps/README.md`、系统设计说明：互补片段 + 两段 unit 窗口共存；`n` = PCS 组；模块 = 支路；画布 `pcs` = 支路。不要把旧 `trina_5.5MW` 整表叙事混进来。

**Acceptance criteria:**
- [ ] 文档不再出现「PCS 单元」作为协议对象
- [ ] 写清 5.5MW 段只覆盖 PCS 组 1、10MW 段 `n=1,2`、两段始终拼装

**Verification:**
- [ ] 人工过一遍 README 与系统设计说明相关段

**Dependencies:** Task 1–3

**Files likely touched:**
- `pointmaps/README.md`
- `docs/系统设计说明.md`
- `docs/用户手册.md`（若 Task 3 未改完）

**Estimated scope:** Small

## 明确不做

- 不把 5.5/10MW 做成 `role=exclusive` 整表
- 不按支路/PCS 台数强制改选型
- 不改 `pcs` 的 `templateId`、不改电气拓扑与双耳求解
- 不按「耳」重排点表（耳 = PCS 组）
- 不把两条支路自动合成一个画布节点（PCS 仍由两条 `pcs` 支路节点组成）
- 不删 `group` CSV 的 pcs3/pcs4 槽位
