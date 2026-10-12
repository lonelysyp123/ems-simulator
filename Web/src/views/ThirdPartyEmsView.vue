<template>
  <div class="ems-page">
    <div class="card ems-banner">
      <p class="card-title">第三方 EMS</p>
      <p class="hint">
        按单元直写 emuN.Emu：目标有功/无功经功率均分到各 PCS，并可下发启停与远程使能。
        占用控制权期间拒绝 Modbus、dpc、命令页；与电站策略互斥，同一时刻只能有一个外部控制源。
      </p>
      <el-alert
        v-if="!dash.available"
        :title="dash.reason || '找不到储能单元数据模型'"
        type="warning"
        :closable="false"
        show-icon
      />
      <el-alert
        v-else-if="blockedByStrategy"
        :title="dash.reason || 'EMS 策略占用中，已拒绝第三方占用。'"
        type="warning"
        :closable="false"
        show-icon
      />
      <el-alert
        v-else-if="dash.exclusive"
        title="第三方 EMS 已占用控制权：外部 Modbus / dpc / 命令下发均被拒绝，直到释放。"
        type="info"
        :closable="false"
        show-icon
      />
      <div class="ems-toolbar">
        <el-button type="primary" :loading="busy" :disabled="!canOccupy" @click="connectAll">占用全部</el-button>
        <el-button :loading="busy" :disabled="!dash.available" @click="disconnectAll">释放全部</el-button>
        <span class="meta">
          全站实发
          {{ fmtPq(dash.station?.activePowerKw, dash.station?.reactivePowerKvar) }}
          · 占用 {{ dash.station?.connectedCount ?? 0 }} / {{ dash.station?.unitCount ?? 0 }}
        </span>
      </div>
    </div>

    <div v-if="dash.units?.length" class="ems-grid">
      <div v-for="unit in dash.units" :key="unit.name" class="card ems-unit">
        <div class="ems-unit-head">
          <div>
            <p class="card-title">储能单元 {{ unit.unitIndex }}</p>
            <p class="ems-endpoint">{{ unit.modelPath || unit.name }}</p>
          </div>
          <el-tag :type="unit.connected ? 'success' : 'info'" size="small">
            {{ unit.connected ? '占用中' : '未占用' }}
          </el-tag>
        </div>

        <div class="ems-actions">
          <el-button size="small" type="primary" :disabled="unit.connected || !canOccupy || busy" :loading="busy" @click="connect(unit.name)">占用</el-button>
          <el-button size="small" :disabled="!unit.connected" :loading="busy" @click="disconnect(unit.name)">释放</el-button>
        </div>

        <el-descriptions :column="2" border size="small" class="ems-telem">
          <el-descriptions-item label="实发 P/Q">{{ liveText(unit, fmtPq(unit.activePowerKw, unit.reactivePowerKvar)) }}</el-descriptions-item>
          <el-descriptions-item label="设定 P/Q">{{ liveText(unit, fmtPq(unit.targetActivePowerKw, unit.targetReactivePowerKvar)) }}</el-descriptions-item>
          <el-descriptions-item label="SOC">{{ liveText(unit, fmtSoc(unit.soc)) }}</el-descriptions-item>
          <el-descriptions-item label="系统状态">{{ liveText(unit, statusLabel(unit.detailedStatus)) }}</el-descriptions-item>
          <el-descriptions-item label="故障总">{{ liveText(unit, unit.faultSummary ? '故障' : '正常') }}</el-descriptions-item>
          <el-descriptions-item label="允许充/放">{{ liveText(unit, fmtPq(unit.maxChargePowerKw, unit.maxDischargePowerKw)) }}</el-descriptions-item>
        </el-descriptions>

        <div class="ems-controls" :class="{ 'is-disabled': !unit.connected }">
          <div class="ems-row">
            <span class="ems-label">远程控制</span>
            <el-switch
              :model-value="unit.remoteEnable === 1"
              :disabled="!unit.connected || busy"
              active-text="使能"
              @change="v => setRemote(unit, v ? 1 : 0, unit.remoteMode ?? 1)"
            />
            <el-switch
              :model-value="unit.remoteMode === 1"
              :disabled="!unit.connected || busy"
              active-text="远程"
              inactive-text="本地"
              @change="v => setRemote(unit, unit.remoteEnable ?? 1, v ? 1 : 0)"
            />
          </div>
          <div class="ems-row">
            <span class="ems-label">系统操作</span>
            <el-button-group>
              <el-button size="small" :disabled="!unit.connected || busy" @click="operate(unit.name, 3)">启动</el-button>
              <el-button size="small" :disabled="!unit.connected || busy" @click="operate(unit.name, 4)">停止</el-button>
              <el-button size="small" :disabled="!unit.connected || busy" @click="operate(unit.name, 5)">待机</el-button>
              <el-button size="small" :disabled="!unit.connected || busy" @click="operate(unit.name, 6)">重置</el-button>
            </el-button-group>
          </div>
          <div class="ems-row ems-power" v-if="drafts[unit.name]">
            <span class="ems-label">目标功率</span>
            <el-input-number v-model="drafts[unit.name].p" :disabled="!unit.connected" :step="50" :precision="0" controls-position="right" />
            <span class="ems-unit-label">kW</span>
            <el-input-number v-model="drafts[unit.name].q" :disabled="!unit.connected" :step="10" :precision="0" controls-position="right" />
            <span class="ems-unit-label">kvar</span>
            <el-button size="small" type="primary" :disabled="!unit.connected || busy" @click="applyPower(unit.name)">下发 P/Q</el-button>
          </div>
        </div>

        <p class="ems-write" :class="{ ok: unit.lastWriteOk, fail: unit.lastWriteOk === false }">
          {{ unit.lastWrite || '尚未下发' }}
        </p>
        <p v-if="unit.lastError && !unit.live" class="ems-error">{{ unit.lastError }}</p>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, reactive, ref, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  getThirdPartyEms,
  postThirdPartyEmsConnect,
  postThirdPartyEmsDisconnect,
  postThirdPartyEmsPower,
  postThirdPartyEmsRemote,
  postThirdPartyEmsOperation
} from '@/services/api.js'

