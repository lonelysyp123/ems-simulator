<template>
  <div class="mainline-zoom-root">
    <div class="mainline-toolbar">
      <el-button-group size="small">
        <el-button :disabled="zoom <= MIN_ZOOM" @click="zoomOut" title="缩小">−</el-button>
        <el-button disabled class="zoom-label">{{ zoomPercent }}%</el-button>
        <el-button :disabled="zoom >= MAX_ZOOM" @click="zoomIn" title="放大">+</el-button>
      </el-button-group>
      <el-slider
        v-model="zoom"
        class="zoom-slider"
        :min="MIN_ZOOM"
        :max="MAX_ZOOM"
        :step="0.1"
        :show-tooltip="true"
        :format-tooltip="v => `${Math.round(v * 100)}%`"
      />
      <el-button size="small" link type="primary" @click="resetZoom">重置 100%</el-button>
      <span class="toolbar-hint">右键平移 · Ctrl/⌘ + 滚轮缩放</span>
    </div>

    <div
      ref="viewportRef"
      class="mainline-viewport"
      :class="{ 'is-panning': isPanning }"
      @wheel="onWheel"
      @contextmenu.prevent
      @mousedown="onPanMouseDown"
    >
      <div
        class="mainline-pan-layer"
        :style="panLayerStyle"
      >
      <svg
        class="mainline-svg"
        :width="renderWidth"
        :height="renderHeight"
        :viewBox="`0 0 ${svgWidth} ${svgHeight}`"
        preserveAspectRatio="xMinYMin meet"
      >
      <!-- 220kV 进线 -->
      <line class="bus-line" :x1="mainX" :y1="20" :x2="mainX" :y2="62" />
      <text :x="mainX + 8" y="28" class="label-text">220kV 电网</text>
      <text :x="mainX + 8" y="42" class="value-text">
        PCC {{ fmtVolt(snap.pccLineVoltageV) }} / {{ fmtHz(snap.systemFrequencyHz) }}
      </text>
      <text :x="mainX + 8" y="56" class="value-text muted-text">
        设定 {{ fmtVolt(snap.gridNominalLineVoltageV) }} / {{ fmtHz(snap.gridNominalFrequencyHz) }}
      </text>

      <!-- 主断路器（可点击） -->
      <line
        :class="lineClass(snap.mainBreakerClosed, snap.mainBreakerTripped)"
        :x1="mainX" :y1="62" :x2="mainX" :y2="86"
      />
      <g class="breaker-hit" @click="$emit('toggle-main-breaker')">
        <rect :x="mainX - 18" y="68" width="36" height="22" rx="3" fill="transparent" />
        <BreakerSymbol :x="mainX" :y="79" :closed="snap.mainBreakerClosed" :tripped="snap.mainBreakerTripped" />
        <text :x="mainX + 22" y="82" class="label-text breaker-label">主断 {{ snap.mainBreakerLabel || fmtBreaker(snap.mainBreakerClosed, snap.mainBreakerTripped) }}</text>
      </g>

      <!-- 主变 -->
      <line class="bus-line" :x1="mainX" :y1="90" :x2="mainX" :y2="106" />
      <TransformerSymbol :x="mainX" :y="122" />
      <text :x="mainX + 22" y="118" class="label-text">主变 220/35kV</text>
      <text :x="mainX + 22" y="132" class="value-text">{{ fmtVolt(snap.mainTransformerSecondary?.lineVoltageV) }}</text>
      <line class="bus-line" :x1="mainX" :y1="136" :x2="mainX" :y2="busY" />

      <!-- 35kV 母线（从主变接入，贯穿所有单元） -->
      <line class="bus-line bus-thick" :x1="mainX" :y1="busY" :x2="busEndX" :y2="busY" />
      <text :x="mainX + 8" :y="busY - 6" class="label-text">35kV 母线 {{ fmtVolt(snap.stationBus35LineVoltageV) }}</text>

      <g v-if="snap.svgPresent" :transform="`translate(${svgCardX}, ${busY})`">
        <line class="bus-line" x1="0" y1="0" x2="0" y2="24" />
        <foreignObject x="-78" y="24" width="156" height="150">
          <SvgStationCard
            :snap="snap"
            @svg-run="v => $emit('svg-run', v)"
            @svg-set-reactive="v => $emit('svg-set-reactive', v)"
          />
        </foreignObject>
      </g>

      <!-- 各储能单元 -->
      <g v-for="(u, i) in snap.units" :key="u.unitIndex ?? i" :transform="`translate(${unitCenterX(i)}, ${busY})`">
        <!-- 接入母线 -->
        <line
          :class="lineClass(u.unitBreakerClosed, u.unitBreakerTripped)"
          x1="0" y1="0" x2="0" y2="28"
        />

        <!-- 单元标题 -->
        <text x="-50" y="-10" class="unit-title">UNIT {{ u.unitNumber ?? (u.unitIndex + 1) }}</text>

        <!-- 单元断路器（可点击） -->
        <g class="breaker-hit" @click="$emit('toggle-unit-breaker', u.unitIndex ?? i)">
          <rect x="-22" y="10" width="44" height="26" rx="3" fill="transparent" />
          <BreakerSymbol :x="0" :y="23" :closed="u.unitBreakerClosed" :tripped="u.unitBreakerTripped" />
          <text x="26" y="26" class="label-text breaker-label">单元断 {{ u.unitBreakerLabel || fmtBreaker(u.unitBreakerClosed, u.unitBreakerTripped) }}</text>
        </g>

        <line
          :class="lineClass(u.unitBreakerClosed, u.unitBreakerTripped)"
          x1="0" y1="36" x2="0" y2="52"
        />

        <!-- 单元变 -->
        <TransformerSymbol :x="0" :y="68" :scale="0.85" />
        <text x="18" y="64" class="label-text">单元变 35/690</text>
        <text x="18" y="78" class="value-text">{{ u.unitTransformerLine || fmtPhasorVi(u.unitTransformerSecondary) }}</text>

        <line class="bus-line" x1="0" y1="82" x2="0" y2="96" />

        <!-- 690V 母线 -->
        <line class="bus-line" :x1="branchX(u, 0)" y1="96" :x2="branchX(u, Math.max(0, (u.channels || []).length - 1))" y2="96" />
        <text :x="branchX(u, 0) + 3" y="110" class="label-text">690V {{ fmtVolt(u.bus690?.lineVoltageV ?? u.unitTransformerSecondary?.lineVoltageV) }}</text>

        <!-- PCS / 舱：逐槽位支路（对称排布，兼容任意台数） -->
        <ChannelBranch
          v-for="(ch, si) in (u.channels || [])"
          :key="`ch-${u.unitIndex}-${si}`"
          :channel="ch"
          :side="String.fromCharCode(65 + si)"
          :x="branchX(u, si)"
          :bus-y="96"
          @pcs-start="n => $emit('pcs-start', n)"
          @pcs-stop="n => $emit('pcs-stop', n)"
          @pcs-set-power="p => $emit('pcs-set-power', p)"
          @pcs-set-reactive="p => $emit('pcs-set-reactive', p)"
          @bms-power-on="n => $emit('bms-power-on', n)"
          @bms-power-off="n => $emit('bms-power-off', n)"
          @bms-fault-clear="n => $emit('bms-fault-clear', n)"
          @bms-set-soc="p => $emit('bms-set-soc', p)"
        />
      </g>
    </svg>
      </div>
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
      <span><i class="legend-line legend-closed" />合闸</span>
      <span><i class="legend-line legend-open" />分闸/跳闸</span>
      <span>点击断路器切换</span>
      <span>数据实时推送</span>
    </div>
  </div>
