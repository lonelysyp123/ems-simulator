# 电站 EMS 策略参数热更新

> 已确认：改参立刻 `UpdateConfig`，并写回与启动加载同一份 `ems-strategy.json`。不合 tab、不改公式。
> LC 中压方案已归档：`tasks/lc-mv-pointmap.md` / `tasks/todo-lc-mv-pointmap.md`。

## 已确认
- [x] 热更新 = 引擎立刻换参 + 落盘（运行目录，与 Load 同路径）
- [x] PATCH 嵌套覆盖，禁止残缺 body 整表替换
- [x] 参数变更不 Reset PI/ACTION；仅模式切换 Reset
- [x] 快照轮询与表单草稿分离；参数按卡「应用」

## Phase 1: Foundation
- [x] Task 1: 配置落盘（可注入路径；占用失败不写盘）
- [x] Task 2: PATCH 嵌套覆盖全部参数对象 + Patcher 单测

## Checkpoint: Foundation
- [x] 不经 UI 也能 POST 嵌套对象热更新并写 JSON
- [x] 改 Kp 不 Reset 积分（断言钉住）
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EmsStrategy"`
- [ ] 与人工确认后再做页面（已按开工继续做完页面）

## Phase 2: Core Features
- [x] Task 3: 快照/草稿分离 + 斜率 / PID / 视在参数卡
- [x] Task 4: 一次调频 / 惯量 / 下垂明细参数卡
- [x] Task 5: 功率分配、计划曲线、恒压与远程设定

## Checkpoint: Core Features
- [ ] 公共算法与辅助服务均可在页面整定
- [ ] 应用后无需重启；刷新/重启值仍在
- [ ] 空曲线 + 曲线模式仍 WAIT
- [ ] 第三方占用时应用失败有提示

## Phase 3: Polish
- [x] Task 6: 折叠默认、脏标记、SystemSwitch / 有功无功使能

## Checkpoint: Complete
- [x] 斜率、PID、视在、分配、曲线、调频/惯量/下垂均可热更新并落盘（代码已接；需浏览器点选确认）
- [x] 未做：合 tab、改公式、参数进组态、双写仓库 JSON
- [ ] Ready for review
