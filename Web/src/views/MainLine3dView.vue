<template>
  <div>
    <el-alert
      v-if="!simReady"
      type="warning"
      :closable="false"
      show-icon
      style="margin-bottom: 12px"
      title="仿真尚未就绪：Modbus 设备未注册，PCS 有功/无功设定会失败"
      description="请确认已同步点表（./scripts/sync-pointmaps-to-root.sh）并重启后端；顶部状态应变为「仿真就绪」。"
    />
    <div class="card">
      <p class="card-title">
        数字孪生
        <span class="card-hint">左键旋转 · 滚轮缩放 · 右键平移 · 单击设备/断路器 · 双击 BMS 进入详情</span>
      </p>
      <MainLineScene
        :snap="snap"
        @toggle-main-breaker="onToggleMainBreaker"
        @toggle-unit-breaker="onToggleUnitBreaker"
        @pcs-set-power="onPcsSetPower"
        @pcs-set-reactive="onPcsSetReactive"
        @pcs-start="onPcsStart"
        @pcs-stop="onPcsStop"
        @bms-power-on="onBmsPowerOn"
        @bms-power-off="onBmsPowerOff"
        @bms-fault-clear="onBmsFaultClear"
        @pv-start="onPvStart"
        @pv-stop="onPvStop"
        @pv-set-power="onPvSetPower"
        @pv-set-reactive="onPvSetReactive"
        @pv-set-temp="onPvSetTemp"
        @pv-set-angle="onPvSetAngle"
        @pv-set-albedo="onPvSetAlbedo"
      />
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { ElMessage } from 'element-plus'
import { getMainLine, postMainBreaker, postUnitBreaker, postCommand, getHealth } from '@/services/api.js'
import { useRealtime, RealtimeMethods, RealtimeChannels } from '@/services/useRealtime.js'
import MainLineScene from '@/components/MainLineScene.vue'

const snap = ref({ units: [], pvUnits: [] })
const simReady = ref(true)
let healthTimer = null

async function refreshHealth() {
  try {
    const h = await getHealth()
    simReady.value = !!h.ready
  } catch {
    simReady.value = false
  }
}

async function refreshSnap() {
  try {
    snap.value = await getMainLine()
  } catch { /* 实时通道稍后会补 */ }
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

async function runChannelCommand(input) {
  try {
    const r = await postCommand(input)
    ElMessage[r.success ? 'success' : 'error'](r.message)
    if (r.success) await refreshSnap()
  } catch (e) {
    ElMessage.error(e.message)
  }
}

/**
 * pcsN → simEmu{N} + yt0/yt1（一台 PCS 一路）
 */
function resolvePcsModbus(pcsNumber, kind = 'p') {
  const n = Number(pcsNumber)
  if (!Number.isFinite(n) || n < 1) return null
  return { emuUnit: n, ytPoint: kind === 'p' ? 'yt0' : 'yt1' }
}

async function onPcsStart(pcsNumber) {
  await runChannelCommand(`esscmd pcs${pcsNumber} start`)
}

async function onPcsStop(pcsNumber) {
  await runChannelCommand(`esscmd pcs${pcsNumber} stop`)
}

async function onPcsSetPower(payload = {}) {
  const kw = Number(payload.powerKw)
  if (!Number.isFinite(kw)) {
    ElMessage.warning('请输入有效的有功功率')
    return
  }
  const resolved = resolvePcsModbus(payload.pcsNumber, 'p')
  const emuUnit = Number(payload.emuUnit) > 0 ? Number(payload.emuUnit) : resolved?.emuUnit
  const ytPoint = payload.ytPoint || resolved?.ytPoint
  if (!emuUnit || !ytPoint) {
    ElMessage.error('无法解析 PCS 对应的 Modbus 设备（缺少 pcsNumber/emuUnit）')
    return
  }
  const raw = Math.round(kw * 10)
  await runChannelCommand(`dpc simEmu${emuUnit}.${ytPoint} set ${raw}`)
}

async function onPcsSetReactive(payload = {}) {
  const kvar = Number(payload.reactiveKvar)
  if (!Number.isFinite(kvar)) {
    ElMessage.warning('请输入有效的无功功率')
    return
  }
  const resolved = resolvePcsModbus(payload.pcsNumber, 'q')
  const emuUnit = Number(payload.emuUnit) > 0 ? Number(payload.emuUnit) : resolved?.emuUnit
  const ytPoint = payload.ytPoint || resolved?.ytPoint
  if (!emuUnit || !ytPoint) {
    ElMessage.error('无法解析 PCS 对应的 Modbus 设备（缺少 pcsNumber/emuUnit）')
    return
  }
  const raw = Math.round(kvar * 10)
  await runChannelCommand(`dpc simEmu${emuUnit}.${ytPoint} set ${raw}`)
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

function resolvePvNumber(payload) {
  const n = Number(payload?.pvNumber ?? payload)
  return Number.isFinite(n) && n >= 1 ? n : 0
}

function resolvePvSide(payload) {
  const side = String(payload?.side || '').trim().toUpperCase()
  return side === 'A' || side === 'B' ? side : ''
}

async function onPvStart(payload) {
  const n = resolvePvNumber(payload)
  if (!n) {
    ElMessage.error('无法解析光伏单元')
    return
  }
  await runChannelCommand(`dpc simPv${n}.yt4 set 1`)
}

async function onPvStop(payload) {
  const n = resolvePvNumber(payload)
  if (!n) {
    ElMessage.error('无法解析光伏单元')
    return
  }
  await runChannelCommand(`dpc simPv${n}.yt4 set 0`)
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
  await runChannelCommand(`dpc simPv${n}.yt5 set ${Math.round(kw * 10)}`)
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
  await runChannelCommand(`dpc simPv${n}.yt7 set ${Math.round(kvar * 10)}`)
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

onMounted(async () => {
  await refreshHealth()
  healthTimer = setInterval(refreshHealth, 3000)
  try {
    snap.value = await getMainLine()
  } catch (e) { console.warn(e) }
})

onBeforeUnmount(() => {
  if (healthTimer) clearInterval(healthTimer)
})

useRealtime(RealtimeChannels.MainLine, {
  [RealtimeMethods.ReceiveMainLine]: data => {
    snap.value = data
  }
})
</script>

<style scoped>
.card-hint { font-size: 12px; color: #909399; font-weight: normal; margin-left: 12px; }
</style>
