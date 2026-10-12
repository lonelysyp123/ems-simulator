# 活岛上合单元高压（空载变涌流按 35 kV 保护）

## 已确认
- [x] 现场允许把空载单元变合到已构网的 35 kV 岛上；保护 CT 在高压侧
- [x] 6.3 MVA / 35 kV 一次涌流（最多 12×In ≈ 1.2 kA）低于单元断 3500 A，不应跳供电侧
- [x] 根因是 690 V 电流拿去比 35 kV 定值，不是定值配小了
- [x] 不抬 `FaultThresholdA`、不关涌流、不做开关 EMT、不改 LC 点表结构
- [x] 不覆盖 `tasks/plan.md` / `tasks/todo.md`（LC 拼装）

## Phase 1: 保护电流电压等级
- [x] Task 1: 线电流按电压等级折算纯函数
- [x] Task 2: 单元支路/主断写入断路器前折到额定电压

## Checkpoint: 保护电流
- [x] 690 V、630 A → 35 kV 约 12 A
- [x] `FaultThresholdA` 仍 3500
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~AcQuantityConverter|FullyQualifiedName~BreakerSimulator"`

## Phase 2: 活岛合空载变
- [x] Task 3: 涌流经供电 PCS \(I_\mathrm{max}\)；保护只看 35 kV 电流
- [x] Task 4: 集成测例：单元 1 构网后合单元 2 高压，单元 1 不跳

## Checkpoint: 合闸
- [x] 合闸后单元 1 `IsTripped==false` 且保持合
- [x] 不修改默认故障电流定值
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~UnitHvLiveClose"`

## Phase 3: 跳闸锁存与遥信
- [x] Task 5: 跳闸后 LC 为分（EE）；未复位不能合；复位后可再合

## Checkpoint: Complete
- [x] Phase 1–3 验收均满足
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Breaker|FullyQualifiedName~UnitHvLiveClose|FullyQualifiedName~LcMv"`
- [x] `dotnet build ./EssSimulator.csproj`
