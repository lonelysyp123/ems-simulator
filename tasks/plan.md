# Implementation Plan: 物理引擎阶段 2（多单元与双耳接替）

## Overview

单元变回写已改完。本切片把相量核接到**所有** 690 V 母线：普通多单元各一条，双耳左右耳各一条。Coupler 不再写已接管耳；每条母线独立 a–d。

## Architecture Decisions

- **AttachAll：** 遍历 `AllUnit690Buses` 置 `KernelOwned` 并建 `ProductionUnit690Host`。
- **双耳 Zth：** 用分裂阻抗与半容量折漏抗。
- **黑启动上下文：** 按 PCS 通道读 `Bus690ForPcsChannel`，不再把左耳电压派给右耳。
- **不做：** 删 35 kV 平均、阶段 3 真 PCS 端口、EMT。

## Task List

- [x] Task 21–22: 停掉已接管岛的单元变电压回写
- [x] Task 23: 所有 690 母线默认接管
- [x] Task 24: 双耳 Coupler 不写 KernelOwned 耳
- [x] Task 25: 左右耳电压可独立
