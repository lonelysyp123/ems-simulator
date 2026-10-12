<template>
  <div class="topo-page">
    <div class="card toolbar">
      <div class="left">
        <el-input v-model="project.name" size="small" style="width:200px" placeholder="工程名称" @change="markDirty" />
        <el-button type="primary" size="small" :loading="saving" @click="saveProject">保存工程</el-button>
        <el-button size="small" @click="reload">重新加载</el-button>
        <el-button size="small" :disabled="!canUndo" @click="undo" title="Ctrl+Z">撤销</el-button>
        <el-button size="small" :disabled="!canRedo" @click="redo" title="Ctrl+Shift+Z">重做</el-button>
        <el-button size="small" type="danger" plain :disabled="!canDelete" @click="deleteSelected">删除选中</el-button>
        <el-button size="small" :disabled="!canSaveLibrary" @click="openSaveLibrary">{{ saveLibraryButtonLabel }}</el-button>
        <el-button size="small" type="success" plain @click="wizardOpen = true">标准拓扑向导</el-button>
        <el-button size="small" link type="primary" @click="goProjectManage">工程配置</el-button>
        <el-button v-if="isNarrow" size="small" @click="propsDrawerOpen = !propsDrawerOpen">{{ propsDrawerOpen ? '收起属性' : '属性' }}</el-button>
      </div>
      <div class="right">
        <el-tag v-if="dirty" size="small" type="warning" style="margin-right:6px">未保存</el-tag>
        <el-tag v-if="editHint" size="small" type="success" style="margin-right:6px">{{ editHint }}</el-tag>
        <el-tag v-if="selectedNodeIds.length" size="small" type="warning" style="margin-right:6px">已选 {{ selectedNodeIds.length }}</el-tag>
        <el-tag size="small" type="info">节点 {{ project.nodes.length }}</el-tag>
        <el-tag size="small" type="info" style="margin-left:6px">连线 {{ project.edges.length }}</el-tag>
      </div>
    </div>

    <div class="workspace" :class="{ 'palette-collapsed': paletteCollapsed, narrow: isNarrow }">
      <PalettePanel
        v-model:collapsed="paletteCollapsed"
        :templates="templates"
        :library="library"
        @drag-template="onDragTemplate"
        @drag-library="onDragLibrary"
        @add-template="addFromTemplate"
        @add-library="addFromLibrary"
        @remove-library="removeLibrary"
      />

      <div
        class="canvas-wrap card"
        @dragover.prevent="ev => { ev.dataTransfer.dropEffect = 'copy' }"
        @drop.prevent="onDrop"
      >
        <TopologyCanvas
          ref="canvasRef"
          :nodes="project.nodes"
          :edges="project.edges"
          :templates="templates"
          :selected-node-id="selectedNodeId"
          :selected-node-ids="selectedNodeIds"
          :selected-edge-id="selectedEdgeId"
          :linking="linking"
          :pointer-world="pointerWorld"
          :problem-node-ids="problemNodeIds"
          :highlight-node-ids="highlightNodeIds"
          :snap="true"
          @select-node="onSelectFromCanvas"
          @select-nodes="onSelectNodes"
          @clear-selection="clearSelection"
          @select-edge="onSelectEdge"
          @port-click="onPortClick"
          @move-commit="onMoveCommit"
          @pointer-world="w => pointerWorld = w"
        />
        <div v-if="linking" class="linking-tip">连线中…再点目标拐角（Esc 取消）</div>
      </div>

      <aside v-show="propsVisible" class="props card">
        <div v-if="isNarrow" class="props-head">
          <el-button link size="small" @click="propsDrawerOpen = false">关闭</el-button>
        </div>
        <el-tabs v-model="inspectorTab" class="inspector-tabs">
          <el-tab-pane label="属性" name="props">
        <div class="inspector-body">
        <template v-if="isMultiSelect">
          <el-form label-position="top" size="small">
            <el-form-item :label="`已选 ${selectedNodes.length} 个`">
              <div class="bound-list">
                <el-tag
                  v-for="n in selectedNodes"
                  :key="n.id"
                  size="small"
                  effect="plain"
                  class="bound-tag"
                  @click="focusProblem(n.id)"
                >{{ n.label }}</el-tag>
              </div>
            </el-form-item>
            <template v-if="batchAssignable.length">
              <el-divider content-position="left">批量归属</el-divider>
              <p class="param-hint">将写入 {{ batchAssignable.length }} 个可归属设备（PCS / 断路器 / 电表 / 变压器）</p>
              <el-form-item label="所属 EMU 储能单元">
                <el-select
                  :model-value="batchEmuState.value"
                  :placeholder="batchEmuState.mixed ? '多个值' : '选择 EMU 储能单元'"
                  clearable
                  style="width:100%"
                  @change="onBatchEmuChange"
                >
                  <el-option v-for="e in emuNodes" :key="e.id" :label="e.label" :value="e.id" />
                </el-select>
              </el-form-item>
              <el-form-item label="所属 EMU 分组">
                <el-select
                  :model-value="batchGroupState.value"
                  :placeholder="batchGroupPlaceholder"
                  :disabled="!batchGroupOptions.length"
                  clearable
                  style="width:100%"
                  @change="onBatchGroupChange"
                >
                  <el-option v-for="g in batchGroupOptions" :key="g.id" :label="g.label" :value="g.id" />
                </el-select>
                <div v-if="!batchGroupOptions.length" class="param-hint">{{ batchGroupHint }}</div>
              </el-form-item>
            </template>
            <p v-else class="empty">所选设备不含 PCS / 断路器 / 电表 / 变压器，无法批量设置单元和组。</p>
          </el-form>
        </template>
        <template v-else-if="selectedNode && selectedTemplate">
          <el-form label-position="top" size="small">
            <el-form-item label="显示名称">
              <el-input v-model="selectedNode.label" @change="onParamEdited" />
            </el-form-item>
            <el-form-item label="模板">
              <el-tag size="small">{{ selectedTemplate.name }}</el-tag>
            </el-form-item>
            <el-divider content-position="left">参数</el-divider>
            <el-form-item
              v-for="def in selectedTemplate.parameters"
              :key="def.key"
              :label="def.unit ? `${def.label} (${def.unit})` : def.label"
            >
              <el-input-number
                v-if="def.type === 'number'"
                v-model="selectedNode.parameters[def.key]"
                v-bind="numberInputBounds(def)"
                :step="numberStep(def)"
                controls-position="right"
                style="width:100%"
                @change="onParamEdited"
              />
              <el-switch
                v-else-if="def.type === 'boolean'"
                :model-value="!!selectedNode.parameters[def.key]"
                @change="v => onBoolParamChange(def.key, v)"
              />
              <el-select
                v-else-if="def.type === 'emu_select'"
                :model-value="selectedNode.parameters[def.key] || ''"
                placeholder="选择 EMU 储能单元"
                clearable
                style="width:100%"
                @change="v => onEmuParamChange(def.key, v)"
              >
                <el-option v-for="e in emuNodes" :key="e.id" :label="e.label" :value="e.id" />
              </el-select>
              <el-select
                v-else-if="def.type === 'group_select'"
                :model-value="selectedNode.parameters[def.key] || ''"
                placeholder="选择 EMU 分组（可选）"
                clearable
                style="width:100%"
                @change="v => onGroupParamChange(def.key, v)"
              >
                <el-option v-for="g in groupOptionsForSelected" :key="g.id" :label="g.label" :value="g.id" />
              </el-select>
              <el-input
                v-else
                v-model="selectedNode.parameters[def.key]"
                @change="onParamEdited"
              />
              <div v-if="def.description" class="param-hint">{{ def.description }}</div>
            </el-form-item>
          </el-form>
          <template v-if="emuBoundView">
            <el-divider content-position="left">已绑定设备</el-divider>
            <el-form label-position="top" size="small">
              <el-form-item label="EMU 分组">
                <el-tag v-for="g in emuBoundView.groups" :key="g.id" size="small" effect="plain" class="bound-tag" @click="focusProblem(g.id)">{{ g.label }}</el-tag>
                <span v-if="!emuBoundView.groups.length" class="empty">暂无分组</span>
              </el-form-item>
              <template v-for="row in emuBoundView.unitRows" :key="`u-${row.role}`">
                <el-form-item v-if="row.nodes.length" :label="`单元级${row.role}`">
                  <el-tag v-for="d in row.nodes" :key="d.id" size="small" effect="plain" class="bound-tag" @click="focusProblem(d.id)">{{ d.label }}</el-tag>
                </el-form-item>
              </template>
              <template v-for="row in emuBoundView.groupRows" :key="`g-${row.role}`">
                <el-form-item v-if="row.nodes.length" :label="`组级${row.role}`">
                  <el-tag v-for="d in row.nodes" :key="d.id" size="small" type="warning" effect="plain" class="bound-tag" @click="focusProblem(d.id)">{{ d.label }} · {{ labelOf(d.parameters?.groupId) }}</el-tag>
                </el-form-item>
              </template>
              <p v-if="emuBoundView.isEmpty" class="empty">尚未绑定设备；在设备属性面板选择「所属 EMU 储能单元」即可归入</p>
            </el-form>
          </template>
          <template v-else-if="groupBoundDevices">
            <el-divider content-position="left">组内设备</el-divider>
            <div class="bound-list">
              <el-tag v-for="d in groupBoundDevices" :key="d.id" size="small" effect="plain" class="bound-tag" @click="focusProblem(d.id)">{{ d.label }}</el-tag>
              <p v-if="!groupBoundDevices.length" class="empty">暂无设备归入本分组</p>
            </div>
          </template>
          <el-alert
            v-if="selectedTemplate.description"
            :title="selectedTemplate.description"
            type="info"
            :closable="false"
            show-icon
            style="margin-top:8px"
          />
        </template>
        <template v-else-if="selectedEdgeId">
          <p class="empty">已选中连线，按 Delete 可断开。</p>
          <el-button size="small" type="danger" @click="deleteSelected">断开连线</el-button>
        </template>
        <p v-else class="empty">从画布选择设备，或打开储能单元。</p>
        </div>
          </el-tab-pane>
          <el-tab-pane name="emu">
            <template #label>储能单元</template>
            <EmuTree
              :nodes="project.nodes"
              :selected-ids="selectedNodeIds"
              @select="onSelectFromTree"
              @delete-emu="deleteEmu"
              @focus-device="onFocusFromTree"
            />
          </el-tab-pane>
          <el-tab-pane name="issues">
            <template #label>
              <span>校验</span>
              <el-badge
                v-if="validationIssues.length"
                :value="validationIssues.length"
                :max="99"
                class="tab-badge"
              />
            </template>
            <div v-if="!validationIssues.length" class="empty">暂无校验问题。保存工程时会自动检查。</div>
            <el-alert
              v-else
              :title="validationMessage || '工程配置不合理'"
              type="error"
              :closable="true"
              show-icon
              @close="clearValidation"
            >
              <ul class="issue-list">
                <li
                  v-for="(issue, i) in validationIssues"
                  :key="i"
                  class="issue-item"
                  :class="{ clickable: !!issue.nodeId }"
                  @click="focusProblem(issue.nodeId)"
                >
                  {{ issue.text }}
                </li>
              </ul>
            </el-alert>
          </el-tab-pane>
        </el-tabs>
      </aside>
    </div>

    <el-dialog v-model="libDialog" :title="libDialogTitle" width="420px">
      <el-form label-width="80px" size="small">
        <el-form-item label="名称">
          <el-input
            v-model="libName"
            :placeholder="savingComposite ? '如：5.5MW 单元馈线' : '如：1250kW PCS 单元 / 大容量 BMS'"
          />
        </el-form-item>
        <template v-if="savingComposite">
          <p class="wizard-desc">将把 {{ compositeTargets.length }} 个设备存成一块组合图元，只保留它们之间的连线。拖入时整组落下；接到母线需再连一次。请不要把电网或站级母线框进去。</p>
          <ul class="lib-target-list">
            <li v-for="n in compositeTargets" :key="n.id">{{ n.label }} · {{ templateName(n.templateId) }}</li>
          </ul>
        </template>
      </el-form>
      <template #footer>
        <el-button size="small" @click="libDialog = false">取消</el-button>
        <el-button size="small" type="primary" :loading="savingLib" @click="saveLibrary">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="wizardOpen" title="标准拓扑向导" width="480px">
      <p class="wizard-desc">生成电网→主断→220kV 母线→主变→35kV 母线→储能 EMU 和/或光伏单元的径向骨架，每个 EMU 默认含 2 台 PCS，并自动三相/直流成组连线。EMU 与光伏单元至少填 1 个。</p>
      <el-form label-width="110px" size="small">
        <el-form-item label="工程名称">
          <el-input v-model="wizardName" :placeholder="wizardNamePlaceholder" />
        </el-form-item>
        <el-form-item label="EMU 单元数">
          <el-input-number v-model="wizardEmuCount" :min="0" :max="20" controls-position="right" />
        </el-form-item>
        <el-form-item label="光伏单元数">
          <el-input-number v-model="wizardPvCount" :min="0" :max="20" controls-position="right" />
        </el-form-item>
        <el-form-item label="站用负载">
          <el-switch v-model="wizardIncludeLoad" active-text="包含" inactive-text="不含" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button size="small" @click="wizardOpen = false">取消</el-button>
        <el-button
          size="small"
          type="primary"
          :loading="wizardLoading"
          :disabled="wizardEmuCount + wizardPvCount < 1"
          @click="applyWizard"
        >生成到画布</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import TopologyCanvas from '@/components/topology/TopologyCanvas.vue'
