# Implementation Plan: 构网双闭环、P-f 下垂与停机预同步

## Overview

在已落地的 **电压斜坡（0→690 V / 5 s）+ Q-V 下垂** 之上做下一迭代，对齐现场构网策略：

1. **单机建压**：电压外环 + 电流内环；电压基准从 0 线性斜坡到额定，电流内环把冲击限在额定电流内。
2. **多台一起构网**：在 Q-V 之外补 **P-f 下垂**，按自身 P/Q 微调频率与电压，均分负荷。
3. **停机且黑启动**：母线电压起来后 PLL 锁主机相位；建压后期预同步（幅值/频率/相位），再无缝切入。**禁止**再把「看见别人的 690 V」当成自己已同步——这就是 pcs2 七千安过流的原因。

不重做斜坡器与 `QvDroopRegulator`。不做组级变压器拆分、LC 新点、DroopSlices 页。

## 现网对照

| 现有 | 缺口 | 本期 |
|---|---|---|
| `VoltageRampGenerator` 0→690 / 5 s | 无电流内环，P/Q 仍按电压差代数算 | 外环出 Iref，内环限流后再变成 P/Q |
| `QvDroopRegulator` | 无 P-f | 新增 `PfDroopRegulator` |
| 构网频率固定额定 | `_blackStartFrequencyRampHzPerSec` 未接线 | 单机建压仍用额定 f；多机用 P-f 改注入频率 |
| `RefreshBlackStartBusContext`：母线 ≥85% 额定 → Synchronized | 把 **pcs1 的母线** 当成 pcs2 已同步，关掉限流 | 只有 **本机斜坡电压** 达标才算同步 |
| 过流 \(I=S/(\sqrt{3}U)\)，U 用本机交流电压 | 本机 U≈0、S 已被当成同步满载 | 内环按 Imax 限 I；U 低时 S 随 U 降 |
| 无相位 | PLL/预同步无法定义 | 构网机积分 \(\theta\)，母线相位跟主机 |
| 停机 PCS 交流口写 0 | 不锁相、不预同步 | Off+母线有电 → PLL；窗口满足 → 跟网切入 |
| `PcsMapper`：无网 + 黑启动/孤岛电压 → 一律「建压运行」 | 后机也变成第二台 V/f | 母线已带电 → 预同步/跟网，不注入电压源 |

## Architecture Decisions

- **双闭环是相量/平均值模型，不是开关 EMT。** 电压外环：\(I_\mathrm{ref}=K_{vp}(V_\mathrm{ref}-V)+K_{vi}\int e_V\)（离散 PI，抗饱和）。电流内环：跟踪 \(I_\mathrm{ref}\)，输出限幅 \(|I|\le I_\mathrm{max}\)（软起再用 `BlackStartCurrentLimitFraction`）。再 \(P=\sqrt{3}\,V I\cos\phi,\; Q=\sqrt{3}\,V I\sin\phi\)，其中 **V 用本机 \(V_\mathrm{ramp}/V_\mathrm{ref}\)**，不用别人的母线电压当本机 U。这样斜坡初期 S 随 U 变小，\(I=S/(\sqrt{3}U)\) 不会爆。
- **软起斜坡保持上期：0→690 在 5 s 内、UpRate≥138 V/s。** 外环的 \(V_\mathrm{ref}\) 跟踪斜坡器输出，不另做一套按时间百分比的建压。
- **「已同步」只看本机。** `V_\mathrm{ramp} \ge V_0 \times 0.85`（可配）才进入 Synchronized。`_unitBusVoltageV` 只作遥测/PLL 输入，**不得**再把 pcs2 直接打成 Synchronized，也 **不得** 在本机斜坡未到位时关闭电流限幅。
- **P-f 下垂（本期恢复，覆盖上期「不做频率」）：** \(f = f_0 - m_p(P-P_0)\)，有功死区与 Q-V 对称。默认 \(m_p\) 按「额定 P 时跌 0.5 Hz」：\(m_p=0.5/\max(P_\mathrm{rated},1)\) Hz/kW。母线频率仍走现有 `SystemFrequencyResolver`（最高电压源的 f）。功角潮流不本期做；多机构网时岛内负荷仍按额定容量比例分摊，下垂只修正各机 P 指令与注入频率。
- **单机建压频率保持 \(f_0\)。** 不接 `BlackStartFrequencyStartHz` 扫频，避免停机 PLL 去追一条斜坡频率。
- **停机角色。** 母线低于锁相门槛（默认 0.20 pu）时 PLL 不转。门槛以上：PLL 跟踪母线 f、θ。主机进入调压后期（本机斜坡 ≥ 预同步门槛，默认 0.70 pu）后，停机 PCS 进入 PreSync：把待输出幅值/频率/相位往母线收。窗口默认 \(\Delta V\le 5\%V_\mathrm{nom}\)、\(\Delta f\le 0.2\,\mathrm{Hz}\)、\(\Delta\theta\le 10^\circ\) 后允许切入。
- **切入不是第二台 V/f。** 预同步完成 + 启停=1 → `Normal` + 离网 **跟网（PQ）**，`TryGetIslandBusVoltageInjection` 必须为 false。无网但母线已带电时，`PcsMapper` 不得再走「黑启动/孤岛建压运行」。
- **算法独立成类**，与上期相同目录 `EssDeviceSimModel/Control/`。`PcsDevice` 只组合。
- **不做：** 组级 690 V 拆分；带电合闸 \(\Delta V/Z\) 电磁暂态；LC/EMU 新点；开关级电流环；三相对称 PLL 的 αβ 变换（用标量 V、f、θ 即可）。

