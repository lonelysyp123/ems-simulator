<template>
  <div class="proj-hub">
    <div class="card run-bar">
      <div class="run-row">
        <span class="card-title" style="margin:0">当前运行</span>
        <el-tag :type="state.source === 'topology' ? 'warning' : 'info'" size="small">
          {{ state.source === 'topology' ? '组态工程' : 'appsettings.json' }}
        </el-tag>
        <span class="meta">储能单元 {{ state.runtimeUnitCount }}</span>
        <span class="meta">光伏单元 {{ state.runtimePvUnitCount }}</span>
        <span v-if="state.activeProjectName" class="meta">运行模板：{{ state.activeProjectName }}</span>
      </div>
      <div class="run-row">
        <span class="mode-label">工程模式</span>
        <el-switch
          v-model="engineeringMode"
          active-text="开启"
          inactive-text="关闭"
          :disabled="busy"
          @change="onModeChange"
        />
        <span class="meta">
          {{ engineeringMode
            ? (selectedProject ? `已选用「${selectedProject.name}」` : '请在工程库中选用一个工程')
            : '关闭后确认，将恢复 appsettings.json' }}
        </span>
        <el-button
          type="primary"
          size="small"
          :loading="applying"
          :disabled="busy || (engineeringMode && !projectId)"
          @click="apply"
        >确认并重新初始化</el-button>
        <el-tag v-if="applyDirty" size="small" type="warning">尚未应用到仿真</el-tag>
      </div>
      <el-alert
        v-if="previewNotes.length"
        type="info"
        :closable="false"
        show-icon
        title="上次应用摘要"
        class="run-notes"
      >
        <ul class="notes">
          <li v-for="(n, i) in previewNotes" :key="i">{{ n }}</li>
        </ul>
      </el-alert>
    </div>

    <el-tabs :model-value="tab" class="hub-tabs" @tab-change="onTab">
      <el-tab-pane label="工程库" name="library">
        <div class="card">
          <div class="head">
            <div>
              <h3 class="card-title">组态工程</h3>
              <p class="desc">选用只指定运行模板，不会重启；确认并重新初始化后才会按该工程重建仿真。</p>
            </div>
            <div class="actions">
              <el-button :disabled="busy" :loading="loading" @click="reloadAll">刷新</el-button>
              <el-button type="primary" :disabled="busy" @click="onCreate">新增工程</el-button>
            </div>
          </div>
          <el-table
            :data="projects"
            v-loading="loading"
            stripe
            border
            size="small"
            empty-text="暂无工程，请点击「新增工程」"
            :row-class-name="rowClassName"
          >
            <el-table-column prop="name" label="工程名称" min-width="180" />
            <el-table-column prop="emuCount" label="储能单元" width="90" align="center" />
            <el-table-column prop="pvCount" label="光伏单元" width="90" align="center" />
            <el-table-column prop="nodeCount" label="节点数" width="90" align="center" />
            <el-table-column label="更新时间" width="180">
              <template #default="{ row }">{{ fmtTime(row.updatedAtUtc) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="110">
              <template #default="{ row }">
                <el-tag v-if="isRunning(row)" type="warning" size="small">运行中</el-tag>
                <el-tag v-else-if="row.id === projectId" type="success" size="small">已选用</el-tag>
                <el-tag v-else-if="row.readOnly" type="info" size="small">只读测试</el-tag>
                <el-tag v-else type="info" size="small" effect="plain">已保存</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="300" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" :disabled="busy" @click="onSelectForRun(row)">选用</el-button>
                <el-button v-if="!row.readOnly" link type="primary" :disabled="busy" @click="onEdit(row)">修改</el-button>
                <el-button v-if="!row.readOnly" link type="primary" :disabled="busy" @click="onCopy(row)">复制</el-button>
                <el-button v-if="!row.readOnly" link type="danger" :disabled="busy" @click="onDelete(row)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </el-tab-pane>

      <el-tab-pane label="设备型号" name="models">
        <div class="card">
          <h3 class="card-title">设备型号与点位表</h3>
          <p class="desc">
            不同设备型号使用不同的 Modbus 点位表；选择后确认并重新初始化，重启后生效。
            新型号点表放入 <code>pointmaps/models/</code> 对应设备类型目录即可自动识别。
            选型独立于工程模式。
          </p>
          <el-form label-width="140px" size="default" class="form" :disabled="busy">
            <el-form-item v-for="t in selectableDeviceTypes" :key="t.id" :label="t.name">
              <el-select
                v-model="deviceSelection[t.id]"
                :placeholder="t.id === 'lc' ? '未选型（片段拼装）' : '未选型（兜底点表）'"
                clearable
                style="width: 360px"
                :disabled="busy"
              >
                <el-option
                  v-for="m in t.models"
                  :key="m.id"
                  :label="m.name"
                  :value="m.id"
                >
                  <span>{{ m.name }}</span>
                  <span v-if="m.description" style="float:right; color:#909399; font-size:12px">{{ m.description }}</span>
                </el-option>
              </el-select>
              <el-tag
                v-if="pointmapEntry(t.id)"
                :type="pointmapEntry(t.id).source === 'selection' ? 'success' : 'info'"
                size="small"
                style="margin-left:8px"
              >
                当前：{{ pointmapEntry(t.id).modelName || pointmapEntry(t.id).modelId || '兜底点表' }}
              </el-tag>
            </el-form-item>
            <el-form-item>
              <el-button
                type="primary"
                :loading="applyingModels"
                :disabled="!deviceModelDirty || busy"
                @click="applyDeviceModels"
              >确认并重新初始化</el-button>
              <span v-if="!deviceModelDirty" class="meta">选型无变更</span>
            </el-form-item>
          </el-form>
        </div>
      </el-tab-pane>
    </el-tabs>

    <div class="card tip-card">
      <div class="card-title">说明</div>
      <ul class="tips">
        <li><strong>选用</strong>只指定运行模板，不会重启；顶栏出现「尚未应用到仿真」后再点确认。</li>
        <li><strong>修改 / 新增 / 复制</strong>进入组态编辑，不影响当前正在跑的仿真，直到确认重新初始化。</li>
        <li><strong>删除</strong>正在运行的工程会清除激活引用，需再选用其它工程或关闭工程模式后确认。</li>
        <li>确认后按工程中的 EMU 数量生成储能单元，并重启后端以重建设备与 Modbus 端口；运行状态（SOC、断路器等）会丢失。</li>
        <li>设备型号与工程模式分开应用，均通过重启生效。未选型按 <code>pointmaps/models/{类型}/standard/</code> 解析；LC 未选型时按片段拼装。</li>
      </ul>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  deleteTopologyProject,
  getDeviceModels,
  getHealth,
  getSystemConfig,
  getTopologyProjects,
  postDeviceModelsApply,
  postSystemApply,
  postTopologyProjectCopy,
  postTopologyProjectNew,
  postTopologyProjectOpen
} from '@/services/api.js'
import { lockSystem, unlockSystem, updateSystemProgress } from '@/services/systemLock.js'

