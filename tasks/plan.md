# Implementation Plan: 物理引擎阶段 2（690 V 影子比对）

## Overview

分域边界已提交。本切片在真实径向图的一个单元 690 V 母线上影子跑新核：旧核仍对外平均电压，影子只用自己的提交状态，不写生产母线、不删 `ApplyLocalVoltageSources`。

## Architecture Decisions

- **只读适配：** `RadialIslandShadowAdapter` 从 `ElectricalBusNode.VoltageSources` 采集构网 E，漏抗从单元变 `ImpedancePercent` 折到低压。
- **影子宿主：** `IslandShadowRunner` 在 Kernel 内装配；并网才建 `SplitDomainHandshake`。`SyncFormingReferences` 只更新 PCS 内电势。
- **PCS Z：** 阶段 2 占位 `MinPcsPort.DefaultOutputImpedanceOhm`，阶段 3 再换真端口方程。
- **不做：** `PlantEngine` 生产接替、删除平均捷径、把影子 P/Q 接到生产戴维南。

## Task List

### Phase 2: Shadow on real 690 V island

- [x] Task 9–11: 分域边界（已提交）
- [x] Task 12: 径向 690 V 只读快照
- [x] Task 13: 影子求解不写生产母线
- [x] Task 14: 电压差在影子中涌现环流；生产仍只平均

### Checkpoint: Shadow

- [x] 真实 `RadialNetworkGraph` 单元岛
- [x] 生产电压/频率不被影子改写
- [x] 尚未删除 `ApplyLocalVoltageSources`
