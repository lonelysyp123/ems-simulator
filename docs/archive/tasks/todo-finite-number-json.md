# 一次图快照避开非有限数

> 已确认：非有限数在 HTTP 和 SignalR 响应里写成 `null`，不开启 `AllowNamedFloatingPointLiterals`。组态文件序列化不动。主变 / 单元变的 0 V 额定电压分别回默认值，设备侧不再做除法。簇内阻不是有限正数时不参与电导分配。仿真主循环节拍不改。
> 300MW 计划已归档：`tasks/mw300-ess-station.md` / `tasks/todo-mw300-ess-station.md`。

## 已确认
- [x] 响应里的 `NaN` / `±Infinity` 写成 `null`，前端显示「—」
- [x] 反序列化 `null` 或非有限数得到 0
- [x] 同一 `类型.属性` 只记一次警告，不刷堆栈
- [x] 主变默认 220 kV / 35 kV，单元变默认 35 kV / 690 V
- [x] 不改 `EnergyStorageSys` 的循环间隔

## Phase 1: 快照能写完
- [x] Task 1: 非有限数 JSON 转换器
- [x] Task 2: 挂到 HTTP 与 SignalR（依赖 Task 1）

## Checkpoint: 快照能写完
- [x] 含 `NaN` 的 `AcPhasorSnapshot` 能序列化成 JSON
- [x] HTTP `JsonOptions` 和 SignalR `PayloadSerializerOptions` 都调用 `Apply`
- [x] `TopologyStore` 的选项没有该转换器
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~FiniteNumberJson"`
- [x] `dotnet build ./EssSimulator.csproj`

## Phase 2: 计算不再产出非有限数
- [x] Task 3: 变压器额定电压保护（可与 Task 4、Task 5 并行）
- [x] Task 4: 簇电流分配避开 0 内阻
- [x] Task 5: 读数与相量计算不传播非有限数

## Checkpoint: Complete
- [x] 额定电压为 0 的变压器步进后状态值为有限数
- [x] 簇内阻为 0 时堆电压和簇电流为有限数
- [x] 相量输入为 `NaN` 时有功、无功为 0，功率因数为 1
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~FiniteNumberJson|FullyQualifiedName~TransformerNominal|FullyQualifiedName~RackCurrentDistribution|FullyQualifiedName~AcQuantityConverter|FullyQualifiedName~SafeGetDouble"`
- [x] `dotnet build ./EssSimulator.csproj`
