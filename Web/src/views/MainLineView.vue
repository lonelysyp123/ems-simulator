<template>
  <div>
    <div class="card">
      <p class="card-title">电站概览</p>
      <div class="metric-grid">
        <div class="metric-item"><div class="label">PCC 电压</div><div class="value">{{ fmtVolt(snap.pccLineVoltageV) }}</div></div>
        <div class="metric-item"><div class="label">电网频率</div><div class="value">{{ fmtHz(snap.systemFrequencyHz) }}</div></div>
        <div class="metric-item"><div class="label">35kV 母线</div><div class="value">{{ fmtVolt(snap.stationBus35LineVoltageV) }}</div></div>
        <div class="metric-item"><div class="label">主断路器</div><div class="value" :class="snap.mainBreakerTripped ? 'tag-offline' : (snap.mainBreakerClosed ? 'tag-online' : 'tag-offline')">{{ breakerLabel }}</div></div>
        <div class="metric-item"><div class="label">求解模式</div><div class="value">{{ snap.propagationEnabled ? '径向传播' : 'Legacy' }}</div></div>
        <div class="metric-item"><div class="label">电表 P</div><div class="value">{{ fmtKw(snap.meterPrimary?.activePowerKw) }}</div></div>
        <div class="metric-item"><div class="label">电表 Q</div><div class="value">{{ fmtKvar(snap.meterPrimary?.reactivePowerKvar) }}</div></div>
        <div class="metric-item metric-item-editable">
          <div class="label">电网电压设定</div>
          <div class="value">{{ fmtVolt(snap.gridNominalLineVoltageV) }}</div>
          <div class="metric-set">
            <input
              v-model="gridVDraft"
              type="text"
              inputmode="decimal"
              class="metric-input"
              placeholder="kV"
              @keydown.enter="onSetGridVoltage"
            />
            <button type="button" class="metric-set-btn" @click="onSetGridVoltage">设定</button>
          </div>
        </div>
        <div class="metric-item metric-item-editable">
          <div class="label">电网频率设定</div>
          <div class="value">{{ fmtHz(snap.gridNominalFrequencyHz) }}</div>
          <div class="metric-set">
            <input
              v-model="gridFDraft"
              type="text"
              inputmode="decimal"
              class="metric-input"
              placeholder="Hz"
              @keydown.enter="onSetGridFrequency"
            />
            <button type="button" class="metric-set-btn" @click="onSetGridFrequency">设定</button>
          </div>
        </div>
        <div
          class="metric-item metric-item-editable"
          :class="{ 'metric-item-disabled': loadControlsDisabled }"
          :title="loadControlsDisabled ? '工程模式未配置负载，概览负载已禁用' : ''"
        >
          <div class="label">35kV 负载 P（≤0 消耗）</div>
          <div class="value">{{ loadControlsDisabled ? '—' : fmtKw(snap.loadActivePowerKw) }}</div>
          <div class="metric-set">
            <input
              v-model="loadPDraft"
              type="text"
              inputmode="decimal"
              class="metric-input"
              placeholder="kW≤0"
              :disabled="loadControlsDisabled"
              @keydown.enter="onSetLoadActive"
            />
            <button type="button" class="metric-set-btn" :disabled="loadControlsDisabled" @click="onSetLoadActive">设定</button>
          </div>
        </div>
        <div
          class="metric-item metric-item-editable"
          :class="{ 'metric-item-disabled': loadControlsDisabled }"
          :title="loadControlsDisabled ? '工程模式未配置负载，概览负载已禁用' : ''"
        >
          <div class="label">35kV 负载 Q</div>
          <div class="value">{{ loadControlsDisabled ? '—' : fmtKvar(snap.loadReactivePowerKvar) }}</div>
          <div class="metric-set">
            <input
              v-model="loadQDraft"
              type="text"
              inputmode="decimal"
              class="metric-input"
              placeholder="kvar"
              :disabled="loadControlsDisabled"
              @keydown.enter="onSetLoadReactive"
            />
            <button type="button" class="metric-set-btn" :disabled="loadControlsDisabled" @click="onSetLoadReactive">设定</button>
          </div>
        </div>
      </div>
    </div>

    <div class="card">
      <p class="card-title">
        电站概览
        <span class="card-hint">
          {{ useTopologyMainLine
            ? '工程模式：经典单线图 · 右键平移 · Ctrl/⌘+滚轮缩放 · PCS/BMS 可启停/设定'
            : '左键点击断路器 · 右键平移 · Ctrl/⌘+滚轮缩放 · PCS 可启停/设定功率' }}
        </span>
      </p>
      <TopologyMainLineSvg
        v-if="useTopologyMainLine"
        :snap="snap"
        @toggle-main-breaker="onToggleMainBreaker"
        @pv-start="onPvStart"
        @pv-stop="onPvStop"
        @pv-set-power="onPvSetPower"
        @pv-set-reactive="onPvSetReactive"
        @pv-set-temp="onPvSetTemp"
        @pv-set-angle="onPvSetAngle"
        @pv-set-albedo="onPvSetAlbedo"
        @pcs-start="onPcsStart"
        @pcs-stop="onPcsStop"
        @pcs-set-power="onPcsSetPower"
        @pcs-set-reactive="onPcsSetReactive"
        @bms-power-on="onBmsPowerOn"
        @bms-power-off="onBmsPowerOff"
        @bms-fault-clear="onBmsFaultClear"
        @bms-set-soc="onBmsSetSoc"
      />
      <MainLineSvg
        v-else
        :snap="snap"
        @toggle-main-breaker="onToggleMainBreaker"
        @toggle-unit-breaker="onToggleUnitBreaker"
        @pcs-start="onPcsStart"
        @pcs-stop="onPcsStop"
        @pcs-set-power="onPcsSetPower"
        @pcs-set-reactive="onPcsSetReactive"
        @bms-power-on="onBmsPowerOn"
        @bms-power-off="onBmsPowerOff"
        @bms-fault-clear="onBmsFaultClear"
        @bms-set-soc="onBmsSetSoc"
      />
    </div>

    <div class="card">
      <p class="card-title">传播母线节点</p>
      <el-table :data="busRows" size="small" border stripe>
        <el-table-column prop="busId" label="节点" width="160" />
        <el-table-column label="V / I / φ"><template #default="{ row }">{{ row.phasor }}</template></el-table-column>
        <el-table-column label="P / Q / PF"><template #default="{ row }">P {{ row.p }} kW &nbsp; Q {{ row.q }} kvar &nbsp; PF {{ row.pf }}</template></el-table-column>
      </el-table>
    </div>

    <div class="card">
      <p class="card-title">交流设备</p>
      <el-table :data="acRows" size="small" border stripe>
        <el-table-column prop="name" label="设备" width="120" />
        <el-table-column prop="phasor" label="总相量" min-width="220" />
        <el-table-column prop="lineVoltages" label="线电压" min-width="220" />
        <el-table-column prop="phaseCurrents" label="相电流" min-width="200" />
        <el-table-column prop="power" label="功率" min-width="200" />
      </el-table>
    </div>

    <div v-if="pcsRows.length || transformerRows.length" class="card unit-detail">
      <p class="card-title">储能单元明细</p>

      <p class="section-title">PCS 明细</p>
      <el-table :data="pcsRows" size="small" border stripe>
        <el-table-column prop="unit" label="UNIT" width="88" fixed />
        <el-table-column prop="pcs" label="PCS" width="88" />
        <el-table-column prop="state" label="状态" min-width="120" />
        <el-table-column prop="startStop" label="启停" min-width="80" />
        <el-table-column prop="targetP" label="目标 P" min-width="110" />
        <el-table-column prop="actualP" label="实际 P" min-width="110" />
        <el-table-column prop="targetQ" label="目标 Q" min-width="110" />
        <el-table-column prop="actualQ" label="实际 Q" min-width="110" />
        <el-table-column prop="mode" label="模式" width="80" />
        <el-table-column prop="blackStart" label="黑启动" min-width="100" />
        <el-table-column prop="ac" label="交流" min-width="180" />
      </el-table>

      <p class="section-title">BMS 明细</p>
      <el-table :data="bmsRows" size="small" border stripe>
        <el-table-column prop="unit" label="UNIT" width="88" fixed />
        <el-table-column prop="bms" label="舱" width="80" />
        <el-table-column prop="compact" label="SOC / Vdc / Idc" min-width="180" />
        <el-table-column prop="run" label="运行" min-width="100" />
        <el-table-column prop="energy" label="电能" min-width="180" />
        <el-table-column prop="grid" label="并网" min-width="90" />
        <el-table-column prop="blackStart" label="黑启动" min-width="100" />
        <el-table-column prop="ac" label="空调" min-width="140" />
      </el-table>

      <p class="section-title">单元变压器明细</p>
      <el-table :data="transformerRows" size="small" border stripe>
        <el-table-column prop="unit" label="UNIT" width="88" fixed />
        <el-table-column prop="primary" label="一次" min-width="180" />
        <el-table-column prop="secondary" label="二次" min-width="180" />
        <el-table-column prop="power" label="功率" min-width="180" />
        <el-table-column prop="load" label="负载率" width="90" />
        <el-table-column prop="oil" label="油温" width="90" />
      </el-table>

      <p class="section-title">单元断路器明细</p>
      <el-table :data="breakerRows" size="small" border stripe>
        <el-table-column prop="unit" label="UNIT" width="88" fixed />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <span :class="row.tripped ? 'tag-offline' : (row.closed ? 'tag-online' : 'tag-offline')">{{ row.state }}</span>
          </template>
        </el-table-column>
      </el-table>

      <p class="section-title">单元电表明细</p>
      <el-table :data="meterRows" size="small" border stripe>
        <el-table-column prop="unit" label="UNIT" width="88" fixed />
        <el-table-column prop="voltages" label="线电压" min-width="220" />
        <el-table-column prop="currents" label="相电流" min-width="200" />
        <el-table-column prop="power" label="功率" min-width="220" />
        <el-table-column prop="freq" label="频率" width="90" />
      </el-table>
    </div>

    <div v-if="pvRows.length" class="card">
      <p class="card-title">光伏单元明细</p>
      <el-table :data="pvRows" size="small" border stripe>
        <el-table-column prop="pv" label="PV" width="80" fixed />
        <el-table-column label="状态" min-width="140"><template #default="{ row }">{{ row.status }}</template></el-table-column>
        <el-table-column label="有功 P" min-width="180"><template #default="{ row }">{{ row.activePower }}</template></el-table-column>
        <el-table-column label="无功 Q" min-width="180"><template #default="{ row }">{{ row.reactivePower }}</template></el-table-column>
        <el-table-column label="方阵-A" min-width="320"><template #default="{ row }">{{ row.arrayA }}</template></el-table-column>
        <el-table-column label="方阵-B" min-width="320"><template #default="{ row }">{{ row.arrayB }}</template></el-table-column>
      </el-table>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { getMainLine, postMainBreaker, postUnitBreaker, postCommand } from '@/services/api.js'
