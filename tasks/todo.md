# 物理引擎阶段 2：分域边界（独立宿主）

> 内核已提交。本切片只做戴维南 / 注入交接，不接生产、不删平均。

## 已确认
- [x] 阶段 1：逻辑时钟与提交契约
- [x] Task 6–8：相量内核、最小 PCS、环流涌现
- [x] 构网源必须带有限 Z；环流是涌现不是估计算子

## Phase 2: Domain boundary
- [x] Task 9: IsolatedAcIsland 并网戴维南贴装
- [x] Task 10: SplitDomainHandshake 一拍延迟与上下事件分流
- [x] Task 11: 失败不提交；影子状态隔离

## Checkpoint: Boundary
- [x] 延迟 / 事件 / 冻结 / 影子测绿
- [x] 不写入 PlantEngine 生产路径
