# Implementation Plan: 第三方 EMS 面板

## Overview

独立弹窗第三方 EMS：**直连 emuN.Emu 数据模型** 下发目标 P/Q 与启停。占用控制权期间，`ExternalControlGate` 拒绝 Modbus 控制管道、LC 回写、dpc、内部直控等外部指令。

不覆盖 `tasks/plan.md` / `tasks/todo.md`（LC 拼装）。

## Task List

### Phase 1: Modbus 主站
- [x] Task 1: LC 系统点 Modbus 主站客户端

### Phase 2: 会话、API、弹窗
- [x] Task 2: 第三方 EMS 会话 + HTTP API
- [x] Task 3: 独立 EMS 页 + 仿真器打开按钮

### Phase 3: 完整 SYSTEM 面板
- [x] Task 4: 远程使能、系统启停、状态遥测
- [x] Task 5: 多单元卡片与连接态打磨