</template>

<script setup>
import { computed, defineComponent, h, onBeforeUnmount, reactive, ref } from 'vue'
import SvgStationCard from './SvgStationCard.vue'

const MIN_ZOOM = 1
const MAX_ZOOM = 10
const ZOOM_STEP = 0.12

const props = defineProps({
  snap: { type: Object, required: true }
})
defineEmits([
  'toggle-main-breaker',
  'toggle-unit-breaker',
  'pcs-start',
  'pcs-stop',
  'pcs-set-power',
  'pcs-set-reactive',
  'bms-power-on',
  'bms-power-off',
  'bms-fault-clear',
  'bms-set-soc',
  'svg-run',
  'svg-set-reactive'
])

const zoom = ref(MIN_ZOOM)
const viewportRef = ref(null)
const panX = ref(0)
const panY = ref(0)
const isPanning = ref(false)

let panDragStart = { x: 0, y: 0, panX: 0, panY: 0 }

const zoomPercent = computed(() => Math.round(zoom.value * 100))
const panLayerStyle = computed(() => ({
  transform: `translate(${panX.value}px, ${panY.value}px)`
}))

const unitCount = computed(() => (props.snap.units || []).length)
const svgWidth = computed(() => {
  const total = (props.snap.units || []).reduce((s, u) => s + unitWidth(u), 0)
  const base = Math.max(900, MARGIN_LEFT + total + MARGIN_RIGHT)
  return props.snap.svgPresent ? Math.max(base, svgCardX.value + 100) : base
})
const renderWidth = computed(() => Math.round(svgWidth.value * zoom.value))
const renderHeight = computed(() => Math.round(svgHeight.value * zoom.value))

