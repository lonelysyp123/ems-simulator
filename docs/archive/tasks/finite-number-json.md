# Implementation Plan: 一次图快照避开非有限数

## Overview

仿真状态里的 `NaN` 或 `±Infinity` 被写进一次图快照时，`System.Text.Json` 会中断整包响应。`GET /api/mainline` 失败，SignalR `ReceiveMainLine` 每 200ms 再失败一次，页面停在空快照，控制台被堆栈刷满。本计划让 HTTP 和 SignalR 把非有限数写成 `null`，并挡住变压器额定电压和簇内阻这两处会稳定造出 `NaN` 的除法。读数和相量计算不再把非有限数继续传下去。

上一份计划（300MW 纯储能测试工程）已归档到 `tasks/mw300-ess-station.md` / `tasks/todo-mw300-ess-station.md`。

## Architecture Decisions

- **写出用 `null`，不算进 JSON 字面量。** 不开启 `JsonNumberHandling.AllowNamedFloatingPointLiterals`。浏览器和 SignalR 客户端都解析不了 `NaN`。一次图的 `fmtVolt` / `fmtKw` / `fmtKvar` 已经把 `null` 显示成「—」。
- **只挂在两条 Web 写出通道上。** `Program.cs` 里现有的 `Configure<JsonOptions>`，以及 SignalR 的 `JsonHubProtocolOptions.PayloadSerializerOptions`。组态文件用的 `TopologyStore` 选项不动。
- **读回 `null` 或非有限数时用 0。** 转换器 `HandleNull = true`，避免请求体里的 `null` 把反序列化打成 400。物理侧的缺测约定本来就是 0。
- **每个类型的每个属性只记一次。** 转换器发现非有限数时打一条警告，键是 `类型名.属性名`。集合元素共用一把键。不在每次快照上刷堆栈。
- **额定电压的默认值按变压器种类分开。** 主变 `CreateConfig(TransformerConfig)` 把不大于 1 V 的一次/二次电压换成 220 kV / 35 kV。单元变 `CreateConfig(UnitTransformerConfig)` 换成 35 kV / 690 V。共用的私有方法不知道种类，不能在那里写死 220 kV。`TransformerDevice.RunPhysics` 再挡一层：额定电压无效时额定电流和负载率写 0，不做除法。
- **簇内阻不是有限正数就不参与电导分配。** `totalConductance <= 0` 挡不住 `1/0` 得到的 `+∞`。该簇电导按 0，总电导非有限或不大正数时整堆电流分配为 0。
- **不改仿真节拍。** `PeriodicTimer` 在单步超时后不再休眠，那是站规模和逐芯运算的耗时。本计划只去掉异常风暴带来的额外占用。

## 依赖图

```
非有限数转换器（Task 1）
        │
        └── 挂到 HTTP 与 SignalR（Task 2）

变压器额定电压（Task 3）
簇电流分配（Task 4）
读数与相量（Task 5）
```

Task 1 是 Task 2 的前提。Task 3、Task 4、Task 5 互不依赖，也可以和 Task 1 并行。建议先做 Task 1 和 Task 2，一次图先能收到快照，再补三处计算。

## Task List

### Phase 1: 快照能写完

- [x] Task 1: 非有限数 JSON 转换器
- [x] Task 2: 挂到 HTTP 与 SignalR

### Checkpoint: 快照能写完

- [x] 含 `NaN` 的相量能序列化成带 `null` 的 JSON，不抛 `JsonException`
- [x] HTTP 与 SignalR 的选项都走同一个 `Apply`
- [x] 组态文件的序列化选项没有这个转换器

### Phase 2: 计算不再产出非有限数

- [x] Task 3: 变压器额定电压保护
- [x] Task 4: 簇电流分配避开 0 内阻
- [x] Task 5: 读数与相量计算不传播非有限数

### Checkpoint: Complete

- [x] 额定电压为 0 的变压器步进后，电流和电压都是有限数
- [x] 簇内阻为 0 时堆更新不产生 `NaN` 电压
- [x] 相量输入为 `NaN` 时有功、无功为 0
- [x] 相关测试与 `dotnet build ./EssSimulator.csproj` 通过

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 前端拿 `null` 做算术，图上坐标变成 `NaN` | Med | 展示函数已把 `null` 显示成「—」；本计划不改布局。若某段直接拿电压做除法，保持该段的 `\|\| 0` |
| SignalR 和 HTTP 各有一份 `JsonSerializerOptions`，只改一处 | High | Task 2 两处都调用 `FiniteNumberJson.Apply` |
| 转换器被加进组态文件选项，磁盘 JSON 出现 `null` | Med | `Apply` 只在 `Program.cs` 的两处 Web 选项上调用 |
| 把 0 V 换成默认额定电压，坏配置看起来像正常主变 | Med | 替换时记一条警告，带上主变或单元变 |
| 每次快照都打日志，CPU 仍被控制台占住 | Med | 同一 `类型.属性` 只记一次，不附带异常堆栈 |
| 单步仿真仍超过定时器间隔 | Low | 明确不改 `EnergyStorageSys` 的循环间隔 |

## Open Questions

无。非有限数在响应里写成 `null`，反序列化和物理缺测用 0。仿真主循环节拍不在本计划内。

## Task 1: 非有限数 JSON 转换器

