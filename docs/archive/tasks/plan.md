# Implementation Plan: 箱变有功在两侧方阵间补缺额

## Overview

一次图箱变「P设」写入 `Logger.SubarrayActivePowerKw` 后，`PvUnitDevice.SetActivePowerCommand` 按逆变器台数均分。每台再取 `min(本机设定, 交流可发, 额定)`。方阵 A、B 的环境温度和入射角不同时，受限侧发不够自己的份额，另一侧有余量也不会加发，箱变实际有功低于设定。本计划在每个仿真步用本步可发功率做缺额转移：发不够的千瓦补给仍有余量的逆变器，使两侧实际有功之和尽量达到各台设定之和。点表回读、无功均分和一次图不改。

上一份计划（Modbus 连接改为异步收包）已归档到 `tasks/modbus-async-receive.md` / `tasks/todo-modbus-async-receive.md`。

## Architecture Decisions

- **缺额转移，不改均分命令。** 箱变设定仍然写成每台相同的 `ActivePowerSettingKw`。点表 `yt0` 回读保持这份命令。本步真正用于限发的是另一份目标，只有「设定高于本步可发」的差额才分给有余量的逆变器。
- **基线是各台当前设定之和，不是 Logger 里的旧设定。** 直接改某一台逆变器设定后，只要它发得够，其他台不会被抬高去补 Logger 上的原设定。`SingleInverterCommand_DoesNotChangeSiblingOrLoggerCommand` 因此仍然成立。
- **同一仿真步内先算可发再分配。** 可发现在在 `PvInverterDevice.Update` 里和出力一起算。分配放在出力之前，温度或入射角改变的这一步就生效。
- **停机或电网断开的逆变器不参与。** 不把它的设定额转给别人，也不接收别人的缺额。整站停机时出力仍为 0。
- **无功继续均分。** 有功加发之后，视在功率顶满时现有 `ClampApparent` 仍可能压低无功。分配函数不处理无功。
- **设定对应逆变器交流合计。** 一次图箱变「实时 P」是 `ActivePowerKw`，即各逆变器交流之和。高压侧还要减去铁损和铜损，不把损耗折进设定。
- **分配按逆变器，不按方阵再切一层。** 同一方阵的逆变器共用温度和入射角，可发相同，补进来的缺额会在该方阵内均分。奇数台时基线仍是「总设定 / 台数」，与现在的均分一致。

## 依赖图

```
纯分配函数（Task 1）────────────┐
                                ├── 箱变仿真步调用分配（Task 3）
逆变器可发预览与本步目标（Task 2）┘
```

Task 1 与 Task 2 互不调用，可以并行。Task 3 依赖两者。

## Task List

### Phase 1: 分配与逆变器目标

- [x] Task 1: 有功缺额分配函数
- [x] Task 2: 逆变器可发预览与本步目标

### Checkpoint: 分配与逆变器目标

- [x] 给定命令和可发上限，分配结果满足下面 Task 1 的数值
- [x] 只抬高本步目标时，`ActivePowerSettingKw` 不变，实际有功上升
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PvActiveShareAllocator|FullyQualifiedName~PvInverterDevice"`
- [x] `dotnet build ./EssSimulator.csproj`

### Phase 2: 箱变仿真步

- [x] Task 3: 箱变仿真步按可发补缺额

### Checkpoint: Complete

- [x] 一侧可发低于均分份额、另一侧有余量时，合计接近设定；两侧都够时仍均分
- [x] 单机设定在发得够时不被抬高
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PvActiveShareAllocator|FullyQualifiedName~PvInverterDevice|FullyQualifiedName~PvUnitDevice"`
- [x] `dotnet build ./EssSimulator.csproj`

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 用 Logger 总设定重新均分，盖掉单机写入 | High | 基线只用各台 `ActivePowerSettingKw`；Task 3 保留单机设定用例，并断言实际功率仍是 80 kW 与 200 kW |
| 可发算在出力之后，补缺额晚一步 | Med | Task 2 把可发预览拆出；Task 3 在 `ApplyStep` 之前分配 |
| 分配写入 `ActivePowerSettingKw`，点表回读变成加发后的值 | Med | 本步目标与设定分开；Task 2 断言设定不变 |
| 停机逆变器把份额送出，停机后其他台反而加发 | Med | 不参与的逆变器缺额不进入余量池；Task 1 有对应用例 |
| 无功较大时视在功率把加发的有功再压低，合计仍低于设定 | Low | 接受；验收用例无功为 0。不改 `ClampApparent` |
| 爬坡让单步看不出补额 | Low | 验收步长用 `TimeSpan.FromSeconds(5)`。默认斜率下这一步的有功变化量远大于额定 |
| 预览和出力各算一次 MPPT | Low | `Update` 改为预览加 `ApplyStep`；单元路径上预览只调用一次 |

