# Implementation Plan: 电站 EMS 策略参数热更新

## Overview

电站策略的斜率、PID、视在限幅、功率分配、计划曲线，以及一次调频 / 惯量 / 下垂调压的明细参数，已经在引擎和 `configs/ems-strategy.json` 里跑，页面只露出模式开关与本地 P/Q。本计划把这些参数接到 Web：**改完立即 `UpdateConfig`（不重启）**，并写回与启动加载同一份 JSON。不改控制律公式、不合并第三方遥控 tab、不把两套下发合成一套。

上一份 `tasks/plan.md`（LC 中压点表）已归档到 `tasks/lc-mv-pointmap.md`。

## Architecture Decisions

- **热更新 = 引擎立刻换参 + 落盘。** 沿用已有 `EmsStrategyRuntime.TryReplace` → `Engine.UpdateConfig`。成功后写入 `AppContext.BaseDirectory/configs/ems-strategy.json`（与 `LoadFileOrDefault` 同路径）。重启读到的就是上次页面应用的值。
- **参数变更不复位控制状态。** 现逻辑：仅有功/无功**模式切换**时 `Reset` PI / ACTION。改死区、Kp、斜率、曲线点只 `Configure`，积分与调频 ACTION 继续。开关类（调频/惯量/下垂使能）同样不强制 Reset，由 `Step` 按新开关跳过或清增量。
- **HTTP 做嵌套覆盖，不当残缺体整表替换。** 现前端 `POST /api/ems-strategy` 只发几个标量。若改成「反序列化成完整 Config 再替换」，缺字段会掉回默认、冲掉 JSON 里的 PID。`EmsStrategyPatchRequest` 增加可空嵌套对象（`Slope`、`ActivePid`、`PrimaryFrequency`…）；服务端 `Clone` 当前配置再覆盖非空字段。占用仍走 `/enable`。
- **快照轮询与表单草稿分离。** 现页每秒 `GET` 后 `applyStatus` 会覆盖输入。参数卡用本地 draft；1s 只刷新 snapshot。每张卡「应用」提交对应嵌套对象（避免每个 spinner 写盘）。
- **周期字段维持 TimeSpan JSON（`00:00:01`）。** 前端从 GET 原样带回；不新增平行 `*Ms` DTO，避免两套单位。
- **`Enabled` 一并落盘。** 文件里 `Enabled: true` 时启动会 `SyncGate` 占用，与「配置即真源」一致。第三方占用时 `TryReplace`/`TrySetEnabled` 仍 409。
- **不做：** 改调频/惯量/分配公式；合 EMS tab；把参数写进组态工程；双写仓库源文件（只写运行目录；`PreserveNewest` 下输出文件更新后重建不会被仓库旧文件盖掉）。

## 依赖图

```
可注入路径的 Persist（与 Load 同文件）
    │
    └── Patch 嵌套覆盖（Clone + 非空字段）
            │
            ├── 公共算法卡：斜率 / PID / 视在额定
            │
            ├── 辅助服务卡：调频 / 惯量 / 下垂明细
            │
            └── 分配 + 曲线表 + 恒压/远程等漏掉的运行量
                    │
                    └── 折叠、模式联动禁用、脏标记
```

实现顺序自下而上：先落盘与覆盖语义（无 UI 也能用 POST 热更新），再按垂直切片补页面，每一刀都能在不重启的仿真里改参见快照。

## Task List

### Phase 1: Foundation

### Task 1: 配置落盘（与 Load 同路径）

**Description:** 给 `EmsStrategyRuntime` 可注入配置路径（默认仍是 `BaseDirectory/configs/ems-strategy.json`）。`TryReplace` / `TrySetEnabled` 在引擎更新成功后，用现有 JSON 选项（含枚举字符串）写回该文件。失败的占用（第三方挡住）不得写盘。抽出 `Save` 便于单测用临时文件，不碰仓库 `configs/ems-strategy.json`。

**Acceptance criteria:**
- [ ] `TryReplace` 成功后临时路径文件可反序列化为相同斜率/PID/调频字段
- [ ] `TrySetEnabled(true)` 在第三方占用失败时文件内容不变
- [ ] 默认路径与 `LoadFileOrDefault` 一致

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EmsStrategyRuntime"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None

**Files likely touched:**
- `EmsStrategy/Adapter/EmsStrategyRuntime.cs`
- `EssSimulator.Tests/EmsStrategy/EmsStrategyRuntimeTests.cs`（新建）

**Estimated scope:** Small: 1-2 files

### Task 2: PATCH 嵌套覆盖全部参数对象