import { useRealtime, RealtimeMethods, RealtimeChannels } from '@/services/useRealtime.js'
import MainLineSvg from '@/components/MainLineSvg.vue'
import TopologyMainLineSvg from '@/components/TopologyMainLineSvg.vue'

const snap = ref({ units: [], pvUnits: [] })
const useTopologyMainLine = computed(() =>
  !!(snap.value.engineeringMode && snap.value.topology?.nodes?.length)
)
/** 工程模式且组态无负载节点：负载显示/设定置灰冻结 */
const loadControlsDisabled = computed(() =>
  !!(snap.value.engineeringMode && !snap.value.hasTopologyLoad)
)
const loadPDraft = ref('')
const loadQDraft = ref('')
const gridVDraft = ref('')
const gridFDraft = ref('')
let lastLoadSetP = null
let lastLoadSetQ = null
let lastGridSetV = null
let lastGridSetF = null

function syncLoadDrafts(force = false) {
  if (loadControlsDisabled.value) {
    loadPDraft.value = ''
    loadQDraft.value = ''
    lastLoadSetP = 0
    lastLoadSetQ = 0
  } else {
    const setP = snap.value.loadActivePowerSetKw
    const setQ = snap.value.loadReactivePowerSetKvar
    if (force || (setP != null && setP !== lastLoadSetP)) {
      loadPDraft.value = Number(setP ?? 0).toFixed(1)
      lastLoadSetP = setP
    }
    if (force || (setQ != null && setQ !== lastLoadSetQ)) {
      loadQDraft.value = Number(setQ ?? 0).toFixed(1)
      lastLoadSetQ = setQ
    }
  }

  const setV = snap.value.gridNominalLineVoltageV
  const setF = snap.value.gridNominalFrequencyHz
  if (force || (setV != null && setV !== lastGridSetV)) {
    gridVDraft.value = (Number(setV ?? 220000) / 1000).toFixed(1)
    lastGridSetV = setV
  }
  if (force || (setF != null && setF !== lastGridSetF)) {
    gridFDraft.value = Number(setF ?? 50).toFixed(2)
    lastGridSetF = setF
  }
}