## Open Questions

无。设定对齐逆变器交流合计。停机逆变器不转出份额。无功仍均分。

## Task 1: 有功缺额分配函数

**Description:** 增加不依赖设备的分配函数。输入每台的设定千瓦、可发上限千瓦、是否参与。参与的逆变器先拿到 `min(设定, 上限)`，发不够的差额均分给仍有余量的参与者，单台不超过余量，有剩余就再分一轮。不参与者输出 0，它的设定不进入差额。

**Acceptance criteria:**
- [ ] 设定 `[200, 200]`、上限 `[320, 80]`，结果 `[320, 80]`
- [ ] 设定 `[200, 200]`、上限 `[250, 80]`，结果 `[250, 80]`
- [ ] 设定 `[100, 100]`、上限 `[320, 320]`，结果 `[100, 100]`
- [ ] 设定 `[100, 100, 100]`、上限 `[40, 200, 200]`，结果 `[40, 130, 130]`
- [ ] 第一台不参与、设定 `[200, 200]`、上限 `[0, 320]`，结果 `[0, 200]`

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PvActiveShareAllocator"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Pv/PvActiveShareAllocator.cs`
- `EssSimulator.Tests/Pv/PvActiveShareAllocatorTests.cs`

**Estimated scope:** Small: 2 files

## Task 2: 逆变器可发预览与本步目标

**Description:** 把 `PvInverterDevice.Update` 拆成可发预览和出力两步。预览写入现有的 `AvailableAcPowerKw`（含效率和低温降额）。新增本步有功目标；未设置时出力仍用 `ActivePowerSettingKw`。设置本步目标不改变 `ActivePowerSettingKw`。限功率原因按本步目标判断。现有只调用 `Update` 的测试行为不变。

**Acceptance criteria:**
- [ ] 调用预览后 `AvailableAcPowerKw` 与同参数调用 `Update` 的结果一致
- [ ] 设定 100 kW、本步目标高于设定且低于可发和额定时，实际有功高于 100 kW，`ActivePowerSettingKw` 仍为 100
- [ ] 本步目标已顶到可发或额定时，限功率原因不再因原设定较低而显示「有功设定」
- [ ] 不设置本步目标时，出力仍等于 `min(设定, 可发, 额定)`

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PvInverterDevice"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Pv/PvInverterDevice.cs`
- `EssSimulator.Tests/Pv/PvInverterDeviceTests.cs`

**Estimated scope:** Medium: 2 files

## Task 3: 箱变仿真步按可发补缺额

**Description:** `PvUnitDevice.Update` 在两侧气候确定后，先对每台逆变器做可发预览，再调用 Task 1 的分配，把结果设为本步目标，然后出力。`SetActivePowerCommand` 仍只做均分写入。运行且并网的逆变器参与，上限为 `min(额定, 交流可发)`。停机或电网断开的不参与。无功设定路径不变。

**Acceptance criteria:**
- [ ] 两台逆变器、箱变设定 400 kW、一侧入射角或温度使可发低于 200 kW、另一侧可发不低于 320 kW 时，一步 `TimeSpan.FromSeconds(5)` 后合计有功在设定值的 1 kW 以内，受限侧不超过其可发，健康侧高于 200 kW
- [ ] 两侧可发都低于各自 200 kW 份额时，合计等于两侧可发之和（容差 1 kW），不超过设定
- [ ] 两侧都能完成 200 kW 均分时，每侧实际有功仍在均分份额的 1 kW 以内
- [ ] 先写入箱变设定，再只改一侧入射角，下一次 `Update` 即补缺额，中间不再写设定
- [ ] 箱变设定 400 kW 后把一台改为 80 kW、另一台保持 200 kW，且两侧都发得够时，三次 `Update` 后实际仍约为 80 kW 与 200 kW，Logger 设定仍为 400 kW

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PvUnitDevice|FullyQualifiedName~PvActiveShareAllocator"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [ ] Manual check: 无界面改动。一次图箱变「P设」与方阵卡片仍走现有命令

**Dependencies:** Task 1, Task 2

**Files likely touched:**
- `EssDeviceSimModel/Pv/PvUnitDevice.cs`
- `EssSimulator.Tests/Pv/PvUnitDeviceTests.cs`
- `EssDeviceSimModel/Pv/PvInverterDevice.cs`

**Estimated scope:** Medium: 3 files
