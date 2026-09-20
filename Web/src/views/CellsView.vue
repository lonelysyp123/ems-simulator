<template>
  <div>
    <div class="card">
      <el-select v-model="unitNumber" style="width:140px" @change="reload">
        <el-option v-for="i in unitCount" :key="i" :label="`舱 ${i}`" :value="i" />
      </el-select>
      <el-select v-model="clusterNumber" style="width:140px;margin-left:12px" @change="reload">
        <el-option v-for="i in clusterCount" :key="i" :label="`簇 ${i}`" :value="i" />
      </el-select>
      <el-button style="margin-left:12px" @click="reload">刷新</el-button>
      <el-tag v-if="data" style="margin-left:12px" size="small" type="warning">
        电池节点温度 {{ formatTemp(data.batteryNodeTempC) }}
      </el-tag>
      <el-tag v-if="data" style="margin-left:12px" size="small">
        电压最高 簇{{ clusterNumber }} 包{{ data.maxCellVoltagePackId }} 单体{{ data.maxCellVoltageCellId }}
        {{ formatVoltage(data.maxCellVoltage) }}
        /
        最低 包{{ data.minCellVoltagePackId }} 单体{{ data.minCellVoltageCellId }}
        {{ formatVoltage(data.minCellVoltage) }}
      </el-tag>
      <el-tag v-if="data" style="margin-left:12px" size="small" type="info">
        温度最高 簇{{ clusterNumber }} 包{{ data.maxCellTempPackId }} 单体{{ data.maxCellTempCellId }}
        {{ formatTemp(data.maxCellTemp) }}
        /
        最低 包{{ data.minCellTempPackId }} 单体{{ data.minCellTempCellId }}
        {{ formatTemp(data.minCellTemp) }}
      </el-tag>
    </div>

    <div class="card" v-if="data">
      <div class="section-title">单体电压</div>
      <div class="cell-grid">
        <div class="cell-pack" v-for="(pack, pi) in data.packs" :key="'v-' + pi">
          <div class="pack-title">包 {{ pi }}（{{ data.cellsPerPack }} 节）</div>
          <div class="cell-grid-inner">
            <div
              v-for="(v, ci) in pack"
              :key="ci"
              class="cell-box"
              :class="voltageCellClass(pi, ci)"
              :title="`簇${clusterNumber} 包${pi} 单体${ci} ${formatVoltage(v)}`"
            >
              {{ formatVoltage(v) }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <div class="card" v-if="data">
      <div class="section-title">单体温度</div>
      <div class="cell-grid">
        <div class="cell-pack" v-for="(pack, pi) in data.tempPacks" :key="'t-' + pi">
          <div class="pack-title">包 {{ pi }}（{{ data.cellsPerPack }} 节）</div>
          <div class="cell-grid-inner">
            <div
              v-for="(t, ci) in pack"
              :key="ci"
              class="cell-box"
              :class="tempCellClass(pi, ci)"
              :title="`簇${clusterNumber} 包${pi} 单体${ci} ${formatTemp(t)}`"
            >
              {{ formatTempShort(t) }}
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getCells, getConfig } from '@/services/api.js'

const unitNumber = ref(1)
const clusterNumber = ref(1)
const unitCount = ref(1)
const clusterCount = ref(12)
const data = ref(null)

function formatVoltage(v) {
  const n = Number(v)
  return Number.isFinite(n) && n > 0 ? n.toFixed(3) : '—'
}

function formatTemp(t) {
  const n = Number(t)
  return Number.isFinite(n) && n !== 0 ? `${n.toFixed(1)} °C` : '—'
}

function formatTempShort(t) {
  const n = Number(t)
  return Number.isFinite(n) && n !== 0 ? n.toFixed(1) : '—'
}

function voltageCellClass(packIndex, cellIndex) {
  const d = data.value
  if (!d) return 'cell-empty'
  const v = d.packs?.[packIndex]?.[cellIndex]
  if (!v || v <= 0) return 'cell-empty'
  if (packIndex === d.maxCellVoltagePackId && cellIndex === d.maxCellVoltageCellId) return 'cell-max'
  if (packIndex === d.minCellVoltagePackId && cellIndex === d.minCellVoltageCellId) return 'cell-min'
  return 'cell-normal'
}

function tempCellClass(packIndex, cellIndex) {
  const d = data.value
  if (!d) return 'cell-empty'
  const t = d.tempPacks?.[packIndex]?.[cellIndex]
  if (!Number.isFinite(Number(t)) || Number(t) === 0) return 'cell-empty'
  if (packIndex === d.maxCellTempPackId && cellIndex === d.maxCellTempCellId) return 'cell-max'
  if (packIndex === d.minCellTempPackId && cellIndex === d.minCellTempCellId) return 'cell-min'
  return 'cell-temp-normal'
}

async function reload() {
  try { data.value = await getCells(unitNumber.value, clusterNumber.value) } catch (e) { console.warn(e) }
}

onMounted(async () => {
  try {
    const cfg = await getConfig()
    unitCount.value = cfg.simulator.channelCount || (cfg.simulator.unitCount * 2) || 1
    if (unitNumber.value > unitCount.value) unitNumber.value = unitCount.value
  } catch { /* ignore */ }
  await reload()
})
</script>

<style scoped>
.section-title {
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 8px;
  color: #303133;
}

.cell-box.cell-normal {
  background: #8bc34a;
  color: #fff;
}

.cell-box.cell-temp-normal {
  background: #26a69a;
  color: #fff;
}

.cell-box.cell-max {
  background: #409eff;
  color: #fff;
}

.cell-box.cell-min {
  background: #f56c6c;
  color: #fff;
}

.cell-box.cell-empty {
  background: #c0c4cc;
  color: #fff;
}
</style>