function clampZoom(v) {
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, +v.toFixed(2)))
}
function zoomIn() { zoom.value = clampZoom(zoom.value + ZOOM_STEP) }
function zoomOut() { zoom.value = clampZoom(zoom.value - ZOOM_STEP) }
function resetZoom() {
  zoom.value = MIN_ZOOM
  panX.value = 0
  panY.value = 0
}

/** 普通滚轮交给页面滚动；按住 Ctrl/⌘ 才缩放接线图 */
function onWheel(e) {
  if (!e.ctrlKey && !e.metaKey) return
  e.preventDefault()
  const delta = e.deltaY > 0 ? -ZOOM_STEP : ZOOM_STEP
  zoom.value = clampZoom(zoom.value + delta)
}

function onPanMouseMove(e) {
  if (!isPanning.value) return
  panX.value = panDragStart.panX + (e.clientX - panDragStart.x)
  panY.value = panDragStart.panY + (e.clientY - panDragStart.y)
}

function stopPanning() {
  if (!isPanning.value) return
  isPanning.value = false
  window.removeEventListener('mousemove', onPanMouseMove)
  window.removeEventListener('mouseup', onPanMouseUp)
}

function onPanMouseUp() {
  stopPanning()
}

function onPanMouseDown(e) {
  // 仅右键拖拽平移，左键留给断路器点击
  if (e.button !== 2) return
  e.preventDefault()
  isPanning.value = true
  panDragStart = { x: e.clientX, y: e.clientY, panX: panX.value, panY: panY.value }
  window.addEventListener('mousemove', onPanMouseMove)
  window.addEventListener('mouseup', onPanMouseUp)
}

onBeforeUnmount(() => {
  stopPanning()
})

const UNIT_WIDTH = 340
const MARGIN_LEFT = 100
const MARGIN_RIGHT = 80
const MAIN_X = 70
const BUS_Y = 195
const BOTTOM_PAD = 12

/** PCS / BMS 支路布局 */
const BRANCH = {
  boxW: 132,
  /** 相邻支路中心距的一半；须满足 2*channelX > boxW + 间距 */
  channelX: 92,
  pcsTop: 24,
  pcsH: 228,
  gap: 28,
  bmsH: 214,
  get bmsTop() { return this.pcsTop + this.pcsH + this.gap },
  get linkMid() { return this.pcsTop + this.pcsH + this.gap / 2 },
  get bottomY() { return 96 + this.bmsTop + this.bmsH }
}

/** 单元第 i 条支路中心相对单元中心的水平偏移（对称排布） */
function branchX(u, i) {
  const n = Math.max(1, (u.channels || []).length)
  return (i - (n - 1) / 2) * 2 * BRANCH.channelX
}

const mainX = MAIN_X
const busY = BUS_Y
const svgHeight = computed(() => busY + BRANCH.bottomY + BOTTOM_PAD)

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

const busEndX = computed(() => {
  const units = props.snap.units || []
  const unitEnd = units.length ? unitCenterX(units.length - 1) : MARGIN_LEFT
  return props.snap.svgPresent ? Math.max(unitEnd, svgCardX.value) : unitEnd
})

const svgCardX = computed(() => {
  const units = props.snap.units || []
  if (!units.length) return MARGIN_LEFT + 160
  const last = units.length - 1
  const u = units[last]
  const n = Math.max(1, (u.channels || []).length)
  return unitCenterX(last) + branchX(u, n - 1) + BRANCH.boxW / 2 + 100
})

/** 单元占宽：随通道台数自适应，不低于原双通道宽 */
function unitWidth(u) {
  const n = Math.max(1, (u?.channels || []).length)
  return Math.max(UNIT_WIDTH, Math.round((n - 1) * 2 * BRANCH.channelX + BRANCH.boxW + 40))
}