**Description:** 把 `ApplyPatch` 抽成可单测的覆盖器：在当前 `Clone()` 上写入 `EmsStrategyPatchRequest` 里非空的标量与嵌套对象（`Slope`、`ReactiveSlope`、`ActivePid`、`ReactivePid`、`PrimaryFrequency`、`Inertia`、`VoltageDroop`、`ActiveCurve`、`ReactiveCurve`、`Distribution`，以及 `ApparentRatedKva`、`PlantRatedKw`、`VoltageSetV`、`VoltageKp`、`PfSign`、`SystemSwitch`、`ActiveEnable`、`ReactiveEnable`、`LocalRemote`、`RemoteReactiveSetKvar`）。嵌套对象整段替换该子树（一次应用一张卡），不是字段级 merge。现有标量补丁行为保持，旧页面不发嵌套对象时 PID 不被清掉。

**Acceptance criteria:**
- [ ] 只 PATCH `ActivePid.Kp` 所在对象时，未出现在 body 的 `PrimaryFrequency.DroopPercent` 保持原值
- [ ] 提交完整 `PrimaryFrequency` 后死区/droop/周期进入 `runtime.Config`，随后 `Step` 使用新死区（可沿用现有调频测例注入 config）
- [ ] 旧标量 PATCH（`localActiveSetKw`、`primaryFrequencyEnabled`）仍有效

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EmsStrategy"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1

**Files likely touched:**
- `Web/EmsStrategy/EmsStrategyEndpoints.cs`
- `EmsStrategy/Application/EmsStrategyConfigPatcher.cs`（新建，或放在 Endpoints 旁并 InternalsVisible）
- `EssSimulator.Tests/EmsStrategy/EmsStrategyConfigPatcherTests.cs`（新建）

**Estimated scope:** Medium: 3-5 files

### Checkpoint: Foundation

- [ ] 不经 UI，用测试或手动 POST 嵌套对象即可热更新并写到运行目录 JSON
- [ ] 模式未变时改 Kp 不 Reset 积分（现 `PidController.Configure` 行为，补一条断言钉住）
- [ ] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EmsStrategy"` 绿
- [ ] 与人工确认后再做页面

### Phase 2: Core Features

### Task 3: 快照与草稿分离 + 斜率 / PID / 视在参数卡

**Description:** `EmsStrategyView` 1s 轮询只更新 snapshot 与 `gateOwner`/`enabled`（enabled 以服务端为准，本地开关仍走 `/enable`）。模式与本地 P/Q/PF 保持现有即时 PATCH。新增可折叠卡：有功/无功斜率、有功/无功 PID、视在额定与站额定。卡内为 draft，点「应用」POST 对应嵌套对象。第三方占用时卡禁用。

**Acceptance criteria:**
- [ ] 打开 PID 卡改 Kp 未点应用时，轮询不会把输入复原
- [ ] 应用后 GET 的 `config.activePid.kp` 与文件（运行目录）一致，快照下一拍起用新 PI（闭环模式）
- [ ] 斜率使能可从页面打开；默认仍关闭，与 JSON 一致

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EmsStrategy"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 策略页改 Kp 点应用，无需重启；刷新页值仍在

**Dependencies:** Task 2

**Files likely touched:**
- `Web/src/views/EmsStrategyView.vue`
- `Web/src/styles/app.css`（仅补参数卡间距，若现有 card 够用则不动）

**Estimated scope:** Small: 1-2 files

### Task 4: 一次调频 / 惯量 / 下垂明细参数卡

**Description:** 在现有三个使能开关下增加明细 draft：调频（额定 f、死区%、droop、段数、过/欠频、周期、复归、限幅）；惯量（Tj、幅度/速率死区、频率范围、闭锁调频、周期/复归）；下垂（额定 U、k1/k2、死区、段数、过/欠压、限幅、周期）。「应用」提交整段 `PrimaryFrequency` / `Inertia` / `VoltageDroop`（含当前 Enabled，避免覆盖开关）。开环有功时调频/惯量卡禁用；下垂卡在无功非闭环固定/曲线时禁用（与现开关规则一致）。

**Acceptance criteria:**
- [ ] 页面能改调频 droop 与死区并热更新；快照在越死区后 ΔP 与新参数相符（可用已有电网频率入口做手工确认）
- [ ] 只应用惯量卡时，调频 JSON 段不被默认值覆盖
- [ ] 使能开关仍即时 PATCH `*Enabled`，与卡内 Enabled 不打架（应用卡时带上开关当前值）

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EmsStrategy"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 改死区后不用重启，ACTION 文案/ΔP 随新死区变化

