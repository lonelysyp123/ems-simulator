# 组态编辑器布局方案 A

## 已确认
- [x] 按方案 A：左调色板 Tab + 右检查器 Tab，不做顶栏图元条 / 底部抽屉
- [x] 左栏只放可添加源（模板、设备库）；EMU 实例只在右侧树
- [x] 拖放 MIME、向导、保存校验、拓扑 API 不改
- [x] 属性表单留在 `TopologyView`；抽出 `PalettePanel` / `EmuTree` 与两个纯函数模块

## Phase 1: Foundation
- [x] Task 1: `paletteGrouping.js` 按固定分类分组 + 搜索；`paletteGrouping.test.mjs`
- [x] Task 2: `emuTree.js` 树模型与绑定高亮 ID；`emuTree.test.mjs`

## Checkpoint: Foundation
- [x] `node --test Web/src/components/topology/paletteGrouping.test.mjs Web/src/components/topology/emuTree.test.mjs`
- [x] 代码已接到 Vue（本轮一次做完方案 A，未在 Foundation 停）

## Phase 2: Core layout
- [x] Task 3: 右栏 `属性` / `储能单元` / `校验` Tab；EMU 卡片从左栏搬到「储能单元」
- [x] Task 4: `PalettePanel.vue`：模板分类图标格 + 设备库 Tab + 搜索
- [x] Task 5: `EmuTree.vue` 替换多行卡片；点选/删除行为与现在一致

## Checkpoint: Core layout
- [x] `node --test Web/src/components/topology/*.test.mjs`
- [x] `dotnet build ./EssSimulator.csproj`
- [ ] 左栏无 EMU 实例；模板/设备库可拖；右侧能管理单元（需浏览器点选）

## Phase 3: Polish
- [x] Task 6: 点树切「属性」；保存失败切「校验」并显示角标；去掉属性上方常驻 Alert
- [x] Task 7: 选中 EMU/分组时画布蓝框高亮绑定设备（problem 优先）
- [x] Task 8: 左栏可收成图标轨；`<1100px` 属性改抽屉，不再 `display:none`

## Checkpoint: Complete
- [x] 方案 A 代码与单测、前端构建完成
- [x] `npm --prefix Web run build`
- [ ] 浏览器走完：拖模板、拖组合图元、选单元看绑定、改属性、保存失败看校验
- [ ] Ready for review