function fmtVolt(v) {
  if (v == null) return '—'
  return v >= 1000 ? `${(v / 1000).toFixed(1)} kV` : `${(v || 0).toFixed(1)} V`
}
function fmtHz(v) { return v == null ? '—' : `${Number(v).toFixed(2)} Hz` }
function fmtKw(v) { return v == null ? '—' : `${Number(v).toFixed(1)} kW` }
function fmtKvar(v) { return v == null ? '—' : `${Number(v).toFixed(1)} kvar` }
function fmtPhasor(p) {
  if (!p) return '—'
  return `${fmtVolt(p.lineVoltageV)} / ${Number(p.lineCurrentA || 0).toFixed(1)} A / φ${Number(p.phaseAngleDeg || 0).toFixed(1)}° / ${Number(p.frequencyHz || 0).toFixed(1)} Hz`
}
function fmtPhasorViPhi(p) {
  if (!p) return '—'
  return `${fmtVolt(p.lineVoltageV)} / ${Number(p.lineCurrentA || 0).toFixed(1)} A / φ${Number(p.phaseAngleDeg || 0).toFixed(1)}°`
}
function fmtLineVoltages(m) {
  if (!m) return '—'
  return `Uab ${fmtVolt(m.lineVoltageAB)} / Ubc ${fmtVolt(m.lineVoltageBC)} / Uca ${fmtVolt(m.lineVoltageCA)}`
}
function fmtPhaseCurrents(m) {
  if (!m) return '—'
  return `Ia ${Number(m.phaseACurrent || 0).toFixed(1)} A / Ib ${Number(m.phaseBCurrent || 0).toFixed(1)} A / Ic ${Number(m.phaseCCurrent || 0).toFixed(1)} A`
}
function fmtBreaker(closed, tripped) { return tripped ? '跳闸' : closed ? '合' : '分' }

