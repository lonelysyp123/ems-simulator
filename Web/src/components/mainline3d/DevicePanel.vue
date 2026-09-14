<template>
  <div
    class="dt-device-box"
    :class="boxClass"
    @pointerdown.stop
    @wheel.stop
    @dblclick.stop
  >
    <div class="box-head">
      <div class="box-title">
        <template v-if="type === 'pcs' && channel">{{ sideLabel }} · PCS{{ channel.pcsNumber }}</template>
        <template v-else-if="type === 'bms' && channel">舱{{ channel.compartmentNumber }}</template>
        <template v-else-if="type === 'pv' && pvUnit">光伏单元 {{ pvUnit.pvNumber }}</template>
        <template v-else-if="type === 'pv-array' && pvUnit">方阵{{ side }} · PV{{ pvUnit.pvNumber }}</template>
      </div>
      <button type="button" class="box-close" title="关闭" @click.stop="emit('close')">×</button>
    </div>
    <template v-if="type === 'pcs' && channel">
      <div v-for="(t, i) in pcsLinesCompact" :key="'p' + i" class="box-line">{{ t }}</div>
      <div class="box-controls">
        <div class="power-row">
          <label class="power-label">P</label>
          <input v-model="pDraft" type="text" inputmode="decimal" class="power-input" @keydown.enter.prevent="applyP" />
          <button type="button" class="act-btn act-set" @click="applyP">设</button>
        </div>
        <div class="power-row">
          <label class="power-label">Q</label>
          <input v-model="qDraft" type="text" inputmode="decimal" class="power-input" @keydown.enter.prevent="applyQ" />
          <button type="button" class="act-btn act-set" @click="applyQ">设</button>
        </div>
        <div class="box-actions">
          <button type="button" class="act-btn act-on" @click="emit('pcs-start', channel.pcsNumber)">启</button>
          <button type="button" class="act-btn act-off" @click="emit('pcs-stop', channel.pcsNumber)">停</button>
        </div>
      </div>
    </template>
    <template v-else-if="type === 'bms' && channel">
      <div v-for="(t, i) in bmsLinesCompact" :key="'b' + i" class="box-line">{{ t }}</div>
      <div class="box-actions">
        <button type="button" class="act-btn act-on" @click="emit('bms-power-on', channel.compartmentNumber)">上电</button>
        <button type="button" class="act-btn act-off" @click="emit('bms-power-off', channel.compartmentNumber)">下电</button>
      </div>
      <div class="box-actions">
        <button type="button" class="act-btn act-clear" @click="emit('bms-fault-clear', channel.compartmentNumber)">清故障</button>
      </div>
    </template>
    <template v-else-if="type === 'pv' && pvUnit">
      <div v-for="(t, i) in pvLinesCompact" :key="'v' + i" class="box-line">{{ t }}</div>
      <div class="box-controls">
        <div class="power-row">
          <label class="power-label">P</label>
          <input v-model="pDraft" type="text" inputmode="decimal" class="power-input" @keydown.enter.prevent="applyPvP" />
          <button type="button" class="act-btn act-set" @click="applyPvP">设</button>
        </div>
        <div class="power-row">
          <label class="power-label">Q</label>
          <input v-model="qDraft" type="text" inputmode="decimal" class="power-input" @keydown.enter.prevent="applyPvQ" />
          <button type="button" class="act-btn act-set" @click="applyPvQ">设</button>
        </div>
        <div class="box-actions">
          <button type="button" class="act-btn act-on" @click="emit('pv-start', pvUnit.pvNumber)">启</button>
          <button type="button" class="act-btn act-off" @click="emit('pv-stop', pvUnit.pvNumber)">停</button>
        </div>
      </div>
    </template>
    <template v-else-if="type === 'pv-array' && pvArray">
      <div v-for="(t, i) in pvArrayLines" :key="'a' + i" class="box-line">{{ t }}</div>
      <div class="box-controls">
        <div class="power-row">
          <label class="power-label">℃</label>
          <input v-model="tempDraft" type="text" inputmode="decimal" class="power-input" @keydown.enter.prevent="applyTemp" />
          <button type="button" class="act-btn act-set" @click="applyTemp">设</button>
        </div>
        <div class="power-row">
          <label class="power-label">°</label>
          <input v-model="angleDraft" type="text" inputmode="decimal" class="power-input" @keydown.enter.prevent="applyAngle" />
          <button type="button" class="act-btn act-set" @click="applyAngle">设</button>
        </div>
        <div class="power-row">
          <label class="power-label">α</label>
          <input v-model="albedoDraft" type="text" inputmode="decimal" class="power-input" @keydown.enter.prevent="applyAlbedo" />
          <button type="button" class="act-btn act-set" @click="applyAlbedo">设</button>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'

