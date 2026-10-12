# Implementation Plan: 活岛上合单元高压（空载变涌流按 35 kV 保护）

## Overview

现场黑启动扩岛时，允许把**尚未带电的单元变压器**合到已经由构网 PCS 撑起的 35 kV 母线上。保护 CT 在高压侧，涌流按一次额定的数倍（默认最多 12 倍）计，6.3 MVA / 35 kV 一次额定约 104 A，峰值约 1.2 kA，远低于单元断路器 3500 A 定值，**不应跳开供电侧单元高压**。

当前仿真把 **690 V 侧电流**直接送给 35 kV 单元断路器去比 3500 A，变比约 50 倍被乘进保护，合上单元 2 空载变时单元 1 立刻跳闸。这是模型错误，不是现场该有的动作。上期构网任务已标明「带电合闸过流另案」，本期做这一案。

不改 LC 点表结构、不做开关 EMT、不靠抬高 `FaultThresholdA` 掩盖。

## 现网对照

| 现有 | 问题 | 本期 |
|---|---|---|
| `UnitBranchCoupler` / `NetworkSolver.SolveUnitBranches` 用 690 V 的 P/Q 算出 \(I\)，写入单元断路器二次侧 | 保护按 35 kV 定值 3500 A，电流却是 690 V | 先把电流折到断路器额定电压再判据 |
| `BreakerConfig.Unit`：35 kV、额定 3000 A、故障 3500 A | 定值本身合理 | **不改定值** |
| `TransformerDevice` 涌流：一次最多 12×In，再乘变比写二次电流 | 视在功率 \(\sqrt{3}VI\) 在两侧相等，功率账不错 | 保护必须用一次（35 kV）电流 |
| `ApplyBlackStartStationElectricalLoadAcrossBus` 把各单元变励磁（含涌流）Q 摊到**已构网** PCS | 供电单元 PCS 会在下一拍吐出等价 690 V 大电流 | 涌流作为 35 kV 负荷；PCS 只经本机 \(I_\mathrm{max}\) 承担折算后的电流 |
| 合闸后 `IsTripped=true`，LC `sysyc171` 仍 0xAA | 电气已跳、协议仍显示合 | 跳闸遥信与合闸闭锁对齐现场：跳闸锁存，需复位才能再合 |
| `CloseBreaker` 在 `IsTripped` 时不动作 | 已正确 | 保持；复位走 `ResetBreakerTrip` 或等价入口 |

## 现场运行逻辑（验收口径）

1. **主断分闸、单元 1 已构网**（690 V 岛经单元变送到 35 kV）。
2. **单元 2 高压分闸**，其变压器一次无压。
3. **合上单元 2 高压**：35 kV 给单元 2 变一次侧加压，产生励磁涌流（一次侧、衰减 \(\tau\approx 0.45\,\mathrm{s}\)）。
4. 涌流由单元 1 构网机经单元 1 变、单元 1 高压断路器供给。单元 1 高压一次电流 ≈ 既有励磁 + 单元 2 变涌流一次值，峰值约 kA 级，**小于 3500 A，不跳**。
5. 构网 PCS 受电流内环限制：承担不了的部分表现为 35 kV 短时跌落，而不是把 690 V 电流送到 6 万安再跳开关。
6. 涌流衰减后，稳态只多一块单元 2 变空载励磁（约 2% In），8 台均摊 Q，单元 1 高压保持合。

禁止的「假修复」：把 `FaultThresholdA` 改到 690 V 量级；关掉 `MagnetizingInrushEnabled`；合闸瞬间强制 `IsTripped=false`。

## Architecture Decisions

- **断路器只判断「本侧电压下的线电流」。** 单元支路额定 35 kV：写入 `BreakerSimulator` 二次侧的 `LineCurrentA` 必须是 35 kV 电流 \(I_{35}=S/(\sqrt{3}\,V_{35})\)，或由 690 V 电流按 \(I_{35}=I_{690}\cdot V_{690}/V_{35}\) 折算。主断仍按自身额定电压（220 kV）折算，避免同一条 bug 出现在主断。抽一个小函数，不要在两处手写变比。
- **不改涌流公式。** 一次峰值倍数、dv/dt 门槛、\(\tau\) 保持 `TransformerDeviceConfig` 默认。错的是保护用的电流所在电压等级，不是 12 倍本身。
- **构网机是限流电压源。** 岛上突然出现的变压器涌流，先折到供电单元 690 V，再进入已有电流内环；超出 \(I_\mathrm{max}\) 的部分不得写进断路器电流、也不得让 PCS 报「设计外过流」除非一次侧真的超过保护。电压允许短时下跌。
- **跳闸锁存保持现场语义。** 过流跳闸后 `IsClosed=false` 且 `IsTripped=true`；LC/EMU 合闸命令不能在未复位时重新合上（现有 `CloseBreaker` 已如此）。遥信：画面「跳闸」已经读 `IsTripped`；LC `sysyc171` 在跳闸时应为分（EE），复位且合闸后才回到 AA。
- **不做：** 开关 EMT / \(\Delta V/Z\) 环流；改变压器空载电流百分比来「减小涌流」；新 LC 点名；主接线前端绑点（上期 unitIndex 另案已做）。

