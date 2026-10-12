---
kind: frontend_style
name: 基于 Element Plus + Vite 的 Vue3 前端样式体系
category: frontend_style
scope:
    - '**'
source_files:
    - Web/package.json
    - Web/vite.config.js
    - Web/src/main.js
    - Web/src/App.vue
    - Web/src/styles/app.css
    - Web/src/views/MainLineView.vue
---

## 1. 使用的系统与方法

- **框架与构建**：Vue 3 (`vue@^3.4`) + Vue Router，使用 Vite (`vite@^5`) 作为开发/构建工具，输出目录为 `wwwroot`，由 ASP.NET Core 宿主直接提供静态文件。
- **UI 组件库**：Element Plus (`element-plus@^2.7`)，并在 `Web/src/main.js` 中全局注册并设置中文语言包 `zhCn`；图标来自 `@element-plus/icons-vue`，全部通过循环注册到应用实例。
- **样式方案**：无 CSS 预处理器、无 Tailwind / UnoCSS 等原子化框架。样式以 **单文件 `<style scoped>`（组件级）** 配合 **全局 `src/styles/app.css`** 的方式组织，采用传统 BEM 风格类名（如 `.app-layout`、`.app-header`、`.app-aside`、`.app-main`、`.card`、`.metric-grid` 等）。
- **图表与可视化**：ECharts (`echarts@^5.5`) 用于数据图表；Three.js (`three@^0.170`) 用于 3D 主接线场景；SVG 主接线图通过自研组件 `MainLineSvg.vue`、`TopologyMainLineSvg.vue` 及 `mainline3d/*` 下的 JS 模块实现。
- **实时通信**：`@microsoft/signalr` 通过 `/hub` 反向代理连接后端 SignalR Hub，用于推送告警、主接线快照等实时数据。

## 2. 关键文件

- `Web/package.json`：声明所有前端依赖与脚本（`dev`/`build`/`preview`）。
- `Web/vite.config.js`：定义 `@` 路径别名、开发服务器端口 `5173`、对 `/api` 和 `/hub` 的反向代理，以及构建产物输出到根目录 `wwwroot`。
- `Web/src/main.js`：创建 Vue 应用、注册 Element Plus（含中文）、注册全部 Element Plus 图标、挂载路由并引入全局样式 `./styles/app.css`。
- `Web/src/App.vue`：应用根布局（header + aside 侧边栏 + main 内容区），使用 Element Plus 的 `el-menu`、`el-tag`、`el-progress` 等组件。
- `Web/src/styles/app.css`：全局基础样式——字体栈（系统字体 + PingFang SC/Microsoft YaHei）、背景色 `#f5f7fa`、头部渐变 `#1e3a5f → #2d5a8c`、侧边栏菜单高亮 `#ecf5ff` + 主题蓝 `#1e6abc`、卡片/指标网格/日志视图/电池单体网格/SVG 主接线等通用样式。
- `Web/src/views/MainLineView.vue`：典型页面示例，大量使用 `<style scoped>` 覆盖局部样式（如 `.metric-item-editable`、`.metric-set`、`.metric-input`、`.metric-set-btn`、`.metric-item-disabled`）。

## 3. 架构与约定

- **样式分层**：
  - 全局层：`src/styles/app.css` 统一定义页面骨架（`.app-layout`、`.app-header`、`.app-aside`、`.app-main`）、通用容器（`.card`、`.metric-grid`、`.metric-item`）、状态标签（`.tag-online`/`.tag-offline`）、日志视图、电池单体网格、以及 SVG 主接线图的共享样式（`.mainline-svg .bus-line`、`.breaker-closed`、`.breaker-open` 等）。这些类名被多个 view 复用。
  - 组件/页面层：每个 `.vue` 文件通过 `<style scoped>` 编写仅作用于当前模板的样式，避免污染全局命名空间。
- **设计令牌**：未使用 CSS 变量或 design token 文件，颜色值以硬编码形式出现在 `app.css` 中，核心调色板包括：
  - 品牌蓝：`#1e6abc`（活跃态、数值强调）
  - 深品牌蓝：`#1e3a5f`（头部渐变起始）
  - 辅助蓝：`#2d5a8c`（头部渐变结束）
  - 成功绿：`#67c23a`
  - 危险红：`#f56c6c`
  - 文字灰：`#303133`（正文）、`#606266`（次要）、`#909399`（占位/禁用）
  - 背景灰：`#f5f7fa`（页面）、`#fafbfc`（指标项）、`#fff`（卡片/侧栏）
- **布局约定**：App 根采用 Flex 纵向布局（`.app-layout { display: flex; flex-direction: column }`），主体区域 `.app-body` 横向 Flex（左侧固定宽度 200px 的 `.app-aside` + 弹性 `.app-main`），子页面通过 `router-view` 渲染。
- **响应式策略**：未引入媒体查询或移动端适配逻辑；主要面向桌面端仪表盘，通过 CSS Grid 的 `repeat(auto-fill, minmax(180px, 1fr))` 在指标网格上实现自适应列数。
- **组件库定制**：通过 Element Plus 的全局配置注入中文语言包，并通过自定义 `.app-aside .el-menu-item` 等选择器覆盖默认菜单样式（高度 44px、圆角 6px、激活态背景 `#ecf5ff` 与主题色 `#1e6abc`）。

## 4. 约定与约束

- **样式来源单一**：所有样式均来源于本地 `src/styles/app.css` 和各组件 `<style scoped>`，不引用任何外部 CSS 框架（除 Element Plus 提供的 `element-plus/dist/index.css`）。
- **类名命名**：遵循 BEM 风格的短横线命名（如 `.system-lock-mask`、`.system-lock-panel`、`.system-lock-spin`、`.system-lock-title`、`.system-lock-stage`、`.system-lock-progress`、`.system-lock-msg`、`.system-lock-sub`），用于系统锁定遮罩弹窗这一独立 UI 片段。
- **主题色集中**：品牌色 `#1e6abc` 与深色 `#1e3a5f` 同时出现在头部渐变、菜单激活态、SVG 数值文本等处，构成统一的视觉基调；未在代码中发现可配置的 theme 开关。
- **构建产物位置**：Vite 构建输出强制写入 `../wwwroot`（见 `vite.config.js` 的 `outDir`），因此前端样式最终随 ASP.NET Core 静态文件发布，不存在运行时动态加载样式的能力。
- **无样式 lint/格式化规则**：仓库中未发现针对 CSS/SCSS 的 ESLint/Prettier 配置文件，样式风格主要由开发者自觉遵循上述约定维持一致。