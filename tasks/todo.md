# 物理引擎阶段 2：网络内核（独立宿主）

> 阶段 1 已提交。本阶段只在 `EssDeviceSimModel/Kernel` 建相量网，不接生产、不删平均。

## 已确认
- [x] 阶段 1：逻辑时钟与提交契约
- [x] 构网源必须带有限 Z；环流是涌现不是估计算子
- [x] 独立测试宿主先于影子/生产接替

## Phase 2: Network kernel
- [x] Task 6: PhasorNetwork 装配与求解
- [x] Task 7: 最小 PCS 端口（受控源串 Z）
- [x] Task 8: KCL / 功率平衡 / 并联源环流 / 拒绝零阻抗并联

## Checkpoint: Kernel
- [x] 解析电路与不变量测绿
- [x] 不写入 PlantEngine 生产路径