## 依赖图

```
上期：VoltageRamp + QvDroop
        │
电压外环 PI + 电流内环限幅 ──→ 单机软起不再假过流
        │
PfDroopRegulator ──→ 多机构网注入频率 / P 均分
        │
构网相位积分（θ̇ = 2πf）
        │
PLL + PreSync 窗口
        │
角色：死母线才构网；活母线只跟网切入
        │
pcs2 过流日志回归（7 kA 不再出现）
```

## Task List

### Phase 1: 单机双闭环建压

- [x] Task 1: 离散 PI（抗饱和）+ 电流内环限幅器
- [x] Task 2: 电压外环：跟踪斜坡 \(V_\mathrm{ref}\)，输出 Iref
- [x] Task 3: 接入构网功率；同步判据改为本机斜坡；限流在同步前始终有效

### Checkpoint: 单机
- [x] 默认 0→690 仍 ≤5 s
- [x] 软起全过程 \(|I| \le I_\mathrm{max}\)（含第一拍预充结束）
- [x] 现有 `PcsBlackStart` / `PcsQvDroopRamp` 绿

### Phase 2: 多机 P-f

- [x] Task 4: `PfDroopRegulator`（公式、有功死区、f 钳位）
- [x] Task 5: 注入频率接线；两台构网 P 均分特性测

### Checkpoint: 下垂
- [x] 两台同时构网时 P 偏差小于额定的 15%（同参数）
- [x] Q-V 行为与上期一致

### Phase 3: PLL / 预同步 / 切入

- [x] Task 6: 构网相位积分；母线相位跟最高电压源
- [x] Task 7: PLL + 预同步窗口（纯算法）
- [x] Task 8: 活母线禁止假同步、禁止第二 V/f；跟网切入
- [x] Task 9: 复现 pcs1 建压、pcs2 启动不报过流

### Checkpoint: Complete
- [x] Phase 1–3 验收均满足
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~VoltageRamp|FullyQualifiedName~QvDroop|FullyQualifiedName~PcsBlackStart|FullyQualifiedName~PfDroop|FullyQualifiedName~Pll|FullyQualifiedName~PreSync|FullyQualifiedName~FormingDualLoop"`
- [x] `dotnet build ./EssSimulator.csproj`
- [x] 未做：组变、合闸电磁环流、点表、开关 EMT

## Task 1: 离散 PI 与电流内环

**Description:** 新增无 I/O 的 `DiscretePiController`（`Kp/Ki`、输出限幅、积分抗饱和）和 `CurrentInnerLoop`：输入 Iref、Imeas、dt、Imax，输出限幅后的 I 与是否饱和。软起 Imax = `MaxCurrent * BlackStartCurrentLimitFraction`，同步后 Imax = `MaxCurrent`。不引用 PCS 类型。

**Acceptance criteria:**
- [ ] 阶跃 Iref 时输出不超过 Imax
- [ ] 饱和时积分不再增长（抗饱和）
- [ ] dt≤0 时输出保持

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~DiscretePiController|FullyQualifiedName~CurrentInnerLoop"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Control/DiscretePiController.cs`（新）
- `EssDeviceSimModel/Control/CurrentInnerLoop.cs`（新）
- `EssSimulator.Tests/Control/CurrentInnerLoopTests.cs`（新）

**Estimated scope:** Small: 2-3 files

## Task 2: 电压外环

**Description:** `VoltageOuterLoop`：\(V_\mathrm{ref}\)（来自斜坡，已经过 Q-V）与本机测量电压（优先本机 \(V_\mathrm{ramp}\) 对应的有效电压，**不用邻机母线**）之差经 PI 得到 Iref，再交给电流内环。Iref 限幅与内环 Imax 一致。

**Acceptance criteria:**
- [ ] Vref 斜坡上升时 Iref 有界
- [ ] Vmeas=Vref 时 Iref→0（空载）
- [ ] 公开 API 无频率项

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~VoltageOuterLoop"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1