## 依赖图

```
电流折算到断路器额定电压
        │
单元/主断 Step 用折算后电流判据
        │
岛内涌流经供电 PCS 电流环，不再用 690 V 原值去跳 35 kV 开关
        │
测例：活岛合空载单元变 → 供电侧高压不跳
        │
跳闸锁存与 LC 遥信（EE / 复位后再合）
```

## Task List

### Phase 1: 保护电流电压等级

- [x] Task 1: 线电流按电压等级折算的纯函数
- [x] Task 2: 单元支路与主断写入 `BreakerSimulator` 前折到额定电压

### Checkpoint: 保护电流
- [x] 690 V、630 A（约 750 kvar 励磁）折到 35 kV 后约 12 A，远低于 3500 A
- [x] 现有 `BreakerSimulatorTests` 绿（主断测例本身已按 220 kV 算电流）

### Phase 2: 活岛合空载变

- [x] Task 3: 涌流需求经供电单元 PCS \(I_\mathrm{max}\)；断路器只看见折算后的 35 kV 电流
- [x] Task 4: 集成测例：单元 1 构网 + 合单元 2 高压，单元 1 不跳、单元 2 合上

### Checkpoint: 合闸
- [x] 12× 一次涌流峰值下供电侧单元高压仍合
- [x] 稳态单元 2 690 V 由变二次侧带电；供电 PCS 仍同步
- [x] 不修改 `FaultThresholdA` 默认值

### Phase 3: 跳闸锁存与遥信

- [x] Task 5: 跳闸后 LC 高压状态为分；未复位时合闸命令保持跳闸；复位后可再合

### Checkpoint: Complete
- [x] Phase 1–3 验收均满足
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Breaker|FullyQualifiedName~UnitHvLiveClose|FullyQualifiedName~LcMv"`
- [x] `dotnet build ./EssSimulator.csproj`

## Task 1: 线电流按电压等级折算

**Description:** 新增纯函数：已知视在功率或已知「另一电压等级下的线电流」，得到目标线电压下的线电流。\(I_2=I_1\cdot V_1/V_2\)（功率不变）。电压非正时返回 0。不断路器、不仿真步进。

**Acceptance criteria:**
- [x] \(I(690\,\mathrm{V}, 630\,\mathrm{A})\) 折到 35 kV 约 12.4 A
- [x] \(S=\sqrt{3}\times 35\,\mathrm{kV}\times 1247\,\mathrm{A}\) 在 35 kV 下就是 1247 A，在 690 V 下约 63 kA
- [x] 不依赖 `BreakerSimulator` / `PcsDevice`

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~AcQuantityConverter"`
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Model/AcQuantityConverter.cs`（或同目录小助手）
- `EssSimulator.Tests/Model/AcQuantityConverterTests.cs`

**Estimated scope:** Small

## Task 2: 写入断路器的电流必须是本侧电压

**Description:** `UnitBranchCoupler` 与 `NetworkSolver.SolveUnitBranches` 今日用 690 V P/Q 得到的 `LineCurrentA` 直接 `Step` 单元断路器。改为折到 `BreakerBranchConfig.RatedVoltageKv`（单元默认 35 kV）再判据。主断路径若同样用了非额定侧电流，一并用额定电压折算。`FaultThresholdA` 保持 3500 / 60000。

**Acceptance criteria:**
- [x] 单元断路器 `Step` 时二次侧 `LineCurrentA` 与 35 kV 功率对应，不再等于 690 V 电流
- [x] 稳态 8 台约 750 kvar 励磁：单元高压电流 ≪ 3500 A，不跳
- [x] `BreakerConfig.Unit.FaultThresholdA` 仍为 3500

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~BreakerSimulator"`
- [x] 补一条：690 V 大电流 + 35 kV 额定的单元断配置，折算后不跳；不折算的对照电流会跳（可用直接 `Step` 测例钉死回归）
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1

**Files likely touched:**
- `EssDeviceSimModel/Propagation/UnitBranchCoupler.cs`
- `EssDeviceSimModel/Solver/NetworkSolver.cs`
- `EssSimulator.Tests/Devices/BreakerSimulatorTests.cs`

**Estimated scope:** Small

## Task 3: 涌流由限流构网机承担，保护看 35 kV