function fmtPf(v) {
  if (v == null || Number.isNaN(Number(v))) return '—'
  return Number(v).toFixed(3)
}
function unitLabel(u) {
  return `UNIT ${u.unitNumber ?? (u.unitIndex ?? 0) + 1}`
}

const pcsRows = computed(() => {
  const rows = []
  for (const u of snap.value.units || []) {
    for (const ch of u.channels || []) {
      if (!ch) continue
      rows.push({
        unit: unitLabel(u),
        pcs: `PCS-${ch.pcsNumber ?? ch.channelNumber ?? ''}`,
        state: ch.pcsDeviceState || '—',
        startStop: ch.pcsStartStop || '—',
        targetP: ch.pcsTargetP || '—',
        actualP: ch.pcsActualP || '—',
        targetQ: ch.pcsTargetQ || '—',
        actualQ: ch.pcsActualQ || '—',
        mode: ch.pcsGridMode || '—',
        blackStart: ch.pcsBlackStart || '—',
        ac: ch.pcsAcLine || '—'
      })
    }
  }
  return rows
})

const bmsRows = computed(() => {
  const rows = []
  for (const u of snap.value.units || []) {
    for (const ch of u.channels || []) {
      if (!ch) continue
      rows.push({
        unit: unitLabel(u),
        bms: `舱-${ch.compartmentNumber ?? ch.channelNumber ?? ''}`,
        compact: ch.bmsCompact || '—',
        run: ch.bmsRunStatus || '—',
        energy: ch.bmsEnergy || `累计充 ${(ch.cumulativeChargeEnergyKwh ?? 0).toFixed(1)} / 放 ${(ch.cumulativeDischargeEnergyKwh ?? 0).toFixed(1)} kWh`,
        grid: ch.gridConnect || '—',
        blackStart: ch.bmsBlackStart || '—',
        ac: ch.bmsAirConditioner || '—'
      })
    }
  }
  return rows
})