import PalettePanel from '@/components/topology/PalettePanel.vue'
import EmuTree from '@/components/topology/EmuTree.vue'
import { nodeSize, snapToGrid } from '@/components/topology/nodeLayout.js'
import {
  applyEmuId,
  applyGroupId,
  assignableNodes,
  captureComposite,
  compositeDropSize,
  compositeEligibleNodes,
  instantiateComposite,
  isCompositeLibraryItem,
  libraryEligibleNodes,
  libraryPayloadsFromNodes,
  mixedParam,
  toggleNodeSelection,
  unionIds
} from '@/components/topology/batchEdit.js'
import {
  devicesOfEmu as devicesOfEmuNodes,
  devicesOfGroup as devicesOfGroupNodes,
  groupsOfEmu as groupsOfEmuNodes,
  highlightIdsForSelection
} from '@/components/topology/emuTree.js'
import {
  getTopologyTemplates,
  getTopologyProject,
  putTopologyProject,
  postTopologyConnect,
  postTopologyDisconnect,
  getTopologyLibrary,
  putTopologyLibrary,
  deleteTopologyLibrary,
  checkTopologyProjectName,
  postTopologyValidate,
  postTopologyScaffold
} from '@/services/api.js'

const route = useRoute()
const router = useRouter()
const editHint = ref('')

