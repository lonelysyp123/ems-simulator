<template>
  <div xmlns="http://www.w3.org/1999/xhtml" class="svg-device-box svg-station-box">
    <div class="box-title">
      SVG
      <span v-if="ratedLabel" class="box-title-meta"> {{ ratedLabel }}</span>
    </div>
    <div class="box-line">实时 Q {{ fmtQ(snap.svgReactivePowerKvar) }}</div>
    <div class="box-line">上限 {{ fmtQ(snap.svgAvailableReactiveUpperKvar) }}</div>
    <div class="box-line">状态 {{ stateLabel }}</div>
    <div class="box-controls">
      <div class="power-row">
        <label class="power-label">Q设(kvar)</label>
        <input
          ref="inputRef"
          v-model="draft"
          class="power-input"
          type="text"
          inputmode="decimal"
          @keydown.enter="apply"
        />
        <button type="button" class="act-btn act-set" @click.stop="apply">设定</button>
      </div>
      <div class="box-actions">
        <button type="button" class="act-btn act-on" @click.stop="emit('svg-run', true)">启动</button>
        <button type="button" class="act-btn act-off" @click.stop="emit('svg-run', false)">停机</button>
      </div>
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

const stateLabel = computed(() => {
  switch (Number(props.snap?.svgRunState)) {
    case 1: return '运行'
    case 2: return '闭锁'
    default: return '停机'
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
  const kvar = Number(draft.value)
  if (!Number.isFinite(kvar)) return
  emit('svg-set-reactive', kvar)
}
</script>