const transformerRows = computed(() => (snap.value.units || []).map(u => ({
  unit: unitLabel(u),
  primary: fmtPhasorViPhi(u.unitTransformerPrimary),
  secondary: u.unitTransformerLine || fmtPhasorViPhi(u.unitTransformerSecondary),
  power: `P ${fmtKw(u.unitTransformerActivePowerKw)}  Q ${fmtKvar(u.unitTransformerReactivePowerKvar)}`,
  load: u.unitTransformerLoadFraction == null ? '—' : `${(Number(u.unitTransformerLoadFraction) * 100).toFixed(1)}%`,
  oil: u.unitTransformerOilTemperatureC == null ? '—' : `${Number(u.unitTransformerOilTemperatureC).toFixed(1)} ℃`
})))

const breakerRows = computed(() => (snap.value.units || []).map(u => ({
  unit: unitLabel(u),
  state: u.unitBreakerLabel || fmtBreaker(u.unitBreakerClosed, u.unitBreakerTripped),
  closed: !!u.unitBreakerClosed,
  tripped: !!u.unitBreakerTripped
})))

const meterRows = computed(() => (snap.value.units || []).map(u => ({
  unit: unitLabel(u),
  voltages: fmtLineVoltages(u.unitMeterThreePhase),
  currents: fmtPhaseCurrents(u.unitMeterThreePhase),
  power: `P ${fmtKw(u.unitMeterActivePowerKw)}  Q ${fmtKvar(u.unitMeterReactivePowerKvar)}  PF ${fmtPf(u.unitMeterPowerFactor)}`,
  freq: fmtHz(u.unitMeterFrequencyHz)
})))

const breakerLabel = computed(() =>
  snap.value.mainBreakerLabel || fmtBreaker(snap.value.mainBreakerClosed, snap.value.mainBreakerTripped)
)

const busRows = computed(() => {
  const rows = []
  const push = b => {
    if (!b) return
    const p = { lineVoltageV: b.lineVoltageV, lineCurrentA: b.lineCurrentA, phaseAngleDeg: b.phaseAngleDeg, frequencyHz: b.frequencyHz }
    const ap = p.lineVoltageV * p.lineCurrentA * Math.cos((p.phaseAngleDeg || 0) * Math.PI / 180) / 1000
    rows.push({
      busId: b.busId,
      phasor: fmtPhasor(p),
      p: Number(ap).toFixed(1),
      q: '0.0',
      pf: '—'
    })
  }
  push(snap.value.busGrid)
  push(snap.value.bus35Propagation)
  for (const u of snap.value.units || []) push(u.bus690)
  return rows
})

const acRows = computed(() => [
  {
    name: 'PCC电表',
    phasor: fmtPhasor(snap.value.meterPrimary),
    lineVoltages: fmtLineVoltages(snap.value.meterThreePhase),
    phaseCurrents: fmtPhaseCurrents(snap.value.meterThreePhase),
    power: `P ${fmtKw(snap.value.meterPrimary?.activePowerKw)}  Q ${fmtKvar(snap.value.meterPrimary?.reactivePowerKvar)}`
  },
  {
    name: '主变一次',
    phasor: fmtPhasorViPhi(snap.value.mainTransformerPrimary),
    lineVoltages: '—',
    phaseCurrents: '—',
    power: `P ${fmtKw(snap.value.mainTransformerPrimary?.activePowerKw)}  Q ${fmtKvar(snap.value.mainTransformerPrimary?.reactivePowerKvar)}`
  },
  {
    name: '主变二次',
    phasor: fmtPhasorViPhi(snap.value.mainTransformerSecondary),
    lineVoltages: '—',
    phaseCurrents: '—',
    power: `P ${fmtKw(snap.value.mainTransformerSecondary?.activePowerKw)}  Q ${fmtKvar(snap.value.mainTransformerSecondary?.reactivePowerKvar)}`
  },
  {
    name: '35kV负载',
    phasor: '—',
    lineVoltages: '—',
    phaseCurrents: '—',
    power: `P ${fmtKw(snap.value.loadActivePowerKw)}  Q ${fmtKvar(snap.value.loadReactivePowerKvar)}`
  }
])

