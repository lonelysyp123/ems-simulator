# 物理引擎阶段 2：690 V 影子比对

> 分域边界已提交。本切片在真实径向拓扑上影子跑新核，旧核仍对外，不接生产、不删平均。

## 已确认
- [x] 阶段 1：逻辑时钟与提交契约
- [x] Task 6–8：相量内核、最小 PCS、环流涌现
- [x] Task 9–11：戴维南 / P/Q 交接、一拍延迟、影子隔离

## Phase 2: Shadow
- [x] Task 12: 径向 690 V 只读快照
- [x] Task 13: IslandShadowRunner 不写生产母线
- [x] Task 14: 环流在影子中涌现，生产仍平均

## Checkpoint: Shadow
- [x] 真实拓扑影子测绿
- [x] 不删除 ApplyLocalVoltageSources