const templates = ref([])
const library = ref([])
const project = reactive({
  schemaVersion: '1.0',
  id: 'current',
  name: '未命名组态',
  nodes: [],
  edges: []
})

const selectedNodeIds = ref([])
const selectedEdgeId = ref(null)
const linking = ref(null)
const pointerWorld = ref(null)
const saving = ref(false)
const libDialog = ref(false)
const libName = ref('')
const libNamePrefix = ref('')
const savingLib = ref(false)
const canvasRef = ref(null)
const connecting = ref(false)
let connectSeq = 0

const dirty = ref(false)
const problemNodeIds = ref([])
const validationMessage = ref('')
const validationIssues = ref([])
const historyPast = ref([])
const historyFuture = ref([])
const HISTORY_MAX = 40
let applyingHistory = false

const wizardOpen = ref(false)
const wizardEmuCount = ref(2)
const wizardPvCount = ref(0)
const wizardIncludeLoad = ref(true)
const wizardName = ref('')
const wizardLoading = ref(false)

const inspectorTab = ref('props')
const paletteCollapsed = ref(false)
const isNarrow = ref(false)
const propsDrawerOpen = ref(true)
const NARROW_MQ = '(max-width: 1100px)'
let narrowMq = null

const wizardNamePlaceholder = computed(() => {
  const e = wizardEmuCount.value
  const p = wizardPvCount.value
  if (p > 0 && e <= 0) return `标准径向-光伏${p}单元`
  if (p > 0) return `标准径向-储能${e}/光伏${p}`
  return `标准径向-${Math.max(1, e)}单元`
})

/** 清空队列后只展示当前一条，避免连线过快时提示堆积/延后爆发 */
function showConnectFeedback(type, title, detail = '') {
  ElMessage.closeAll()
  if (type === 'success') return
  const text = detail ? `${title}：${detail}` : title
  ElMessage({
    type: type === 'error' ? 'error' : 'info',
    message: text,
    duration: 4500,
    showClose: true,
    grouping: false,
    offset: 72,
    appendTo: typeof document !== 'undefined' ? document.body : undefined
  })
}

function dropWorldPosition(ev, templateId, sizeOverride) {
  const size = sizeOverride || nodeSize(templateId)
  const world = canvasRef.value?.clientToWorld?.(ev.clientX, ev.clientY)
  if (!world) {
    return {
      x: snapToGrid(80 + project.nodes.length * 24),
      y: snapToGrid(80 + (project.nodes.length % 5) * 40)
    }
  }
  return {
    x: snapToGrid(world.x - size.w / 2),
    y: snapToGrid(world.y - size.h / 2)
  }
}

/** 编辑期仅拦结构错误；电气规则在保存时统一校验 */
function quickReject(edge) {
  const fromNode = project.nodes.find(n => n.id === edge.fromNodeId)
  const toNode = project.nodes.find(n => n.id === edge.toNodeId)
  if (!fromNode || !toNode) return { code: 'NODE_MISSING', message: '连线端点设备不存在' }
  if (fromNode.id === toNode.id) return { code: 'SELF_LINK', message: '不能将设备连接到自身' }
  const fromTpl = templates.value.find(t => t.id === fromNode.templateId)
  const toTpl = templates.value.find(t => t.id === toNode.templateId)
  const fromPort = fromTpl?.ports?.find(p => p.id === edge.fromPortId)
  const toPort = toTpl?.ports?.find(p => p.id === edge.toPortId)
  if (!fromPort || !toPort) return { code: 'PORT_MISSING', message: '拐角（端口）不存在' }
  return null
}