function fmtPvArray(a) {
  if (!a) return '—'
  const parts = [
    `辐照 ${Number(a.planeOfArrayWm2 || 0).toFixed(0)} W/m²`,
    `环境 ${Number(a.ambientTemperatureC || 0).toFixed(1)}℃ / 组件 ${Number(a.cellTemperatureC || 0).toFixed(1)}℃`,
    `入射角 ${Number(a.incidenceAngleDeg || 0).toFixed(0)}° / 反照率 ${Number(a.albedo || 0).toFixed(2)}`,
    `DC ${Number(a.dcVoltageV || 0).toFixed(0)}V / ${Number(a.dcCurrentA || 0).toFixed(0)}A`,
    `出力 ${fmtKw(a.activePowerKw)}`
  ]
  if (a.limitReason) parts.push(`限电:${a.limitReason}`)
  return parts.join(' ')
}

const pvRows = computed(() => (snap.value.pvUnits || []).map(p => ({
  pv: `PV ${p.pvNumber ?? p.pvIndex + 1}`,
  status: `${p.running ? '运行' : '待机'} | 并网设备 ${p.gridConnectedDeviceCount ?? 0} 台`,
  activePower: `设 ${fmtKw(p.targetActivePowerKw)} / 实 ${fmtKw(p.activePowerKw)}`,
  reactivePower: `设 ${fmtKvar(p.targetReactivePowerKvar)} / 实 ${fmtKvar(p.reactivePowerKvar)}`,
  arrayA: fmtPvArray(p.arrayA),
  arrayB: fmtPvArray(p.arrayB)
})))

async function refreshSnap() {
  try {
    snap.value = await getMainLine()
    syncLoadDrafts()
  } catch { /* 实时通道稍后会补 */ }
}

async function runChannelCommand(input) {
  try {
    const r = await postCommand(input)
    ElMessage[r.success ? 'success' : 'error'](r.message)
    if (r.success) await refreshSnap()
  } catch (e) {
    ElMessage.error(e.message)
  }
}

async function onToggleMainBreaker() {
  if (snap.value.mainBreakerTripped) {
    ElMessage.warning('主断路器已跳闸，请先复位')
    return
  }
  try {
    const next = !snap.value.mainBreakerClosed
    const r = await postMainBreaker(next)
    ElMessage[r.success ? 'success' : 'error'](r.message)
    if (r.success) await refreshSnap()
  } catch (e) {
    ElMessage.error(e.message)
  }
}

async function onToggleUnitBreaker(unitIndex) {
  const u = (snap.value.units || []).find(x => x.unitIndex === unitIndex)
  if (u?.unitBreakerTripped) {
    ElMessage.warning(`UNIT ${unitIndex + 1} 单元断路器已跳闸`)
    return
  }
  try {
    const next = !(u?.unitBreakerClosed ?? false)
    const r = await postUnitBreaker(unitIndex + 1, next)
    ElMessage[r.success ? 'success' : 'error'](r.message)
    if (r.success) await refreshSnap()
  } catch (e) {
    ElMessage.error(e.message)
  }
}

async function onPcsStart(pcsNumber) {
  await runChannelCommand(`esscmd pcs${pcsNumber} start`)
}

async function onPcsStop(pcsNumber) {
  await runChannelCommand(`esscmd pcs${pcsNumber} stop`)
}

async function onPcsSetPower(payload = {}) {
  const n = Number(payload.pcsNumber)
  if (!Number.isFinite(n) || n < 1) {
    ElMessage.error('无法解析 PCS 编号')
    return
  }
  const kw = Number(payload.powerKw)
  if (!Number.isFinite(kw)) {
    ElMessage.warning('请输入有效的有功功率')
    return
  }
  await runChannelCommand(`esscmd pcs${n} power ${kw}`)
}