const router = useRouter()
const route = useRoute()
const tab = ref(route.query.tab === 'models' ? 'models' : 'library')
const loading = ref(false)
const applying = ref(false)
const applyingModels = ref(false)
const engineeringMode = ref(false)
const projectId = ref(null)
const projects = ref([])
const previewNotes = ref([])
const deviceTypes = ref([])
const deviceSelection = reactive({})
const deviceSelectionLoaded = ref({})
const state = reactive({
  source: 'appsettings',
  runtimeUnitCount: 0,
  runtimePvUnitCount: 0,
  activeProjectName: '',
  activeProjectId: null,
  pointmaps: []
})

const busy = computed(() => applying.value || applyingModels.value)
const selectableDeviceTypes = computed(() => deviceTypes.value || [])
const selectedProject = computed(() => projects.value.find(p => p.id === projectId.value) || null)
const applyDirty = computed(() => {
  const wantMode = engineeringMode.value
  const wantId = wantMode ? (projectId.value || null) : null
  const curMode = state.source === 'topology'
  const curId = curMode ? (state.activeProjectId || null) : null
  if (wantMode !== curMode) return true
  return wantMode && wantId !== curId
})
const deviceModelDirty = computed(() => {
  for (const t of selectableDeviceTypes.value) {
    const cur = deviceSelection[t.id] ?? null
    const old = deviceSelectionLoaded.value[t.id] ?? null
    if (cur !== old) return true
  }
  return false
})

