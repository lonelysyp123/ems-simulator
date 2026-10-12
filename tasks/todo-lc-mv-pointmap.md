> 已归档。原 `tasks/plan.md` / `todo.md` 被电站 EMS 策略参数热更新方案覆盖。

# LC 中压点表归属与运行时继承

## Phase 1: 点表归到 LC
- [x] Task 1: 把 5.5MW / 10MW 点表迁到 LC 型号目录（emu.csv → lc.csv）
- [x] Task 2: 组态自动选型改打 LC，并迁移过期 emu=trina_* 选型
- [x] Task 3: 测试与说明改读 `models/lc/{id}/lc.csv`

## Checkpoint: Phase 1
- [x] 系统配置页 LC 能选 5.5MW/10MW，EMU 只剩 standard
- [x] 全量 `dotnet test EssSimulator.Tests` 绿
- [ ] 与人确认：standard LC 桥接与迁表前一致

## Phase 2: LC 运行时基类
- [x] Task 4: 抽出 LcRuntimeBase 与选型工厂，standard 行为不变
- [x] Task 5: ModelBoundLcRuntime — ModelSim 点表走 DataExchange，跳过点名桥

## Checkpoint: Phase 2
- [ ] standard LC：写 param60 仍能启动 PCS1（需重启后手工确认）
- [ ] 5.5MW LC：写 syst6=3 启动该机组全部模块（契约测试已钉 syst6 绑定；需重启后手工确认）
- [x] 全量测试绿

## Phase 3: 中压子类与扩展点
- [x] Task 6: Trina55 / Trina10 子类（可增采集、可覆写控制）
- [x] Task 7: 文档与系统配置文案

## Checkpoint: Complete
- [x] Phase 1–3 任务验收标准均满足（手工联调项除外）
- [x] `dotnet test EssSimulator.Tests` 与 `dotnet build ./EssSimulator.csproj` 通过
- [x] 未做项已明确排除：CSV extends 合并、标准 LC 全面 ModelSim 化、改 PlantEngine、多 csproj
