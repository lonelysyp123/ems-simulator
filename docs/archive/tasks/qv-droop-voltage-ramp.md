# Implementation Plan: 构网 PCS 的 Q-V 下垂与电压斜坡

## Overview

在现有 PCS 离散步进与黑启动 V/f 路径上，补两个**纯算法、无频率**的控制模块：无功-电压（Q-V）下垂，以及交流线电压幅值的升降斜坡发生器。不实现 P-f 下垂，不改频率斜坡字段。算法做成独立 C# 类，由 `PcsDevice` 在离网构网时组合调用；并网 PQ 模式不走这两块。

现场含义：多台构网 PCS 靠 Q-V 静态外特性分担无功、避免电压源互顶；软起/改设定靠斜坡限制 dV/dt，减轻变压器涌流。本期只做**单机电压参考生成**，不做组级变压器拆分、也不做带电合闸过流（另案）。

## 现网对照（必须接上，不要另起一套电压环）

| 现有实现 | 问题 | 本期 |
|---|---|---|
| `RampSoftCapTowardCommand`：单一 `_blackStartVoltageRampVs` | 升降同一速率，逻辑写死在 `PcsDevice` | 抽出 `VoltageRampGenerator`，Up/Down 可分 |
| `IslandVoltageRampDurationMs`：按时间比例趋近 | 不是 V/s 斜坡，且非黑启动路径 | 构网路径不再用它；保留字段以免旧配置报错 |
| `BlackStartReactiveVoltageGainKvarPerV`：电压差 → 无功支撑 | 这是内环「欠压补 Q」，**不是** Q-V 下垂 | 保留作建压期功率分配；下垂只改 **Vref** |
| `Web/DroopSlices/*` | 白盒切片采集，不是控制器 | 不改 |
| `ReactiveVoltageInfluenceCoefficient` | 主变/PCC 的电网 Q-V，不是 PCS | 不改 |
| `BlackStartFrequencyStartHz` / `FrequencyRampHzPerSec` | 频率相关 | **不接、不实现 P-f**；构网频率仍固定额定 |

电压源注入点不变：`TryGetIslandBusVoltageInjection` 仍把线电压交给 `PcsBusVoltageSource`。

## Architecture Decisions

- **独立算法类，不塞进 PcsDevice 私有方法。** `EssDeviceSimModel/Control/VoltageRampGenerator.cs`、`QvDroopRegulator.cs`。无 I/O、无 Modbus、无仿真时钟，只吃 `dt` 与标量。`PcsDevice` 持有实例，在已有子步循环里 `Step`。
- **级联顺序：先斜坡，再下垂。**  
  \(V_0\) = EMS 孤岛电压设定（`IslandVoltageCommandV`）  
  \(V_{\mathrm{ramp}}\) = 斜坡跟踪 \(V_0\)  
  \(V_{\mathrm{ref}} = V_{\mathrm{ramp}} - n_q \cdot \Delta Q_{\mathrm{db}}\)  
  注入母线的是 \(V_{\mathrm{ref}}\)（钳位后）。软起时无功（励磁）只会让电压参考略降，不会在 0→690 上叠加阶跃。
- **下垂公式（与需求一致）：**  
  \(V_{\mathrm{ref}} = V_{\mathrm{ramp}} - n_q \times (Q - Q_0)\)，但 \(|Q-Q_0| \le Q_{\mathrm{dead}}\) 时 \(\Delta Q=0\)。  
  \(Q>0\) 为 PCS 发出无功（与现网 `ReactivePower` 正号一致）。发出无功则 \(V_{\mathrm{ref}}\) 下降。