watch(
  () => route.query.tab,
  t => { tab.value = t === 'models' ? 'models' : 'library' }
)

function onTab(name) {
  router.replace({ path: '/projects', query: name === 'models' ? { tab: 'models' } : {} })
}

function fmtTime(v) {
  if (!v) return '—'
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  return d.toLocaleString()
}

function isRunning(row) {
  return state.source === 'topology' && row.id === state.activeProjectId
}

function rowClassName({ row }) {
  return row.id === projectId.value ? 'is-selected' : ''
}

function pointmapEntry(typeId) {
  return state.pointmaps.find(p => p.typeId === typeId) || null
}

function onModeChange(on) {
  if (!on) projectId.value = null
}

function onSelectForRun(row) {
  engineeringMode.value = true
  projectId.value = row.id
  ElMessage.success(`已选用「${row.name}」，确认后才会重新初始化`)
}

async function reload() {
  const [list, cfg] = await Promise.all([
    getTopologyProjects(),
    getSystemConfig().catch(() => null)
  ])
  projects.value = list || cfg?.projects || []
  if (!cfg) return
  engineeringMode.value = !!cfg.engineeringMode
  projectId.value = cfg.activeProjectId || null
  state.source = cfg.source || 'appsettings'
  state.runtimeUnitCount = cfg.runtimeUnitCount || 0
  state.runtimePvUnitCount = cfg.runtimePvUnitCount || 0
  state.activeProjectName = cfg.activeProjectName || ''
  state.activeProjectId = cfg.activeProjectId || null
  state.pointmaps = cfg.pointmaps || []
  previewNotes.value = cfg.overlaySummary?.notes || []
}

async function reloadDeviceModels() {
  const dm = await getDeviceModels()
  deviceTypes.value = dm.types || []
  for (const key of Object.keys(deviceSelection)) delete deviceSelection[key]
  for (const t of deviceTypes.value)
    deviceSelection[t.id] = (dm.selection || {})[t.id] || null
  deviceSelectionLoaded.value = { ...deviceSelection }
}

async function reloadAll() {
  loading.value = true
  try {
    await Promise.all([reload(), reloadDeviceModels()])
  } catch (e) {
    ElMessage.error(e.message || '加载工程配置失败')
  } finally {
    loading.value = false
  }
}