**Files likely touched:**
- `EssDeviceSimModel/Control/VoltageOuterLoop.cs`（新）
- `EssSimulator.Tests/Control/VoltageOuterLoopTests.cs`（新）

**Estimated scope:** Small: 1-2 files

## Task 3: 接入单机构网 + 修正假同步

**Description:** `ApplyBlackStartPowerControl` 改为外环+内环得到 I，再用 **本机 FormingVoltageRef** 换成 P/Q。删除「Synchronized 就跳过电流限幅」。`RefreshBlackStartBusContext` 进入 Synchronized 的条件改为本机 `_blackStartSoftCapV`（或斜坡输出）≥ 设定×`BlackStartBusEnergizedFraction`，**去掉** `_unitBusVoltageV >= energizedV`。软起离开 SoftStarting 同样只看本机斜坡，不看邻机母线 35%。保持 5 s 到 690。

**Acceptance criteria:**
- [ ] 邻机母线已 690、本机斜坡仍 0 时，本机相位不是 Synchronized
- [ ] 预充结束第一拍 \(|I| \le I_\mathrm{max}\)
- [ ] 默认配置 0→690 在 5 s 内（Q 在死区内）
- [ ] 现有 PcsBlackStart 测例意图不变（容差可放）

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PcsBlackStart|FullyQualifiedName~PcsQvDroopRamp|FullyQualifiedName~FormingDualLoop"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 2

**Files likely touched:**
- `EssDeviceSimModel/Devices/PcsDevice.BlackStart.cs`
- `EssDeviceSimModel/Devices/PcsDevice.Core.cs`
- `EssSimulator.Tests/Devices/FormingDualLoopTests.cs`（新）
- `EssSimulator.Tests/Devices/PcsBlackStartTests.cs`（仅当假同步假设被测到）

**Estimated scope:** Medium: 3-4 files

## Task 4: PfDroopRegulator

**Description:** \(f_\mathrm{ref}=f_0-m_p(P-P_0)\)；\(|P-P_0|\le P_\mathrm{dead}\) 时不调频；结果钳位到 `[f0-1.0, f0+0.5]`（可配）。\(m_p\le 0\) 时输出 f0。无电压项。

**Acceptance criteria:**
- [ ] P 增加则 f 下降；死区内 f=f0
- [ ] 默认公式：额定功率时跌 0.5 Hz
- [ ] 公开 API 无 Q、无 V

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PfDroopRegulator"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None（可与 Phase 1 并行）

**Files likely touched:**
- `EssDeviceSimModel/Control/PfDroopRegulator.cs`（新）
- `EssSimulator.Tests/Control/PfDroopRegulatorTests.cs`（新）
- `Configuration/SimulatorConfig.cs`、`PcsDeviceConfig`、`PcsDeviceFactory`（系数默认值）

**Estimated scope:** Small: 2-3 files

## Task 5: 多机构网频率与 P 均分

**Description:** `TryGetIslandBusVoltageInjection` 的频率改为 P-f 输出。两台构网、同参数、带岛内负荷时，各机 P 趋向均分（偏差 < 额定 15%）。`SystemFrequencyResolver` 仍取最高电压源频率，不改取大电压规则。单机构网时 P≈0 则 f=f0。

**Acceptance criteria:**
- [ ] 单机空载建压 f 在 49.9–50.1 Hz
- [ ] 两台构网带负荷，P 相对差 < 15%
- [ ] Q-V 死区/降压测例仍过

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PfDroop|FullyQualifiedName~PcsQvDroopRamp|FullyQualifiedName~PcsBlackStart"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 3, Task 4

**Files likely touched:**
- `EssDeviceSimModel/Devices/PcsDevice.BlackStart.cs`
- `EssSimulator.Tests/Devices/PfDroopParallelTests.cs`（新）

**Estimated scope:** Medium: 2-3 files

## Task 6: 构网相位

**Description:** 构网 PCS 每子步 \(\theta \mathrel{+}= 2\pi f\,\mathrm{d}t\)（包到 \([-\pi,\pi)\)）。母线相位取当前最高电压注入源的 θ（与频率 resolver 同一赢家）。停机/非构网 θ 保持，不积分。供 PLL 当参考，不接入电磁暂态。

**Acceptance criteria:**
- [ ] f=50 Hz 时 1 s 内 Δθ = 100π rad（模 2π 后可验证圈数）
- [ ] 非构网不推进 θ
- [ ] 两台构网时母线 θ 等于电压更高者

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~FormingPhase"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 5（频率已从下垂来）

**Files likely touched:**
- `EssDeviceSimModel/Control/PhaseIntegrator.cs`（新）
- `EssDeviceSimModel/Devices/PcsDevice.BlackStart.cs`
- `EssDeviceSimModel/PcsTypes.cs`（PcsState 增加相位）
- `EssSimulator.Tests/Control/PhaseIntegratorTests.cs`（新）

