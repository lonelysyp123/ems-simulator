# 300MW 纯储能测试工程

> 已确认：独立工程 `mw300-ess-test`。拓扑和端口写死在代码里，启动不读画布 JSON 和 `protocol-ports.json`。不能通过组态或端口页修改。现有工程逻辑保持原样。
> 站用 SVG 计划已归档：`tasks/svg-constant-q.md` / `tasks/todo-svg-constant-q.md`。

## 已确认
- [x] 60 单元 × 2 台 2500 kW PCS = 300 MW；每 20 台为一段，只用于命名
- [x] 电气模型仍是一台 220/35 kV 主变；不新做多主变网络
- [x] 单元 `n` 使用 TCP `10000 + n`；LC 从站 1，两台 PCS 为 2 和 3，两台 BMS 堆为 10 与 42（簇各占其后 12 个号）
- [x] 全站电表 1500、SEL 2001、SVG 2201；IEC 61850 不启动
- [x] 电池固定簇等效；这份工程不参与按单元数的裁剪
- [x] 主接线布局不在本计划内

## Phase 1: 写死定义
- [x] Task 1: 300MW 运行时拓扑常量
- [x] Task 2: 300MW 端口计划常量（可与 Task 1 并行）

## Checkpoint: 常量可单测
- [x] 60 单元、120 PCS、无光伏，分 3 段命名
- [x] 端口 10001–10060，同端口从站不重叠
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Mw300EssStation"`
- [x] `dotnet build ./EssSimulator.csproj`

## Phase 2: 启动切割
- [x] Task 3: 按工程 Id 加载代码拓扑（依赖 Task 1）
- [x] Task 4: 代码端口并关闭 IEC 61850（依赖 Task 2、Task 3）
- [x] Task 5: 锁定簇等效（依赖 Task 3）

## Checkpoint: 启动路径分开
- [x] 改过的 overlay JSON 和 `protocol-ports.json` 对这份工程无效
- [x] 其它工程仍走 JSON 和覆盖文件
- [x] `MaxEssUnits = 2` 时这份工程仍是 60 单元
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Mw300|FullyQualifiedName~TopologyOverlayLoader|FullyQualifiedName~ProtocolPortPlan"`

## Phase 3: 禁止人为修改
- [x] Task 6: 应用入口与工程列表（依赖 Task 1、Task 3）
- [x] Task 7: 拒绝端口覆盖写入（依赖 Task 4）

## Checkpoint: 写入被挡住
- [x] 应用不产生可编辑的 `projects/{id}.json`
- [x] 保存、打开、复制、删除该 Id 失败
- [x] 端口 PUT / 重置不写覆盖文件

## Phase 4: 只读界面
- [x] Task 8: 工程库只读行（依赖 Task 6）
- [x] Task 9: 端口页与组态页锁定（依赖 Task 6、Task 7）

## Checkpoint: Complete
- [x] 工程库可应用、不可编辑
- [x] 端口页不可改号，组态画布打不开这份工程
- [x] 普通工程的组态和端口页仍可编辑
- [x] `dotnet build ./EssSimulator.csproj`
