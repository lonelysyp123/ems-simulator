# Implementation Plan: 失电观测量与构网公共参考

## Overview
在现有准稳态模型内修观测量：去掉 690V 1% 电压地板；分闸后断路器负荷侧不再残留；失压时负荷测量功率归零；构网相位/电压指令与公共母线一致；母线初值与相角注释对齐。

## Architecture Decisions
- 1% 地板：回代与 Coupler 一律用真实母线电压；`V≈0` 时 `FromLineVoltageAndPower` 已得 0 电流。
- 断路器分闸：本步 Step 负荷侧电压；无源为 0，有岛压则保留。主断分闸补 `StepMainBreakerIsolated`。单元断开时二次侧写 0，不用 `ReferToRated` 伪造额定电压。
- 负荷：设定值不变；端口电压≤1V 时测量 P/Q 为 0。
- 构网参考：注入源频率算术平均、相位圆周平均、电压指令算术平均。
- 母线构造：`LineVoltageV=0`、`FrequencyHz=0`。相角注释与 `atan2(Q,P)` / Q>0 容性对齐，不改公式。
- 功率汇总在母线尚未定压时用额定电压还原意图 P/Q，避免初值 0 把第一拍潮流算没。

## Task List
- [x] Task 1: 去掉 1% 电压地板
- [x] Task 2: 断路器分闸清二次侧残留
- [x] Task 3: 负荷失压测量功率归零
- [x] Task 4: 构网相位/电压指令公共参考
- [x] Task 5: 母线初值与相角注释