async function onCreate() {
  try {
    await ElMessageBox.confirm(
      '将清空当前组态画布并创建新工程，是否继续？',
      '新增工程',
      { type: 'warning', confirmButtonText: '创建并编辑', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  try {
    const p = await postTopologyProjectNew({ name: '未命名工程' })
    ElMessage.success('已创建空工程，请开始搭建拓扑')
    await router.push({ path: '/topology', query: { mode: 'new', id: p.id } })
  } catch (e) {
    ElMessage.error(e.message || '创建失败')
  }
}

async function onEdit(row) {
  try {
    await ElMessageBox.confirm(
      `将工程「${row.name}」导入组态编辑器，当前画布内容会被覆盖，是否继续？`,
      '修改工程',
      { type: 'info', confirmButtonText: '打开编辑', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  try {
    await postTopologyProjectOpen(row.id)
    ElMessage.success(`已载入工程「${row.name}」`)
    await router.push({ path: '/topology', query: { mode: 'edit', id: row.id } })
  } catch (e) {
    ElMessage.error(e.message || '打开失败')
  }
}

async function onCopy(row) {
  let name
  try {
    const res = await ElMessageBox.prompt(
      `以工程「${row.name}」为模板复制新工程，请输入副本名称：`,
      '复制工程',
      {
        confirmButtonText: '复制',
        cancelButtonText: '取消',
        inputValue: `${row.name}-副本`,
        inputValidator: v => (v && v.trim() ? true : '名称不能为空')
      }
    )
    name = res.value.trim()
  } catch {
    return
  }
  try {
    const copy = await postTopologyProjectCopy(row.id, name)
    ElMessage.success(`已复制为「${copy.name}」`)
    await reload()
    try {
      await ElMessageBox.confirm(
        `是否立即打开副本「${copy.name}」进入组态编辑？当前画布内容会被覆盖。`,
        '复制成功',
        { type: 'success', confirmButtonText: '打开编辑', cancelButtonText: '留在列表' }
      )
    } catch {
      return
    }
    await postTopologyProjectOpen(copy.id)
    await router.push({ path: '/topology', query: { mode: 'edit', id: copy.id } })
  } catch (e) {
    ElMessage.error(e.message || '复制失败')
  }
}

async function onDelete(row) {
  const running = isRunning(row)
  try {
    await ElMessageBox.confirm(
      running
        ? `工程「${row.name}」当前为运行模板，删除后将清除激活引用。确定删除？`
        : `确定删除工程「${row.name}」？此操作不可恢复。`,
      '删除工程',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  try {
    await deleteTopologyProject(row.id)
    if (projectId.value === row.id) projectId.value = null
    ElMessage.success('已删除')
    await reload()
  } catch (e) {
    ElMessage.error(e.message || '删除失败')
  }
}

async function waitBackendReady(timeoutMs = 90000) {
  const start = Date.now()
  updateSystemProgress(38, '正在停止当前仿真进程…', '等待后端下线')
  for (let i = 0; i < 40; i++) {
    try {
      await getHealth()
      updateSystemProgress(
        38 + Math.min(12, Math.round((i / 40) * 12)),
        '正在停止当前仿真进程…',
        '等待后端下线'
      )
      await new Promise(r => setTimeout(r, 300))
    } catch {
      break
    }
  }
  updateSystemProgress(52, '后端已停止，正在重新拉起…', '等待后端就绪')
  while (Date.now() - start < timeoutMs) {
    const elapsed = Date.now() - start
    const t = Math.min(1, elapsed / Math.max(timeoutMs * 0.45, 1))
    const p = 52 + Math.round(t * 36)
    updateSystemProgress(
      Math.min(88, p),
      '正在等待模拟器就绪（设备与 Modbus 重建中）…',
      '等待后端就绪'
    )
    try {
      const h = await getHealth()
      if (h?.status === 'ok') {
        updateSystemProgress(92, '后端已就绪，正在同步配置…', '同步配置')
        return true
      }
    } catch { /* still down */ }
    await new Promise(r => setTimeout(r, 800))
  }
  return false
}

async function finishRestart(okMessage) {
  const ok = await waitBackendReady()
  if (!ok) {
    ElMessage.error('等待后端重启超时，请检查终端或手动重启')
    return false
  }
  updateSystemProgress(96, '正在刷新界面状态…', '完成收尾')
  await Promise.all([reload(), reloadDeviceModels()])
  updateSystemProgress(100, okMessage, '完成')
  ElMessage.success(okMessage)
  await new Promise(r => setTimeout(r, 350))
  return true
}

async function apply() {
  if (engineeringMode.value && !projectId.value) {
    ElMessage.warning('请先在工程库中选用一个工程')
    return
  }
  const modeText = engineeringMode.value
    ? `使用工程「${selectedProject.value?.name || projectId.value}」重新初始化`
    : '关闭工程模式，恢复 appsettings.json'
  try {
    await ElMessageBox.confirm(
      `${modeText}。\n当前运行状态（SOC、断路器等）将丢失，后端将重启，是否继续？`,
      '确认应用到仿真',
      { type: 'warning', confirmButtonText: '确认并重启', cancelButtonText: '取消' }
    )
  } catch {
    return
  }

  applying.value = true
  lockSystem('正在提交系统配置…', 8, '提交配置')
  try {
    updateSystemProgress(18, '正在写入工程配置 / overlay…', '应用配置')
    const res = await postSystemApply({
      engineeringMode: engineeringMode.value,
      projectId: engineeringMode.value ? projectId.value : null,
      confirmRestart: true
    })
    if (!res.ok) {
      ElMessage.error(res.message || '应用失败')
      return
    }
    previewNotes.value = res.details || res.overlay?.notes || []
    updateSystemProgress(35, res.message || '配置已提交，准备重启后端…', '准备重启')
    if (res.restarting) {
      const done = await finishRestart('模拟器已按新配置就绪')
      if (done) {
        unlockSystem()
        applying.value = false
      }
      return
    }
    updateSystemProgress(100, '配置已更新', '完成')
    await reload()
  } catch (e) {
    if (String(e.message || '').includes('Network') || String(e.message || '').includes('ECONN')) {
      updateSystemProgress(40, '连接已中断，正在等待后端重启…', '等待后端重启')
      const done = await finishRestart('模拟器已重启')
      if (done) {
        unlockSystem()
        applying.value = false
      }
      return
    }
    ElMessage.error(e.message || '应用失败')
  } finally {
    applying.value = false
    unlockSystem()
  }
}

async function applyDeviceModels() {
  const changed = deviceTypes.value
    .filter(t => (deviceSelection[t.id] ?? null) !== (deviceSelectionLoaded.value[t.id] ?? null))
    .map(t => {
      const m = t.models.find(x => x.id === deviceSelection[t.id])
      return `${t.name}: ${m ? m.name : (deviceSelection[t.id] || '兜底点表')}`
    })
  try {
    await ElMessageBox.confirm(
      `应用设备型号选型：\n${changed.join('\n')}\n当前运行状态将丢失，后端将重启，是否继续？`,
      '确认应用设备型号',
      { type: 'warning', confirmButtonText: '确认并重启', cancelButtonText: '取消' }
    )
  } catch {
    return
  }

  applyingModels.value = true
  lockSystem('正在提交设备型号选型…', 8, '提交选型')
  try {
    updateSystemProgress(18, '正在写入设备型号选型…', '应用选型')
    const selections = {}
    for (const t of deviceTypes.value) {
      if (deviceSelection[t.id]) selections[t.id] = deviceSelection[t.id]
    }
    const res = await postDeviceModelsApply({ selections, confirmRestart: true })
    if (!res.ok) {
      ElMessage.error(res.message || '应用失败')
      return
    }
    updateSystemProgress(35, res.message || '选型已提交，准备重启后端…', '准备重启')
    if (res.restarting) {
      const done = await finishRestart('模拟器已按新点表就绪')
      if (done) {
        unlockSystem()
        applyingModels.value = false
      }
      return
    }
    updateSystemProgress(100, '选型已更新', '完成')
    await Promise.all([reload(), reloadDeviceModels()])
  } catch (e) {
    if (String(e.message || '').includes('Network') || String(e.message || '').includes('ECONN')) {
      updateSystemProgress(40, '连接已中断，正在等待后端重启…', '等待后端重启')
      const done = await finishRestart('模拟器已重启')
      if (done) {
        unlockSystem()
        applyingModels.value = false
      }
      return
    }
    ElMessage.error(e.message || '应用失败')
  } finally {
    applyingModels.value = false
    unlockSystem()
  }
}

onMounted(reloadAll)
</script>

<style scoped>
.proj-hub { display: flex; flex-direction: column; gap: 12px; }
.run-bar { display: flex; flex-direction: column; gap: 10px; }
.run-row { display: flex; align-items: center; flex-wrap: wrap; gap: 8px 12px; }
.mode-label { font-size: 14px; font-weight: 600; color: #303133; }
.meta { font-size: 13px; color: #909399; }
.run-notes { margin-top: 4px; }
.notes { margin: 6px 0 0; padding-left: 18px; font-size: 12px; color: #606266; }
.hub-tabs :deep(.el-tabs__header) { margin: 0 0 12px; }
.head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}
.card-title { margin: 0 0 6px; font-size: 16px; }
.desc { margin: 0; font-size: 13px; color: #606266; line-height: 1.6; }
.desc code, .tips code { background: #f4f4f5; padding: 1px 6px; border-radius: 3px; }
.actions { display: flex; gap: 8px; flex-shrink: 0; }
.form { max-width: 640px; margin-top: 8px; }
.tips {
  margin: 0;
  padding-left: 18px;
  font-size: 13px;
  color: #606266;
  line-height: 1.7;
}
.tip-card .card-title { margin-bottom: 8px; }
.proj-hub :deep(.el-table .is-selected) { background: #ecf5ff; }
</style>