- **死区是无功死区，不是电压死区。** 进出死区不做滞回（本期）；\(\Delta Q\) 在死区外按全偏差计算，不减去死区宽度（突然跳变小于斜坡步长即可被斜坡吃掉）。
- **软起阶段默认也走下垂。** 励磁 Q 若大于死区，建压顶点会略低于设定，符合构网外特性。若现场要「软起绝对跟踪 V0」，用配置 `QvDroopEnableAfterSoftStartOnly`（默认 `false`）。不要为这个再写第二套管线。
- **0→690 V 必须能在 5 s 内抬完。** 设计下限 \(\mathrm{UpRate} \ge V_{\mathrm{nom}}/5\)。额定 690 V 时 **138 V/s**。默认 `VoltageRampUpVs` / `BlackStartVoltageRampVs` 改为 **138**（现网 120 V/s 要 5.75 s，不满足本条，不再回退 120）。工厂：未单独写 `VoltageRampUpVs` 时 `UpRate = max(BlackStartVoltageRampVs, Vnom/5)`，避免旧配置 120 把 5 s 能力配没。允许显式把 `VoltageRampUpVs` 配得更慢（降涌流），但默认路径与「未写新键」路径都必须 5 s 内到 690。禁止再引入第三套「按时间百分比」斜坡到构网注入。
- **升降斜率分开。** `DownRate` 默认与 `UpRate` 相同，**不受 5 s 约束**（降压可更慢）。
- **钳位：** \(V_{\mathrm{ref}} \in [0,\, V_{\mathrm{nom}}\times 1.10]\)。\(n_q \ge 0\)；负系数在工厂里钳成 0 并视为关闭下垂。
- **\(n_q\) 工程单位：V/kvar。** 默认按「额定视在功率当作 Q 时跌落 4% \(V_{\mathrm{nom}}\)」计算：  
  \(n_q = 0.04 \cdot V_{\mathrm{nom}} / \max(S_{\mathrm{rated}}, 1)\)。  
  690 V、1725 kVA → 约 0.016 V/kvar。配置可覆盖。死区默认 2% \(S_{\mathrm{rated}}\)（kvar 数值）。\(Q_0\) 默认 0。
- **只在构网时运行。** `IsBlackStartActive` 或 `IsPcsIslandVoltageBuilding` 为真时更新斜坡/下垂；停机、待机、并网 PQ 时斜坡复位到 0、下垂不改注入。
- **不做：** P-f 下垂；频率斜坡接线；组级母线/箱变；带电合闸过流；LC/EMU 新点；DroopSlices 页；把 `BlackStartReactiveVoltageGainKvarPerV` 改成下垂。

## 依赖图

```
VoltageRampGenerator（纯函数步进）
        │
QvDroopRegulator（Vramp, Q → Vref）
        │
PcsDeviceConfig / PcsPhysicalConfig 参数
        │
PcsDevice.BlackStart：软起目标改走 Generator；注入改走 Vref
        │
PcsBlackStartTests + 新 Control 单测
```

## Task List

### Phase 1: 纯算法类

- [ ] Task 1: `VoltageRampGenerator`（升降斜率、dt 步进、到位判定）
- [ ] Task 2: `QvDroopRegulator`（公式、无功死区、电压钳位）

### Checkpoint: 算法
- [ ] 不依赖 PcsDevice 的单测全绿
- [ ] 无频率、无 P 项出现在这两个类的公开 API 上
- [ ] 默认/设计 UpRate 下 0→690 在 ≤5 s 内到达

### Phase 2: 配置与接入构网电压源

- [ ] Task 3: 配置项贯通 `PcsPhysicalConfig` → `PcsDeviceConfig` → `PcsDeviceFactory`
- [ ] Task 4: `PcsDevice` 用斜坡器替换 `RampSoftCapTowardCommand`；注入改为 \(V_{\mathrm{ref}}\)

### Checkpoint: 接入
- [ ] 默认构网软起：0→690 V 在 ≤5 s 内到达（UpRate ≥ 138 V/s）
- [ ] 发出无功时注入电压低于设定；死区内等于斜坡值
- [ ] 现有 `PcsBlackStartTests` 绿（必要时只改断言容差，不改场景意图）

### Phase 3: 构网回归

- [ ] Task 5: 黑启动+下垂特性测例（死区、Q 增加降压、斜坡无阶跃）

### Checkpoint: Complete
- [ ] Phase 1–3 验收均满足
- [ ] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~VoltageRamp|FullyQualifiedName~QvDroop|FullyQualifiedName~PcsBlackStart"`
- [ ] `dotnet build ./EssSimulator.csproj`
- [ ] 未做项已排除：P-f、组变、合闸过流、点表、DroopSlices

## Task 1: VoltageRampGenerator

**Description:** 新增离散斜坡发生器：目标值、当前输出、上升斜率 `UpRate`（单位/秒）、下降斜率 `DownRate`（单位/秒）。`Step(dt)` 按符号选择斜率，一步不超过剩余差，到达后输出等于目标。`Reset(value)` 无斜坡跳变。斜率与 dt 非法时钳成安全值（斜率 ≥0，dt>0）。不引用 PCS/电网类型。设计能力：**UpRate=138 V/s（690/5）时，0→690 在 5.0 s 内到达且不超过 690。**

