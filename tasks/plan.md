# Implementation Plan: 物理引擎阶段 2（分域边界）

## Overview

内核已提交。本切片在独立宿主中实现 690 V 岛与上域的戴维南 / P/Q 交接：漏抗归上域，下域注入晚一拍进入上域，上域事件重跑 a–d、下域事件只重跑 c–d，影子副本不得回写生产边界。仍不接 `PlantEngine`、不删径向平均。

## Architecture Decisions

- **上域：** 内电势 `GridEmf` + 网架 `GridZ`（用上一拍戴维南电流算 Voc）+ 漏抗 `TheveninZ`（每拍贴给下域）。
- **下域：** `IsolatedAcIsland` 贴 `Voc` 后 `Zth`；并网后角度锁在 Vth。
- **提交：** 两侧都成功才写入 `CommittedVoc` / `CommittedInjectionVa` / `CommittedTheveninCurrent`。
- **影子：** `CreateShadow()` 克隆拓扑，提交状态从零开始，Tick 影子不影响生产。
- **不做：** 接入生产步进、删 `ApplyLocalVoltageSources`、对真实 35 kV 径向做影子比对。

## 依赖图

```
PhasorNetwork
    └── IsolatedAcIsland（可贴戴维南）
            └── SplitDomainHandshake（a–d / 一拍延迟 / 影子隔离）
```

## Task List

### Phase 2: Domain boundary (isolated)

- [x] Task 6–8: 相量内核与最小 PCS（已提交）
- [x] Task 9: IsolatedAcIsland 并网戴维南贴装
- [x] Task 10: SplitDomainHandshake 一拍延迟与事件分流
- [x] Task 11: 求解失败不提交；影子不回写生产

### Checkpoint: Boundary

- [x] 下域功率阶跃本拍不改 Voc
- [x] 上域 Emf 阶跃本拍改 Voc
- [x] 失败冻结边界
- [x] 尚未写入生产 PlantEngine

## Risks

| Risk | Mitigation |
|---|---|
| 线性上域 Voc 与 Zth 双重计数 | GridZ 管延迟压降，Zth 只管漏抗贴装 |
| 与生产径向双真值 | 本切片仍不接生产 |