const STATUS = {
  1: '停机', 2: '待机', 4: '充电运行', 5: '放电运行', 6: '未知'
}

const dash = reactive({
  available: true,
  exclusive: false,
  gateOwner: 'None',
  reason: '',
  station: { activePowerKw: null, reactivePowerKvar: null, connectedCount: 0, unitCount: 0 },
  units: []
})
const drafts = reactive({})
const busy = ref(false)
const route = useRoute()
const blockedByStrategy = computed(() => dash.gateOwner === 'EmsStrategy')
const canOccupy = computed(() => dash.available && !blockedByStrategy.value)
let timer = null

function applyDash(next) {
  Object.assign(dash, next || {})
  if (!dash.station) dash.station = { connectedCount: 0, unitCount: 0 }
  if (!Array.isArray(dash.units)) dash.units = []
  for (const unit of dash.units) {
    if (!drafts[unit.name]) {
      drafts[unit.name] = {
        p: Number(unit.targetActivePowerKw ?? 0),
        q: Number(unit.targetReactivePowerKvar ?? 0)
      }
    }
  }
}

async function reload() {
  try {
    applyDash(await getThirdPartyEms())
  } catch (e) {
    dash.available = false
    dash.reason = e.message
    dash.units = []
  }
}

async function run(fn, okMsg) {
  busy.value = true
  try {
    const r = await fn()
    if (r?.dashboard) applyDash(r.dashboard)
    else await reload()
    if (okMsg) ElMessage.success(okMsg)
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    busy.value = false
  }
}

function connectAll() { return run(() => postThirdPartyEmsConnect(), '已占用全部单元，外部指令已拒绝') }
function disconnectAll() { return run(() => postThirdPartyEmsDisconnect(), '已释放控制权') }
function connect(name) { return run(() => postThirdPartyEmsConnect(name), `已占用 ${name}`) }
function disconnect(name) { return run(() => postThirdPartyEmsDisconnect(name), `已释放 ${name}`) }

function applyPower(name) {
  const d = drafts[name] || { p: 0, q: 0 }
  return run(() => postThirdPartyEmsPower(name, d.p, d.q), `已下发 ${name} P/Q`)
}

function setRemote(unit, enable, mode) {
  return run(() => postThirdPartyEmsRemote(unit.name, enable, mode), '已下发远程控制')
}

function operate(name, operation) {
  const labels = { 3: '启动', 4: '停止', 5: '待机', 6: '重置' }
  return run(() => postThirdPartyEmsOperation(name, operation), `已下发${labels[operation] || '操作'}`)
}

function fmtNum(v) {
  if (v == null || Number.isNaN(Number(v))) return '—'
  return Number(v).toFixed(1)
}

function fmtPq(p, q) {
  if (p == null && q == null) return '—'
  return `${fmtNum(p)} / ${fmtNum(q)}`
}

function fmtSoc(v) {
  if (v == null) return '—'
  const n = Number(v)
  return n <= 1.5 ? `${(n * 100).toFixed(1)} %` : `${n.toFixed(1)} %`
}

function statusLabel(v) {
  if (v == null) return '—'
  const key = Math.round(Number(v))
  return STATUS[key] || String(key)
}

function liveText(unit, text) {
  return unit.live ? text : '—'
}

watch(() => dash.units, units => {
  for (const unit of units || []) {
    if (!drafts[unit.name]) {
      drafts[unit.name] = { p: 0, q: 0 }
    }
  }
}, { immediate: true })

onMounted(async () => {
  if (route.meta.standalone)
    document.title = '第三方 EMS'
  await reload()
  timer = setInterval(reload, 1000)
})

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>