async function onPcsSetReactive(payload = {}) {
  const n = Number(payload.pcsNumber)
  if (!Number.isFinite(n) || n < 1) {
    ElMessage.error('无法解析 PCS 编号')
    return
  }
  const kvar = Number(payload.reactiveKvar)
  if (!Number.isFinite(kvar)) {
    ElMessage.warning('请输入有效的无功功率')
    return
  }
  await runChannelCommand(`esscmd pcs${n} reactive ${kvar}`)
}

function resolvePvNumber(payload) {
  const n = Number(payload?.pvNumber ?? payload)
  return Number.isFinite(n) && n >= 1 ? n : 0
}

async function onPvStart(payload) {
  const n = resolvePvNumber(payload)
  if (!n) {
    ElMessage.error('无法解析光伏单元')
    return
  }
  await runChannelCommand(`esscmd setpv${n} run on`)
}

async function onPvStop(payload) {
  const n = resolvePvNumber(payload)
  if (!n) {
    ElMessage.error('无法解析光伏单元')
    return
  }
  await runChannelCommand(`esscmd setpv${n} run off`)
}

async function onPvSetPower(payload = {}) {
  const n = resolvePvNumber(payload)
  const kw = Number(payload.powerKw)
  if (!n) {
    ElMessage.error('无法解析光伏单元')
    return
  }
  if (!Number.isFinite(kw) || kw < 0) {
    ElMessage.warning('请输入有效的有功功率（≥0）')
    return
  }
  await runChannelCommand(`esscmd setpv${n} power ${kw}`)
}

async function onPvSetReactive(payload = {}) {
  const n = resolvePvNumber(payload)
  const kvar = Number(payload.reactiveKvar)
  if (!n) {
    ElMessage.error('无法解析光伏单元')
    return
  }
  if (!Number.isFinite(kvar)) {
    ElMessage.warning('请输入有效的无功功率')
    return
  }
  await runChannelCommand(`esscmd setpv${n} reactive ${kvar}`)
}

function resolvePvSide(payload) {
  const side = String(payload?.side || '').trim().toUpperCase()
  return side === 'A' || side === 'B' ? side : ''
}

async function onPvSetTemp(payload = {}) {
  const n = resolvePvNumber(payload)
  const side = resolvePvSide(payload)
  const temperatureC = Number(payload.temperatureC)
  if (!n || !side) {
    ElMessage.error('无法解析光伏方阵')
    return
  }
  if (!Number.isFinite(temperatureC)) {
    ElMessage.warning('请输入有效的温度')
    return
  }
  await runChannelCommand(`esscmd setpv${n} array ${side} temperature ${temperatureC}`)
}

async function onPvSetAngle(payload = {}) {
  const n = resolvePvNumber(payload)
  const side = resolvePvSide(payload)
  const angleDeg = Number(payload.angleDeg)
  if (!n || !side) {
    ElMessage.error('无法解析光伏方阵')
    return
  }
  if (!Number.isFinite(angleDeg)) {
    ElMessage.warning('请输入有效的入射角')
    return
  }
  await runChannelCommand(`esscmd setpv${n} array ${side} angle ${angleDeg}`)
}

async function onPvSetAlbedo(payload = {}) {
  const n = resolvePvNumber(payload)
  const side = resolvePvSide(payload)
  const albedo = Number(payload.albedo)
  if (!n || !side) {
    ElMessage.error('无法解析光伏方阵')
    return
  }
  if (!Number.isFinite(albedo)) {
    ElMessage.warning('请输入有效的反照率（0~1）')
    return
  }
  await runChannelCommand(`esscmd setpv${n} array ${side} albedo ${albedo}`)
}

async function onBmsPowerOn(bmsNumber) {
  await runChannelCommand(`esscmd setbms${bmsNumber} power on`)
}

async function onBmsPowerOff(bmsNumber) {
  await runChannelCommand(`esscmd setbms${bmsNumber} power off`)
}

async function onBmsFaultClear(bmsNumber) {
  await runChannelCommand(`esscmd bms${bmsNumber} fault clear`)
}