function unitCenterX(i) {
  const units = props.snap.units || []
  let x = MARGIN_LEFT
  for (let k = 0; k < i; k++) x += unitWidth(units[k])
  return x + unitWidth(units[i]) / 2
}

function fmtVolt(v) {
  if (v == null) return '—'
  return v >= 1000 ? `${(v / 1000).toFixed(1)} kV` : `${(v || 0).toFixed(1)} V`
}
function fmtHz(v) {
  if (v == null) return '—'
  return `${Number(v).toFixed(2)} Hz`
}
function fmtBreaker(closed, tripped) { return tripped ? '跳闸' : closed ? '合' : '分' }
function fmtPhasorVi(p) {
  if (!p) return '—'
  return `${fmtVolt(p.lineVoltageV)} / ${Number(p.lineCurrentA || 0).toFixed(1)}A / φ${Number(p.phaseAngleDeg || 0).toFixed(1)}°`
}
function lineClass(closed, tripped) {
  if (tripped) return 'breaker-open'
  return closed ? 'breaker-closed' : 'breaker-open'
}

/** 标准变压器双圈符号（上下两圆相交） */
const TransformerSymbol = defineComponent({
  props: { x: Number, y: Number, scale: { type: Number, default: 1 } },
  setup(p) {
    return () => {
      const r = 9 * (p.scale || 1)
      const gap = 7 * (p.scale || 1)
      return h('g', { transform: `translate(${p.x}, ${p.y})` }, [
        h('circle', { cx: 0, cy: -gap, r, fill: 'none', stroke: '#303133', 'stroke-width': 2 }),
        h('circle', { cx: 0, cy: gap, r, fill: 'none', stroke: '#303133', 'stroke-width': 2 })
      ])
    }
  }
})

/** 断路器符号 */
const BreakerSymbol = defineComponent({
  props: { x: Number, y: Number, closed: Boolean, tripped: Boolean },
  setup(p) {
    return () => {
      const color = p.tripped ? '#f56c6c' : p.closed ? '#67c23a' : '#f56c6c'
      const children = [
        h('line', { x1: p.x - 10, y1: p.y, x2: p.x - 2, y2: p.y, stroke: color, 'stroke-width': 2.5 }),
        h('line', { x1: p.x + 2, y1: p.y, x2: p.x + 10, y2: p.y, stroke: color, 'stroke-width': 2.5 })
      ]
      if (!p.closed || p.tripped) {
        children.push(h('line', { x1: p.x - 4, y1: p.y - 5, x2: p.x + 4, y2: p.y + 5, stroke: color, 'stroke-width': 2 }))
      }
      return h('g', children)
    }
  }
})

/** PCS + BMS 支路 */
const powerDrafts = reactive({})