const props = defineProps({
  type: { type: String, required: true },
  side: { type: String, default: 'A' },
  channel: { type: Object, default: null },
  pvUnit: { type: Object, default: null },
  pvArray: { type: Object, default: null }
})

const emit = defineEmits([
  'close',
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

const pDraft = ref('0.0')
const qDraft = ref('0.0')
const tempDraft = ref('25.0')
const angleDraft = ref('90.0')
const albedoDraft = ref('0')
let lastP = null
let lastQ = null
let lastTemp = null
let lastAngle = null
let lastAlbedo = null

const sideLabel = computed(() => (props.side === 'A' ? 'PCS-A' : 'PCS-B'))
const boxClass = computed(() => {
  if (props.type === 'pcs') return 'pcs-box'
  if (props.type === 'pv' || props.type === 'pv-array') return 'pv-box'
  return 'bms-box'
})

const pcsLinesCompact = computed(() => {
  const ch = props.channel
  if (!ch) return []
  return [
    ch.pcsDeviceState,
    ch.pcsActualP || ch.pcsTargetP,
    ch.pcsActualQ || ch.pcsTargetQ
  ].filter(Boolean)
})

const bmsLinesCompact = computed(() => {
  const ch = props.channel
  if (!ch) return []
  return [
    ch.bmsCompact,
    ch.bmsRunStatus || null,
    ch.bmsAirConditioner || null,
    `并网:${ch.gridConnect}`
  ].filter(Boolean)
})

const pvLinesCompact = computed(() => {
  const u = props.pvUnit
  if (!u) return []
  return [
    u.running ? '运行' : '停机',
    `P ${(Number(u.activePowerKw) || 0).toFixed(1)} kW`,
    `并网 ${u.gridConnectedDeviceCount ?? 0} 台`
  ]
})

const pvArrayLines = computed(() => {
  const a = props.pvArray
  if (!a) return []
  return [
    `P ${(Number(a.activePowerKw) || 0).toFixed(1)} kW`,
    `${(Number(a.planeOfArrayWm2) || 0).toFixed(0)} W/㎡ · 组件 ${(Number(a.cellTemperatureC) || 0).toFixed(1)}℃`,
    `${(Number(a.ambientTemperatureC) || 0).toFixed(1)}℃ / ${(Number(a.incidenceAngleDeg) || 0).toFixed(0)}°`
  ]
})

watch(
  () => props.channel,
  (ch) => {
    if (!ch || props.type !== 'pcs') return
    const tp = ch.targetActivePowerKw
    const tq = ch.targetReactivePowerKvar
    if (tp != null && tp !== lastP) {
      pDraft.value = String(Number(tp).toFixed(1))
      lastP = tp
    }
    if (tq != null && tq !== lastQ) {
      qDraft.value = String(Number(tq).toFixed(1))
      lastQ = tq
    }
  },
  { immediate: true, deep: true }
)

watch(
  () => props.pvUnit,
  (u) => {
    if (!u || props.type !== 'pv') return
    const tp = u.targetActivePowerKw
    const tq = u.targetReactivePowerKvar
    if (tp != null && tp !== lastP) {
      pDraft.value = String(Number(tp).toFixed(1))
      lastP = tp
    }
    if (tq != null && tq !== lastQ) {
      qDraft.value = String(Number(tq).toFixed(1))
      lastQ = tq
    }
  },
  { immediate: true, deep: true }
)

watch(
  () => props.pvArray,
  (a) => {
    if (!a || props.type !== 'pv-array') return
    const t = a.ambientTemperatureC
    const ang = a.incidenceAngleDeg
    const alb = a.albedo
    if (t != null && t !== lastTemp) {
      tempDraft.value = String(Number(t).toFixed(1))
      lastTemp = t
    }
    if (ang != null && ang !== lastAngle) {
      angleDraft.value = String(Number(ang).toFixed(0))
      lastAngle = ang
    }
    if (alb != null && alb !== lastAlbedo) {
      albedoDraft.value = String(Number(alb).toFixed(2))
      lastAlbedo = alb
    }
  },
  { immediate: true, deep: true }
)

function applyP() {
  const ch = props.channel
  if (!ch) return
  const kw = Number(pDraft.value)
  if (!Number.isFinite(kw)) return
  const pcsNumber = Number(ch.pcsNumber)
  const emuUnit = Number(ch.emuUnitNumber) > 0
    ? Number(ch.emuUnitNumber)
    : pcsNumber
  const ytPoint = ch.activePowerYtPoint || 'yt0'
  emit('pcs-set-power', {
    pcsNumber,
    emuUnit,
    ytPoint,
    powerKw: kw
  })
}

function applyQ() {
  const ch = props.channel
  if (!ch) return
  const kvar = Number(qDraft.value)
  if (!Number.isFinite(kvar)) return
  const pcsNumber = Number(ch.pcsNumber)
  const emuUnit = Number(ch.emuUnitNumber) > 0
    ? Number(ch.emuUnitNumber)
    : pcsNumber
  const ytPoint = ch.reactivePowerYtPoint || 'yt1'
  emit('pcs-set-reactive', {
    pcsNumber,
    emuUnit,
    ytPoint,
    reactiveKvar: kvar
  })
}

function applyPvP() {
  const u = props.pvUnit
  if (!u) return
  const kw = Number(pDraft.value)
  if (!Number.isFinite(kw)) return
  emit('pv-set-power', { pvNumber: u.pvNumber, powerKw: kw })
}

function applyPvQ() {
  const u = props.pvUnit
  if (!u) return
  const kvar = Number(qDraft.value)
  if (!Number.isFinite(kvar)) return
  emit('pv-set-reactive', { pvNumber: u.pvNumber, reactiveKvar: kvar })
}

function applyTemp() {
  const u = props.pvUnit
  if (!u) return
  const temperatureC = Number(tempDraft.value)
  if (!Number.isFinite(temperatureC)) return
  emit('pv-set-temp', { pvNumber: u.pvNumber, side: props.side, temperatureC })
}

function applyAngle() {
  const u = props.pvUnit
  if (!u) return
  const angleDeg = Number(angleDraft.value)
  if (!Number.isFinite(angleDeg)) return
  emit('pv-set-angle', { pvNumber: u.pvNumber, side: props.side, angleDeg })
}

function applyAlbedo() {
  const u = props.pvUnit
  if (!u) return
  const albedo = Number(albedoDraft.value)
  if (!Number.isFinite(albedo)) return
  emit('pv-set-albedo', { pvNumber: u.pvNumber, side: props.side, albedo })
}
</script>

<style scoped>
.dt-device-box {
  font-size: 9px;
  line-height: 1.25;
  padding: 4px 5px 5px;
  border-radius: 3px;
  box-sizing: border-box;
  width: 112px;
  pointer-events: auto;
  user-select: none;
  box-shadow: 0 3px 10px rgba(0, 0, 0, 0.22);
  opacity: 0.55;
  transition: opacity 0.15s ease;
}
.dt-device-box:hover {
  opacity: 0.92;
}
.dt-device-box.pcs-box {
  background: rgba(238, 245, 255, 0.55);
  border: 1px solid rgba(30, 106, 188, 0.65);
  color: #303133;
}
.dt-device-box.bms-box {
  background: rgba(255, 247, 230, 0.55);
  border: 1px solid rgba(230, 162, 60, 0.65);
  color: #303133;
}
.dt-device-box.pv-box {
  background: rgba(240, 248, 236, 0.58);
  border: 1px solid rgba(103, 168, 60, 0.7);
  color: #303133;
}
.box-head {
  display: flex;
  align-items: flex-start;
  gap: 2px;
  margin-bottom: 2px;
}
.box-title {
  flex: 1;
  font-weight: 700;
  font-size: 9px;
  color: #1e3a5f;
  line-height: 1.2;
}
.box-close {
  flex: 0 0 auto;
  width: 14px;
  height: 14px;
  padding: 0;
  border: none;
  border-radius: 2px;
  background: transparent;
  color: #909399;
  font-size: 12px;
  line-height: 1;
  cursor: pointer;
}
.box-close:hover {
  color: #f56c6c;
  background: rgba(245, 108, 108, 0.12);
}
.box-line {
  margin-bottom: 1px;
  word-break: break-word;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.box-actions {
  display: flex;
  gap: 3px;
  margin-top: 3px;
}
.box-controls {
  margin-top: 2px;
}
.act-btn {
  flex: 1;
  font-size: 9px;
  line-height: 1.15;
  padding: 1px 0;
  border-radius: 2px;
  border: 1px solid #c0c4cc;
  background: #fff;
  cursor: pointer;
  color: #303133;
}
.act-btn:hover {
  border-color: #409eff;
  color: #409eff;
}
.act-btn.act-on:hover {
  border-color: #67c23a;
  color: #67c23a;
}
.act-btn.act-off:hover {
  border-color: #e6a23c;
  color: #e6a23c;
}
.act-btn.act-clear:hover {
  border-color: #f56c6c;
  color: #f56c6c;
}
.act-btn.act-set {
  flex: 0 0 auto;
  min-width: 22px;
  padding: 1px 3px;
}
.power-row {
  display: flex;
  align-items: center;
  gap: 2px;
  margin-top: 2px;
}
.power-label {
  font-size: 8px;
  color: #606266;
  width: 10px;
}
.power-input {
  flex: 1;
  min-width: 0;
  font-size: 9px;
  padding: 1px 2px;
  border: 1px solid #c0c4cc;
  border-radius: 2px;
  box-sizing: border-box;
}
</style>