async function onBmsSetSoc(payload = {}) {
  const bmsNumber = Number(payload.bmsNumber)
  const pct = Number(payload.socPercent)
  if (!Number.isFinite(bmsNumber) || bmsNumber < 1) {
    ElMessage.warning('无效的 BMS 舱号')
    return
  }
  if (!Number.isFinite(pct) || pct < 0 || pct > 100) {
    ElMessage.warning('请输入 0~100 的 SOC(%)')
    return
  }
  await runChannelCommand(`esscmd setbms${bmsNumber} soc ${pct}`)
}

async function onSetLoadActive() {
  if (loadControlsDisabled.value) {
    ElMessage.warning('当前工程未配置负载，无法设定')
    return
  }
  const kw = Number(loadPDraft.value)
  if (!Number.isFinite(kw)) {
    ElMessage.warning('请输入有效的负载有功（kW）')
    return
  }
  if (kw > 0) {
    ElMessage.warning('负载有功只能消耗不能释放：请输入 ≤0（负值=从电网取电）')
    return
  }
  await runChannelCommand(`esscmd setLoad activePower ${kw}`)
  lastLoadSetP = kw
}

async function onSetLoadReactive() {
  if (loadControlsDisabled.value) {
    ElMessage.warning('当前工程未配置负载，无法设定')
    return
  }
  const kvar = Number(loadQDraft.value)
  if (!Number.isFinite(kvar)) {
    ElMessage.warning('请输入有效的负载无功（kvar）')
    return
  }
  await runChannelCommand(`esscmd setLoad reactivePower ${kvar}`)
  lastLoadSetQ = kvar
}

async function onSetGridVoltage() {
  const kv = Number(gridVDraft.value)
  if (!Number.isFinite(kv) || kv <= 0) {
    ElMessage.warning('请输入有效的电网线电压（kV，如 220）')
    return
  }
  const volts = kv * 1000
  await runChannelCommand(`esscmd setGrid voltage ${volts}`)
  lastGridSetV = volts
}

async function onSetGridFrequency() {
  const hz = Number(gridFDraft.value)
  if (!Number.isFinite(hz) || hz <= 0 || hz > 75) {
    ElMessage.warning('请输入有效的电网频率（Hz，范围 (0, 75]）')
    return
  }
  await runChannelCommand(`esscmd setGrid frequency ${hz}`)
  lastGridSetF = hz
}

onMounted(async () => {
  try {
    snap.value = await getMainLine()
    syncLoadDrafts(true)
  } catch (e) { console.warn(e) }
})

useRealtime(RealtimeChannels.MainLine, {
  [RealtimeMethods.ReceiveMainLine]: data => {
    snap.value = data
    syncLoadDrafts()
  }
})
</script>

<style scoped>
.card-hint { font-size: 12px; color: #909399; font-weight: normal; margin-left: 12px; }

.section-title {
  margin: 16px 0 8px;
  font-size: 13px;
  font-weight: 600;
  color: #303133;
}
.unit-detail .section-title:first-of-type {
  margin-top: 4px;
}

.metric-item-editable {
  position: relative;
  min-height: 72px;
  padding-bottom: 30px;
}

.metric-set {
  position: absolute;
  right: 8px;
  bottom: 6px;
  display: flex;
  align-items: center;
  gap: 4px;
}

.metric-input {
  width: 62px;
  font-size: 11px;
  padding: 2px 4px;
  border: 1px solid #dcdfe6;
  border-radius: 3px;
  box-sizing: border-box;
}

.metric-set-btn {
  font-size: 11px;
  line-height: 1.2;
  padding: 2px 6px;
  border: 1px solid #c0c4cc;
  border-radius: 3px;
  background: #fff;
  color: #303133;
  cursor: pointer;
}

.metric-set-btn:hover {
  border-color: #409eff;
  color: #409eff;
}

.metric-item-disabled {
  opacity: 0.55;
  filter: grayscale(0.35);
  background: #f4f4f5;
  pointer-events: none;
}
.metric-item-disabled .value,
.metric-item-disabled .label {
  color: #909399 !important;
}
.metric-item-disabled .metric-input,
.metric-item-disabled .metric-set-btn {
  cursor: not-allowed;
  background: #f0f2f5;
  color: #c0c4cc;
  border-color: #e4e7ed;
}
</style>