const ChannelBranch = defineComponent({
  props: { channel: Object, side: String, x: Number, busY: Number },
  emits: ['pcs-start', 'pcs-stop', 'pcs-set-power', 'pcs-set-reactive', 'bms-power-on', 'bms-power-off', 'bms-fault-clear', 'bms-set-soc'],
  setup(p, { emit }) {
    const halfW = BRANCH.boxW / 2

    function draftKey(ch, kind) {
      return `${kind}-${ch?.pcsNumber ?? ch?.compartmentNumber ?? ''}`
    }

    function getDraft(ch, kind, fallback) {
      const key = draftKey(ch, kind)
      if (powerDrafts[key] === undefined) {
        powerDrafts[key] = String(Number(fallback ?? 0).toFixed(1))
      }
      return powerDrafts[key]
    }

    function setDraft(ch, kind, value) {
      powerDrafts[draftKey(ch, kind)] = value
    }

    function buildSetRow(ch, label, kind, fallback, onApply) {
      return h('div', { class: 'power-row' }, [
        h('label', { class: 'power-label' }, label),
        h('input', {
          type: 'text',
          inputMode: 'decimal',
          class: 'power-input',
          value: getDraft(ch, kind, fallback),
          onInput: (e) => setDraft(ch, kind, e.target.value),
          onKeydown: (e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              onApply()
            }
          }
        }),
        h('button', {
          type: 'button',
          class: 'act-btn act-set',
          onClick: (e) => { e.stopPropagation(); onApply() }
        }, '设定')
      ])
    }

    return () => {
      const ch = p.channel
      const label = p.side === 'A' ? 'PCS-A' : 'PCS-B'
      const pcsLines = [
        ch.pcsDeviceState,
        ch.pcsStartStop,
        ch.pcsTargetP,
        ch.pcsActualP,
        ch.pcsTargetQ,
        ch.pcsActualQ,
        ch.pcsBlackStart,
        ch.pcsGridMode !== '—' ? `模式:${ch.pcsGridMode}` : null
      ].filter(Boolean)

      const bmsLines = [
        ch.bmsCompact,
        ch.bmsRunStatus || '运行:—',
        ch.bmsAirConditioner,
        ch.bmsEnergy || `累计充 ${(ch.cumulativeChargeEnergyKwh ?? 0).toFixed(1)} / 放 ${(ch.cumulativeDischargeEnergyKwh ?? 0).toFixed(1)} kWh`,
        `并网:${ch.gridConnect}`,
        ch.bmsBlackStart
      ].filter(Boolean)

      const btnRow = (startLabel, stopLabel, onStart, onStop) =>
        h('div', { class: 'box-actions' }, [
          h('button', {
            type: 'button',
            class: 'act-btn act-on',
            onClick: (e) => { e.stopPropagation(); onStart() }
          }, startLabel),
          h('button', {
            type: 'button',
            class: 'act-btn act-off',
            onClick: (e) => { e.stopPropagation(); onStop() }
          }, stopLabel)
        ])

      return h('g', { transform: `translate(${p.x}, ${p.busY})` }, [
        h('line', { class: 'bus-line', x1: 0, y1: 0, x2: 0, y2: BRANCH.pcsTop }),
        h('foreignObject', {
          x: -halfW,
          y: BRANCH.pcsTop,
          width: BRANCH.boxW,
          height: BRANCH.pcsH
        }, [
          h('div', { xmlns: 'http://www.w3.org/1999/xhtml', class: 'svg-device-box pcs-box' }, [
            h('div', { class: 'box-title' }, `${label} (PCS${ch.pcsNumber})`),
            ...pcsLines.map(t => h('div', { class: 'box-line' }, t)),
            h('div', { class: 'box-controls' }, [
              buildSetRow(ch, 'P设(kW)', 'p', ch.targetActivePowerKw, () => {
                const kw = Number(getDraft(ch, 'p', ch.targetActivePowerKw))
                if (!Number.isFinite(kw)) return
                emit('pcs-set-power', {
                  pcsNumber: ch.pcsNumber,
                  emuUnit: ch.emuUnitNumber,
                  ytPoint: ch.activePowerYtPoint,
                  powerKw: kw
                })
              }),
              buildSetRow(ch, 'Q设(kvar)', 'q', ch.targetReactivePowerKvar, () => {
                const kvar = Number(getDraft(ch, 'q', ch.targetReactivePowerKvar))
                if (!Number.isFinite(kvar)) return
                emit('pcs-set-reactive', {
                  pcsNumber: ch.pcsNumber,
                  emuUnit: ch.emuUnitNumber,
                  ytPoint: ch.reactivePowerYtPoint,
                  reactiveKvar: kvar
                })
              }),
              btnRow('启动', '停机',
                () => emit('pcs-start', ch.pcsNumber),
                () => emit('pcs-stop', ch.pcsNumber))
            ])
          ])
        ]),
        h('line', {
          class: 'bus-line',
          x1: 0,
          y1: BRANCH.pcsTop + BRANCH.pcsH,
          x2: 0,
          y2: BRANCH.bmsTop
        }),
        h('foreignObject', {
          x: -halfW,
          y: BRANCH.bmsTop,
          width: BRANCH.boxW,
          height: BRANCH.bmsH
        }, [
          h('div', { xmlns: 'http://www.w3.org/1999/xhtml', class: 'svg-device-box bms-box' }, [
            h('div', { class: 'box-title' }, `BMS 舱${ch.compartmentNumber}`),
            ...bmsLines.map(t => h('div', { class: 'box-line' }, t)),
            h('div', { class: 'box-controls' }, [
              buildSetRow(ch, 'SOC(%)', 'soc', ch.socPercent, () => {
                const pct = Number(getDraft(ch, 'soc', ch.socPercent))
                if (!Number.isFinite(pct)) return
                emit('bms-set-soc', { bmsNumber: ch.compartmentNumber, socPercent: pct })
              }),
              btnRow('上电', '下电',
                () => emit('bms-power-on', ch.compartmentNumber),
                () => emit('bms-power-off', ch.compartmentNumber)),
              h('div', { class: 'box-actions' }, [
                h('button', {
                  type: 'button',
                  class: 'act-btn act-clear',
                  onClick: (e) => { e.stopPropagation(); emit('bms-fault-clear', ch.compartmentNumber) }
                }, '故障清除')
              ])
            ])
          ])
        ])
      ])
    }
  }
})
</script>