const selectedNodeId = computed(() => selectedNodeIds.value[0] || null)
const selectedNodes = computed(() =>
  selectedNodeIds.value.map(id => project.nodes.find(n => n.id === id)).filter(Boolean)
)
const selectedNode = computed(() => selectedNodes.value.length === 1 ? selectedNodes.value[0] : null)
const selectedTemplate = computed(() => templates.value.find(t => t.id === selectedNode.value?.templateId) || null)
const isMultiSelect = computed(() => selectedNodes.value.length > 1)
const canDelete = computed(() => !!(selectedNodeIds.value.length || selectedEdgeId.value))
const canUndo = computed(() => historyPast.value.length > 0)
const canRedo = computed(() => historyFuture.value.length > 0)
const libraryTargets = computed(() => libraryEligibleNodes(selectedNodes.value))
const compositeTargets = computed(() => compositeEligibleNodes(selectedNodes.value))
const canSaveLibrary = computed(() => libraryTargets.value.length > 0)
const savingComposite = computed(() => compositeTargets.value.length >= 2)
const saveLibraryButtonLabel = computed(() => savingComposite.value ? '存为组合图元' : '存入设备库')
const libDialogTitle = computed(() =>
  savingComposite.value ? `存为组合图元（${compositeTargets.value.length} 个设备）` : '存入设备库'
)
const batchAssignable = computed(() => assignableNodes(selectedNodes.value))
const batchEmuState = computed(() => mixedParam(batchAssignable.value, 'emuId'))
const batchGroupState = computed(() => mixedParam(batchAssignable.value, 'groupId'))
const batchGroupOptions = computed(() => {
  if (batchEmuState.value.mixed || !batchEmuState.value.value) return []
  return groupsOfEmu(batchEmuState.value.value)
})
const batchGroupPlaceholder = computed(() => {
  if (batchEmuState.value.mixed) return '请先统一所属单元'
  if (!batchEmuState.value.value) return '请先选择所属单元'
  if (batchGroupState.value.mixed) return '多个值'
  return '选择 EMU 分组（可选）'
})
const batchGroupHint = computed(() => {
  if (batchEmuState.value.mixed) return '所选设备分属不同单元，请先统一所属单元再设分组。'
  if (!batchEmuState.value.value) return '先选择所属 EMU 储能单元后，才能指定分组。'
  return '该单元下暂无分组。'
})

/** EMU 虚拟节点列表（画布不渲染，由右侧单元树管理） */
const emuNodes = computed(() => project.nodes.filter(n => n.templateId === 'emu'))
const propsVisible = computed(() => !isNarrow.value || propsDrawerOpen.value)
const highlightNodeIds = computed(() => highlightIdsForSelection(project.nodes, selectedNodeId.value))

function groupsOfEmu(emuId) {
  return groupsOfEmuNodes(project.nodes, emuId)
}

function devicesOfEmu(emuId) {
  return devicesOfEmuNodes(project.nodes, emuId)
}

/** 当前选中节点的 EMU 分组候选：仅列其所属 EMU 下的分组（未选 EMU 时无候选） */
const groupOptionsForSelected = computed(() => {
  const n = selectedNode.value
  if (!n || n.templateId === 'emu_group') return []
  return groupsOfEmu(n.parameters?.emuId || '')
})

/** 属性面板绑定设备视图：仅选中 EMU 虚拟节点时返回（分组/单元级/组内设备分行），否则 null */
const emuBoundView = computed(() => {
  const n = selectedNode.value
  if (!n || n.templateId !== 'emu') return null
  const roles = [['pcs', 'PCS'], ['ac_breaker', '断路器'], ['ac_meter', '电表'], ['transformer', '变压器'], ['split_transformer', '双耳变压器']]
  const unitRows = roles.map(([tid, role]) => ({
    role,
    nodes: project.nodes.filter(x => x.templateId === tid && x.parameters?.emuId === n.id && !x.parameters?.groupId)
  }))
  const groupRows = roles.map(([tid, role]) => ({
    role,
    nodes: project.nodes.filter(x => x.templateId === tid && x.parameters?.emuId === n.id && x.parameters?.groupId)
  }))
  const groups = groupsOfEmu(n.id)
  const isEmpty = !unitRows.some(r => r.nodes.length) && !groupRows.some(r => r.nodes.length)
  return { groups, unitRows, groupRows, isEmpty }
})

/** 某分组下绑定的设备（PCS/断路器/电表/变压器） */
function devicesOfGroup(emuId, groupId) {
  return devicesOfGroupNodes(project.nodes, emuId, groupId)
}

/** 属性面板：选中 EMU 分组时列出组内设备，否则 null */
const groupBoundDevices = computed(() => {
  const n = selectedNode.value
  if (!n || n.templateId !== 'emu_group') return null
  return devicesOfGroup(n.parameters?.emuId, n.id)
})

/** 节点 id → 展示名（找不到时回退 id） */
function labelOf(id) {
  return project.nodes.find(n => n.id === id)?.label || id
}

function onEmuParamChange(key, value) {
  if (!selectedNode.value) return
  pushHistory()
  selectedNode.value.parameters[key] = value || ''
  // 切换所属 EMU 后原分组必然失效，同步清空 groupId
  if (key === 'emuId' && 'groupId' in selectedNode.value.parameters)
    selectedNode.value.parameters.groupId = ''
  clearValidation()
}

function onGroupParamChange(key, value) {
  if (!selectedNode.value) return
  pushHistory()
  selectedNode.value.parameters[key] = value || ''
  clearValidation()
}

function onBatchEmuChange(value) {
  if (!batchAssignable.value.length) return
  pushHistory()
  applyEmuId(batchAssignable.value, value)
  clearValidation()
}

function onBatchGroupChange(value) {
  if (!batchAssignable.value.length) return
  pushHistory()
  applyGroupId(batchAssignable.value, value, project.nodes)
  clearValidation()
}

/** 解除该 EMU 下全部设备（pcs/ac_breaker/ac_meter/transformer/split_transformer）的 emuId/groupId 归属 */
function unassignDevicesFromEmu(emuId) {
  for (const n of devicesOfEmu(emuId)) {
    n.parameters.emuId = ''
    if ('groupId' in n.parameters) n.parameters.groupId = ''
  }
}

function deleteEmu(id) {
  pushHistory()
  // 同步清理指向该 EMU 的连线（旧工程 EMU 可能带 AC/DC 连线），避免保存回放报「连线端点设备不存在」
  project.edges = project.edges.filter(e => e.fromNodeId !== id && e.toNodeId !== id)
  // 一并删除该 EMU 下的分组虚拟节点，并解除设备归属（含 groupId）
  const removedGroupIds = new Set(groupsOfEmu(id).map(g => g.id))
  project.nodes = project.nodes.filter(n => n.id !== id && !removedGroupIds.has(n.id))
  const bound = devicesOfEmu(id)
  const pcsOrphans = bound.filter(n => n.templateId === 'pcs').length
  unassignDevicesFromEmu(id)
  selectedNodeIds.value = selectedNodeIds.value.filter(sid => sid !== id && !removedGroupIds.has(sid))
  clearValidation()
  if (bound.length > 0)
    ElMessage.warning(`已删除 EMU，${pcsOrphans} 台 PCS、${bound.length - pcsOrphans} 台断路器/电表已解除归属，请重新选择所属 EMU 后再保存`)
}

