<template>
  <div>
    <div class="card">
      <p class="card-title">设备告警 / 故障</p>
      <p class="hint">未触发为绿色，已触发为红色。数据来自仿真模型告警属性（BMS 簇/堆 SystemAlarms、PCS）。</p>
      <el-space wrap :size="12">
        <el-radio-group v-model="deviceFilter" size="small">
          <el-radio-button value="all">全部设备</el-radio-button>
          <el-radio-button value="bms">BMS</el-radio-button>
          <el-radio-button value="pcs">PCS</el-radio-button>
        </el-radio-group>
        <el-select v-model="unitFilter" style="width:120px" clearable placeholder="全部舱">
          <el-option v-for="i in unitCount" :key="i" :label="`舱 ${i}`" :value="i" />
        </el-select>
        <el-select v-model="kindFilter" style="width:130px" clearable placeholder="类型">
          <el-option label="故障" value="fault" />
          <el-option label="告警" value="alarm" />
          <el-option label="保护" value="protection" />
          <el-option label="其他" value="other" />
        </el-select>
        <el-checkbox v-model="onlyActive">仅显示已触发</el-checkbox>
        <el-checkbox v-model="collapseOk">折叠无告警设备</el-checkbox>
        <el-button @click="() => reload({ silent: false })" :loading="loading">刷新</el-button>
        <el-tag type="info" effect="plain">触发设备 {{ snap?.activeDeviceCount ?? 0 }}</el-tag>
        <el-tag :type="(snap?.activeFlagCount || 0) > 0 ? 'danger' : 'success'" effect="plain">
          触发位 {{ snap?.activeFlagCount ?? 0 }}
        </el-tag>
        <span class="auto">自动刷新 {{ autoSec }}s</span>
      </el-space>
    </div>

    <div v-if="!visibleDevices.length" class="card empty">
      {{ emptyHint }}
    </div>

    <div v-for="dev in visibleDevices" :key="dev.deviceId" class="card device-card">
      <div class="device-head">
        <div>
          <span class="device-title">{{ dev.title }}</span>
          <el-tag size="small" class="ml" effect="plain">{{ typeLabel(dev.deviceType) }}</el-tag>
          <code class="device-id">{{ dev.deviceId }}</code>
        </div>
        <div>
          <el-tag :type="dev.activeCount > 0 ? 'danger' : 'success'" effect="dark" size="small">
            {{ dev.activeCount }} / {{ filteredFlagCount(dev) }}
          </el-tag>
        </div>
      </div>
      <div class="flag-grid">
        <div
          v-for="f in visibleFlags(dev)"
          :key="f.name"
          class="flag-chip"
          :class="f.active ? 'on' : 'off'"
          :title="`${f.name} · ${kindLabel(f.kind)}`"
        >
          <span class="dot" />
          <span class="txt">{{ f.label }}</span>
        </div>
      </div>
      <div v-if="!visibleFlags(dev).length" class="empty-flags">当前筛选下无属性</div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { ElMessage } from 'element-plus'
import { getAlarms, getConfig } from '@/services/api.js'

const snap = ref(null)
const loading = ref(false)
const unitCount = ref(1)
const unitFilter = ref(null)
const deviceFilter = ref('all')
const kindFilter = ref(null)
const onlyActive = ref(false)
const collapseOk = ref(true)
const autoSec = 2
let timer = null
let inFlight = null

const visibleDevices = computed(() => {
  let list = snap.value?.devices || []
  if (deviceFilter.value === 'bms') {
    list = list.filter(d => d.deviceType === 'bms-rack' || d.deviceType === 'bms-stack')
  } else if (deviceFilter.value === 'pcs') {
    list = list.filter(d => d.deviceType === 'pcs')
  }
  if (unitFilter.value) {
    list = list.filter(d => d.unitNumber === unitFilter.value)
  }
  if (collapseOk.value) {
    list = list.filter(d => d.activeCount > 0 || visibleFlags(d).some(f => f.active))
    // 无触发时：全部/BMS 仍展示堆级卡片，避免整页空白；指定 PCS 时保持空列表，不盖掉筛选
    if (!list.length && deviceFilter.value !== 'pcs' && (snap.value?.devices || []).length) {
      list = (snap.value.devices || []).filter(d => d.deviceType === 'bms-stack')
      if (unitFilter.value) list = list.filter(d => d.unitNumber === unitFilter.value)
    }
  }
  return list
})