<style scoped>
.mainline-zoom-root {
  width: 100%;
}
.mainline-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 8px;
  padding: 4px 0;
}
.zoom-slider {
  width: 140px;
  margin: 0 4px;
}
.zoom-label {
  min-width: 52px;
  font-variant-numeric: tabular-nums;
}
.toolbar-hint {
  font-size: 12px;
  color: #909399;
  margin-left: auto;
}
.mainline-viewport {
  overflow: hidden;
  width: 100%;
  height: min(72vh, 720px);
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fafbfc;
  position: relative;
  touch-action: none;
}
.mainline-viewport.is-panning {
  cursor: grabbing;
  user-select: none;
}
.mainline-pan-layer {
  position: absolute;
  left: 0;
  top: 0;
  will-change: transform;
}
.mainline-svg {
  display: block;
  flex-shrink: 0;
}
.breaker-hit { cursor: pointer; }
.breaker-hit:hover .breaker-label { fill: #1e6abc; font-weight: 600; }
.breaker-label { font-size: 11px; }
.unit-title { font-size: 13px; font-weight: 700; fill: #1e3a5f; }
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
.mainline-legend .legend-line {
  display: inline-block;
  width: 18px;
  height: 0;
  margin-right: 4px;
  vertical-align: middle;
  border-top-width: 2px;
  border-top-style: solid;
}
.mainline-legend .legend-closed {
  border-top-color: #67c23a;
}
.mainline-legend .legend-open {
  border-top-color: #f56c6c;
  border-top-style: dashed;
}
</style>

<style>
/* foreignObject 内 HTML（不可 scoped） */
.svg-device-box {
  font-size: 10px;
  line-height: 1.4;
  padding: 5px 6px;
  border-radius: 4px;
  box-sizing: border-box;
  word-break: break-word;
  height: 100%;
  display: flex;
  flex-direction: column;
}
.svg-device-box.pcs-box {
  background: #eef5ff;
  border: 1px solid #1e6abc;
  color: #303133;
}
.svg-device-box.bms-box {
  background: #fff7e6;
  border: 1px solid #e6a23c;
  color: #303133;
}
.svg-device-box .box-title {
  font-weight: 700;
  margin-bottom: 3px;
  color: #1e3a5f;
}
.svg-device-box .box-line {
  white-space: normal;
  overflow: visible;
  margin-bottom: 1px;
}
.svg-device-box .box-actions {
  display: flex;
  gap: 4px;
  margin-top: 4px;
}
.svg-device-box .box-controls {
  margin-top: auto;
  padding-top: 4px;
}
.svg-device-box .act-btn {
  flex: 1;
  font-size: 10px;
  line-height: 1.2;
  padding: 2px 0;
  border-radius: 3px;
  border: 1px solid #c0c4cc;
  background: #fff;
  cursor: pointer;
  color: #303133;
}
.svg-device-box .act-btn:hover {
  border-color: #409eff;
  color: #409eff;
}
.svg-device-box .act-btn.act-on:hover {
  border-color: #67c23a;
  color: #67c23a;
}
.svg-device-box .act-btn.act-off:hover {
  border-color: #e6a23c;
  color: #e6a23c;
}
.svg-device-box .act-btn.act-clear {
  flex: 1;
}
.svg-device-box .act-btn.act-clear:hover {
  border-color: #f56c6c;
  color: #f56c6c;
}
.svg-device-box .power-row {
  display: flex;
  align-items: center;
  gap: 3px;
  margin-top: 4px;
}
.svg-device-box .power-label {
  font-size: 9px;
  color: #606266;
  white-space: nowrap;
}
.svg-device-box .power-input {
  flex: 1;
  min-width: 0;
  font-size: 10px;
  padding: 1px 3px;
  border: 1px solid #c0c4cc;
  border-radius: 3px;
  box-sizing: border-box;
}
.svg-device-box .act-btn.act-set {
  flex: 0 0 auto;
  min-width: 32px;
  padding: 2px 4px;
}
.svg-device-box .act-btn.act-set:hover {
  border-color: #409eff;
  color: #409eff;
}
</style>