function templateName(id) { return templates.value.find(t => t.id === id)?.name || id }
function numberStep(def) {
  if (def.key?.toLowerCase().includes('efficiency') || def.key?.toLowerCase().includes('soc')) return 0.01
  if (Number.isFinite(def.max) && def.max <= 2) return 0.01
  return 1
}

/** null/undefined 的 max 会被 Element Plus 当成 0，触发 min>max 异常并拖垮整页交互 */
function numberInputBounds(def) {
  const bounds = {}
  if (Number.isFinite(def.min)) bounds.min = def.min
  if (Number.isFinite(def.max)) bounds.max = def.max
  return bounds
}

function uid() {
  return Math.random().toString(16).slice(2) + Date.now().toString(16)
}

function cloneParams(obj) {
  return JSON.parse(JSON.stringify(obj || {}))
}

function projectPayload() {
  return {
    schemaVersion: project.schemaVersion,
    id: project.id,
    name: project.name,
    nodes: project.nodes,
    edges: project.edges
  }
}

function snapshotJson() {
  return JSON.stringify(projectPayload())
}

function applyProject(p, { resetHistory = false, clearDirty = false } = {}) {
  project.schemaVersion = p.schemaVersion || '1.0'
  project.id = p.id || 'current'
  project.name = p.name || '未命名组态'
  project.nodes = (p.nodes || []).map(n => ({
    ...n,
    parameters: n.parameters || {}
  }))
  // 剔除端点节点不存在的悬空连线（如节点删除后的残留），画布上它们不可见也无法选中，
  // 若保留会在保存回放时报「连线端点设备不存在」
  const nodeIds = new Set(project.nodes.map(n => n.id))
  project.edges = (p.edges || []).filter(e => nodeIds.has(e.fromNodeId) && nodeIds.has(e.toNodeId))
  selectedNodeIds.value = selectedNodeIds.value.filter(id => nodeIds.has(id))
  if (selectedEdgeId.value && !project.edges.some(e => e.id === selectedEdgeId.value))
    selectedEdgeId.value = null
  if (resetHistory) {
    historyPast.value = []
    historyFuture.value = []
  }
  if (clearDirty) dirty.value = false
}

function pushHistory() {
  if (applyingHistory) return
  historyPast.value.push(snapshotJson())
  if (historyPast.value.length > HISTORY_MAX) historyPast.value.shift()
  historyFuture.value = []
  dirty.value = true
}

function markDirty() {
  dirty.value = true
}

function onParamEdited() {
  pushHistory()
  clearValidation()
}

function undo() {
  if (!historyPast.value.length) return
  historyFuture.value.push(snapshotJson())
  const prev = historyPast.value.pop()
  applyingHistory = true
  try {
    applyProject(JSON.parse(prev))
    dirty.value = true
    clearValidation()
  } finally {
    applyingHistory = false
  }
}

function redo() {
  if (!historyFuture.value.length) return
  historyPast.value.push(snapshotJson())
  const next = historyFuture.value.pop()
  applyingHistory = true
  try {
    applyProject(JSON.parse(next))
    dirty.value = true
    clearValidation()
  } finally {
    applyingHistory = false
  }
}

function clearValidation() {
  problemNodeIds.value = []
  validationMessage.value = ''
  validationIssues.value = []
}

function applyValidationResult(validation) {
  const ok = validation?.ok ?? validation?.Ok
  if (ok) {
    clearValidation()
    return true
  }
  validationMessage.value = validation?.message || validation?.Message || '工程配置不合理'
  const details = validation?.details || validation?.Details || []
  const nodes = validation?.problemNodeIds || validation?.ProblemNodeIds || []
  problemNodeIds.value = [...nodes]
  validationIssues.value = details.length
    ? details.map((text, i) => ({ text, nodeId: nodes[i] || nodes[0] || null }))
    : [{ text: validationMessage.value, nodeId: nodes[0] || null }]
  return false
}

function focusProblem(nodeId) {
  if (!nodeId) return
  selectedNodeIds.value = [nodeId]
  selectedEdgeId.value = null
  if (isNarrow.value) propsDrawerOpen.value = true
}

function openPropsInspector() {
  inspectorTab.value = 'props'
  if (isNarrow.value) propsDrawerOpen.value = true
}

async function confirmDiscardIfDirty(actionLabel = '继续') {
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(
      `当前组态有未保存修改，${actionLabel}将丢失这些改动。`,
      '未保存修改',
      { type: 'warning', confirmButtonText: actionLabel, cancelButtonText: '取消' }
    )
    return true
  } catch {
    return false
  }
}

async function reload() {
  if (!(await confirmDiscardIfDirty('重新加载'))) return
  const [tpl, proj, lib] = await Promise.all([
    getTopologyTemplates(),
    getTopologyProject(),
    getTopologyLibrary()
  ])
  templates.value = tpl
  library.value = lib
  applyProject(proj, { resetHistory: true, clearDirty: true })
  selectedNodeIds.value = []
  selectedEdgeId.value = null
  linking.value = null
  clearValidation()
  syncEditHintFromRoute()
}

function syncEditHintFromRoute() {
  const mode = route.query.mode
  if (mode === 'new') editHint.value = '新建工程'
  else if (mode === 'edit') editHint.value = '编辑工程'
  else editHint.value = ''
}

function goProjectManage() {
  router.push('/projects')
}

function onBoolParamChange(key, value) {
  if (!selectedNode.value) return
  pushHistory()
  selectedNode.value.parameters[key] = !!value
  if (key === 'isMainBreaker' && value) {
    for (const n of project.nodes) {
      if (n.templateId === 'ac_breaker' && n.id !== selectedNode.value.id)
        n.parameters.isMainBreaker = false
    }
  }
  if (key === 'isPccMeter' && value) {
    for (const n of project.nodes) {
      if (n.templateId === 'ac_meter' && n.id !== selectedNode.value.id)
        n.parameters.isPccMeter = false
    }
  }
  clearValidation()
}

