<template>
  <div class="mainline-zoom-root">
    <div
      ref="fsRootRef"
      class="mainline-fs-root"
      :class="{ 'is-fullscreen': isFullscreen }"
    >
      <div class="mainline-toolbar">
        <el-button-group size="small">
          <el-button @click="onFit" title="适配全景">适配</el-button>
          <el-button @click="onPreset('top')" title="俯视">俯视</el-button>
          <el-button @click="onPreset('side')" title="侧视">侧视</el-button>
          <el-button @click="onPreset('iso')" title="复位透视">复位</el-button>
        </el-button-group>
        <el-button
          v-if="viewMode === 'device'"
          size="small"
          type="warning"
          @click="onBackToStation"
          title="返回全站 (Esc)"
        >
          返回全站
        </el-button>
        <el-button
          size="small"
          :type="isFullscreen ? 'warning' : 'primary'"
          plain
          @click="toggleFullscreen"
          :title="isFullscreen ? '退出全屏 (Esc)' : '全屏显示'"
        >
          {{ isFullscreen ? '退出全屏' : '全屏' }}
        </el-button>
        <span class="toolbar-hint">{{ toolbarHint }}</span>
      </div>

      <div ref="viewportRef" class="mainline-viewport mainline-viewport-3d" @contextmenu.prevent />
    </div>

    <div v-if="blackStartChips.length" class="mainline-blackstart">
      <span class="footer-label">黑启动</span>
      <span
        v-for="chip in blackStartChips"
        :key="chip.pcs"
        class="bs-chip"
        :class="chipStatusClass(chip.status)"
      >PCS{{ chip.pcs }} {{ chip.status }}</span>
    </div>
    <div class="mainline-legend">
      <span><i class="legend-swatch legend-closed" />合闸通电</span>
      <span><i class="legend-swatch legend-open" />分闸/跳闸</span>
      <span><i class="legend-swatch legend-discharge" />放电流向电网 / 光伏送电</span>
      <span><i class="legend-swatch legend-charge" />充电流向电池</span>
      <span><i class="legend-swatch legend-idle" />待机通电</span>
      <span>数据实时推送</span>
    </div>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { SceneController } from './mainline3d/SceneController.js'

const props = defineProps({
  snap: { type: Object, required: true }
})

const emit = defineEmits([
  'toggle-main-breaker',
  'toggle-unit-breaker',
  'pcs-start',
  'pcs-stop',
  'pcs-set-power',
  'pcs-set-reactive',
  'bms-power-on',
  'bms-power-off',
  'bms-fault-clear',
  'pv-start',
  'pv-stop',
  'pv-set-power',
  'pv-set-reactive',
  'pv-set-temp',
  'pv-set-angle',
  'pv-set-albedo'
])

const viewportRef = ref(null)
const fsRootRef = ref(null)
const isFullscreen = ref(false)
const viewMode = ref('station')
const detailKey = ref(null)
/** @type {import('./mainline3d/SceneController.js').SceneController | null} */
let controller = null

const toolbarHint = computed(() => {
  if (viewMode.value === 'device') {
    const key = detailKey.value || ''
    const kind = key.startsWith('bms-') ? 'BMS 舱' : 'PCS 柜'
    return `设备详情（${kind}）· 左键旋转 · 滚轮缩放 · Esc / 返回全站`
  }
  return '左键旋转 · 滚轮缩放 · 右键平移 · 单击设备打开面板 · 双击 BMS 进入详情'
})

const blackStartChips = computed(() => {
  const summary = props.snap.blackStartSummary
  if (!summary) return []
  return summary.split(/\s{2,}/).filter(Boolean).map(part => {
    const m = part.match(/^PCS(\d+):(.+)$/)
    return m ? { pcs: m[1], status: m[2] } : { pcs: part, status: '' }
  })
})

