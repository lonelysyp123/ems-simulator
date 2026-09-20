# Implementation Plan: 物理引擎阶段 2（35 kV 生产接替）

## Overview

边界对照已通过。本切片把生产 35 kV 交给核：并网写 Voc 折算，离网写 690 导纳加权反送；GridEmf 用 PCC 开路电压，避免加载 Bus35 回灌造成 GridZ 双计。不删 NetworkSolver 夹具、不做阶段 3 PCS 端口、不接管 220 kV。

## Architecture Decisions

- **Bus35.KernelOwned：** 主变/主断 Coupler 仍 Step 设备，但不再写 35 kV。
- **GridEmf：** 并网只从 `OpenCircuitBus35LineVoltageV`（PCC 折算）进入下域。
- **并网 V35：** `GridEmf − (ΣI)·GridZ` 折回高压；GridZ 仍为 10·Zth 占位。
- **离网 V35：** 单元 HV 合闸的 690 母线按漏抗导纳加权；无源岛第二遍用该 35 kV 作戴维南。
- **不做：** 阶段 3 真 PCS 端口、220 kV 内核、删除 NetworkSolver 岛估。

## Task List

- [x] Task 26–28: 35 kV 只读对照（已提交）
- [x] Task 29: Bus35 KernelOwned，Coupler 不写
- [x] Task 30: 生产写 35 kV；GridEmf 用开路电压
- [x] Task 31: 离网导纳加权；停用径向 35 kV 平均
- [x] Task 32: 影子仍不写生产；失败回滚 Bus35