async function saveProject() {
  const name = (project.name || '').trim()
  if (!name) {
    ElMessage.warning('请填写工程名称')
    return
  }
  project.name = name

  if (!project.id || project.id === 'current')
    project.id = uid()

  saving.value = true
  try {
    const validation = await postTopologyValidate(projectPayload())
    if (!applyValidationResult(validation)) {
      inspectorTab.value = 'issues'
      if (isNarrow.value) propsDrawerOpen.value = true
      await ElMessageBox.alert(
        validationIssues.value.map(i => i.text).join('\n') || validationMessage.value,
        '无法保存',
        { type: 'error', confirmButtonText: '知道了' }
      )
      return
    }

    const check = await checkTopologyProjectName(name, project.id)
    if (check?.exists && check.project?.id) {
      try {
        await ElMessageBox.confirm(
          `已存在同名工程「${check.project.name}」。\n确定后将覆盖该工程的组态内容（保留其工程 ID）。`,
          '同名工程',
          { type: 'warning', confirmButtonText: '替换并保存', cancelButtonText: '取消' }
        )
      } catch {
        return
      }
      project.id = check.project.id
    }

    const saved = await putTopologyProject(projectPayload())
    applyProject(saved, { clearDirty: true })
    historyPast.value = []
    historyFuture.value = []
    editHint.value = '已保存'
    ElMessage.success(`工程「${saved.name}」已保存，可在工程配置中选用并应用到仿真`)
  } catch (e) {
    ElMessage.error(e.message || '保存失败')
  } finally {
    saving.value = false
  }
}

function onDragTemplate(ev, t) {
  ev.dataTransfer.effectAllowed = 'copy'
  ev.dataTransfer.setData('application/x-topo', JSON.stringify({ kind: 'template', templateId: t.id }))
}

function onDragLibrary(ev, item) {
  ev.dataTransfer.effectAllowed = 'copy'
  ev.dataTransfer.setData('application/x-topo', JSON.stringify({ kind: 'library', itemId: item.id }))
}

function onDrop(ev) {
  const raw = ev.dataTransfer.getData('application/x-topo')
  if (!raw) return
  let payload
  try { payload = JSON.parse(raw) } catch { return }
  if (payload.kind === 'template') {
    const t = templates.value.find(i => i.id === payload.templateId)
    if (!t) return
    const { x, y } = dropWorldPosition(ev, t.id)
    addFromTemplate(t, x, y)
  } else if (payload.kind === 'library') {
    const item = library.value.find(i => i.id === payload.itemId)
    if (!item) return
    if (isCompositeLibraryItem(item)) {
      const size = compositeDropSize(item, n => nodeSize(n.templateId))
      const { x, y } = dropWorldPosition(ev, item.templateId, size)
      addFromLibrary(item, x, y)
      return
    }
    const { x, y } = dropWorldPosition(ev, item.templateId)
    addFromLibrary(item, x, y)
  }
}

function addFromTemplate(t, x = 120, y = 100) {
  pushHistory()
  const node = {
    id: uid(),
    templateId: t.id,
    libraryItemId: null,
    label: t.name,
    x: snapToGrid(x),
    y: snapToGrid(y),
    parameters: cloneParams(t.defaultParameters)
  }
  // PCS 新增时自动归入第一个 EMU 虚拟单元（若有）；分组自动归入第一个 EMU
  if ((t.id === 'pcs' || t.id === 'emu_group') && emuNodes.value.length > 0)
    node.parameters.emuId = emuNodes.value[0].id
  project.nodes.push(node)
  selectedNodeIds.value = [node.id]
  selectedEdgeId.value = null
  clearValidation()
}

function addFromLibrary(item, x = 140, y = 120) {
  if (isCompositeLibraryItem(item)) {
    const placed = instantiateComposite(item, { x, y, uid, snap: snapToGrid })
    if (!placed.nodes.length) return
    pushHistory()
    project.nodes.push(...placed.nodes)
    project.edges.push(...placed.edges)
    selectedNodeIds.value = placed.nodes.map(n => n.id)
    selectedEdgeId.value = null
    clearValidation()
    ElMessage.success(`已放入组合「${item.name}」（${placed.nodes.length} 个设备）`)
    return
  }
  pushHistory()
  const t = templates.value.find(i => i.id === item.templateId)
  const node = {
    id: uid(),
    templateId: item.templateId,
    libraryItemId: item.id,
    label: item.name,
    x: snapToGrid(x),
    y: snapToGrid(y),
    parameters: cloneParams({ ...(t?.defaultParameters || {}), ...(item.parameters || {}) })
  }
  project.nodes.push(node)
  selectedNodeIds.value = [node.id]
  selectedEdgeId.value = null
  clearValidation()
}

function onSelectNode(idOrPayload) {
  selectedEdgeId.value = null
  if (idOrPayload && typeof idOrPayload === 'object') {
    const { id, additive } = idOrPayload
    if (!id) {
      selectedNodeIds.value = []
      return
    }
    selectedNodeIds.value = additive
      ? toggleNodeSelection(selectedNodeIds.value, id)
      : [id]
    return
  }
  selectedNodeIds.value = idOrPayload ? [idOrPayload] : []
}

function onSelectFromCanvas(payload) {
  onSelectNode(payload)
  if (selectedNodeIds.value.length) openPropsInspector()
}

function onSelectFromTree(id) {
  onSelectNode(id)
  openPropsInspector()
}

function onFocusFromTree(id) {
  focusProblem(id)
  openPropsInspector()
}

function onSelectNodes({ ids, additive }) {
  selectedEdgeId.value = null
  const next = Array.isArray(ids) ? ids.filter(Boolean) : []
  selectedNodeIds.value = additive ? unionIds(selectedNodeIds.value, next) : next
  if (selectedNodeIds.value.length) openPropsInspector()
}

function clearSelection() {
  selectedNodeIds.value = []
  selectedEdgeId.value = null
}

function onSelectEdge(id) {
  selectedEdgeId.value = id
  selectedNodeIds.value = []
}

function onMoveCommit(items) {
  if (!items?.length) return
  const changed = items.some(it => it.x !== it.fromX || it.y !== it.fromY)
  if (!changed) return
  for (const it of items) {
    const n = project.nodes.find(i => i.id === it.id)
    if (n) {
      n.x = it.fromX
      n.y = it.fromY
    }
  }
  pushHistory()
  for (const it of items) {
    const n = project.nodes.find(i => i.id === it.id)
    if (!n) continue
    n.x = snapToGrid(it.x)
    n.y = snapToGrid(it.y)
  }
}

