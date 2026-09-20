# Implementation Plan: 物理引擎阶段 3（平均变流器与直流结算）

## Overview

阶段 2 已把 690/35 kV 交给相量核，PCS 仍是最小端口（E 串 Z）。本切片在独立宿主把该端口包成平均变流器：交流仍由网络解，直流只按端口功率做能量结算。不改生产 `PcsDevice` / `PcsBmsDcCouplingLink`，不做限流重装配，不接管 220 kV。

## Architecture Decisions

- **控制与物理分离：** `InternalVoltageV` 仍是上一步控制参考；本拍只 stamp + 求解，结算不得改 E。
- **电流约定：** 流入设备为正。交流口 `I_into = −I_toward_bus`。
- **直流结算：** `P_dc_into + P_ac_into = P_loss ≥ 0`；放电 `P_dc = |P_ac|/η`，充电 `P_dc = −P_ac·η`。
- **Vdc 是输入**（电池侧），不是从交流反算。
- **不做：** 生产接替、限流重跑步骤 4、删 `MinPcsPort`、删 `PcsBmsDcCouplingLink`。

## Task List

- [x] Task 33: AverageConverterPort / DcEnergySettlement
- [x] Task 34: 充放电能量守恒；结算不改交流 E
- [x] Task 35: 两机各自 Pac 结算直流，不均分流
