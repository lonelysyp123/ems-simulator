# 站用 SVG 恒无功模型

> 已确认：全站一台，并联 35 kV，只做恒无功。默认关闭。不做恒电压、恒功率因数、谐波、不平衡。主接线有一张 35 kV 卡片。
> EMS 策略参数方案已归档：`tasks/ems-strategy-params.md` / `tasks/todo-ems-strategy-params.md`。

## 已确认
- [x] 无功为正是容性发出；可用上限 \(Q_{rated}\times U/U_{nom}\)；设定值不回写夹限
- [x] 运行状态 0 停机 / 1 运行 / 2 闭锁；闭锁带 0.85–1.15 倍额定
- [x] 单位：V、A、kvar、Hz；变比 1
- [x] 一阶滞后 20 ms；运行损耗为额定容量的 0.8%
- [x] `EnableSvg` 默认 false，额定默认 10000 kvar，端口默认 2201

## Phase 1: 点表与设备
- [x] Task 1: 点表类型与单位（`Uint16`→`u16`，描述补单位和枚举）
- [x] Task 2: `SvgDevice` 恒无功（可与 Task 1 并行）

## Checkpoint: 设备可单测
- [x] `svg1` 解析为 `UInt16`
- [x] 100 ms 跟上设定，5 ms 仍在滞后；越限夹住；关机/闭锁输出为 0 且设定保留
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~SvgPointMap|FullyQualifiedName~SvgDevice"`
- [x] `dotnet build ./EssSimulator.csproj`

## Phase 2: 母线
- [x] Task 3: `SvgBusContributor` 挂 `Bus35`（依赖 Task 2）

## Checkpoint: 功率进母线
- [x] 关闭时不注册贡献者
- [x] 开启发 +5000 kvar 时母线无功增加、有功减少损耗；关机后退出
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~SvgBus"`

## Phase 3: 协议
- [x] Task 4: `svg` 根对象 + `ModelSim`（依赖 Task 1、Task 2；可与 Task 3 并行）
- [x] Task 5: `simSvg` 从站（依赖 Task 3、Task 4）

## Checkpoint: Complete
- [x] 写开关机和无功设定进 `svg` 对象，遥测读到设备输出
- [x] 关闭时端口计划没有 `simSvg`
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Svg"`
- [x] `dotnet build ./EssSimulator.csproj`
- [x] 主接线 35 kV 卡片：状态、实际无功、可用上限、开关机与无功设定
- [x] 未做：恒电压、恒功率因数、谐波、不平衡