async function onPortClick({ nodeId, portId }) {
  if (connecting.value) return

  if (!linking.value) {
    linking.value = { nodeId, portId }
    return
  }
  if (linking.value.nodeId === nodeId && linking.value.portId === portId) {
    linking.value = null
    pointerWorld.value = null
    return
  }

  const source = { ...linking.value }
  const edge = {
    id: uid(),
    fromNodeId: source.nodeId,
    fromPortId: source.portId,
    toNodeId: nodeId,
    toPortId: portId
  }

  const localReject = quickReject(edge)
  if (localReject) {
    showConnectFeedback('error', '连接被拒绝', `[${localReject.code}] ${localReject.message}`)
    linking.value = source
    pointerWorld.value = null
    problemNodeIds.value = [edge.fromNodeId, edge.toNodeId]
    return
  }

  const seq = ++connectSeq
  connecting.value = true
  try {
    const res = await postTopologyConnect({
      project: projectPayload(),
      edge,
      expandBundle: true
    })
    if (seq !== connectSeq) return

    const validation = res?.validation || res?.Validation || {}
    const ok = validation.ok ?? validation.Ok
    const message = validation.message || validation.Message || ''
    const code = validation.code || validation.Code || ''

    if (!ok) {
      showConnectFeedback(
        'error',
        '连接被拒绝',
        code ? `[${code}] ${message || '校验未通过'}` : (message || '校验未通过')
      )
      linking.value = source
      pointerWorld.value = null
      const nodes = validation.problemNodeIds || validation.ProblemNodeIds || [edge.fromNodeId, edge.toNodeId]
      problemNodeIds.value = [...nodes]
      return
    }
    pushHistory()
    applyProject(res.project)
    linking.value = null
    pointerWorld.value = null
    clearValidation()
  } catch (e) {
    if (seq !== connectSeq) return
    showConnectFeedback('error', '连线失败', e.message || '请求异常')
    linking.value = source
    pointerWorld.value = null
  } finally {
    if (seq === connectSeq) connecting.value = false
  }
}

async function deleteSelected() {
  if (selectedEdgeId.value && !selectedNodeIds.value.length) {
    try {
      const updated = await postTopologyDisconnect({
        project: projectPayload(),
        edgeId: selectedEdgeId.value
      })
      pushHistory()
      applyProject(updated)
      selectedEdgeId.value = null
      clearValidation()
    } catch (e) {
      ElMessage.error(e.message || '断开失败')
    }
    return
  }
  if (!selectedNodeIds.value.length) return
  pushHistory()
  const ids = new Set(selectedNodeIds.value)
  const removed = project.nodes.filter(n => ids.has(n.id))
  project.edges = project.edges.filter(e => !ids.has(e.fromNodeId) && !ids.has(e.toNodeId))
  project.nodes = project.nodes.filter(n => !ids.has(n.id))
  let emuWarning = ''
  for (const item of removed) {
    if (item.templateId === 'emu_group') {
      for (const n of project.nodes)
        if (n.parameters?.groupId === item.id) n.parameters.groupId = ''
    }
    if (item.templateId === 'emu') {
      const groupIds = new Set(groupsOfEmu(item.id).map(g => g.id))
      project.nodes = project.nodes.filter(n => !groupIds.has(n.id))
      const bound = devicesOfEmu(item.id)
      const pcsOrphans = bound.filter(n => n.templateId === 'pcs').length
      unassignDevicesFromEmu(item.id)
      if (bound.length > 0)
        emuWarning = `${pcsOrphans} 台 PCS、${bound.length - pcsOrphans} 台断路器/电表已解除归属，请重新选择所属 EMU 后再保存`
    }
  }
  selectedNodeIds.value = []
  selectedEdgeId.value = null
  clearValidation()
  if (emuWarning) ElMessage.warning(emuWarning)
}

function openSaveLibrary() {
  if (!libraryTargets.value.length) return
  if (savingComposite.value)
    libName.value = ''
  else
    libName.value = libraryTargets.value[0].label || '未命名设备'
  libNamePrefix.value = ''
  libDialog.value = true
}

async function saveLibrary() {
  const targets = libraryTargets.value
  if (!targets.length) return
  savingLib.value = true
  try {
    if (savingComposite.value) {
      const payload = captureComposite(compositeTargets.value, project.edges, {
        name: (libName.value || '').trim() || '未命名组合'
      })
      if (!payload) {
        ElMessage.warning('请至少选中 2 个设备再存为组合图元')
        return
      }
      const saved = await putTopologyLibrary(payload)
      for (const n of compositeTargets.value)
        n.libraryItemId = saved.id
      library.value = await getTopologyLibrary()
      libDialog.value = false
      ElMessage.success(`组合图元「${saved.name}」已写入设备库，可从左侧整组拖入`)
      return
    }

    const payloads = libraryPayloadsFromNodes(targets).map(p => ({
      ...p,
      name: libName.value || p.name
    }))
    for (const payload of payloads) {
      const { nodeId, ...item } = payload
      const saved = await putTopologyLibrary(item)
      const node = project.nodes.find(n => n.id === nodeId)
      if (node) node.libraryItemId = saved.id
    }
    library.value = await getTopologyLibrary()
    libDialog.value = false
    ElMessage.success('已写入设备库')
  } catch (e) {
    ElMessage.error(e.message || '保存设备库失败')
  } finally {
    savingLib.value = false
  }
}

async function removeLibrary(id) {
  try {
    await ElMessageBox.confirm('确定删除该设备库条目？', '确认', { type: 'warning' })
    await deleteTopologyLibrary(id)
    library.value = await getTopologyLibrary()
  } catch { /* cancel */ }
}

async function applyWizard() {
  const emuCount = Number(wizardEmuCount.value) || 0
  const pvCount = Number(wizardPvCount.value) || 0
  if (emuCount + pvCount < 1) {
    ElMessage.warning('EMU 与光伏单元至少需要 1 个')
    return
  }
  if (!(await confirmDiscardIfDirty('生成骨架'))) return
  wizardLoading.value = true
  try {
    const scaffolded = await postTopologyScaffold({
      emuCount,
      pvCount,
      name: wizardName.value || undefined,
      includeLoad: wizardIncludeLoad.value
    })
    applyProject(scaffolded, { resetHistory: true })
    dirty.value = true
    selectedNodeIds.value = []
    selectedEdgeId.value = null
    linking.value = null
    clearValidation()
    wizardOpen.value = false
    editHint.value = '向导已生成'
    const parts = []
    if (emuCount > 0) parts.push(`EMU×${emuCount}`)
    if (pvCount > 0) parts.push(`光伏×${pvCount}`)
    ElMessage.success(`已生成标准径向拓扑（${parts.join('、')}），请检查后保存`)
  } catch (e) {
    ElMessage.error(e.message || '生成失败')
  } finally {
    wizardLoading.value = false
  }
}