**Acceptance criteria:**
- [ ] 目标 0→690、`UpRate=138`、累计 `dt` 满 5.0 s 后输出为 690，且 5.0 s 之前任一步 < 690
- [ ] 同一组参数下第 5 秒末输出不得因离散过冲超过 690
- [ ] 690→0、`DownRate=60` 时每步 −60，与 UpRate 无关（降压无 5 s 要求）
- [ ] `dt=0` 或负 dt 时输出不变
- [ ] `Reset(0)` 后当前值立即为 0，不经斜坡
- [ ] 公开 API 无频率、无功、有功字段

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~VoltageRampGenerator"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Control/VoltageRampGenerator.cs`（新）
- `EssSimulator.Tests/Control/VoltageRampGeneratorTests.cs`（新）

**Estimated scope:** Small: 1-2 files

## Task 2: QvDroopRegulator

**Description:** 新增无状态（或仅持配置）调节器：`Compute(vRamp, qKvar) → vRef`。实现 \(V_{\mathrm{ref}}=V_{\mathrm{ramp}}-n_q(Q-Q_0)\)；\(|Q-Q_0|\le Q_{\mathrm{dead}}\) 时第二项为 0。结果钳位到 `[vMin, vMax]`（构造时给定，默认 0 … 1.1×Vnom）。\(n_q\le 0\) 时退化为 `vRef=vRamp`。无 P、无 f。

**Acceptance criteria:**
- [ ] \(V_{\mathrm{ramp}}=690\)，\(n_q=0.02\)，\(Q_0=0\)，\(Q=100\)，死区 0 → `vRef=688`
- [ ] 同上但死区 150 kvar → `vRef=690`
- [ ] \(Q=-100\)（吸收无功）→ `vRef>690`，且不超过 `vMax`
- [ ] \(n_q=0\) → 恒等于 `vRamp`
- [ ] 公开 API 无频率、无功设定以外的 P 项

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~QvDroopRegulator"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None（可与 Task 1 并行）

**Files likely touched:**
- `EssDeviceSimModel/Control/QvDroopRegulator.cs`（新）
- `EssSimulator.Tests/Control/QvDroopRegulatorTests.cs`（新）

**Estimated scope:** Small: 1-2 files

## Task 3: 配置贯通

**Description:** 在 `PcsPhysicalConfig` / `PcsDeviceConfig` 增加：`VoltageRampUpVs`、`VoltageRampDownVs`、`QvDroopEnabled`（默认 true）、`QvDroopCoefficientVPerKvar`（≤0 表示用 4% 额定公式）、`QvDroopDeadbandKvar`（≤0 表示 2% 额定）、`QvDroopQ0Kvar`（默认 0）、`QvDroopEnableAfterSoftStartOnly`（默认 false）、`QvDroopVmaxPu`（默认 1.10）。把 **`BlackStartVoltageRampVs` 默认从 120 改为 138**。`VoltageRampUpVs` 未设或 ≤0 时：`UpRate = max(BlackStartVoltageRampVs, Vnom/5)`，保证未写新键的旧 120 配置仍满足 5 s 到 690。显式写出的 `VoltageRampUpVs>0` 原样采用（允许更慢）。`DownRate` 未设时等于 `UpRate`。`PcsDeviceFactory.CreateConfig` 拷贝上述结果。

**Acceptance criteria:**
- [ ] C# 默认 `BlackStartVoltageRampVs == 138`
- [ ] 只配旧键 `BlackStartVoltageRampVs=120` 且未写 `VoltageRampUpVs` 时，工厂 `UpRate == 690/5`（138），不是 120
- [ ] 显式 `VoltageRampUpVs=80` 时 UpRate 为 80（允许慢于 5 s）
- [ ] `QvDroopCoefficientVPerKvar=0` 时工厂算出 4% 公式系数 > 0
- [ ] 现有 `PcsDeviceFactory` 调用点无需手填新参数即可创建设备

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PcsDeviceFactory|FullyQualifiedName~PcsDeviceConfig"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1, Task 2（仅需类型存在；也可先加字段后接入）

**Files likely touched:**
- `Configuration/SimulatorConfig.cs`（`PcsPhysicalConfig`）
- `EssDeviceSimModel/Model/PcsDeviceConfig.cs`
- `EssDeviceSimModel/Devices/PcsDeviceFactory.cs`

**Estimated scope:** Small: 3 files

