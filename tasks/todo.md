# 物理引擎阶段 1：时钟与提交契约

> 已确认：只做阶段 1。不改径向潮流、不删平均、不接 690 V 岛。
> EMS 热更新方案已归档：`tasks/ems-strategy-hot-update.md` / `tasks/todo-ems-strategy-hot-update.md`。

## 已确认
- [x] 逻辑时钟与墙钟分离
- [x] 提交 = memento，失败整步冻结
- [x] 控制一拍滞后本阶段不改 PCS 公式
- [x] IntegrationStepMultiplier 暂留为已知偏差

## Phase 1: Foundation
- [x] Task 1: SimulationClock 替代墙钟 dt
- [x] Task 2: PlantEngine memento 与失败整步冻结
- [x] Task 3: Host 接时钟与 SolverFault
- [x] Task 4: 重放一致测试
- [x] Task 5: 一拍滞后步长表征测试

## Checkpoint: 阶段 1
- [x] 墙钟不再充当物理 dt
- [x] 失败整步冻结（电气 + 动态）
- [x] 重放测绿
- [x] 径向引擎与平均电压逻辑未删
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~SimulationClock|FullyQualifiedName~PlantStepCommit|FullyQualifiedName~PlantEngineReplay|FullyQualifiedName~ControlLag"`
- [x] 存量：`FullyQualifiedName~PlantEngineCharacterization|FullyQualifiedName~PcsBlackStart`
