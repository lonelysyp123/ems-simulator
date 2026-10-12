<template>
  <div>
    <div class="card">
      <el-select v-model="unitNumber" style="width:160px" @change="onUnitChange">
        <el-option v-for="i in unitCount" :key="i" :label="`舱 ${i}`" :value="i" />
      </el-select>
    </div>

    <div class="card" v-if="data">
      <p class="card-title">电池舱总览 — 舱 {{ data.unitNumber }}</p>
      <div class="metric-grid">
        <div class="metric-item"><div class="label">总电压</div><div class="value">{{ data.totalVoltage.toFixed(1) }} V</div></div>
        <div class="metric-item"><div class="label">总电流</div><div class="value">{{ data.totalCurrent.toFixed(1) }} A</div></div>
        <div class="metric-item"><div class="label">SOC</div><div class="value">{{ data.soc.toFixed(1) }} %</div></div>
        <div class="metric-item"><div class="label">SOH</div><div class="value">{{ data.soh.toFixed(1) }} %</div></div>
        <div class="metric-item"><div class="label">簇内最高单体</div><div class="value">{{ data.maxCellVoltage.toFixed(3) }} V @ 簇{{ data.maxCellVoltageClusterId }}/包{{ data.maxCellVoltagePackId }}/单体{{ data.maxCellVoltageCellId }}</div></div>
        <div class="metric-item"><div class="label">簇内最低单体</div><div class="value">{{ data.minCellVoltage.toFixed(3) }} V @ 簇{{ data.minCellVoltageClusterId }}/包{{ data.minCellVoltagePackId }}/单体{{ data.minCellVoltageCellId }}</div></div>
        <div class="metric-item"><div class="label">并离网状态</div><div class="value">{{ data.gridConnectStatus }}</div></div>
        <div class="metric-item"><div class="label">黑启动模式</div><div class="value">{{ data.blackStartModeStatus }}</div></div>
      </div>
    </div>

    <div class="card" v-if="data">
      <p class="card-title">簇列表</p>
      <el-table :data="data.clusters" size="small" border stripe>
        <el-table-column prop="clusterId" label="簇Id" width="70" />
        <el-table-column label="总电压(V)"><template #default="{ row }">{{ row.totalVoltage.toFixed(2) }}</template></el-table-column>
        <el-table-column label="总电流(A)"><template #default="{ row }">{{ row.totalCurrent.toFixed(2) }}</template></el-table-column>
        <el-table-column label="功率(kW)"><template #default="{ row }">{{ row.powerKw.toFixed(2) }}</template></el-table-column>
        <el-table-column label="SOC(%)"><template #default="{ row }">{{ row.soc.toFixed(2) }}</template></el-table-column>
        <el-table-column label="SOH(%)"><template #default="{ row }">{{ row.soh.toFixed(2) }}</template></el-table-column>
        <el-table-column label="平均单体(V)"><template #default="{ row }">{{ row.avgCellVoltage.toFixed(4) }}</template></el-table-column>
        <el-table-column label="单体最高(V)"><template #default="{ row }">{{ row.maxCellVoltage.toFixed(4) }}</template></el-table-column>
        <el-table-column label="单体最低(V)"><template #default="{ row }">{{ row.minCellVoltage.toFixed(4) }}</template></el-table-column>
      </el-table>
    </div>

    <div class="card" v-if="data">
      <p class="card-title">簇 SOC / 功率分布</p>
      <div ref="chartEl" style="width:100%;height:280px"></div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount, nextTick, watch } from 'vue'
import * as echarts from 'echarts'
import { getBattery, getConfig, joinHubChannel, leaveHubChannel, onHubMethod, offHubMethod } from '@/services/api.js'
import { RealtimeMethods, RealtimeChannels } from '@/services/constants.js'

const unitNumber = ref(1)
const unitCount = ref(1)
const data = ref(null)
const chartEl = ref(null)
let chart = null

async function reload() {
  try { data.value = await getBattery(unitNumber.value) } catch (e) { console.warn(e) }
  await nextTick()
  renderChart()
}

function renderChart() {
  if (!chartEl.value || !data.value) return
  if (!chart) chart = echarts.init(chartEl.value)
  const clusters = data.value.clusters || []
  chart.setOption({
    tooltip: { trigger: 'axis' },
    legend: { data: ['SOC(%)', '功率(kW)'] },
    grid: { left: 50, right: 30, top: 40, bottom: 40 },
    xAxis: { type: 'category', data: clusters.map(c => `簇${c.clusterId}`) },
    yAxis: [
      { type: 'value', name: 'SOC(%)', min: 0, max: 100 },
      { type: 'value', name: '功率(kW)' }
    ],
    series: [
      { name: 'SOC(%)', type: 'bar', data: clusters.map(c => Number(c.soc.toFixed(2))) },
      { name: '功率(kW)', type: 'line', yAxisIndex: 1, data: clusters.map(c => Number(c.powerKw.toFixed(2))) }
    ]
  })
}

async function joinGroup(n) {
  await joinHubChannel(`${RealtimeChannels.Battery}.${n}`)
}
async function leaveGroup(n) {
  await leaveHubChannel(`${RealtimeChannels.Battery}.${n}`)
}

function onReceiveBattery(d) {
  if (d && d.unitNumber === unitNumber.value) {
    data.value = d
    renderChart()
  }
}

async function onUnitChange(n) {
  const old = n
  // watch 会处理 group 切换；这里只触发 reload
}

watch(unitNumber, async (n, o) => {
  if (o) await leaveGroup(o)
  await joinGroup(n)
  await reload()
})

onMounted(async () => {
  try {
    const cfg = await getConfig()
    unitCount.value = cfg.simulator.channelCount || (cfg.simulator.unitCount * 2) || 1
    if (unitNumber.value > unitCount.value) unitNumber.value = unitCount.value
  } catch { /* ignore */ }
  await reload()
  try {
    onHubMethod(RealtimeMethods.ReceiveBattery, onReceiveBattery)
    await joinGroup(unitNumber.value)
  } catch { /* ignore */ }
})

onBeforeUnmount(() => {
  if (chart) { chart.dispose(); chart = null }
  offHubMethod(RealtimeMethods.ReceiveBattery, onReceiveBattery)
  leaveGroup(unitNumber.value)
})
</script>