**Description:** 活岛合空载变时，`GetSecondaryMagnetizingReactiveKvar` / `GetInrushDemandKwKvar` 会在下一拍把数万 kvar 加到供电 PCS。须保证：进入 PCS 的是折到 690 V 且经电流内环限幅后的量；写入供电侧单元断路器的是限幅后的 35 kV 电流。禁止励磁/涌流 Q 绕过 `CurrentInnerLoop` 直接变成 690 V 端口电流。电压允许跌。PCS 不得因「折算前的二次涌流」报设计外过流。

**Acceptance criteria:**
- [x] 供电 PCS 端口 \(|I|\le I_\mathrm{max}\)（含合闸后若干拍）
- [x] 供电侧单元高压一次电流在涌流峰值时仍 < 3500 A（12× 一次 In 量级）
- [x] 不关闭 `MagnetizingInrushEnabled`

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~UnitHvLiveClose|FullyQualifiedName~FormingDualLoop"`
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 2

**Files likely touched:**
- `EssDeviceSimModel/UnitTransformerIslandSync.cs`
- `EssDeviceSimModel/Devices/PcsDevice.BlackStart.cs`（若励磁 Q 未走内环）
- `EssSimulator.Tests/Devices/UnitHvLiveCloseTests.cs`（新，可与 Task 4 同一文件先写失败测例）

**Estimated scope:** Medium

## Task 4: 集成测例钉死「合空载变不跳供电侧」

**Description:** 用最小电站（2 单元，每单元若干 PCS）复现现场顺序：主断分、单元 1 黑启动建压到约 690 V、单元 2 高压分、再合单元 2 高压。断言合闸后及涌流 \(\tau\) 若干拍内：单元 1 `IsTripped==false` 且合闸；单元 2 合闸未跳；单元 1 PCS 仍离网同步。允许 35 kV / 690 V 短时下跌。禁止靠改阈值过测例。

**Acceptance criteria:**
- [x] 合单元 2 后 `ess.IsUnitBreakerClosed(0)==true` 且电气网络单元 0 `IsTripped==false`
- [x] `ess.IsUnitBreakerClosed(1)==true`
- [x] 单元 1 通道仍为黑启动运行/已同步，交流电压回到设定附近（容许下垂，与现网 686–690 V 一致）

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~UnitHvLiveClose"`
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 3

**Files likely touched:**
- `EssSimulator.Tests/Devices/UnitHvLiveCloseTests.cs`

**Estimated scope:** Small（测例文件；实现已在 Task 3）

## Task 5: 跳闸锁存与 LC 高压遥信

**Description:** 过流跳闸后画面已是「跳闸」。LC `sysyc171` / `mv_param1` 反馈在跳闸时不得仍为 0xAA。合闸命令（AA）在 `IsTripped` 时保持跳闸、不得合上。提供复位后才能合（与 `ResetBreakerTrip` 对齐，可从现有 `DeviceControlFacade` 或 LC 非法值回退路径接入，不新增点名除非现网已有复位点）。

**Acceptance criteria:**
- [x] 单元高压跳闸 → `sysyc171` 为 EE（分），画面 `unitBreakerTripped==true`
- [x] 跳闸锁存期间写 AA 仍为跳闸/分
- [x] 复位后写 AA 可以合闸，`IsTripped==false`

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~LcMv|FullyQualifiedName~UnitHvLiveClose"`
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 2

**Files likely touched:**
- `LocalControl/LcSystemMap.cs` / `StandardLcRuntime.cs`
- `EssSimModelApi/Mappers/DeviceControlFacade.cs`
- `EssSimulator.Tests/LocalControl/LcMvMapTests.cs`

**Estimated scope:** Medium

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 只折算电流、励磁 Q 仍绕过内环 | PCS 690 V 过流故障，岛压崩溃 | Task 3 强制励磁/涌流走 \(I_\mathrm{max}\) |
| 折算公式用错（乘反变比） | 合闸仍跳或保护拒动 | Task 1 用 630 A @690 V → 12 A @35 kV 钉死 |
| 主断路径被误改 | 并网额定潮流误跳主断 | 主断测例保持 220 kV 功率→电流；有功 50 MW 仍远低于 60 kA |
| 复位入口不清晰 | 跳闸后无法用 LC 恢复 | Task 5 先测「必须复位」，入口与现有命令对齐，不发明点表 |

## Open Questions

- 现场高压跳闸复位是单独遥控还是合闸脉冲兼复位：未确认则 Task 5 用仿真已有 `ResetBreakerTrip`，LC 先保证遥信为分，复位 API/命令与主断复位同一套风格。
- 单元 2 合上后其 690 V 由变二次反送、PCS 仍停机：现网已如此，本期保持，不在本期做单元 2 自动开机。