**Description:** 新增 `FiniteNumberJson.Apply`，给 `JsonSerializerOptions` 加上 `double` 和 `float` 转换器。写出时非有限数变成 `null`，读入时 `null` 或非有限数变成 0。属性上的非有限数按 `类型名.属性名` 各记一条警告；集合元素共用一把键。本任务不改 `Program.cs`。

**Acceptance criteria:**
- [x] `double.NaN`、`double.PositiveInfinity`、`double.NegativeInfinity` 以及对应的 `float` 序列化结果是 `null`；`0` 和 `1.5` 保持原数字
- [x] 反序列化 `null` 或非有限数字得到 0，不抛异常
- [x] 同一个属性连续序列化两次非有限数，警告只出现一次

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~FiniteNumberJson"`
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [x] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `Web/FiniteNumberJson.cs`
- `EssSimulator.Tests/Web/FiniteNumberJsonTests.cs`

**Estimated scope:** Small: 1–2 files

## Task 2: 挂到 HTTP 与 SignalR

**Description:** 在现有 `Configure<JsonOptions>` 和 SignalR 的 `PayloadSerializerOptions` 上调用 `FiniteNumberJson.Apply`。一次图的首次 `GET /api/mainline` 和之后的 `ReceiveMainLine` 都走这个转换器。`TopologyStore` 的选项不调用。

**Acceptance criteria:**
- [x] `Program.cs` 里 HTTP `JsonOptions` 和 SignalR `JsonHubProtocolOptions` 都调用了 `Apply`
- [x] 用 `Apply` 之后的选项序列化带 `NaN` 电压的 `AcPhasorSnapshot`，得到 JSON 对象而不是 `JsonException`
- [x] `TopologyStore` 的 `JsonSerializerOptions` 没有注册该转换器

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~FiniteNumberJson"`
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [x] Manual check: 确认两处 `Apply` 调用，且 `TopologyStore.JsonOpts` 未调用

**Dependencies:** Task 1

**Files likely touched:**
- `Program.cs`
- `EssSimulator.Tests/Web/FiniteNumberJsonTests.cs`

**Estimated scope:** Small: 1–2 files

## Task 3: 变压器额定电压保护

**Description:** 主变和单元变的 `CreateConfig` 在一次或二次电压不大于 1 V 时使用各自的默认额定电压。`TransformerDevice.RunPhysics` 在额定电压无效时把对应额定电流和负载率写成 0，不再做除法，避免 `∞ × 0` 变成 `NaN`。

**Acceptance criteria:**
- [x] `CreateConfig(TransformerConfig)` 在电压为 0 时得到 220000 V 和 35000 V
- [x] `CreateConfig(UnitTransformerConfig)` 在电压为 0 时得到 35000 V 和 690 V
- [x] 一次额定电压为 0 的 `TransformerDevice` 步进后，`GetCurrentState()` 的电压、电流、负载率都是有限数

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~TransformerNominal"`
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [x] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Devices/TransformerDeviceFactory.cs`
- `EssDeviceSimModel/Devices/TransformerDevice.cs`
- `EssSimulator.Tests/Devices/TransformerNominalVoltageTests.cs`

**Estimated scope:** Small: 1–2 files

## Task 4: 簇电流分配避开 0 内阻

**Description:** `CalculateCurrentDistribution` 只把有限且大于 0 的簇内阻算进电导。总电导不是有限正数时返回零电流，不再执行 `∞ / ∞`。

**Acceptance criteria:**
- [x] 簇内阻为 0 时，`BatteryRackSimulator.Update` 之后堆电压、簇电压和簇电流都是有限数
- [x] 内阻为 0.1 Ω 的现有分配结果不变，等效运算测试仍然通过

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ClusterEquivalentCalc|FullyQualifiedName~RackCurrentDistribution"`
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [x] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Battery/batteryHeapModel.cs`
- `EssSimulator.Tests/Battery/RackCurrentDistributionTests.cs`

**Estimated scope:** Small: 1–2 files

## Task 5: 读数与相量计算不传播非有限数

**Description:** `SafeGetDouble` 在转换结果不是有限数时返回 fallback。`AcQuantityConverter` 的有功、无功在任一输入非有限时返回 0；功率因数返回 1，与现有的零视在功率约定一致。`AcPhasorSnapshot` 的计算属性因此也不再产出 `NaN`。

**Acceptance criteria:**
- [x] `ComputeActivePowerKw` 和 `ComputeReactivePowerKvar` 在电压、电流或相角为 `NaN` 或无穷大时返回 0
- [x] 同样输入下 `ComputePowerFactor` 返回 1
- [x] `new AcPhasorSnapshot(double.NaN, 10, 0, 50).ActivePowerKw` 为 0
- [x] `SafeGetDouble` 读到非有限数时返回调用方传入的 fallback，而不是 `NaN`

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~AcQuantityConverter|FullyQualifiedName~SafeGetDouble"`
- [x] Build succeeds: `dotnet build ./EssSimulator.csproj`
- [x] Manual check: 无

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Model/AcQuantityConverter.cs`
- `Display/GuiSimDataAccess.cs`
- `EssSimulator.Tests/Model/AcQuantityConverterTests.cs`
- `EssSimulator.Tests/Display/SafeGetDoubleTests.cs`

**Estimated scope:** Medium: 3–5 files
