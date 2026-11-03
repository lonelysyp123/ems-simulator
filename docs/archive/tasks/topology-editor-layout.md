# Implementation Plan: 组态编辑器布局方案 A

## Overview

组态页 `TopologyView` 左侧 220px palette 把「基础模板」「EMU 储能单元」「设备库」竖着叠在一起：模板和设备库是拖入源，EMU 列表是工程导航（虚拟节点不画在画布上）。结果是画布被挤、EMU 绑定信息被截断、拖与点混在同一套卡片上。

方案 A 只改布局与导航，**不改组态数据、拖放协议、向导、保存校验**。左栏变成「只添加」的 Tab 调色板，右栏变成「属性 / 储能单元 / 校验」检查器，EMU 实例改成树。方案 B（顶栏图元条）和方案 C（底部单元抽屉）明确不做。

## Architecture Decisions

- **调色板与工程树分离。** 左栏只放可拖入源（模板、设备库）。EMU / EMU 分组**模板**仍在「储能」分类里；EMU **实例**只出现在右侧树。
- **状态仍留在 `TopologyView`。** 工程、选择、拖放、历史、保存仍由该页持有。抽出无状态组件：`PalettePanel`、`EmuTree`。属性表单不拆文件，避免把 200 行表单改成 prop 钻透。
- **纯函数先落地。** 模板按分类分组、搜索过滤、EMU 树与绑定 ID 做成 `paletteGrouping.js` / `emuTree.js`，用现有 `node --test` + `.test.mjs` 钉住（与 `batchEdit.test.mjs` 相同）。没有 Vue 组件测试框架，UI 用浏览器点选验收。
- **拖放契约不动。** `application/x-topo`、`kind: template | library`、双击添加、标准拓扑向导、工具栏都不改。
- **检查器三个 Tab。** `属性` | `储能单元` | `校验`。校验有问题时 Tab 显示数字角标；保存失败自动切到校验。不再在属性上方永远占一块 Alert。
- **选中联动。** 点树节点 → 选中该虚拟节点并切到「属性」（绑定详情已有）。点画布设备 → 「属性」。点校验条目 → 仍走现有 `focusProblem`。空选中不强制切 Tab。
- **窄屏不藏属性。** 现状 `<1100px` 把 `.props { display: none }` 等于残了编辑。改为先收左栏，属性改为覆盖抽屉。
- **分类顺序写死。** `电源 → 母线 → 开关 → 变电 → 测量 → 负荷 → 储能 → 光伏`；未知分类附在末尾。

## 现状对照

| 区块 | 现在 | 方案 A |
|---|---|---|
| 基础模板 | 左栏长列表，12 项占满一屏 | 左 Tab「模板」，按分类两列图标格 |
| 设备库 | 左栏最底部，常被 EMU 挤出视口 | 左 Tab「设备库」 |
| EMU 实例 | 左栏多行卡片（PCS/断路器/电表/变） | 右 Tab「储能单元」树 |
| 属性 | 右栏，未选中时一句空提示 | 右 Tab「属性」，表单逻辑不变 |
| 校验 | 属性上方常驻 Alert | 右 Tab「校验」+ 角标 |

## Task List

### Phase 1: Foundation
- [ ] Task 1: 模板分类分组与搜索纯函数
- [ ] Task 2: EMU 树模型与绑定 ID 纯函数

### Checkpoint: Foundation
- [ ] `node --test Web/src/components/topology/paletteGrouping.test.mjs Web/src/components/topology/emuTree.test.mjs`
- [ ] 无 UI 行为变化

### Phase 2: Core layout
- [ ] Task 3: 右侧检查器 Tab，EMU 实例移出左栏
- [ ] Task 4: 左侧 PalettePanel（模板格 + 设备库 Tab + 搜索）
- [ ] Task 5: 储能单元树替换多行卡片

### Checkpoint: Core layout
- [ ] 左栏不再出现 EMU 实例列表
- [ ] 模板按分类可拖、设备库可拖，向导与保存仍可用
- [ ] 右侧能选 EMU/分组并打开已有属性/绑定表单
- [ ] 与人工确认后再做联动与收起

