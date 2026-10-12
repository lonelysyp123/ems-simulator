# 箱变有功在两侧方阵间补缺额

> 已确认：箱变「P设」仍按逆变器台数均分写入 `ActivePowerSettingKw`。每个仿真步按本步可发把发不够的份额补给有余量的逆变器。设定回读不变。停机或电网断开的逆变器不转出份额。无功仍均分。设定对齐逆变器交流合计，不把箱变损耗折进设定。
> Modbus 异步收包计划已归档：`tasks/modbus-async-receive.md` / `tasks/todo-modbus-async-receive.md`。

## 已确认
- [x] 缺额转移，不改均分命令，不改点表回读
- [x] 基线是各台当前设定，单机写低且发得够时不补回 Logger 原设定
- [x] 同一仿真步内先算可发再分配
- [x] 不改一次图，不改无功分配

## Phase 1: 分配与逆变器目标
- [x] Task 1: 有功缺额分配函数
- [x] Task 2: 逆变器可发预览与本步目标（可与 Task 1 并行）

## Checkpoint: 分配与逆变器目标
- [x] 分配函数覆盖：单侧不够并被补满、两侧都不够、两侧都够、三台分摊、不参与者不转出
- [x] 本步目标高于设定时实际有功上升，`ActivePowerSettingKw` 不变
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PvActiveShareAllocator|FullyQualifiedName~PvInverterDevice"`
- [x] `dotnet build ./EssSimulator.csproj`

## Phase 2: 箱变仿真步
- [x] Task 3: 箱变仿真步按可发补缺额（依赖 Task 1、Task 2）

## Checkpoint: Complete
- [x] 一侧受限时合计接近设定；两侧都够时仍均分
- [x] 改入射角的下一步即补缺额，无需重写「P设」
- [x] 单机 80 kW / 200 kW 在发得够时保持不变
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PvActiveShareAllocator|FullyQualifiedName~PvInverterDevice|FullyQualifiedName~PvUnitDevice"`
- [x] `dotnet build ./EssSimulator.csproj`