**Estimated scope:** Medium: 3-4 files

## Task 7: PLL 与预同步窗口

**Description:** `PllTracker`：母线 V 高于 0.20 pu 才使能；跟踪 f_bus、θ_bus（一阶或 PI，锁相时间常数可配，默认 ~100 ms）。`PreSyncSupervisor`：输入本机待发 V/f/θ 与母线，判断是否进入预同步（母线 ≥0.70 pu）以及窗口是否满足。停机 PCS 不输出电压源。

**Acceptance criteria:**
- [ ] 母线 0 时 PLL 不锁
- [ ] 母线阶跃到 690 V / 50 Hz 后，200 ms 内 |Δf|<0.2 Hz、|Δθ|<10°
- [ ] 窗口三条件同时满足才 `IsReadyToCutIn`

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PllTracker|FullyQualifiedName~PreSyncSupervisor"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 6

**Files likely touched:**
- `EssDeviceSimModel/Control/PllTracker.cs`（新）
- `EssDeviceSimModel/Control/PreSyncSupervisor.cs`（新）
- `EssSimulator.Tests/Control/PllTrackerTests.cs`（新）
- `EssSimulator.Tests/Control/PreSyncSupervisorTests.cs`（新）

**Estimated scope:** Medium: 4 files

## Task 8: 活母线角色与跟网切入

**Description:** 本机斜坡未建压且母线已高于锁相门槛时：即使 `BlackStartEnabled`，也 **不** `TryGetIslandBusVoltageInjection`，相位不得进 SoftStarting/Synchronized。启停=1 且 `IsReadyToCutIn` → Normal + Islanded 跟网，功率从 0 按现有功率爬坡爬。`PcsMapper`：无网但 `GetUnitAcBusVoltage` 已带电且本机非构网主机 → 不要再标「黑启动/孤岛建压运行」。主机仍是「死母线上第一个成功构网者」。

**Acceptance criteria:**
- [ ] pcs1 构网中，pcs2 黑启动+启停：pcs2 不注入电压源
- [ ] 预同步未就绪时 pcs2 保持 Off/Standby，不报过流
- [ ] 窗口满足后 pcs2 切入，注入标志仍为 false
- [ ] 死母线上第一台仍能 5 s 建到 690

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PreSync|FullyQualifiedName~PcsMapper|FullyQualifiedName~PcsBlackStart"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 7

**Files likely touched:**
- `EssDeviceSimModel/Devices/PcsDevice.BlackStart.cs`
- `EssDeviceSimModel/Devices/PcsDevice.Core.cs`
- `EssSimModelApi/Mappers/PcsMapper.cs`
- `EssSimulator.Tests/Devices/LiveBusFollowerTests.cs`（新）

**Estimated scope:** Medium: 4 files

## Task 9: pcs2 过流回归

**Description:** 用两台 PCS、共享单元母线：pcs1 黑启动建压至 ≥0.85 pu 后启动 pcs2（黑启动+启停）。断言 pcs2 在切入前后 FaultType=0，交流电流 ≤ MaxCurrent，且日志原因不再出现 `Over current: 7xxxA`。把「邻机 690 V + 本机 U≈0 + 假同步」写成显式反例测（应保持 Off 或预同步，不得 Synchronized）。

**Acceptance criteria:**
- [ ] 复现原时序（预充 ~300 ms 后第一拍）pcs2 不过流
- [ ] pcs2 电流始终 ≤ MaxCurrent
- [ ] pcs1 建压不被 pcs2 拖垮到故障

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PcsFollowerCutIn|FullyQualifiedName~PcsBlackStart|FullyQualifiedName~FormingDualLoop"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 8

**Files likely touched:**
- `EssSimulator.Tests/Devices/PcsFollowerCutInTests.cs`（新）

**Estimated scope:** Small: 1-2 files

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| 现有测例依赖「母线 35%/85% 提前结束软起」 | Med | Task 3 改判据后放宽/改这些测例的 Refresh 为本机斜坡 |
| 两台都当构网时 max(V) 合并，P-f 均分弱 | Med | Task 8 默认只允许一台构网；Task 5 的两机构网是显式双主机测例 |
| PLL 无三相瞬时值，锁相只是标量 | Low | 文档写明相量模型；窗口用 θ 积分差 |
| 跟网切入仍走 `S/U` 过流 | Med | 切入功率从 0 爬；U 用母线电压不是 0 |
| 与上期「不做 P-f」冲突 | Low | 本期需求明确恢复；单机仍固定 f0 |

## Open Questions

- 多台是否允许同时构网（双主机），还是现场永远一主机+N 跟网？默认实现 **一主机**；Task 5 保留双主机测例便于以后开。
- 预同步门槛 0.70 pu 与切入窗口是否要进 appsettings：本期进 `PcsPhysicalConfig`，不加点表。
