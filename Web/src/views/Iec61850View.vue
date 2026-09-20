<template>
  <div>
    <div class="card">
      <div class="card-title" style="display:flex;align-items:center;justify-content:space-between;gap:12px">
        <span>IEC 61850</span>
        <span style="display:flex;gap:8px;align-items:center">
          <el-tag :type="paused ? 'warning' : 'success'" size="small">{{ paused ? '已暂停' : '实时' }}</el-tag>
          <el-button size="small" @click="paused = !paused">{{ paused ? '继续' : '暂停' }}</el-button>
          <el-button size="small" @click="clearMessages">清空报文</el-button>
          <el-button size="small" text @click="reload">刷新</el-button>
        </span>
      </div>
      <el-alert
        type="info"
        :closable="false"
        show-icon
        style="margin-bottom:8px"
        title="仿真设备 = MMS 服务端 + GOOSE 订户。点选 IED 后按 Tab 查看该台 GOOSE / MMS 报文。端口开关请到「协议端口」。"
      />
    </div>

    <div class="card">
      <p class="card-title">IED 总览（点选）</p>
      <el-table
        :data="devices"
        size="small"
        border
        stripe
        highlight-current-row
        :row-class-name="iedRowClass"
        @current-change="onSelectIed"
      >
        <el-table-column prop="serverName" label="服务" width="120" />
        <el-table-column prop="iedName" label="IED" width="130" />
        <el-table-column prop="port" label="MMS" width="90" />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.online ? 'success' : 'danger'" size="small">{{ row.online ? '在线' : '离线' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="associatedClients" label="客户端" width="80" />
        <el-table-column prop="gooseInterface" label="网卡" width="90" />
        <el-table-column label="GOOSE" width="130" align="center">
          <template #default="{ row }">
            <el-tag :type="row.gooseSubscribing ? 'success' : 'info'" size="small" :title="row.gooseSubscribeSkip || ''">
              {{ row.gooseSubscribing ? formatAppId(row.gooseSubscribeAppId) : '关' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="stNum" width="90">
          <template #default="{ row }">{{ row.lastGooseStNum ?? '—' }}</template>
        </el-table-column>
        <el-table-column label="最近">
          <template #default="{ row }">{{ formatLocalFromUtc(row.lastGooseUtc) }}</template>
        </el-table-column>
      </el-table>
    </div>

    <div v-if="selected" class="card">
      <p class="card-title">选中：{{ selected.iedName }}</p>
      <div class="muted" style="font-size:13px;line-height:1.7;margin-bottom:8px">
        <div>AppID：{{ formatAppId(selected.gooseSubscribeAppId) }} · GoCbRef：{{ selected.gooseSubscribeGoCbRef || '—' }}</div>
        <div>网卡：{{ selected.gooseInterface || '—' }}{{ selected.gooseSubscribeSkip ? ` · 订阅跳过：${selected.gooseSubscribeSkip}` : '' }}</div>
        <div>
          入向点序：
          <span v-for="p in (selected.goosePoints || [])" :key="p.paramName" style="margin-right:10px">
            {{ p.index }} {{ p.paramName }}{{ p.description ? `(${p.description})` : '' }}
          </span>
        </div>
      </div>

      <el-tabs v-model="tab" @tab-change="expanded = null">
        <el-tab-pane label="GOOSE" name="goose" />
        <el-tab-pane label="MMS" name="mms" />
      </el-tabs>

        <div style="display:flex;gap:8px;margin-bottom:8px;flex-wrap:wrap;align-items:center">
          <el-radio-group v-model="resultFilter" size="small">
            <el-radio-button value="all">全部结果</el-radio-button>
            <el-radio-button value="applied">仅成功</el-radio-button>
          </el-radio-group>
          <el-select
            v-if="tab === 'mms'"
            v-model="mmsServiceFilter"
            size="small"
            clearable
            placeholder="全部服务"
            style="width:160px"
          >
            <el-option v-for="s in mmsServiceOptions" :key="s" :label="s" :value="s" />
          </el-select>
          <span class="muted" style="font-size:12px">共 {{ filteredMessages.length }} 条（内存环）</span>
        </div>

      <el-table
        :data="filteredMessages"
        size="small"
        border
        stripe
        row-key="id"
        @row-click="onRowClick"
      >
        <el-table-column prop="localTime" label="时间" width="120" />
        <el-table-column label="方向" width="90">
          <template #default="{ row }">{{ directionLabel(row) }}</template>
        </el-table-column>
        <el-table-column v-if="tab === 'mms'" prop="service" label="服务" width="100" />
        <el-table-column prop="summary" label="摘要" min-width="280" show-overflow-tooltip />
        <el-table-column label="结果" width="140">
          <template #default="{ row }">
            <el-tag :type="resultTagType(row.result)" size="small">{{ resultLabel(row.result) }}</el-tag>
          </template>
        </el-table-column>
      </el-table>

      <div v-if="expanded" class="card" style="margin-top:12px;background:#fafafa">
        <p class="card-title">报文详情 #{{ expanded.id }}</p>
        <el-descriptions :column="2" size="small" border style="margin-bottom:10px">
          <el-descriptions-item label="接收时间">{{ expanded.localTime }}</el-descriptions-item>
          <el-descriptions-item label="结果">{{ resultLabel(expanded.result) }}</el-descriptions-item>
        </el-descriptions>

        <template v-if="expanded.protocol === 'goose'">
          <p class="pdu-title">GOOSE</p>
          <el-descriptions :column="2" size="small" border class="pdu-block">
            <el-descriptions-item label="APPID">{{ formatAppId(expanded.appId) }} ({{ expanded.appId ?? '—' }})</el-descriptions-item>
            <el-descriptions-item label="simulation / test">{{ boolLabel(expanded.simulation ?? expanded.isTest) }}</el-descriptions-item>
          </el-descriptions>

          <p class="pdu-title">goosePdu</p>
          <el-descriptions :column="1" size="small" border class="pdu-block">
            <el-descriptions-item label="gocbRef">{{ expanded.goCbRef || '—' }}</el-descriptions-item>
            <el-descriptions-item label="timeAllowedToLive">{{ expanded.timeAllowedToLive ?? '—' }}</el-descriptions-item>
            <el-descriptions-item label="datSet">{{ expanded.datSet || '—' }}</el-descriptions-item>
            <el-descriptions-item label="goID">{{ expanded.goId || '—' }}</el-descriptions-item>
            <el-descriptions-item label="t">{{ formatGooseT(expanded.t) }}</el-descriptions-item>
            <el-descriptions-item label="stNum">{{ expanded.stNum ?? '—' }}</el-descriptions-item>
            <el-descriptions-item label="sqNum">{{ expanded.sqNum ?? '—' }}</el-descriptions-item>
            <el-descriptions-item label="simulation">{{ boolLabel(expanded.simulation ?? expanded.isTest) }}</el-descriptions-item>
            <el-descriptions-item label="confRev">{{ expanded.confRev ?? '—' }}</el-descriptions-item>
            <el-descriptions-item label="ndsCom">{{ boolLabel(expanded.ndsCom) }}</el-descriptions-item>
            <el-descriptions-item label="numDatSetEntries">{{ expanded.numDatSetEntries ?? (expanded.allData?.length ?? '—') }}</el-descriptions-item>
          </el-descriptions>

          <p class="pdu-title">allData</p>
          <el-table :data="expanded.allData || allDataFromValues(expanded)" size="small" border>
            <el-table-column prop="index" label="#" width="50" />
            <el-table-column prop="paramName" label="ParamName" width="100" />
            <el-table-column prop="type" label="Type" width="120" />
            <el-table-column label="Value">
              <template #default="{ row }">{{ formatCellValue(row.value) }}</template>
            </el-table-column>
            <el-table-column prop="description" label="说明" min-width="140" show-overflow-tooltip />
          </el-table>
        </template>

          <template v-else-if="expanded.protocol === 'mms'">
          <p class="pdu-title">MMS</p>
          <el-descriptions :column="1" size="small" border class="pdu-block">
            <el-descriptions-item label="service">{{ expanded.service || '—' }}</el-descriptions-item>
            <el-descriptions-item label="direction">{{ directionLabel(expanded) }}</el-descriptions-item>
            <el-descriptions-item label="peer">{{ expanded.clientPeer || '—' }}</el-descriptions-item>
            <el-descriptions-item label="paramName">{{ expanded.paramName || '—' }}</el-descriptions-item>
            <el-descriptions-item label="objectRef">{{ expanded.objectRef || '—' }}</el-descriptions-item>
            <el-descriptions-item label="datSet">{{ expanded.datSet || '—' }}</el-descriptions-item>
            <el-descriptions-item label="orCat / fc">{{ expanded.orCat || '—' }}</el-descriptions-item>
            <el-descriptions-item label="ctlNum">{{ expanded.ctlNum ?? '—' }}</el-descriptions-item>
            <el-descriptions-item label="sqNum / seq">{{ expanded.sqNum ?? '—' }}</el-descriptions-item>
            <el-descriptions-item label="test">{{ boolLabel(expanded.isTest) }}</el-descriptions-item>
            <el-descriptions-item label="summary">{{ expanded.summary || '—' }}</el-descriptions-item>
          </el-descriptions>
          <div v-if="expanded.values" style="margin-top:10px">
            <p class="pdu-title">values</p>
            <el-table :data="dictRows(expanded.values)" size="small" border>
              <el-table-column prop="key" label="Ref / Param" width="220" />
              <el-table-column prop="value" label="值" />
            </el-table>
          </div>
          <div v-if="expanded.allData?.length" style="margin-top:10px">
            <p class="pdu-title">report data</p>
            <el-table :data="expanded.allData" size="small" border>
              <el-table-column prop="index" label="#" width="50" />
              <el-table-column prop="paramName" label="Ref" min-width="200" show-overflow-tooltip />
              <el-table-column prop="type" label="Type" width="120" />
              <el-table-column label="Value">
                <template #default="{ row }">{{ formatCellValue(row.value) }}</template>
              </el-table-column>
            </el-table>
          </div>
        </template>

        <template v-else>
          <el-descriptions :column="1" size="small" border>
            <el-descriptions-item label="摘要">{{ expanded.summary }}</el-descriptions-item>
          </el-descriptions>
        </template>

        <div v-if="expanded.writes" style="margin-top:10px">
          <p class="pdu-title">写入（已应用）</p>
          <el-table :data="dictRows(expanded.writes)" size="small" border>
            <el-table-column prop="key" label="ParamName" width="120" />
            <el-table-column prop="value" label="值" />
          </el-table>
        </div>
      </div>
    </div>

    <div v-else class="card muted">请先在上方表格点选一台 IED。</div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import {
  getIec61850, getIec61850Messages, clearIec61850Messages,
  joinHubChannel, leaveHubChannel, onHubMethod
} from '@/services/api.js'
import { RealtimeMethods, RealtimeChannels } from '@/services/constants.js'
import { ElMessage } from 'element-plus'

const devices = ref([])
const selectedKey = ref('')
const messages = ref([])
const tab = ref('goose')
const resultFilter = ref('all')
const mmsServiceFilter = ref('')
const paused = ref(false)
const expanded = ref(null)
const maxKeep = 3000

const selected = computed(() =>
  devices.value.find(d => d.serverName === selectedKey.value) || null
)

const mmsServiceOptions = computed(() => {
  const set = new Set()
  for (const m of messages.value) {
    if (m.protocol === 'mms' && m.service) set.add(m.service)
  }
  return [...set].sort()
})

const filteredMessages = computed(() => {
  let list = messages.value
  if (selectedKey.value)
    list = list.filter(m => m.server === selectedKey.value || m.iedName === selected.value?.iedName)
  if (tab.value === 'mms') {
    list = list.filter(m => m.protocol === 'mms')
    if (mmsServiceFilter.value)
      list = list.filter(m => m.service === mmsServiceFilter.value)
  } else {
    list = list.filter(m => m.protocol === 'goose' || m.protocol === 'system')
  }
  if (resultFilter.value === 'applied')
    list = list.filter(m => m.result === 'applied')
  return list
})

function formatAppId(id) {
  if (id == null || id === '') return '—'
  return '0x' + Number(id).toString(16).toUpperCase().padStart(4, '0')
}

function formatGooseT(utc) {
  if (!utc) return '—'
  const d = new Date(utc)
  if (Number.isNaN(d.getTime())) return String(utc)
  return d.toISOString().replace('T', ' ').replace('Z', ' UTC')
}

function boolLabel(v) {
  return v ? 'True' : 'False'
}

function formatCellValue(v) {
  if (v === true) return 'True'
  if (v === false) return 'False'
  if (v == null) return '—'
  return String(v)
}

function allDataFromValues(row) {
  const vals = row?.values
  if (!vals) return []
  return Object.entries(vals).map(([paramName, value], index) => ({
    index,
    paramName,
    type: typeof value === 'boolean' ? 'boolean' : typeof value === 'number' ? 'floating-point' : typeof value,
    value,
    description: ''
  }))
}

function formatLocalFromUtc(utc) {
  if (!utc) return '—'
  const d = new Date(utc)
  if (Number.isNaN(d.getTime())) return '—'
  const pad = (n, w = 2) => String(n).padStart(w, '0')
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`
}

function directionLabel(row) {
  if (row.direction === 'system' || row.service === 'Associate' || row.service === 'Release')
    return '系统'
  if (row.direction === 'egress') return '→ 出向'
  return '← 入向'
}

function resultTagType(result) {
  if (result === 'applied') return 'success'
  if (result === 'system') return 'info'
  if (String(result || '').startsWith('skip:')) return 'warning'
  if (result === 'error') return 'danger'
  return 'info'
}

function resultLabel(result) {
  if (result === 'applied') return '已应用'
  if (result === 'system') return '系统'
  if (result === 'skip:test') return '丢弃 test'
  if (result === 'skip:stNum') return '忽略 stNum'
  if (String(result || '').startsWith('skip:')) return result
  return result || '—'
}

function dictRows(obj) {
  return Object.entries(obj || {}).map(([key, value]) => ({ key, value: String(value) }))
}

function iedRowClass({ row }) {
  return row.serverName === selectedKey.value ? 'current-ied-row' : ''
}

function onSelectIed(row) {
  if (!row) return
  selectedKey.value = row.serverName
  expanded.value = null
  loadMessages()
}

function onRowClick(row) {
  expanded.value = row
}

function prependMessage(msg) {
  if (paused.value) return
  if (messages.value.some(m => m.id === msg.id)) return
  messages.value = [msg, ...messages.value].slice(0, maxKeep)
}

async function reload() {
  try {
    const data = await getIec61850()
    devices.value = data.devices || []
    if (!selectedKey.value && devices.value.length)
      selectedKey.value = devices.value[0].serverName
    else if (selectedKey.value && !devices.value.some(d => d.serverName === selectedKey.value))
      selectedKey.value = devices.value[0]?.serverName || ''
    await loadMessages()
  } catch (e) {
    ElMessage.error(e.message)
  }
}

async function loadMessages() {
  try {
    const data = await getIec61850Messages(selectedKey.value || undefined, 200)
    messages.value = data.messages || []
  } catch (e) {
    console.warn(e)
  }
}

async function clearMessages() {
  try {
    await clearIec61850Messages()
    messages.value = []
    expanded.value = null
    ElMessage.success('已清空')
  } catch (e) {
    ElMessage.error(e.message)
  }
}

onMounted(async () => {
  await reload()
  try {
    onHubMethod(RealtimeMethods.ReceiveIec61850Message, msg => {
      prependMessage(msg)
      if (msg.server) {
        const d = devices.value.find(x => x.serverName === msg.server)
        if (d) {
          if (msg.result === 'applied' && msg.stNum != null) {
            d.lastGooseStNum = msg.stNum
            d.lastGooseUtc = msg.utc
          }
        }
        if (msg.protocol === 'mms' && (msg.service === 'Associate' || msg.service === 'Release')) {
          getIec61850().then(data => { devices.value = data.devices || [] }).catch(() => {})
        }
      }
    })
    await joinHubChannel(RealtimeChannels.Iec61850)
  } catch { /* ignore */ }
})

onBeforeUnmount(() => {
  try { leaveHubChannel(RealtimeChannels.Iec61850) } catch { /* ignore */ }
})
</script>

<style scoped>
.muted { color: #909399; }
:deep(.current-ied-row) { --el-table-tr-bg-color: #ecf5ff; }
.pdu-title {
  font-weight: 600;
  margin: 12px 0 6px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 13px;
}
.pdu-block { margin-bottom: 4px; }
.pdu-block :deep(.el-descriptions__label) {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  width: 160px;
}
</style>
