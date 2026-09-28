# Implementation Plan: 站用 SVG 恒无功模型

## Overview

全站一台静止无功发生器，并联在 35 kV 母线上，只做恒无功。电压和频率读母线，设备向母线注入无功和很小的有功损耗。点表用 `pointmaps/models/svg/standard/svg.csv`。不做恒电压、恒功率因数、谐波、三相不平衡。主接线只加一张 35 kV 卡片：状态、实际无功、可用上限、开关机和无功设定。

`tasks/plan.md` 的上一份（EMS 策略参数热更新）已归档到 `tasks/ems-strategy-params.md`。

## 点表检查

地址、宽度、参数名、功能码分区可以用。

- 参数名 `svg1`–`svg14` 唯一；频率已是 `svg10`；开关机已是 `u16`。
- 输入寄存器（功能码 4）地址 0–22 连续、无重叠。控制寄存器（功能码 6）从 23 起，和输入寄存器不是同一空间。
- float 均为 32 位、变比 1。`ModelSim` 仍为 `0`，绑定留到协议任务。

还要改两处，放在 Task 1，不挡设备公式：

- 运行状态的类型写成了 `Uint16`。`ModbusPointCodec` 只认 `u16`；认不出时按 Size=16 落到 `Int16`。0/1/2 的寄存器映像和 `u16` 相同，但类型和点表意图不一致。
- 描述里没有单位，也没有运行状态枚举和无功符号。

## Architecture Decisions

- **电流源，不是电压源。** 挂在 `Bus35` 的 `IBusPowerContributor` 上，和负荷、光伏同一层。不实现电压源接口。恒电压以后也是测到电压再改无功。
- **符号。** 无功为正：容性、向电网发出、抬电压。为负：感性、吸收。与负荷设备的无功约定一致。
- **单位（变比 1，寄存器值即工程值）。** 线电压 V，电流 A，无功和容量 kvar，频率 Hz，功率因数无量纲。
- **运行状态。** 0 停机（关机命令）。1 运行（开机，且线电压在 0.85–1.15 倍额定、频率有效）。2 闭锁（越限或母线失压）：无功和电流清零，设定值保留。
- **可用无功。** 只在运行时有值：\(Q_{avail}=Q_{rated}\times U/U_{nom}\)，单位 kvar，报正数。设定值夹在 \([-Q_{avail}, Q_{avail}]\)，容性感性对称。停机和闭锁时该点为 0。
- **设定值与实际值分开。** `svg14` 保持主站写入的设置值。夹限只作用在输出上，实际无功看 `svg8`。不把夹限结果写回控制寄存器。
- **动态。** 一阶滞后，时间常数 20 ms。默认母线步长 100 ms 时一个步长内基本跟上。测滞后必须用更短的步长。
- **损耗。** 运行时有功取电 \(0.008\times Q_{rated}\)（kW）。停机和闭锁为 0。点表不暴露有功。
- **功率因数。** 无功接近 0 时为 1。否则符号跟随无功，幅值 \(\lvert P\rvert/S\)，范围约 −1～1。纯无功时接近 0。
- **电流。** 三相对称，\(I=\lvert Q\rvert\times 1000/(\sqrt{3}\,U)\)，报正的有效值。停机或闭锁为 0。三相线电压都等于母线线电压。
- **默认关闭。** `EnableSvg=false`，额定容量默认 10000 kvar，额定电压用站内 35 kV 母线额定值（35000 V）。未启用时不注册贡献者、不起从站，现有功率平衡不变。
- **协议。** 新 `ProtocolDeviceType.Svg`，一台 `simSvg`。遥测和遥控都用 `model=4|arg1=svg.<属性>`，走现有 `ObjectPathResolver`。控制点同样写入 `ParamModelLookup`（功能码 6 不进遥测刷新分组，这是现有点表加载行为）。

## 依赖图

