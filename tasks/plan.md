# Implementation Plan: 物理引擎阶段 2（网络内核）

## Overview

在独立测试宿主中建立三相平衡基波相量网络内核：节点导纳装配、有限阻抗电压源、阻抗支路、开关。最小 PCS 端口为受控电压源串阻抗。本阶段不接生产 `PlantEngine`、不删径向平均。环流只作为并联电压源的验收现象。

阶段 1 已提交：`feature/physics-engine-clock-commit`。

## Architecture Decisions

- **Norton 装配优先：** 构网源必须带有限 Z，可把 \(E,Z\) 贴成 \(I=E/Z\) 与 \(Y=1/Z\)。|Z| 过小则拒绝并联，不做理想电压源 MNA，直到真正需要变压器理想变比约束。
- **datum：** 无源岛才固定一节点 \(V=0\)；本切片的测试岛用参考地。
- **独立宿主：** `Kernel` 命名空间不引用 `RadialPowerSweepEngine`。影子/生产接替是后续任务。
- **不做：** 改 PCS 控制公式、删 `ApplyLocalVoltageSources`、分域戴维南接线、EMT。

## 依赖图

```
PhasorNetwork（Y V = I）
    │
    ├── 阻抗支路 / 开关
    │
    └── 有限 Z 电压源（最小 PCS 端口）
            │
            └── 解析电路 + 并联源环流验收
```

## Task List

### Phase 2: Network kernel (isolated)

- [x] Task 6: PhasorNetwork 装配与求解
- [x] Task 7: 最小 PCS 端口（受控源串 Z）
- [x] Task 8: 不变量与环流涌现验收

### Checkpoint: Kernel

- [ ] 解析电路、KCL、功率平衡
- [ ] 无输出支路的电压源拒绝并联
- [ ] 设备/支路顺序不影响结果
- [ ] 尚未写入生产 PlantEngine

## Risks

| Risk | Mitigation |
|---|---|
| 稠密 GE 只适合小岛 | 阶段 2 岛 <20 节点，可接受 |
| 与径向双真值 | 本阶段不接生产 |
