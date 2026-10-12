# LC 5.5MW / 10MW 单元片段共存

> 已确认：两段始终拼装、始终采集；`n` = EMU 内 PCS 组号（固定 1、2）；画布 `pcs` = PCS 变流器支路（两条支路 = 一台 PCS）。不互斥选型。

计划：`tasks/lc-unit-5.5-10-coexist.md`

## 已确认
- [x] 5.5MW / 10MW unit 片段共存，不是槽位二选一
- [x] 无「PCS 单元」；对象为 支路 → PCS → PCS 组 → EMU
- [x] 两条 PCS 支路组成一台 PCS；组态 `pcs` 节点是支路
- [x] 10MW 表 `n=2` = 两个 PCS 组；5.5MW 表只写组 1
- [x] `templateId` 保持 `pcs`，只改显示名

## Phase 1
- [x] Task 1: 元数据与拼装（两段始终并入；10MW 固定展开 n=1,2；5.5MW 只 n=1）
- [x] Task 2: 按 PCS 组双写 `unit_param` / `unit1_param`
- [x] Task 3: 组态 `pcs` 显示名改为「PCS 变流器支路」+ EMU 树「支路×N」+ 文案/手册

## Checkpoint
- [x] 任意工程拼装同时有 2600 / 2900 / 5000 三段
- [x] 组 2 只出现在 10MW 段与 group 段
- [x] 设备栏看到「PCS 变流器支路」，旧工程无需迁移
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~LcPointMapComposer|FullyQualifiedName~LcUnit|FullyQualifiedName~TopologyTemplates"`

## Phase 2
- [x] Task 4: pointmaps README + 系统设计说明用语对齐

## Checkpoint: Complete
- [x] Ready for review
