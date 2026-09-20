# Implementation Plan: 物理引擎阶段 2（交流真值绑定）

## Overview

单元 0 生产接替已提交。本切片禁止 PCS `Update` 在核求解之后用构网参考、线损公式或变二次覆盖交流端子电压。被接管岛上，PCS 遥测与 `GetUnitAcBusVoltage` 都读核母线。

## Architecture Decisions

- **锁：** `PcsDevice.AcTerminalLockedToNetwork`，由 `SolvePcsBmsPair` 按母线 `KernelOwned` 置位。
- **提交后绑定：** `Update` 末尾 `BindAcTerminalFromNetwork` 用端口入口（核已写入的母线电压）覆盖 `AcVoltage`。
- **单元母线查询：** `GetUnitAcBusVoltage` 对 KernelOwned 节点直接返回 `LineVoltageV`。
- **不做：** 改 PCS 控制/构网 E、删 `PcsBmsDcCouplingLink` 直流结算、接管双耳。

## Task List

- [x] Task 15–17: 单元 0 生产接替（已提交）
- [x] Task 18: 锁定端子后 Update 不得覆盖
- [x] Task 19: GetUnitAcBusVoltage / ApplyGridResults 走核母线
- [x] Task 20: 并网与离网 PCS 遥测对齐母线
