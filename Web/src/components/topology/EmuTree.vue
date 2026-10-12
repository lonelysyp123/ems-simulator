<template>
  <div class="emu-tree">
    <p v-if="!tree.length" class="empty">从模板 Tab 拖入 EMU 储能单元</p>
    <div
      v-for="emu in tree"
      :key="emu.id"
      class="emu-block"
    >
      <div
        class="row emu-row"
        :class="{ active: selectedIds.includes(emu.id) }"
        @click="emit('select', emu.id)"
      >
        <span class="name">{{ emu.label }}</span>
        <span class="count">支路×{{ emu.pcsCount }}</span>
        <el-button link type="danger" size="small" @click.stop="emit('delete-emu', emu.id)">删</el-button>
      </div>
      <div
        v-for="g in emu.groups"
        :key="g.id"
        class="row indent"
        :class="{ active: selectedIds.includes(g.id) }"
        @click.stop="emit('select', g.id)"
      >
        <span class="name">└ {{ g.label }}</span>
        <span class="count">支路×{{ g.pcsCount }}</span>
      </div>
      <div
        v-for="b in emu.bindings"
        :key="`${emu.id}-${b.templateId}`"
        class="row indent bind"
        :class="{ muted: !b.nodes.length, clickable: b.nodes.length > 0 }"
        @click.stop="onBindClick(b)"
      >
        <span class="role">{{ b.role }}</span>
        <span class="bind-label">{{ b.label || '—' }}</span>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { buildEmuTree } from './emuTree.js'

const props = defineProps({
  nodes: { type: Array, default: () => [] },
  selectedIds: { type: Array, default: () => [] }
})

const emit = defineEmits(['select', 'delete-emu', 'focus-device'])

const tree = computed(() => buildEmuTree(props.nodes))

function onBindClick(b) {
  if (b.nodes[0])
    emit('focus-device', b.nodes[0].id)
}
</script>

<style scoped>
.emu-tree { font-size: 13px; }
.emu-block { margin-bottom: 10px; padding-bottom: 6px; border-bottom: 1px solid #ebeef5; }
.emu-block:last-child { border-bottom: 0; }
.row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 6px;
  border-radius: 4px;
  cursor: pointer;
  min-width: 0;
}
.row:hover { background: #f5f7fa; }
.row.active { background: #ecf5ff; color: #409eff; }
.row.indent { padding-left: 16px; font-size: 12px; }
.row.bind { cursor: default; color: #606266; }
.row.bind.clickable { cursor: pointer; }
.row.muted { color: #c0c4cc; }
.name { flex: 1; min-width: 0; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.bind .name, .bind-label { font-weight: 400; flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.role { width: 42px; flex-shrink: 0; color: #909399; }
.count { font-size: 11px; color: #909399; flex-shrink: 0; }
.row.active .count { color: #79bbff; }
.empty { font-size: 12px; color: #909399; line-height: 1.5; }
</style>
