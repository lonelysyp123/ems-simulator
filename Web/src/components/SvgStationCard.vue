<template>
  <div xmlns="http://www.w3.org/1999/xhtml" class="svg-comp-box">
    <div class="box-title">SVG <span class="rated">{{ ratedLabel }}</span></div>
    <div class="box-line" :class="stateClass">{{ stateLabel }}</div>
    <div class="box-line">Q {{ fmtQ(snap.svgReactivePowerKvar) }}</div>
    <div class="box-line">上限 {{ fmtQ(snap.svgAvailableReactiveUpperKvar) }}</div>
    <div class="box-actions">
      <button type="button" class="act-btn act-on" :class="{ 'is-active': running }" @click.stop="emit('svg-run', true)">开机</button>
      <button type="button" class="act-btn act-off" :class="{ 'is-active': !running }" @click.stop="emit('svg-run', false)">关机</button>
    </div>
    <div class="power-row">
      <span class="power-label">Q设</span>
      <input
        ref="inputRef"
        v-model="draft"
        class="power-input"
        type="text"
        inputmode="decimal"
        placeholder="kvar"
        @keydown.enter="apply"
      />
      <button type="button" class="act-btn act-set" @click.stop="apply">设定</button>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'

const props = defineProps({
  snap: { type: Object, required: true }
})
const emit = defineEmits(['svg-run', 'svg-set-reactive'])

const draft = ref('')
const inputRef = ref(null)

watch(() => props.snap?.svgReactiveSetpointKvar, (v) => {
  if (inputRef.value && document.activeElement === inputRef.value) return
  const n = Number(v ?? 0)
  draft.value = Number.isFinite(n) ? n.toFixed(1) : ''
}, { immediate: true })

const running = computed(() => Number(props.snap?.svgRunCommand) === 1)
const stateLabel = computed(() => {
  switch (Number(props.snap?.svgRunState)) {
    case 1: return '运行'
    case 2: return '闭锁'
    default: return '停机'
  }
})
const stateClass = computed(() => {
  switch (Number(props.snap?.svgRunState)) {
    case 1: return 'state-run'
    case 2: return 'state-block'
    default: return 'state-off'
  }
})
const ratedLabel = computed(() => {
  const n = Number(props.snap?.svgRatedCapacityKvar)
  if (!Number.isFinite(n) || n <= 0) return ''
  return Math.abs(n) >= 1000 ? `${(n / 1000).toFixed(1)} Mvar` : `${n.toFixed(0)} kvar`
})

function fmtQ(v) {
  const n = Number(v)
  return Number.isFinite(n) ? `${n.toFixed(1)} kvar` : '—'
}

function apply() {
  emit('svg-set-reactive', Number(draft.value))
}
</script>

<style scoped>
.svg-comp-box {
  box-sizing: border-box;
  height: 100%;
  padding: 6px 8px;
  font-size: 11px;
  line-height: 1.25;
  background: #f3fbf6;
  border: 1px solid #3d9a6a;
  border-radius: 4px;
  color: #303133;
  display: flex;
  flex-direction: column;
}
.box-title { font-weight: 700; margin-bottom: 2px; color: #1e3a5f; }
.rated { font-weight: 500; color: #606266; }
.box-line { margin-bottom: 1px; }
.state-run { color: #3d9a6a; font-weight: 700; }
.state-block { color: #e6a23c; font-weight: 700; }
.state-off { color: #909399; }
.box-actions { display: flex; gap: 4px; margin-top: 4px; }
.power-row { display: flex; align-items: center; gap: 3px; margin-top: 4px; }
.power-label { font-size: 10px; color: #606266; white-space: nowrap; }
.power-input {
  flex: 1;
  min-width: 0;
  height: 18px;
  padding: 0 4px;
  border: 1px solid #dcdfe6;
  border-radius: 3px;
  font-size: 11px;
}
.act-btn {
  height: 18px;
  padding: 0 6px;
  border: 1px solid #dcdfe6;
  border-radius: 3px;
  background: #fff;
  font-size: 11px;
  cursor: pointer;
}
.act-btn.act-set { min-width: 32px; }
.act-btn.is-active.act-on { border-color: #3d9a6a; color: #3d9a6a; }
.act-btn.is-active.act-off { border-color: #909399; color: #606266; }
</style>