```
点表类型/单位（Task 1）
        │
SVG 设备公式（Task 2）
        │
        ├── 35 kV 母线贡献（Task 3）
        │
        └── 协议属性 + ModelSim（Task 4）
                    │
                    └── Modbus 从站（Task 5，依赖 Task 3 的设备实例）
```

Task 1 与 Task 2 互不依赖，可以并行。Task 3 与 Task 4 在 Task 2 之后可以并行。Task 5 最后做。

## Task List

### Phase 1: 点表与设备

- [x] Task 1: 点表类型与单位
- [x] Task 2: 恒无功设备

### Checkpoint: 设备可单测

- [x] `SvgDevice` 不挂母线、不起从站即可通过公式测试
- [x] 点表 `svg1` 解析为 `UInt16`

### Phase 2: 母线

- [x] Task 3: 挂到 35 kV 母线

### Checkpoint: 功率进母线

- [x] 关闭时母线功率与现在一致
- [x] 开启后无功进入 `Bus35` 合计，关机后退出

### Phase 3: 协议

- [x] Task 4: 协议属性与 ModelSim
- [x] Task 5: Modbus 从站

### Checkpoint: Complete

- [x] 主站能读遥测、写开关机和无功设定
- [x] 主接线 35 kV 卡片可开关机并下发无功设定
- [x] 未做：恒电压、恒功率因数、谐波、不平衡

## Task 1: 点表类型与单位

**Description:** 把运行状态改成编解码器认识的 `u16`，并在描述里写上单位、运行状态枚举和无功符号。不填 `ModelSim`。

**Acceptance criteria:**
- [ ] `svg1` 的 Type 为 `u16`，`ModbusPointCodec.ToClrType` 得到 `System.UInt16`
- [ ] 描述含单位：电压 V，电流 A，无功/容量 kvar，频率 Hz；运行状态写明 0 停机、1 运行、2 闭锁；无功设定写明正为容性发出、负为感性吸收
- [ ] 地址仍无重叠，参数名仍唯一，功能码与位宽不变

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~SvgPointMap"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 打开 `svg.csv`，只有类型名和描述变化

**Dependencies:** None

**Files likely touched:**
- `pointmaps/models/svg/standard/svg.csv`
- `EssSimulator.Tests/Svg/SvgPointMapTests.cs`

**Estimated scope:** Small: 1-2 files

## Task 2: 恒无功设备

**Description:** 增加 `SvgDevice`。输入是线电压、频率、步长、开关机命令和无功设定；输出是运行状态、三相电压电流、实际无功、功率因数、额定容量和可用无功上限。本任务不接母线、不接 Modbus。

**Acceptance criteria:**
- [ ] 额定电压下开机，设定 5000 kvar，步长 100 ms 后实际无功达到设定的 99% 以上；步长 5 ms 时仍明显落后于设定
- [ ] 设定超出 \(\pm Q_{rated}\times U/U_{nom}\) 时实际无功被夹住，可用上限等于该正值；设定属性仍保留主站写入的原值
- [ ] 关机时状态 0、无功和电流为 0；电压超出 0.85–1.15 倍额定或母线失压时状态 2、输出为 0；两种情况下设定值都还在。运行时有功损耗为额定容量的 0.8%

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~SvgDevice"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Model/ElectricalDeviceKind.cs`
- `EssDeviceSimModel/Svg/SvgConfig.cs`
- `EssDeviceSimModel/Svg/SvgDevice.cs`
- `EssSimulator.Tests/Svg/SvgDeviceTests.cs`

**Estimated scope:** Medium: 3-5 files

## Task 3: 挂到 35 kV 母线

**Description:** `EnableSvg` 为真时构造一台 SVG，经 `SvgBusContributor` 在收集母线功率时用当前 `Bus35` 电压和频率步进，并把 \((P_{loss}, Q)\) 计入母线。默认关闭。