function onKey(ev) {
  const mod = ev.metaKey || ev.ctrlKey
  if (mod && ev.key.toLowerCase() === 'z') {
    const tag = (ev.target?.tagName || '').toLowerCase()
    if (tag === 'input' || tag === 'textarea') return
    ev.preventDefault()
    if (ev.shiftKey) redo()
    else undo()
    return
  }
  if (mod && ev.key.toLowerCase() === 'y') {
    const tag = (ev.target?.tagName || '').toLowerCase()
    if (tag === 'input' || tag === 'textarea') return
    ev.preventDefault()
    redo()
    return
  }
  if (ev.key === 'Escape') {
    linking.value = null
    pointerWorld.value = null
    if (!selectedEdgeId.value && !selectedNodeIds.value.length) return
    clearSelection()
    return
  }
  if (ev.key === 'Delete' || ev.key === 'Backspace') {
    const tag = (ev.target?.tagName || '').toLowerCase()
    if (tag === 'input' || tag === 'textarea') return
    if (canDelete.value) {
      ev.preventDefault()
      deleteSelected()
    }
  }
}

function onBeforeUnload(ev) {
  if (!dirty.value) return
  ev.preventDefault()
  ev.returnValue = ''
}

watch(
  () => `${route.query.mode || ''}:${route.query.id || ''}`,
  () => { syncEditHintFromRoute() }
)

function applyNarrow(mq) {
  isNarrow.value = mq.matches
  if (mq.matches) {
    paletteCollapsed.value = true
    if (!selectedNodeIds.value.length) propsDrawerOpen.value = false
  } else {
    propsDrawerOpen.value = true
  }
}

onMounted(async () => {
  if (route.query.id === 'mw300-ess-test') {
    ElMessage.warning('300MW 纯储能测试工程的拓扑已写死，不能在组态里修改')
    await router.push('/projects')
    return
  }
  try {
    await reload()
  } catch (e) {
    ElMessage.error(e.message || '加载组态失败')
  }
  window.addEventListener('keydown', onKey)
  window.addEventListener('beforeunload', onBeforeUnload)
  if (typeof window.matchMedia === 'function') {
    narrowMq = window.matchMedia(NARROW_MQ)
    applyNarrow(narrowMq)
    narrowMq.addEventListener('change', applyNarrow)
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKey)
  window.removeEventListener('beforeunload', onBeforeUnload)
  narrowMq?.removeEventListener?.('change', applyNarrow)
  linking.value = null
  pointerWorld.value = null
  connecting.value = false
  ElMessage.closeAll()
})
</script>

<style scoped>
.topo-page {
  height: 100%;
  max-height: calc(100vh - 80px);
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}
.toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 12px; margin-bottom: 0; flex-shrink: 0; }
.toolbar .left { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.toolbar .right { display: flex; align-items: center; justify-content: flex-end; min-width: 240px; flex-shrink: 0; font-variant-numeric: tabular-nums; }
.workspace {
  flex: 1;
  display: grid;
  grid-template-columns: 248px minmax(0, 1fr) 280px;
  gap: 8px;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
  position: relative;
}
.workspace.palette-collapsed { grid-template-columns: 52px minmax(0, 1fr) 280px; }
.workspace.narrow { grid-template-columns: 248px minmax(0, 1fr); }
.workspace.narrow.palette-collapsed { grid-template-columns: 52px minmax(0, 1fr); }
.props {
  margin-bottom: 0;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  padding: 10px 12px;
}
.workspace.narrow .props {
  position: absolute;
  right: 0;
  top: 0;
  bottom: 0;
  width: 280px;
  z-index: 20;
  box-shadow: 0 4px 16px rgba(0, 0, 0, .12);
}
.props-head { display: flex; justify-content: flex-end; flex-shrink: 0; margin: -4px 0 4px; }
.inspector-tabs { flex: 1; min-height: 0; display: flex; flex-direction: column; }
.inspector-tabs :deep(.el-tabs__header) { margin: 0 0 8px; flex-shrink: 0; }
.inspector-tabs :deep(.el-tabs__item) { padding: 0 10px; font-size: 13px; }
.inspector-tabs :deep(.el-tabs__content) { flex: 1; overflow: auto; }
.inspector-body { padding-bottom: 8px; }
.tab-badge { margin-left: 4px; }
.tab-badge :deep(.el-badge__content) { position: relative; transform: none; }
.canvas-wrap { margin-bottom: 0; padding: 0; overflow: hidden; display: flex; min-width: 0; min-height: 0; position: relative; }
/* 连线提示浮层：不占工具栏空间，避免右侧状态组宽度变化引起工具栏换行、画布上下抖动 */
.linking-tip {
  position: absolute; top: 10px; left: 50%; transform: translateX(-50%); z-index: 10;
  padding: 4px 12px; font-size: 12px; color: #e6a23c;
  background: #fdf6ec; border: 1px solid #faecd8; border-radius: 4px;
  pointer-events: none; box-shadow: 0 2px 8px rgba(0, 0, 0, .12);
}
.linking-tip {
  position: absolute; top: 10px; left: 50%; transform: translateX(-50%); z-index: 10;
  padding: 4px 12px; font-size: 12px; color: #e6a23c;
  background: #fdf6ec; border: 1px solid #faecd8; border-radius: 4px;
  pointer-events: none; box-shadow: 0 2px 8px rgba(0, 0, 0, .12);
}
.empty { font-size: 12px; color: #909399; line-height: 1.5; }
.param-hint { font-size: 11px; color: #909399; margin-top: 2px; line-height: 1.3; }
.bound-list { display: flex; flex-wrap: wrap; gap: 4px; }
.bound-tag { margin: 0 6px 4px 0; cursor: pointer; }
.validation-box { margin-bottom: 12px; }
.issue-list { margin: 6px 0 0; padding-left: 18px; }
.issue-item { font-size: 12px; line-height: 1.5; margin-bottom: 4px; }
.issue-item.clickable { cursor: pointer; color: #c45656; text-decoration: underline; }
.wizard-desc { font-size: 13px; color: #606266; line-height: 1.5; margin: 0 0 12px; }
.lib-target-list {
  margin: 0 0 8px;
  padding-left: 18px;
  max-height: 180px;
  overflow: auto;
  font-size: 12px;
  color: #606266;
  line-height: 1.6;
}
</style>