### Phase 3: Polish
- [ ] Task 6: 检查器 Tab 自动切换、空态、校验角标
- [ ] Task 7: 选中 EMU/分组时画布高亮已绑定设备
- [ ] Task 8: 左栏可收成图标轨；窄屏属性改抽屉

### Checkpoint: Complete
- [ ] 方案 A 验收条件全部满足
- [ ] 浏览器走完：拖模板、拖组合图元、选单元、改属性、保存校验
- [ ] Ready for review

## Task 1: 模板分类分组与搜索

**Description:** 新增 `paletteGrouping.js`：按 `category` 分组、按固定顺序排列、按名称/分类过滤。给左侧模板格用，本任务不改 Vue。

**Acceptance criteria:**
- [ ] 12 个内置模板按「电源 / 母线 / 开关 / 变电 / 测量 / 负荷 / 储能 / 光伏」分组；`ac_bus` 与 `dc_bus` 同属母线；`emu`、`emu_group`、`pcs`、`bms` 同属储能
- [ ] 搜索「pcs」只命中 PCS；空串返回全部分组
- [ ] 未知 category 排在已知顺序之后，不丢项

**Verification:**
- [ ] Tests pass: `node --test Web/src/components/topology/paletteGrouping.test.mjs`
- [ ] Build succeeds: 本任务无 C# 改动，不强制 `dotnet build`

**Dependencies:** None

**Files likely touched:**
- `Web/src/components/topology/paletteGrouping.js`
- `Web/src/components/topology/paletteGrouping.test.mjs`

**Estimated scope:** Small: 1-2 files

## Task 2: EMU 树模型

**Description:** 新增 `emuTree.js`：从 `project.nodes` 抽出 EMU → 分组 → 单元级/组级绑定设备。同时给出「当前选中 EMU 或分组应对画布高亮的 nodeId 列表」。把 `TopologyView` 里 `pcsCountOfEmu` / `groupsOfEmu` / `boundDeviceLabel` / `devicesOfEmu` 的展示逻辑迁过来，页面稍后改为调用这些函数（本任务可以先加模块、暂不改 Vue，避免和 Task 3 抢同一段模板）。

**Acceptance criteria:**
- [ ] 一个 EMU + 两个分组 + 若干 PCS/断路器/电表/变，树节点种类与计数正确
- [ ] 单元级断路器优先于仅组级绑定，与现有 `boundDeviceLabel` 语义一致
- [ ] 选中 EMU 时高亮 ID 含该单元全部可归属设备；选中分组时只含该组设备
- [ ] 无 EMU 时返回空数组

**Verification:**
- [ ] Tests pass: `node --test Web/src/components/topology/emuTree.test.mjs`
- [ ] Build succeeds: 本任务无 C# 改动，不强制 `dotnet build`

**Dependencies:** None（可与 Task 1 并行）

**Files likely touched:**
- `Web/src/components/topology/emuTree.js`
- `Web/src/components/topology/emuTree.test.mjs`

**Estimated scope:** Small: 1-2 files

## Task 3: 右侧检查器 Tab，移走左栏 EMU 实例

**Description:** 右栏改为 `el-tabs`：`属性` / `储能单元` / `校验`。把现有 EMU 卡片列表（含删按钮、分组行、绑定摘要）原样搬到「储能单元」Tab。左栏只剩基础模板 + 设备库两段（仍是现在的列表，下一任务再改成格）。校验 Alert 先仍可留在校验 Tab 内。属性表单 DOM 不改逻辑。

**Acceptance criteria:**
- [ ] 左栏不再渲染 `emuNodes` 实例列表
- [ ] 右侧「储能单元」能选中 EMU/分组、删除 EMU，行为与现在左栏一致
- [ ] 选中 PCS 等画布节点时，「属性」Tab 仍能改参数、设所属 EMU
- [ ] 工具栏保存 / 向导 / 拖模板不受影响

