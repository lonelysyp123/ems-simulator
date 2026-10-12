# Implementation Plan: EMS 入口合并

> 已完成。原 `tasks/plan.md` 归档于此，避免被组态布局方案 A 覆盖。

## Overview

侧栏现在有两处「EMS」：菜单项「EMS 策略」（电站 PPC 算法）和按钮「打开第三方 EMS」（单元级遥控弹窗）。两者都占用 `ExternalControlGate`、都直连仿真模型，但职责不同，后端已互斥。本计划只合并**入口与占用体验**，不把两套引擎合成一个。完成后侧栏只留一个「EMS」，页内用两个模式切换；第三方仍可选择新窗口打开。

上一版 `tasks/plan.md`（EMS 策略算法层）与 `tasks/third-party-ems.md` 均已完成，本文件覆盖为下一期入口工作。算法、`AfterPlantStep`、第三方会话 API 不回退。

## Architecture Decisions

- **合入口、不合引擎。** `/api/ems-strategy` 与 `/api/third-party-ems` 保持独立。策略走 `AfterPlantStep` 分配 PCS；第三方直写 `emuN.Emu`（启停/远程/P/Q）。
- **闸门 Owner 继续二分。** `ExternalControlOwner.EmsStrategy` 与 `ThirdPartyEms` 互斥保留。策略占用时跳过 `EmuPowerDispatcher` 均分，第三方占用时不跳过均分——行为不能混。
- **枢纽页 + 子路由。** 新页 `EmsHubView`：`/ems/strategy`、`/ems/third-party`。侧栏只一项「EMS」。`/ems-strategy` 重定向到 `/ems/strategy`。
- **弹窗降为次要动作。** 保留 `/third-party-ems`（`standalone`）模拟外部 HMI；主壳第三方面板提供「新窗口打开」，去掉侧栏主按钮。
- **Exclusive 语义收紧。** 当前第三方 dashboard 的 `Exclusive = ExternalControlGate.IsBlocked`，策略占用时第三方面会误显示「第三方 EMS 已占用」。改为 `Owner == ThirdPartyEms`，并增加 `gateOwner` 字段。

## 现状对照

| | EMS 策略 | 第三方 EMS |
|---|---|---|
| 入口 | 侧栏菜单 `/ems-strategy` | 侧栏按钮弹窗 `/third-party-ems` |
| 角色 | 站级 PPC：开环/闭环/曲线、调频、惯量、无功/下垂 | 单元遥控：占用、启停、远程、目标 P/Q |
| 下发 | 策略引擎 → 各 PCS 设定 | `emuN.Emu` 数据模型 |
| 闸门 | `EmsStrategy` | `ThirdPartyEms` |
| 互斥 | 已实现：一方占用，另一方 `TryOccupy` 失败 | 同左 |

## Task List

### Phase 1: 入口合一
- [x] Task 1: EMS 枢纽页、单一侧栏入口、旧路径重定向、弹窗改为次要动作

### Checkpoint: 入口
- [x] 侧栏只剩一个「EMS」，无「打开第三方 EMS」主按钮
- [x] 枢纽内能打开策略页与第三方面板
- [x] `/ems-strategy` 进入策略子页
- [x] 「新窗口打开」仍弹出独立第三方页

### Phase 2: 占用体验
- [x] Task 2: 第三方 dashboard 暴露 `gateOwner`，`Exclusive` 仅表示本源占用
- [x] Task 3: 对方占用时禁用本页操作并显示正确原因

### Checkpoint: 占用
- [x] 策略启用后，第三方页提示「EMS 策略占用中」，占用按钮不可点
- [x] 第三方占用后，策略开关不可开，提示「第三方 EMS 占用中」
- [x] 释放后另一方可以占用

### Phase 3: 收口
- [x] Task 4: 文案与 standalone 标题；嵌套打开时不改 `document.title`

### Checkpoint: Complete
- [x] 所有验收条件满足
- [x] 人工确认入口与互斥后再合入

任务明细与验收见当时实现；清单见 `tasks/todo-ems-hub.md`。
