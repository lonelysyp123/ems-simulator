# Implementation Plan: 物理引擎阶段 1（时钟与提交契约）

## Overview

按 `docs/物理引擎演进方案.md` 落地阶段 1：逻辑仿真时钟、步进 memento 与失败整步冻结、重放一致、一拍滞后步长表征。不改径向潮流、不删平均电压、不接 690 V 岛 MNA。

上一份 `tasks/plan.md`（EMS 策略热更新）已归档到 `tasks/ems-strategy-hot-update.md`。

## Architecture Decisions

- **逻辑时钟与墙钟分离：** `SimulationClock` 的 dt 来自 `PropagationIntervalMs`。Host `PeriodicTimer` 只控制墙钟速率。
- **提交 = memento：** 步进前捕获 PCS/BMS/母线/热关键量；成功后投影；失败恢复并置 `SolverFault`，不调用 `AfterPlantStep`。
- **控制一拍滞后本阶段不改 PCS 公式：** 只发布 `LastCommittedMeasurements`，用量测延迟 PI 表征稳定 dt。
- **`IntegrationStepMultiplier`：** 仍只放大积分 dt，标为已知偏差。
- **电流方向：** `ElectricalSignConvention.InflowPositive` 为内部约定，不改协议符号。
- **不做：** 节点方程、删平均、分域戴维南、影子模式、改点表。

## 依赖图

```
SimulationClock
    │
    ├── PlantEngine memento / SolverFault
    │       │
    │       ├── Host ExecuteAsync 接时钟
    │       │
    │       └── 重放一致测试
    │
    └── 一拍滞后表征（可并行）
```

## Task List

### Phase 1: Foundation

- [ ] Task 1: SimulationClock
- [ ] Task 2: 步进 memento 与失败整步冻结
- [ ] Task 3: Host 接时钟 + SolverFault
- [ ] Task 4: 重放一致
- [ ] Task 5: 一拍滞后步长表征

### Checkpoint: 阶段 1

- [ ] 墙钟不再充当物理 dt
- [ ] 失败整步冻结（电气 + 动态）
- [ ] 重放测绿
- [ ] 径向引擎与平均电压逻辑未删

### Phase 2+（本轮不实现）

网络内核、最小 PCS 端口、690 V 岛接替、影子模式。见 `docs/物理引擎演进方案.md` 第七节。

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| 全站深拷贝太重 | High | 只 memento 约定字段 |
| 电池 Random 破坏重放 | Med | 堆/簇/包构造用确定性种子 |
| 失败钩子与真实抛错路径不同 | Low | 钩子放在物理步之后、提交之前 |

## Open Questions

无。本轮范围已确认为阶段 1。
