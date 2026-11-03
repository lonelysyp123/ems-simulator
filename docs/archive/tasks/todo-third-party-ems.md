# 第三方 EMS 面板

## 已确认
- [x] 写 simLc 系统点（syst1010 / syst1011），自动打开远程使能
- [x] 独立弹窗，完整 SYSTEM 口径 EMS（启停、P/Q、遥测）
- [x] 不覆盖 `tasks/plan.md` / `tasks/todo.md`（LC 拼装）

## Phase 1: Modbus 主站
- [x] Task 1: LC 系统点 Modbus TCP 主站客户端 + 编解码单测

## Checkpoint: Foundation
- [x] 对本地从站写 1010、读 125 的单测通过
- [x] `dotnet build ./EssSimulator.csproj` 通过

## Phase 2: 会话、API、弹窗
- [x] Task 2: ThirdPartyEmsSession + /api/third-party-ems
- [x] Task 3: 独立路由/弹窗 EMS 页，可下发 P/Q

## Checkpoint: Core Features
- [x] 从仿真器弹出新窗口，一键连接本机 simLc
- [x] 下发走 LC SYSTEM 点（syst4/5/1010/1011），不写 EMU
- [x] 页面能读回设定值与实发 P/Q

## Phase 3: 完整 SYSTEM 面板
- [x] Task 4: 远程使能、syst6 启停、SOC/状态/故障遥测
- [x] Task 5: 多单元卡片、连接态与端口覆盖、全站 P/Q 合计

## Checkpoint: Complete
- [x] 每单元一张卡片：连接、启停、P/Q、SOC/状态/实发
- [x] 无 LC 时有明确中文错误，不误连 EMU
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ThirdPartyEms"`
- [x] `dotnet build ./EssSimulator.csproj`
