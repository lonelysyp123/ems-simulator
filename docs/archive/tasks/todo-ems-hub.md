# EMS 入口合并

> 已完成。原 `tasks/todo.md` 归档于此。

## 已确认
- [x] 合入口、不合引擎：策略 API 与第三方 API 保持独立
- [x] `ExternalControlGate` 两个 Owner 继续互斥
- [x] 侧栏只留一个 EMS；第三方弹窗降为「新窗口打开」
- [x] `Exclusive` 仅表示第三方本源占用，并暴露 `gateOwner`

## Phase 1: 入口合一
- [x] Task 1: `EmsHubView` + 子路由 `/ems/strategy`、`/ems/third-party`；侧栏单一入口；`/ems-strategy` 重定向；去掉侧栏第三方按钮

## Checkpoint: 入口
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EmsStrategy|FullyQualifiedName~ThirdPartyEms"`
- [x] `dotnet build ./EssSimulator.csproj`
- [ ] 侧栏只有一个 EMS；两 Tab 可用；弹窗仍可用（需浏览器点选确认）
- [x] 与人工确认后再做占用体验

## Phase 2: 占用体验
- [x] Task 2: dashboard `GateOwner` + `Exclusive` 仅 `ThirdPartyEms`
- [x] Task 3: 对方占用时禁用本页操作；枢纽顶栏显示占用源（可选一键释放）

## Checkpoint: 占用
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~ExternalControlGateOwnerTests|FullyQualifiedName~ThirdPartyEmsSession"`
- [ ] 策略占用 ≠ 第三方页显示「第三方已占用」（需浏览器点选确认）
- [ ] 反向互斥在界面上可操作、可理解（需浏览器点选确认）

## Phase 3: 收口
- [x] Task 4: 互斥文案；主壳不改 `document.title`；弹窗标题保持「第三方 EMS」

## Checkpoint: Complete
- [x] 侧栏无重复 EMS 入口（代码已改）
- [x] 两模式互斥且提示正确（单测 + 控件禁用）
- [x] 弹窗仍可作为外部 HMI
- [x] Ready for review
