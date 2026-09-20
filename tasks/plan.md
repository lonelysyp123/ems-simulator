# Implementation Plan: 物理引擎阶段 2（690 V 生产接替）

## Overview

影子比对已提交。本切片把**无双耳的单元 0** 690 V 母线交给相量核：该节点禁止算术平均，单元变 Coupler 不再写 690；上层径向仍解 35/220 kV，本拍 Voc 交给下层，下层 P/Q 晚一拍进入上层。求解失败走现有整步冻结。

## Architecture Decisions

- **所有权：** `ElectricalBusNode.KernelOwned`；仅单元 0 且无裂解变时默认接管。
- **电压真值：** `ProductionUnit690Host.ApplyAfterUpper` 在径向 Phase4 之后写 690，`notifyCouplers: false`。
- **平均退役范围：** 只停被接管节点的 `ApplyLocalVoltageSources`；35 kV 与其它 690 仍平均。
- **不做：** 删全局平均、改 PCS 控制公式、接管双耳/多单元、阶段 3 真端口方程。

## Task List

### Phase 2: Production cutover (unit 0)

- [x] Task 12–14: 影子比对（已提交）
- [x] Task 15: 接管节点禁平均、Coupler 不写 690
- [x] Task 16: ProductionUnit690Host 嵌入径向 Phase4
- [x] Task 17: 失败时 690 母线随 memento 回滚

### Checkpoint: Cutover

- [x] 并网 690 由核写出合理电压
- [x] 离网双源不再落到 0
- [x] 未接管节点行为不变