## Task 4: PcsDevice 构网路径接入

**Description:** `PcsDevice` 持有 `VoltageRampGenerator` 与 `QvDroopRegulator`。黑启动 `RampSoftCapTowardCommand` 改为对斜坡器设目标 \(V_0\) 并 `Step(dt)`；`_blackStartSoftCapV` 改为斜坡输出（或与其同步）。`TryGetIslandBusVoltageInjection` 在构网相位使用 \(V_{\mathrm{ref}}=\) 下垂(`软起电压`, 当前 `ReactivePower`)，而不是未下垂的 softCap。停机/`ResetBlackStartRuntime` 时 `Reset(0)`。并网 PQ 与 Off 不调用下垂注入。保留 `BlackStartReactiveVoltageGainKvarPerV` 的功率分配，不把它改成 Vref。

**Acceptance criteria:**
- [ ] 默认配置、孤岛电压设定 690 V、Q 在死区内：从注入为 0 起累计 5.0 s 内注入达到 690 V（容差 1 V）
- [ ] 构网且 Q 超过死区时，注入电压 < 斜坡电压
- [ ] Q 在死区内，注入电压 = 斜坡电压（容差 0.5 V）
- [ ] `ApplyBlackStartEnabled(false)` 后注入为 0
- [ ] 不读取、不写入频率下垂相关新状态

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PcsBlackStart"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1, Task 2, Task 3

**Files likely touched:**
- `EssDeviceSimModel/Devices/PcsDevice.Core.cs`（字段、构造、复位）
- `EssDeviceSimModel/Devices/PcsDevice.BlackStart.cs`（斜坡步进、注入）
- `EssSimulator.Tests/Devices/PcsBlackStartTests.cs`（若软起断言过死）

**Estimated scope:** Medium: 3 files

## Task 5: 特性测例

**Description:** 覆盖需求原文的两条路径，不启全站：① 斜坡无阶跃且 **0→690 在 5 s 内完成**（子步电压单调、dV/dt 不超过 UpRate×1.1，满 5 s 时 ≥689 V）；② 下垂外特性（固定 V0=690，抬高无功则 Vref 下降，死区内不变）。可在 `PcsDevice` 上 `SetTransformerMagnetizingReactiveKvar` 制造 Q，或直接单测调节器+斜坡组合的薄封装。禁止为测频率去改 GMode 以外的东西。

**Acceptance criteria:**
- [ ] 默认 UpRate 下，0→690 在 ≤5.0 s 到达；相邻 200 ms 步长电压增幅 ≤ `UpRate * 0.2 * 1.1`
- [ ] 死区外 Q 加倍，稳态 Vref 更低
- [ ] 死区内改变 Q，Vref 不变

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~QvDroop|FullyQualifiedName~VoltageRamp|FullyQualifiedName~PcsBlackStart"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 4

**Files likely touched:**
- `EssSimulator.Tests/Devices/PcsQvDroopRampTests.cs`（新）

**Estimated scope:** Small: 1-2 files

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| 励磁 Q 让建压顶点长期低于 yt3，现有「同步」判据用母线相对设定的 85% | Med | 同步判据继续相对 **V0** 而非 Vref；测例确认仍能进入 Synchronized |
| 下垂与 `BlackStartReactiveVoltageGainKvarPerV` 同时作用：欠压补 Q，Q 又压 Vref | Med | 功率环仍用母线电压 vs V0；下垂只改电压源幅值。Task 5 看是否振荡，必要时软起阶段关下垂 |
| 旧测例按「注入=softCap」写死 | Low | Task 4 只放宽容差，不改黑启动相位机 |
| 与「group2 带电合闸过流」混淆 | Low | 本期明确不做；下垂不能替代同步合闸 |
| 138 V/s 快于旧默认 120 V/s，dV/dt 略增 | Low | 仍远低于变压器涌流阈值（约 0.8 pu/s ≈ 552 V/s）；5 s 约束优先 |
| appsettings 仍写着 `BlackStartVoltageRampVs: 120` | Med | 工厂 `max(配置, Vnom/5)`，未显式写 `VoltageRampUpVs` 时抬到 138 |

## Open Questions

- 死区外 \(\Delta Q\) 是否减去死区（无扰设计）本期选「不减」；若现场要无扰，Task 2 加一个配置开关即可。
- \(n_q\) 是否要在 LC/EMU 点表暴露：本期只走 `Pcs` 配置节，不加点。