function chipStatusClass(status) {
  if (!status || status === '关') return 'bs-off'
  if (status.includes('运行') || status === '开' || status === '运') return 'bs-on'
  return 'bs-partial'
}

function onEvent(name, payload) {
  // 显式分发，避免动态 emit 在部分场景丢载荷
  switch (name) {
    case 'toggle-main-breaker':
      emit('toggle-main-breaker')
      break
    case 'toggle-unit-breaker':
      emit('toggle-unit-breaker', payload)
      break
    case 'pcs-start':
      emit('pcs-start', payload)
      break
    case 'pcs-stop':
      emit('pcs-stop', payload)
      break
    case 'pcs-set-power':
      emit('pcs-set-power', payload || {})
      break
    case 'pcs-set-reactive':
      emit('pcs-set-reactive', payload || {})
      break
    case 'bms-power-on':
      emit('bms-power-on', payload)
      break
    case 'bms-power-off':
      emit('bms-power-off', payload)
      break
    case 'bms-fault-clear':
      emit('bms-fault-clear', payload)
      break
    case 'pv-start':
      emit('pv-start', payload)
      break
    case 'pv-stop':
      emit('pv-stop', payload)
      break
    case 'pv-set-power':
      emit('pv-set-power', payload || {})
      break
    case 'pv-set-reactive':
      emit('pv-set-reactive', payload || {})
      break
    case 'pv-set-temp':
      emit('pv-set-temp', payload || {})
      break
    case 'pv-set-angle':
      emit('pv-set-angle', payload || {})
      break
    case 'pv-set-albedo':
      emit('pv-set-albedo', payload || {})
      break
    case 'view-mode':
      viewMode.value = payload?.mode || 'station'
      detailKey.value = payload?.detailKey || null
      break
    default:
      break
  }
}

function onFit() {
  controller?.fitAll()
}
function onPreset(p) {
  if (p === 'iso') controller?.fitAll()
  else controller?.setViewPreset(p)
}
function onBackToStation() {
  controller?.exitDeviceDetail()
}

function onKeyDown(e) {
  if (e.key !== 'Escape') return
  if (viewMode.value === 'device') {
    e.preventDefault()
    controller?.exitDeviceDetail()
  }
}

function getFullscreenElement() {
  return document.fullscreenElement
    || document.webkitFullscreenElement
    || null
}

function syncFullscreenState() {
  const el = fsRootRef.value
  isFullscreen.value = !!(el && getFullscreenElement() === el)
  // 全屏尺寸变化后刷新 WebGL / CSS2D
  requestAnimationFrame(() => controller?.resize())
}

async function toggleFullscreen() {
  const el = fsRootRef.value
  if (!el) return
  try {
    if (getFullscreenElement() === el) {
      if (document.exitFullscreen) await document.exitFullscreen()
      else if (document.webkitExitFullscreen) document.webkitExitFullscreen()
    } else if (el.requestFullscreen) {
      await el.requestFullscreen()
    } else if (el.webkitRequestFullscreen) {
      el.webkitRequestFullscreen()
    }
  } catch (e) {
    console.warn('fullscreen failed', e)
  }
}

onMounted(() => {
  if (!viewportRef.value) return
  controller = new SceneController(viewportRef.value, { onEvent })
  controller.updateFromSnap(props.snap)
  document.addEventListener('fullscreenchange', syncFullscreenState)
  document.addEventListener('webkitfullscreenchange', syncFullscreenState)
  window.addEventListener('keydown', onKeyDown)
})

watch(
  () => props.snap,
  (s) => controller?.updateFromSnap(s),
  { deep: true }
)

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeyDown)
  document.removeEventListener('fullscreenchange', syncFullscreenState)
  document.removeEventListener('webkitfullscreenchange', syncFullscreenState)
  if (getFullscreenElement() === fsRootRef.value) {
    try {
      if (document.exitFullscreen) document.exitFullscreen()
      else if (document.webkitExitFullscreen) document.webkitExitFullscreen()
    } catch { /* ignore */ }
  }
  controller?.dispose()
  controller = null
})
</script>

