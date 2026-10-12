<template>
  <div>
    <div class="card">
      <p class="card-title">白盒切片</p>
      <p class="hint">
        开启采集后，每当 EMS（或 dpc）写入 PCS 有功/无功设定（yt0/yt1）时，记录当时电网、电表、PCS、BMS 瞬时状态。
        用于验证下垂调压：先调电网电压 → 观察 EMS 写 Q → 对照切片中的 PCC V 与设定值。
      </p>
      <div class="toolbar">
        <el-switch
          v-model="enabled"
          active-text="采集开启"
          inactive-text="采集关闭"
          @change="onToggleEnabled"
        />
        <span class="meta">已存 {{ status.count ?? 0 }} / {{ status.maxCount ?? '—' }}</span>
        <el-button size="small" @click="refresh">刷新</el-button>
        <el-button size="small" type="danger" plain @click="onClear">清空</el-button>
      </div>
    </div>

    <div class="card">
      <el-table
        :data="rows"
        size="small"
        border
        stripe
        highlight-current-row
        @row-click="onSelect"
        style="width: 100%"
      >
        <el-table-column prop="sequence" label="#" width="70" />
        <el-table-column label="时间(本地)" width="180">
          <template #default="{ row }">{{ fmtTime(row.timestampUtc) }}</template>
        </el-table-column>
        <el-table-column prop="serverName" label="设备" width="90" />
        <el-table-column prop="paramName" label="点名" width="70" />
        <el-table-column label="设定变化" min-width="160">
          <template #default="{ row }">
            {{ fmtDelta(row) }}
          </template>
        </el-table-column>
        <el-table-column label="电网设定 V" width="110">
          <template #default="{ row }">{{ fmtVolt(row.gridNominalLineVoltageV) }}</template>
        </el-table-column>
        <el-table-column label="PCC V" width="100">
          <template #default="{ row }">{{ fmtVolt(row.pccLineVoltageV) }}</template>
        </el-table-column>
        <el-table-column label="电表 P/Q" min-width="140">
          <template #default="{ row }">
            {{ Number(row.meterActivePowerKw).toFixed(1) }} / {{ Number(row.meterReactivePowerKvar).toFixed(1) }}
          </template>
        </el-table-column>
        <el-table-column label="PCS 设定 P/Q" min-width="140">
          <template #default="{ row }">
            {{ Number(row.pcsActiveSettingKw).toFixed(1) }} / {{ Number(row.pcsReactiveSettingKvar).toFixed(1) }}
          </template>
        </el-table-column>
        <el-table-column label="PCS 实际 P/Q" min-width="140">
          <template #default="{ row }">
            {{ Number(row.pcsActiveKw).toFixed(1) }} / {{ Number(row.pcsReactiveKvar).toFixed(1) }}
          </template>
        </el-table-column>
      </el-table>
    </div>

    <div v-if="detail" class="card">
      <p class="card-title">切片详情 #{{ detail.sequence }}</p>
      <el-descriptions :column="2" border size="small">
        <el-descriptions-item label="触发点">
          {{ detail.trigger?.serverName }}.{{ detail.trigger?.paramName }}
          ({{ detail.trigger?.kind }})
        </el-descriptions-item>
        <el-descriptions-item label="设定值">
          {{ detail.trigger?.previousEngineeringValue ?? '—' }}
          → {{ detail.trigger?.engineeringValue }} {{ detail.trigger?.unit }}
        </el-descriptions-item>
        <el-descriptions-item label="电网额定 V">{{ fmtVolt(detail.grid?.nominalLineVoltageV) }}</el-descriptions-item>
        <el-descriptions-item label="PCC V">{{ fmtVolt(detail.grid?.pccLineVoltageV) }}</el-descriptions-item>
        <el-descriptions-item label="35kV 母线">{{ fmtVolt(detail.grid?.stationBus35LineVoltageV) }}</el-descriptions-item>
        <el-descriptions-item label="系统频率">{{ Number(detail.grid?.systemFrequencyHz ?? 0).toFixed(2) }} Hz</el-descriptions-item>
        <el-descriptions-item label="主断">{{ detail.grid?.mainBreakerClosed ? '合' : '分' }}</el-descriptions-item>
        <el-descriptions-item label="单元断">{{ detail.topology?.unitBreakerClosed ? '合' : '分' }}</el-descriptions-item>
      </el-descriptions>

      <p class="section-title">电表</p>
      <el-descriptions :column="2" border size="small">
        <el-descriptions-item label="线电压">
          Uab {{ fmtVolt(detail.meter?.lineVoltageAB) }} /
          Ubc {{ fmtVolt(detail.meter?.lineVoltageBC) }} /
          Uca {{ fmtVolt(detail.meter?.lineVoltageCA) }}
        </el-descriptions-item>
        <el-descriptions-item label="相电流">
          Ia {{ Number(detail.meter?.phaseACurrent ?? 0).toFixed(1) }} /
          Ib {{ Number(detail.meter?.phaseBCurrent ?? 0).toFixed(1) }} /
          Ic {{ Number(detail.meter?.phaseCCurrent ?? 0).toFixed(1) }} A
        </el-descriptions-item>
        <el-descriptions-item label="P / Q / S">
          {{ Number(detail.meter?.totalActivePowerKw ?? 0).toFixed(1) }} kW /
          {{ Number(detail.meter?.totalReactivePowerKvar ?? 0).toFixed(1) }} kvar /
          {{ Number(detail.meter?.totalApparentPowerKva ?? 0).toFixed(1) }} kVA
        </el-descriptions-item>
        <el-descriptions-item label="PF / f">
          {{ Number(detail.meter?.powerFactor ?? 0).toFixed(3) }} /
          {{ Number(detail.meter?.frequencyHz ?? 0).toFixed(2) }} Hz
        </el-descriptions-item>
      </el-descriptions>

      <p class="section-title">PCS（通道 {{ detail.pcs?.channelIndex }}）</p>
      <el-descriptions :column="2" border size="small">
        <el-descriptions-item label="设定 P/Q">
          {{ Number(detail.pcs?.pcsActivePowerSettingKw ?? 0).toFixed(1) }} kW /
          {{ Number(detail.pcs?.pcsReactivePowerSettingKvar ?? 0).toFixed(1) }} kvar
        </el-descriptions-item>
        <el-descriptions-item label="实际 P/Q">
          {{ Number(detail.pcs?.activePowerKw ?? 0).toFixed(1) }} kW /
          {{ Number(detail.pcs?.reactivePowerKvar ?? 0).toFixed(1) }} kvar
        </el-descriptions-item>
        <el-descriptions-item label="交流 V / f">
          {{ fmtVolt(detail.pcs?.lineVoltageV) }} /
          {{ Number(detail.pcs?.frequencyHz ?? 0).toFixed(2) }} Hz
        </el-descriptions-item>
        <el-descriptions-item label="状态">
          启停={{ detail.pcs?.pcsOnOffSwitch ? 1 : 0 }}
          mode={{ detail.pcs?.simulatorMode }}
          op={{ detail.pcs?.operationStatus }}
          黑启动={{ detail.pcs?.blackStartEnabled ? 1 : 0 }}
        </el-descriptions-item>
      </el-descriptions>

      <p class="section-title">BMS（bms{{ (detail.bms?.bmsIndex ?? 0) + 1 }}）</p>
      <el-descriptions :column="2" border size="small">
        <el-descriptions-item label="并网链路">
          linked={{ detail.bms?.isPcsLinked ? 1 : 0 }}
          GridConnectStatus={{ detail.bms?.gridConnectStatus }}
        </el-descriptions-item>
        <el-descriptions-item label="SOC / V / I / P">
          {{ Number(detail.bms?.socPercent ?? 0).toFixed(1) }}% /
          {{ Number(detail.bms?.totalVoltageV ?? 0).toFixed(1) }} V /
          {{ Number(detail.bms?.currentA ?? 0).toFixed(1) }} A /
          {{ Number(detail.bms?.powerKw ?? 0).toFixed(1) }} kW
        </el-descriptions-item>
        <el-descriptions-item label="运行 / 限功率">
          op={{ detail.bms?.operationStatus ?? '—' }}
          MaxChg={{ detail.bms?.maxChargePowerKw ?? '—' }}
          MaxDis={{ detail.bms?.maxDischargePowerKw ?? '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="采集说明">{{ detail.note }}</el-descriptions-item>
      </el-descriptions>

      <el-collapse style="margin-top:12px">
        <el-collapse-item title="原始 JSON" name="json">
          <pre class="json-pre">{{ JSON.stringify(detail, null, 2) }}</pre>
        </el-collapse-item>
      </el-collapse>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  getDroopSliceStatus,
  getDroopSlices,
  getDroopSlice,
  clearDroopSlices,
  setDroopSliceConfig
} from '@/services/api.js'

const enabled = ref(false)
const status = ref({})
const rows = ref([])
const detail = ref(null)
let timer = null

function fmtTime(utc) {
  if (!utc) return '—'
  const d = new Date(utc)
  return d.toLocaleString()
}
function fmtVolt(v) {
  if (v == null || Number.isNaN(Number(v))) return '—'
  const n = Number(v)
  return n >= 1000 ? `${(n / 1000).toFixed(2)} kV` : `${n.toFixed(1)} V`
}
function fmtDelta(row) {
  const unit = row.unit || ''
  const prev = row.previousEngineeringValue
  const cur = row.engineeringValue
  if (prev == null) return `${Number(cur).toFixed(1)} ${unit}`
  return `${Number(prev).toFixed(1)} → ${Number(cur).toFixed(1)} ${unit}`
}

async function refresh() {
  try {
    status.value = await getDroopSliceStatus()
    enabled.value = !!status.value.enabled
    rows.value = await getDroopSlices(200, 0)
  } catch (e) {
    ElMessage.error(e.message)
  }
}

async function onToggleEnabled(val) {
  try {
    status.value = await setDroopSliceConfig({ enabled: !!val })
    enabled.value = !!status.value.enabled
    ElMessage.success(enabled.value ? '切片采集已开启' : '切片采集已关闭')
  } catch (e) {
    ElMessage.error(e.message)
    enabled.value = !val
  }
}

async function onClear() {
  try {
    await ElMessageBox.confirm('确定清空全部切片？', '确认', { type: 'warning' })
    status.value = await clearDroopSlices()
    detail.value = null
    await refresh()
  } catch (e) {
    if (e !== 'cancel') ElMessage.error(e.message)
  }
}

async function onSelect(row) {
  try {
    detail.value = await getDroopSlice(row.id)
  } catch (e) {
    ElMessage.error(e.message)
  }
}

onMounted(async () => {
  await refresh()
  timer = setInterval(refresh, 2000)
})
onUnmounted(() => {
  if (timer) clearInterval(timer)
})
</script>

<style scoped>
.hint {
  font-size: 13px;
  color: #606266;
  margin: 0 0 12px;
  line-height: 1.5;
}
.toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.meta {
  font-size: 13px;
  color: #909399;
}
.section-title {
  margin: 16px 0 8px;
  font-weight: 600;
  color: #303133;
}
.json-pre {
  margin: 0;
  max-height: 360px;
  overflow: auto;
  font-size: 12px;
  background: #f5f7fa;
  padding: 8px;
  border-radius: 4px;
}
</style>