const emptyHint = computed(() => {
  if (!(snap.value?.devices || []).length) return '暂无匹配设备（仿真未就绪或筛选过严）'
  if (deviceFilter.value === 'pcs' && collapseOk.value)
    return '当前无已触发的 PCS，可取消「折叠无告警设备」查看全部 PCS'
  return '暂无匹配设备（仿真未就绪或筛选过严）'
})

function visibleFlags(dev) {
  let flags = dev.flags || []
  if (kindFilter.value) flags = flags.filter(f => f.kind === kindFilter.value)
  if (onlyActive.value) flags = flags.filter(f => f.active)
  return flags
}

function filteredFlagCount(dev) {
  let flags = dev.flags || []
  if (kindFilter.value) flags = flags.filter(f => f.kind === kindFilter.value)
  return flags.length
}

function typeLabel(t) {
  if (t === 'bms-rack') return 'BMS 簇'
  if (t === 'bms-stack') return 'BMS 堆'
  if (t === 'pcs') return 'PCS'
  return t
}

function kindLabel(k) {
  if (k === 'fault') return '故障'
  if (k === 'alarm') return '告警'
  if (k === 'protection') return '保护'
  return '其他'
}

/** 原地合并，避免整表替换导致卡片/芯片闪烁。 */
function patchSnap(cur, next) {
  cur.time = next.time
  cur.unitCount = next.unitCount
  cur.activeDeviceCount = next.activeDeviceCount
  cur.activeFlagCount = next.activeFlagCount

  const nextById = new Map((next.devices || []).map(d => [d.deviceId, d]))
  const curById = new Map((cur.devices || []).map(d => [d.deviceId, d]))

  for (const [id, nDev] of nextById) {
    const cDev = curById.get(id)
    if (!cDev) {
      cur.devices.push(nDev)
      continue
    }
    cDev.title = nDev.title
    cDev.activeCount = nDev.activeCount
    cDev.totalCount = nDev.totalCount
    const nFlags = new Map((nDev.flags || []).map(f => [f.name, f]))
    for (const f of cDev.flags || []) {
      const nf = nFlags.get(f.name)
      if (nf && f.active !== nf.active) f.active = nf.active
    }
  }
}

async function reload({ silent = false } = {}) {
  if (inFlight) return inFlight
  if (!silent) loading.value = true

  inFlight = (async () => {
    try {
      const next = await getAlarms()
      if (snap.value && silent) patchSnap(snap.value, next)
      else snap.value = next
    } catch (e) {
      if (!silent) ElMessage.error(e.message || String(e))
    } finally {
      loading.value = false
      inFlight = null
    }
  })()

  return inFlight
}

onMounted(async () => {
  try {
    const cfg = await getConfig()
    unitCount.value = Math.max(1, cfg?.simulator?.channelCount || cfg?.simulator?.unitCount || 1)
  } catch { /* ignore */ }
  await reload({ silent: false })
  timer = setInterval(() => reload({ silent: true }), autoSec * 1000)
})

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>

<style scoped>
.hint {
  margin: 0 0 10px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}
.auto { color: #909399; font-size: 12px; }
.empty { color: #909399; text-align: center; padding: 24px; }
.device-card { padding-bottom: 10px; }
.device-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
  gap: 12px;
}
.device-title { font-weight: 600; font-size: 14px; color: #303133; }
.device-id {
  margin-left: 8px;
  font-size: 11px;
  color: #909399;
  background: #f4f4f5;
  padding: 1px 6px;
  border-radius: 3px;
}
.ml { margin-left: 8px; }
.flag-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.flag-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  border-radius: 4px;
  font-size: 12px;
  border: 1px solid transparent;
  max-width: 100%;
  transition: background-color .2s ease, border-color .2s ease, color .2s ease;
}
.flag-chip .txt {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 220px;
}
.flag-chip .dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}
.flag-chip.off {
  background: #f0f9eb;
  border-color: #e1f3d8;
  color: #67c23a;
}
.flag-chip.off .dot { background: #67c23a; }
.flag-chip.on {
  background: #fef0f0;
  border-color: #fde2e2;
  color: #f56c6c;
  font-weight: 600;
}
.flag-chip.on .dot { background: #f56c6c; box-shadow: 0 0 0 2px rgba(245,108,108,.25); }
.empty-flags { color: #c0c4cc; font-size: 12px; padding: 4px 0; }
</style>
