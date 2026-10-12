<template>
  <aside class="palette card" :class="{ collapsed }">
    <div v-if="collapsed" class="rail">
      <button type="button" class="rail-btn" title="模板" @click="expand('templates')">模</button>
      <button type="button" class="rail-btn" title="设备库" @click="expand('library')">库</button>
    </div>
    <template v-else>
      <div class="palette-head">
        <el-input
          v-model="query"
          size="small"
          clearable
          placeholder="搜索模板 / 设备库"
        />
        <el-button link type="info" size="small" @click="emit('update:collapsed', true)">收起</el-button>
      </div>
      <el-tabs v-model="tab" class="palette-tabs">
        <el-tab-pane label="模板" name="templates">
          <div v-if="!templateGroups.length" class="empty">无匹配模板</div>
          <section v-for="g in templateGroups" :key="g.category" class="cat">
            <button type="button" class="cat-title" @click="toggleCat(g.category)">
              {{ collapsedCats[g.category] ? '▸' : '▾' }} {{ g.category }}
            </button>
            <div v-show="!collapsedCats[g.category]" class="grid">
              <div
                v-for="t in g.items"
                :key="t.id"
                class="tile"
                draggable="true"
                :title="t.name"
                @dragstart="emit('drag-template', $event, t)"
                @dblclick="emit('add-template', t)"
              >
                <span class="dot" :style="{ background: colorOf(t.id) }" />
                <span class="tile-name">{{ t.name }}</span>
              </div>
            </div>
          </section>
        </el-tab-pane>
        <el-tab-pane label="设备库" name="library">
          <div v-if="!filteredLibrary.length" class="empty">
            {{ query.trim() ? '无匹配条目' : '框选已连线的一组设备，存为组合图元后可整组拖入' }}
          </div>
          <div
            v-for="item in filteredLibrary"
            :key="item.id"
            class="palette-item"
            draggable="true"
            @dragstart="emit('drag-library', $event, item)"
            @dblclick="emit('add-library', item)"
          >
            <span class="dot" :style="{ background: colorOf(item.templateId) }" />
            <div class="meta">
              <div class="name">{{ item.name }}</div>
              <div class="desc">{{ libraryItemDesc(item) }}</div>
            </div>
            <el-button link type="danger" size="small" @click.stop="emit('remove-library', item.id)">删</el-button>
          </div>
        </el-tab-pane>
      </el-tabs>
    </template>
  </aside>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { templateColor } from './nodeLayout.js'
import { isCompositeLibraryItem } from './batchEdit.js'
import { filterLibraryItems, groupTemplatesByCategory } from './paletteGrouping.js'

const props = defineProps({
  templates: { type: Array, default: () => [] },
  library: { type: Array, default: () => [] },
  collapsed: { type: Boolean, default: false }
})

const emit = defineEmits([
  'update:collapsed',
  'drag-template',
  'drag-library',
  'add-template',
  'add-library',
  'remove-library'
])

const query = ref('')
const tab = ref('templates')
const collapsedCats = ref({})

const templateGroups = computed(() => groupTemplatesByCategory(props.templates, query.value))
const filteredLibrary = computed(() => filterLibraryItems(props.library, query.value))

watch(query, q => {
  if (q.trim() && tab.value === 'templates' && !templateGroups.value.length && filteredLibrary.value.length)
    tab.value = 'library'
})

function expand(name) {
  tab.value = name
  emit('update:collapsed', false)
}

function toggleCat(category) {
  collapsedCats.value = { ...collapsedCats.value, [category]: !collapsedCats.value[category] }
}

function colorOf(id) {
  return templateColor(id)
}

function libraryItemDesc(item) {
  if (isCompositeLibraryItem(item))
    return `组合 · ${(item.nodes || []).length} 设备`
  return props.templates.find(t => t.id === item.templateId)?.name || item.templateId
}
</script>

<style scoped>
.palette {
  margin-bottom: 0;
  min-width: 0;
  min-height: 0;
  height: 100%;
  overflow: auto;
  display: flex;
  flex-direction: column;
  padding: 10px;
}
.palette.collapsed {
  overflow: hidden;
  padding: 8px 4px;
}
.rail {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}
.rail-btn {
  width: 36px;
  height: 36px;
  border: 1px solid #dcdfe6;
  border-radius: 6px;
  background: #fafbfc;
  color: #303133;
  cursor: pointer;
  font-size: 12px;
  font-weight: 600;
}
.rail-btn:hover { border-color: #409eff; color: #409eff; }
.palette-head {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-bottom: 6px;
  flex-shrink: 0;
}
.palette-tabs { flex: 1; min-height: 0; display: flex; flex-direction: column; }
.palette-tabs :deep(.el-tabs__header) { margin: 0 0 8px; }
.palette-tabs :deep(.el-tabs__item) { padding: 0 12px; font-size: 13px; }
.palette-tabs :deep(.el-tabs__content) { overflow: auto; flex: 1; }
.cat { margin-bottom: 8px; }
.cat-title {
  display: block;
  width: 100%;
  border: 0;
  background: transparent;
  text-align: left;
  font-size: 12px;
  font-weight: 600;
  color: #606266;
  padding: 2px 0 6px;
  cursor: pointer;
}
.grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px;
}
.tile {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fafbfc;
  cursor: grab;
  min-width: 0;
}
.tile:hover { border-color: #c0c4cc; background: #fff; }
.tile-name {
  font-size: 12px;
  font-weight: 600;
  color: #303133;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
.palette-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  margin-bottom: 6px;
  cursor: grab;
  background: #fafbfc;
}
.palette-item:hover { border-color: #c0c4cc; background: #fff; }
.palette-item .meta { flex: 1; min-width: 0; }
.palette-item .name { font-size: 13px; font-weight: 600; color: #303133; }
.palette-item .desc { font-size: 11px; color: #909399; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.empty { font-size: 12px; color: #909399; line-height: 1.5; }
</style>