**Dependencies:** Task 3

**Files likely touched:**
- `Web/src/views/EmsStrategyView.vue`

**Estimated scope:** Small: 1-2 files（若单文件过大则抽 `EmsStrategyAuxParams.vue`，仍 ≤2）

### Task 5: 功率分配、计划曲线、恒压与远程设定

**Description:** 补三块：① `Distribution`（SOC 均衡开关、SOC 上下限），应用后支路表分配权重变化可在快照表观察；② 有功/无功曲线：匹配模式（星期/日期）+ 点表增删（Weekday/Date、Start、End、Power），应用 `ActiveCurve`/`ReactiveCurve`；选「闭环曲线」且无命中点时沿用现有 WAIT 告警；③ 恒压模式启用 `VoltageSetV`/`VoltageKp`；本地/远程切换与远程 P/Q 设定（现 PATCH 已有部分标量，页面补齐）。

**Acceptance criteria:**
- [ ] 增加一条覆盖当前时刻的有功曲线点并应用后，WAIT 消失且站级 P 指令跟曲线功率（斜率关闭时）
- [ ] SOC 均衡打开后 GET `config.distribution.socBalance === true`
- [ ] 无功恒压可改 `VoltageSetV` 并出现在 GET config 中

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EmsStrategy"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 曲线模式加当前时段点 → 应用 → WAIT 消失；重启后点还在

**Dependencies:** Task 3

**Files likely touched:**
- `Web/src/views/EmsStrategyView.vue`
- `Web/src/views/EmsStrategyView.vue` 若过大则 `EmsStrategyCurveEditor.vue` + 主视图

**Estimated scope:** Medium: 3-5 files

### Checkpoint: Core Features

- [ ] 电站策略页能整定提示词要求的公共算法与辅助服务参数
- [ ] 应用后无需重启；刷新/重启（读运行目录 JSON）值仍在
- [ ] 空曲线 + 曲线模式仍 WAIT
- [ ] 第三方占用时参数应用失败有明确提示

### Phase 3: Polish

### Task 6: 折叠默认、脏标记与主机/分轴使能

**Description:** 参数卡默认折叠，运行条（模式、本地 P/Q、三个辅助开关、快照）默认展开。未应用的 draft 显示脏标记；离开卡前提示。补 `SystemSwitch`、`ActiveEnable`、`ReactiveEnable`（影响是否对外输出）。不把 `BypassMasterCheck` 做成显眼开关（仿真默认真，避免误关导致全 0）。文案标明放电为正。

**Acceptance criteria:**
- [ ] 首次进入策略页，明细参数是折叠的，模式与快照可见
- [ ] 改了 PID 未应用时有脏标记；应用后清除
- [ ] 关闭有功使能后站级 P 指令为 0（与引擎 `ActiveEnable` 语义一致）

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EmsStrategy"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 折叠/脏标记/有功使能联调

**Dependencies:** Task 4, Task 5

**Files likely touched:**
- `Web/src/views/EmsStrategyView.vue`
- `Web/src/styles/app.css`

**Estimated scope:** Small: 1-2 files

### Checkpoint: Complete

- [ ] 提示词中的斜率、PID、视在、分配、曲线、调频/惯量/下垂均可在页面热更新并落盘
- [ ] 算法测试集与新增 Runtime/Patcher 测试通过
- [ ] 未做项已排除：合 tab、改公式、参数进组态工程、双写仓库 JSON
- [ ] Ready for review

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 整表反序列化冲掉未提交字段 | High | 嵌套覆盖 + 单测「只改 Pid 不动调频」 |
| 1s GET 覆盖正在编辑的数字 | High | 轮询与 draft 分离（Task 3 最先做 UI） |
| 每个 spinner 写盘/热更新 | Med | 按卡「应用」，不按控件即时 POST 嵌套对象 |
| 改死区时调频正 ACTION | Med | 不 Reset；下一拍用新死区，可能提前复归。文档化，不擅自清 ACTION |
| 开发时改仓库 JSON、运行改 bin JSON 两套 | Med | 只约定运行目录为真源；任务说明里写明 |
| `EmsStrategyView.vue` 膨胀 | Med | Task 4/5 超 400 行再抽子组件，单任务仍 ≤5 文件 |
| `Enabled: true` 落盘导致下次启动直接占用 | Low | 接受；顶栏可释放。第三方已占用则启动 `TryOccupy` 失败，策略实际不启用 |

## Open Questions

- 无。热更新与落盘已由本次需求确认。`BypassMasterCheck` 不进主界面。