<style scoped>
.mainline-zoom-root {
  width: 100%;
}
.mainline-fs-root {
  width: 100%;
}
.mainline-fs-root.is-fullscreen {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  background: #8a9aab;
  padding: 10px 12px;
  box-sizing: border-box;
}
.mainline-fs-root.is-fullscreen .mainline-toolbar {
  margin-bottom: 10px;
  flex-shrink: 0;
}
.mainline-fs-root.is-fullscreen .toolbar-hint {
  color: #b8c0cc;
}
.mainline-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 8px;
  padding: 4px 0;
}
.toolbar-hint {
  font-size: 12px;
  color: #909399;
  margin-left: auto;
}
.mainline-viewport-3d {
  overflow: hidden;
  width: 100%;
  height: min(78vh, 820px);
  border: 1px solid #6a7f70;
  border-radius: 6px;
  background: #8a9aab;
  position: relative;
  touch-action: none;
}
.mainline-blackstart {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 8px;
  margin-top: 8px;
  padding: 8px 10px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fafbfc;
}
.footer-label {
  font-size: 12px;
  font-weight: 600;
  color: #606266;
  margin-right: 2px;
}
.bs-chip {
  font-size: 11px;
  line-height: 1.2;
  padding: 2px 8px;
  border-radius: 10px;
  border: 1px solid #dcdfe6;
  background: #fff;
  color: #606266;
  white-space: nowrap;
}
.bs-chip.bs-on {
  border-color: #b3e19d;
  background: #f0f9eb;
  color: #529b2e;
}
.bs-chip.bs-partial {
  border-color: #f3d19e;
  background: #fdf6ec;
  color: #b88230;
}
.bs-chip.bs-off {
  border-color: #e4e7ed;
  background: #f4f4f5;
  color: #909399;
}
.mainline-legend {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 14px;
  margin-top: 6px;
  font-size: 11px;
  color: #909399;
}
.legend-swatch {
  display: inline-block;
  width: 14px;
  height: 8px;
  margin-right: 4px;
  vertical-align: middle;
  border-radius: 2px;
}
.legend-closed {
  background: #67c23a;
}
.legend-open {
  background: #f56c6c;
}
.legend-discharge {
  background: #fbbf24;
}
.legend-charge {
  background: #38bdf8;
}
.legend-idle {
  background: #67c23a;
}
</style>

<style>
.dt-float-label {
  color: #f2f6fb;
  font-size: 11px;
  line-height: 1.35;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.75);
  white-space: nowrap;
  /* 标签不拦截鼠标，缩放/旋转/点选直接穿透到画布 */
  pointer-events: none;
  opacity: 0.45;
  transition: opacity 0.18s ease;
  padding: 2px 6px;
  border-radius: 4px;
  background: rgba(20, 28, 38, 0.35);
}
.dt-float-label.dt-unit-title {
  font-size: 13px;
  font-weight: 700;
  color: #b8d4ff;
}
.mainline3d-labels .dt-panel-host {
  transform: translate(-50%, -100%);
}
.dt-cluster-info {
  min-width: 168px;
  max-width: 220px;
  padding: 6px 8px;
  border-radius: 4px;
  background: rgba(16, 24, 36, 0.82);
  border: 1px solid rgba(125, 211, 252, 0.45);
  color: #e8eef6;
  font-size: 11px;
  line-height: 1.4;
  pointer-events: none;
  white-space: nowrap;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.28);
  transform: translate(-50%, -110%);
}
.dt-cluster-info .ci-title {
  font-weight: 700;
  font-size: 12px;
  color: #b8d4ff;
  margin-bottom: 2px;
}
.dt-cluster-info .ci-hot {
  color: #fca5a5;
  margin-top: 2px;
}
.dt-cluster-info .ci-cold {
  color: #86efac;
}
</style>