**Acceptance criteria:**
- [ ] `EnableSvg=false` 时不注册贡献者
- [ ] 开启、开机、设定 +5000 kvar 时，`Bus35` 无功合计比未投入多约 5000 kvar，有功合计少一个损耗
- [ ] 同一台设备关机后，这两项回到未投入时的值

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~SvgBus"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** Task 2

**Files likely touched:**
- `Configuration/SimulatorConfig.cs`
- `EssDeviceSimModel/Propagation/BusPowerContributors.cs`
- `EssDeviceSimModel/Propagation/RadialNetworkGraph.cs`
- `EssDeviceSimModel/EnergyStorageSys.cs`
- `EssSimulator.Tests/Svg/SvgBusContributionTests.cs`

**Estimated scope:** Medium: 3-5 files

## Task 4: 协议属性与 ModelSim

**Description:** 用一个挂在 `SimulatorHost` 根键 `svg` 上的对象暴露点表属性，`ObjectPathResolver` 能读遥测、写 `svg13`/`svg14`。点表 `ModelSim` 写成 `model=4|arg1=svg.<属性>`。写设定只改设备设定，不改写控制寄存器里的原始值。

**Acceptance criteria:**
- [ ] 14 个参数名都能解析到 `svg` 上的属性；遥测可读，开关机和无功设定可写
- [ ] 写入超出可用上限的无功设定后，属性里仍是写入值，设备实际无功被夹住
- [ ] 功能码 4 的 12 点在 `DataMaps`，功能码 6 的 2 点在 `ControlMaps`

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~SvgPointMap"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** Task 1, Task 2

**Files likely touched:**
- `EssDeviceSimModel/Svg/SvgProtocolData.cs`
- `pointmaps/models/svg/standard/svg.csv`
- `EssSimulator.Tests/Svg/SvgPointMapBindingTests.cs`

**Estimated scope:** Medium: 3-5 files

## Task 5: Modbus 从站

**Description:** 启用时注册 `ProtocolDeviceType.Svg`、端口计划里的 `simSvg`，并在 `ModbusHostedService` 里加载 `svg.csv`。根对象在 `Program.cs` 注册为 `svg`。默认端口避开现有 1500–2101，用 2201。关闭时不注册。

**Acceptance criteria:**
- [ ] `EnableSvg=false` 时端口计划里没有 `simSvg`
- [ ] `EnableSvg=true` 时计划含 `simSvg`、类型 Svg、点表 `svg.csv`、默认端口 2201
- [ ] 从站启动后，写开关机和无功设定会进 `svg` 对象，下一步遥测读到设备输出

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Svg"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无。主接线不在本任务

**Dependencies:** Task 3, Task 4

**Files likely touched:**
- `Protocol/Modbus/ProtocolPortPlan.cs`
- `Configuration/SimulatorConfig.cs`
- `Protocol/ModbusHostedService.cs`
- `Program.cs`
- `EssSimulator.Tests/Svg/SvgPortPlanTests.cs`

**Estimated scope:** Medium: 3-5 files

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 默认步长 100 ms 把 20 ms 滞后吃掉，测试误判成“没有动态” | Med | Task 2 用 5 ms 与 100 ms 两个步长分别断言 |
| SVG 无功经现有短路容量模型抬母线电压，测试若断言电压会脆 | Med | Task 3 只断言功率贡献，不断言电压数值 |
| 忘了默认关闭，现有母线测试被 80 kW 损耗带偏 | High | `EnableSvg` 默认 false；Task 3 先断言关闭时贡献者不存在 |
| 功能码 6 不进遥测刷新分组，控制寄存器不会被模型覆盖 | Low | 这是想要的：设定留在寄存器，实际值看 `svg8` |

## Open Questions

- 无。额定 10 Mvar、电压闭锁 0.85–1.15、损耗 0.8%、端口 2201 都按上面的默认。要改的话在 Task 2 之前说。