**Verification:**
- [ ] Tests pass: `node --test Web/src/components/topology/batchEdit.test.mjs`（回归）
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`（确认未误改后端）
- [ ] Manual check: 打开组态页，左栏无 EMU 卡片；右侧能点单元；拖一个 PCS 到画布仍可绑定

**Dependencies:** None（不依赖 Task 1/2 的新 API，可先搬现有卡片）

**Files likely touched:**
- `Web/src/views/TopologyView.vue`

**Estimated scope:** Small: 1-2 files

## Task 4: 左侧 PalettePanel

**Description:** 抽出 `PalettePanel.vue`。两个 Tab：`模板`、`设备库`。模板用 Task 1 的分组做成两列图标格（色点 + 短名），分组可折叠，默认全开。顶部搜索框同时过滤两 Tab。设备库条目保持拖、双击、删除。拖放仍由父组件 `onDragTemplate` / `onDragLibrary` 处理。

**Acceptance criteria:**
- [ ] 模板 Tab 按分类分组，EMU 模板在「储能」分类，不与实例树混淆
- [ ] 搜索能筛到「三相断路器」和设备库名称
- [ ] 从格/列表拖到画布、双击添加，节点仍按现有 `addFromTemplate` / `addFromLibrary` 落下
- [ ] 空设备库仍显示「框选已连线的一组设备…」提示

**Verification:**
- [ ] Tests pass: `node --test Web/src/components/topology/paletteGrouping.test.mjs`
- [ ] Build succeeds: 前端 `npm --prefix Web run build`（若过重则至少 `dotnet build ./EssSimulator.csproj`）
- [ ] Manual check: 模板格拖 PCS、设备库拖组合图元；搜索「母线」只剩交流/直流母线

**Dependencies:** Task 1, Task 3

**Files likely touched:**
- `Web/src/components/topology/PalettePanel.vue`
- `Web/src/views/TopologyView.vue`

**Estimated scope:** Medium: 3-5 files

## Task 5: 储能单元树

**Description:** 抽出 `EmuTree.vue`，用 Task 2 的树模型替换检查器里的多行卡片。一行一个节点：EMU / 分组 / 绑定角色（断路器、电表、变压器用子行，未绑定显示「—」）。PCS 计数放在 EMU/分组行右侧。EMU 行保留删除；分组删除继续走工具栏「删除选中」（与现在一致，不新开口）。点击绑定设备行调用现有 `focusProblem` / `onSelectNode` 聚焦画布节点。

**Acceptance criteria:**
- [ ] 树展示与 `emuTree.js` 一致：分组缩进、PCS 计数、未绑定为「—」
- [ ] 点 EMU/分组会选中对应虚拟节点；点已绑定设备会选中该画布节点
- [ ] 删除 EMU 仍解除 PCS 归属并提示，与现 `deleteEmu` 相同
- [ ] 无 EMU 时显示「从模板 Tab 拖入 EMU 储能单元」

**Verification:**
- [ ] Tests pass: `node --test Web/src/components/topology/emuTree.test.mjs`
- [ ] Manual check: 用向导生成 2 个 EMU，树能展开；点断路器行画布选中该断路器；删一个 EMU 后树与属性同步

**Dependencies:** Task 2, Task 3

**Files likely touched:**
- `Web/src/components/topology/EmuTree.vue`
- `Web/src/views/TopologyView.vue`

**Estimated scope:** Medium: 3-5 files

## Task 6: 检查器 Tab 联动与校验角标

**Description:** 给三个 Tab 加上切换规则和空态。保存校验失败时切到「校验」并显示角标数字。点校验条目仍 `focusProblem`。属性 Tab 在未选中时提示「从画布选择设备，或打开储能单元」。不要在空选中时强行改用户当前 Tab。

**Acceptance criteria:**
- [ ] 点树中的 EMU/分组后检查器切到「属性」，绑定表单可见
- [ ] 点画布设备后停在「属性」
- [ ] `validationIssues.length > 0` 时「校验」Tab 有数字；为 0 时无角标
- [ ] `saveProject` 校验失败会打开「校验」Tab
- [ ] 属性上方不再常驻 Alert（内容只在校验 Tab）

**Verification:**
- [ ] Manual check: 选单元 → 属性；故意保存不合法工程 → 校验 Tab 出现问题列表并可点击定位
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 3, Task 5

**Files likely touched:**
- `Web/src/views/TopologyView.vue`

**Estimated scope:** Small: 1-2 files

## Task 7: 画布高亮绑定设备

**Description:** `TopologyCanvas` 增加 `highlightNodeIds`（与 `problemNodeIds` 并列，样式用蓝色描边，避免和校验红框、选中橙框抢语义）。`TopologyView` 用 `emuTree` 的绑定 ID：当前选中为 EMU 或分组时传入。虚拟节点本身不在画布上，只高亮实物设备。

**Acceptance criteria:**
- [ ] 选中 EMU 时，其 PCS / 已绑定断路器 / 电表 / 变出现高亮，未绑定的不出现幽灵框
- [ ] 选中分组时只高亮该组设备
- [ ] 选中普通画布节点时不高亮其它设备（仅保留原 selected / problem）
- [ ] problem 红框优先级高于 highlight

**Verification:**
- [ ] Manual check: 向导工程里点右侧 EMU，画布对应馈线设备描边；再点 PCS，高亮消失、该 PCS 为选中态
- [ ] Tests pass: `node --test Web/src/components/topology/emuTree.test.mjs`

**Dependencies:** Task 2, Task 5

**Files likely touched:**
- `Web/src/components/topology/TopologyCanvas.vue`
- `Web/src/views/TopologyView.vue`

**Estimated scope:** Small: 1-2 files

## Task 8: 左栏收起与窄屏

**Description:** PalettePanel 支持收成约 48px 图标轨（模板 / 设备库两个入口，点击展开对应 Tab）。`<1100px` 不再 `display:none` 右栏：左栏默认收起，属性改为覆盖在画布上的抽屉（宽约 280px），可用 Tab 或关闭按钮收回。

**Acceptance criteria:**
- [ ] 宽屏可手动收起/展开左栏，展开后仍能拖模板
- [ ] 窄屏仍能打开属性并修改名称，不再出现「只有画布没有属性」
- [ ] 收起状态不破坏画布 pan/zoom 与连线

**Verification:**
- [ ] Manual check: 桌面宽度收起左栏画布变宽；把窗口缩到约 1000px 仍能选设备改属性
- [ ] Build succeeds: `npm --prefix Web run build`

**Dependencies:** Task 4, Task 6

**Files likely touched:**
- `Web/src/components/topology/PalettePanel.vue`
- `Web/src/views/TopologyView.vue`

**Estimated scope:** Medium: 3-5 files

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| `TopologyView.vue` 已超 1300 行，继续堆模板会难审 | Med | 只抽 PalettePanel / EmuTree；属性表单留在页内 |
| 搬 EMU 列表时漏删按钮或漏选中 | High | Task 3 先原样搬卡片，Task 5 再换成树 |
| 校验改 Tab 后用户看不见保存失败原因 | High | 失败自动切校验 Tab + 角标；保留 `ElMessageBox.alert` |
| 高亮、选中、problem 三种描边打架 | Med | 约定优先级 problem > selected > highlight，分色 |
| 窄屏抽屉挡住画布操作 | Low | 抽屉可关；默认只在选中或主动打开时出现 |
| 误改拖放 MIME / 向导 / 后端拓扑 API | High | 计划禁止动 `TopologyEndpoints`、`TopologyTemplates`（除非只读 category） |

## Open Questions

- 无。分类顺序、Tab 名称、高亮颜色（蓝）、窄屏策略已在 Architecture Decisions 锁定。
- 方案 B / C 不做；若 Task 8 之后画布仍嫌窄，另开计划。

## Parallelization

- **可并行：** Task 1 与 Task 2；Task 4 与 Task 5（Task 3 完成之后）。
- **必须串行：** Task 3 → 4/5 → 6 → 8；Task 7 依赖树选中（Task 5）。
- **不要并行改：** `TopologyView.vue` 同一时段只交给一个任务。
