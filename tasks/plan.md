# Implementation Plan: 物理引擎阶段 2（35 kV 边界对照）

## Overview

690 V 已全部交给相量核。本切片只读对照径向 35 kV 与核戴维南边界：Voc 从本拍径向折算，P/Q 晚一拍进上层；影子交接不得写 Bus35。不接管 35 kV、不删径向平均。

## Architecture Decisions

- **上域只读：** `RadialTheveninPort` 用 Bus35 折低压 Voc，Zth 仍是单元变漏抗。
- **对照样本：** `StationBoundaryCompare.Capture` 记录径向 V35、折算 Voc、核 Voc/注入。
- **已知偏差：** GridZ = 10·Zth 占位；35 kV 岛估平均暂留。
- **不做：** 核写 Bus35、删 `EstimateIslandedBus35`、阶段 3 PCS 端口。

## Task List

- [x] Task 23–25: 全部 690 接替（已提交）
- [x] Task 26: RadialTheveninPort / StationBoundaryCompare
- [x] Task 27: 首拍 Voc 对齐；次拍延迟压降
- [x] Task 28: 影子交接不写生产 35 kV
