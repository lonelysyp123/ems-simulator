# Implementation Plan: 三项物理修正

## Overview
按联调仿真可落地的物理修正：变压器 Step 打开漏抗压降；构网并机母线取下垂源的公共电压而非 max V；PCS 并网/离网交流电流符号统一为「正=从母线取电」。

## Architecture Decisions
- 漏抗压降沿用已有 `ΔU_pu = k · z_pu · (Q / S_r)`，`V2 = V1/n · (1+ΔU_pu)`；Q>0 升压。生产 `Step` 与孤岛 `UnitTransformerIslandSync` 打开；失电清零路径保持关闭。
- 690V 母线本地电压源：对正在注入的源做电压/频率算术平均，作为下垂后的公共母线量。
- 交流电流：抽取统一符号函数，放电为负、充电为正。

## Task List
- [x] Task 1: 变压器 Step 漏抗压降
- [x] Task 2: 构网并机公共电压
- [x] Task 3: PCS 电流符号统一
