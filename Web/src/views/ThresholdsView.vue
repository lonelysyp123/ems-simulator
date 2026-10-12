<template>
  <div>
    <div class="card">
      <p class="card-title">BMS 告警门限</p>
      <p class="hint">
        编辑工程值后写入簇模型 <code>ClusterThresholds</code>；保护评估在设备模型层，与点表无关。
        若当前 BMS 点表暴露了对应寄存器，会同步写入 Holding。
      </p>
      <el-space wrap :size="12">
        <el-select v-model="unitNumber" style="width:120px" @change="reload">
          <el-option v-for="i in unitCount" :key="i" :label="`舱 ${i}`" :value="i" />
        </el-select>
        <el-select v-model="rackMode" style="width:160px">
          <el-option label="全部簇 (r*)" value="*" />
          <el-option
            v-for="i in clusterCount"
            :key="i"
            :label="`簇 ${i - 1} (r${i - 1})`"
            :value="String(i - 1)"
          />
        </el-select>
        <el-select v-model="levelFilter" style="width:140px" clearable placeholder="告警等级">
          <el-option label="三级（保护）" :value="3" />
          <el-option label="二级（告警）" :value="2" />
          <el-option label="一级（故障）" :value="1" />
        </el-select>
        <el-select v-model="categoryFilter" style="width:150px" clearable placeholder="类别" filterable>
          <el-option v-for="c in categories" :key="c" :label="c" :value="c" />
        </el-select>
        <el-input v-model="keyword" clearable placeholder="搜索属性/说明" style="width:200px" />
        <el-checkbox v-model="hideRecovery">隐藏恢复门限</el-checkbox>
        <el-button @click="reload" :loading="loading">刷新</el-button>
        <el-button type="primary" :loading="applying" :disabled="!dirtyRows.length" @click="applyDirty">
          下发已改 ({{ dirtyRows.length }})
        </el-button>
        <el-button type="warning" :loading="applying" :disabled="!filteredRows.length" @click="applyFiltered">
          下发当前列表
        </el-button>
      </el-space>
    </div>

    <div class="card" v-if="snap">
      <p class="card-title">
        {{ snap.device }} · 参考簇 r{{ snap.rackIndex }}（共 {{ snap.clusterCount }} 簇）
        <span class="meta">目标：{{ targetRackLabel }}</span>
      </p>
      <el-table :data="filteredRows" size="small" border stripe max-height="640" row-key="propertyName">
        <el-table-column prop="propertyName" label="属性" width="220" fixed show-overflow-tooltip />
        <el-table-column prop="level" label="等级" width="72">
          <template #default="{ row }">
            <el-tag size="small" :type="levelTagType(row.level)">{{ levelLabel(row.level) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="category" label="类别" width="100" />
        <el-table-column prop="description" label="说明" min-width="220" show-overflow-tooltip />
        <el-table-column label="单位" width="80">
          <template #default="{ row }">{{ row.unitHint || '—' }}</template>
        </el-table-column>
        <el-table-column label="Scale" width="70">
          <template #default="{ row }">{{ row.exposedOnProtocol && row.scale ? row.scale : '—' }}</template>
        </el-table-column>
        <el-table-column label="当前工程值" width="110">
          <template #default="{ row }">
            {{ formatEng(row.engineeringValue) }}
          </template>
        </el-table-column>
        <el-table-column label="设定工程值" width="130">
          <template #default="{ row }">
            <el-input-number
              v-model="draft[row.propertyName]"
              :controls="false"
              :precision="precisionOf(row)"
              size="small"
              style="width:110px"
            />
          </template>
        </el-table-column>
        <el-table-column label="原始值" width="90">
          <template #default="{ row }">
            {{ rawOf(row) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="100" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" :loading="applying === row.propertyName" @click="applyOne(row)">
              下发
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <div class="card" v-if="output.length">
      <p class="card-title">执行输出</p>
      <div class="log-view" style="height:220px">
        <div v-for="(line, i) in output" :key="i" class="log-line" :class="line.cls">{{ line.text }}</div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, reactive, onMounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getConfig, getRackThresholds, postRackThresholds } from '@/services/api.js'

const unitNumber = ref(1)
const unitCount = ref(1)
const rackMode = ref('*')
const levelFilter = ref(null)
const categoryFilter = ref(null)
const keyword = ref('')
const hideRecovery = ref(true)
const loading = ref(false)
const applying = ref(false)
const snap = ref(null)
const draft = reactive({})
const output = ref([])

const clusterCount = computed(() => snap.value?.clusterCount || 1)

const targetRackLabel = computed(() => {
  const device = snap.value?.device || `bms${unitNumber.value}`
  return rackMode.value === '*' ? `${device} 全部簇` : `${device} 簇 r${rackMode.value}`
})

const categories = computed(() => {
  const set = new Set((snap.value?.points || []).map(p => p.category).filter(Boolean))
  return [...set].sort()
})

const filteredRows = computed(() => {
  let rows = snap.value?.points || []
  if (hideRecovery.value) rows = rows.filter(r => !r.isRecovery)
  if (levelFilter.value) rows = rows.filter(r => r.level === levelFilter.value)
  if (categoryFilter.value) rows = rows.filter(r => r.category === categoryFilter.value)
  const kw = keyword.value.trim().toLowerCase()
  if (kw) {
    rows = rows.filter(r =>
      (r.paramName || '').toLowerCase().includes(kw) ||
      (r.propertyName || '').toLowerCase().includes(kw) ||
      (r.protocolParamName || '').toLowerCase().includes(kw) ||
      (r.description || '').toLowerCase().includes(kw)
    )
  }
  return rows
})

const dirtyRows = computed(() =>
  filteredRows.value.filter(r => {
    const cur = draft[r.propertyName]
    if (cur == null || Number.isNaN(Number(cur))) return false
    if (r.engineeringValue == null) return true
    return Math.abs(Number(cur) - Number(r.engineeringValue)) > 1e-9
  })
)

function levelLabel(lv) {
  if (lv === 1) return '一级'
  if (lv === 2) return '二级'
  if (lv === 3) return '三级'
  return '—'
}

function levelTagType(lv) {
  if (lv === 1) return 'danger'
  if (lv === 2) return 'warning'
  if (lv === 3) return 'info'
  return ''
}

function formatEng(v) {
  if (v == null || Number.isNaN(Number(v))) return '—'
  const n = Number(v)
  if (Math.abs(n) >= 100) return n.toFixed(1)
  if (Math.abs(n) >= 10) return n.toFixed(2)
  return n.toFixed(3)
}

function precisionOf(row) {
  if ((row.unitHint || '').includes('pu')) return 3
  if (row.unitHint === '°C') return 1
  if (row.unitHint === 'V' && (row.propertyName || '').includes('Cell')) return 3
  if (row.unitHint === 'V') return 1
  if (row.scale >= 1000) return 3
  if (row.scale >= 100) return 2
  if (row.scale >= 10) return 1
  return 0
}

function rawOf(row) {
  if (!row.exposedOnProtocol || !row.scale) return '—'
  const eng = draft[row.propertyName]
  if (eng == null || Number.isNaN(Number(eng))) return '—'
  return Math.round(Number(eng) * row.scale)
}

function pushOut(text, ok = true) {
  output.value.unshift({ text, cls: ok ? 'ok' : 'err' })
  if (output.value.length > 200) output.value.length = 200
}

async function reload() {
  loading.value = true
  try {
    // 读参考簇：全部簇模式下用 0 号簇展示当前值
    const rackForRead = rackMode.value === '*' ? 0 : Number(rackMode.value)
    const data = await getRackThresholds(unitNumber.value, rackForRead)
    snap.value = data
    for (const p of data.points || []) {
      draft[p.propertyName] = p.engineeringValue != null ? Number(p.engineeringValue) : null
    }
  } catch (e) {
    ElMessage.error(e.message || String(e))
  } finally {
    loading.value = false
  }
}

async function applyRows(rows) {
  if (!rows.length) return
  const items = rows.map(row => ({
    propertyName: row.propertyName,
    engineeringValue: Number(draft[row.propertyName])
  }))
  try {
    await ElMessageBox.confirm(
      `将写入 ${items.length} 条门限到模型（目标 ${targetRackLabel.value}）。确认？`,
      '确认下发门限',
      { type: 'warning', confirmButtonText: '下发', cancelButtonText: '取消' }
    )
  } catch {
    return
  }

  applying.value = true
  try {
    const res = await postRackThresholds(unitNumber.value, {
      rack: rackMode.value,
      items
    })
    const ok = !!res?.ok
    for (const msg of res?.messages || []) pushOut(msg, ok)
    for (const err of res?.errors || []) pushOut(err, false)
    if (ok) {
      ElMessage.success(`完成：写入 ${res.written} 条`)
      await reload()
    } else {
      ElMessage.error(res?.errors?.[0] || '下发失败')
    }
  } catch (e) {
    ElMessage.error(e.message || String(e))
    pushOut(e.message || String(e), false)
  } finally {
    applying.value = false
  }
}

async function applyOne(row) {
  applying.value = row.propertyName
  try {
    const res = await postRackThresholds(unitNumber.value, {
      rack: rackMode.value,
      items: [{ propertyName: row.propertyName, engineeringValue: Number(draft[row.propertyName]) }]
    })
    const ok = !!res?.ok
    for (const msg of res?.messages || []) pushOut(msg, ok)
    for (const err of res?.errors || []) pushOut(err, false)
    if (ok) {
      ElMessage.success('已下发')
      await reload()
    } else {
      ElMessage.error(res?.errors?.[0] || '下发失败')
    }
  } catch (e) {
    ElMessage.error(e.message || String(e))
    pushOut(e.message || String(e), false)
  } finally {
    applying.value = false
  }
}

function applyDirty() {
  return applyRows(dirtyRows.value)
}

function applyFiltered() {
  return applyRows(filteredRows.value)
}

watch(rackMode, async (mode, prev) => {
  // 切换到具体簇时刷新该簇当前值；切到 * 时仍用 r0 作参考
  if (mode !== '*' || prev !== '*') await reload()
})

onMounted(async () => {
  try {
    const cfg = await getConfig()
    unitCount.value = Math.max(1, cfg?.simulator?.channelCount || cfg?.simulator?.unitCount || 1)
  } catch { /* ignore */ }
  await reload()
})
</script>

<style scoped>
.hint {
  margin: 0 0 10px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}
.hint code {
  font-size: 12px;
  background: #f4f4f5;
  padding: 1px 4px;
  border-radius: 3px;
}
.meta {
  margin-left: 8px;
  font-weight: 400;
  color: #909399;
  font-size: 12px;
}
.log-view {
  overflow: auto;
  background: #1e1e1e;
  color: #d4d4d4;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 12px;
  padding: 8px;
  border-radius: 4px;
}
.log-line { white-space: pre-wrap; word-break: break-all; margin-bottom: 2px; }
.log-line.ok { color: #b5cea8; }
.log-line.err { color: #f48771; }
</style>
