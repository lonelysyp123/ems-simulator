<template>
  <div v-if="isStandalone" class="app-layout ems-shell">
    <header class="app-header ems-header">
      <span class="title">第三方 EMS</span>
      <span class="status">
        <el-tag size="small" effect="dark" type="info">直连数据模型</el-tag>
      </span>
    </header>
    <main class="app-main ems-main">
      <router-view />
    </main>
  </div>
  <div v-else class="app-layout" :class="{ 'is-system-locked': systemLock.locked }">
    <header class="app-header">
      <span class="title">储能仿真模拟器</span>
      <span class="status">
        <el-tag :type="ready ? 'success' : 'warning'" size="small" effect="dark">
          {{ ready ? '仿真就绪' : '加载中' }}
        </el-tag>
        <el-tag v-if="alert.isActive" type="danger" size="small" effect="dark" style="margin-left:8px">
          严重故障：{{ alert.message }}（{{ alert.secondsUntilExit }}s 后退出）
        </el-tag>
      </span>
    </header>
    <div class="app-body">
      <aside class="app-aside">
        <el-menu :default-active="menuActive" router class="app-menu">
          <div class="menu-group-label">电气接线</div>
          <el-menu-item index="/mainline">
            <el-icon><Connection /></el-icon>
            <span>电站概览</span>
          </el-menu-item>
          <el-menu-item v-if="allowMainline3d" index="/mainline-3d">
            <el-icon><Monitor /></el-icon>
            <span>数字孪生</span>
          </el-menu-item>
          <el-menu-item v-if="allowTopologyEditor" index="/topology">
            <el-icon><EditPen /></el-icon>
            <span>组态编辑</span>
          </el-menu-item>
          <el-menu-item v-if="allowTopologyEditor" index="/projects">
            <el-icon><FolderOpened /></el-icon>
            <span>工程配置</span>
          </el-menu-item>

          <div class="menu-group-label">电池系统</div>
          <el-menu-item index="/battery">
            <el-icon><BatteryStackIcon /></el-icon>
            <span>电池堆簇信息</span>
          </el-menu-item>
          <el-menu-item index="/cells">
            <el-icon><Grid /></el-icon>
            <span>电池单体信息</span>
          </el-menu-item>
          <el-menu-item index="/thresholds">
            <el-icon><SetUp /></el-icon>
            <span>BMS 告警门限</span>
          </el-menu-item>
          <el-menu-item index="/alarms">
            <el-icon><Warning /></el-icon>
            <span>设备告警</span>
          </el-menu-item>

          <div class="menu-group-label">运维工具</div>
          <el-menu-item index="/command">
            <el-icon><Promotion /></el-icon>
            <span>命令输入</span>
          </el-menu-item>
          <el-menu-item index="/ems">
            <el-icon><Odometer /></el-icon>
            <span>EMS</span>
          </el-menu-item>
          <el-menu-item v-if="allowDroopSlices" index="/droop-slices">
            <el-icon><DataAnalysis /></el-icon>
            <span>白盒切片</span>
          </el-menu-item>
          <el-menu-item index="/connections">
            <el-icon><Link /></el-icon>
            <span>连接信息</span>
          </el-menu-item>
          <el-menu-item index="/iec61850">
            <el-icon><Connection /></el-icon>
            <span>IEC 61850</span>
          </el-menu-item>
          <el-menu-item index="/protocol-ports">
            <el-icon><Operation /></el-icon>
            <span>协议端口</span>
          </el-menu-item>
        </el-menu>
      </aside>
      <main class="app-main">
        <router-view />
      </main>
    </div>

    <!-- 系统重新初始化：全屏遮罩，禁止切换与其它操作 -->
    <div
      v-if="systemLock.locked"
      class="system-lock-mask"
      role="alertdialog"
      aria-modal="true"
      aria-busy="true"
    >
      <div class="system-lock-panel">
        <el-icon class="system-lock-spin" :size="36"><Loading /></el-icon>
        <div class="system-lock-title">正在重新初始化</div>
        <div class="system-lock-stage">{{ systemLock.stage || '处理中' }}</div>
        <el-progress
          class="system-lock-progress"
          :percentage="systemLock.progress"
          :stroke-width="12"
          striped
          striped-flow
          :duration="12"
          :status="systemLock.progress >= 100 ? 'success' : undefined"
        />
        <div class="system-lock-msg">{{ systemLock.message }}</div>
        <div class="system-lock-sub">请勿切换页面或关闭窗口</div>
      </div>
    </div>
    <!-- PCS / BMS 故障提示 -->
    <el-dialog
      v-model="faultDialog.visible"
      title="设备故障"
      class="fault-dialog"
      width="520px"
      :close-on-click-modal="false"
      append-to-body
      align-center
    >
      <div class="fault-dialog-body">
        <p class="fault-dialog-lead">以下设备新发生故障，请确认。</p>
        <ul class="fault-dialog-list">
          <li v-for="item in faultDialog.items" :key="item.key">
            <span class="fault-dev">{{ item.title }}</span>
            <span class="fault-label">{{ item.label }}</span>
          </li>
        </ul>
      </div>
      <template #footer>
        <el-button @click="goAlarmsPage">查看告警页</el-button>
        <el-button type="danger" @click="ackFaultDialog">知道了</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Loading } from '@element-plus/icons-vue'
import { getHealth, getAlert, getAlarms, getHub, onHubMethod } from '@/services/api.js'
import { RealtimeMethods } from '@/services/constants.js'
import { systemLock } from '@/services/systemLock.js'
import { getEditionFeatures, loadEditionFeatures } from '@/services/editionFeatures.js'
import { diffNewFaults, mergeFaultItems } from '@/services/faultPopup.js'
import BatteryStackIcon from '@/components/icons/BatteryStackIcon.vue'

const router = useRouter()
const route = useRoute()
const isStandalone = computed(() => route.meta.standalone === true)
const menuActive = computed(() => {
  const p = route.path
  if (p === '/ems' || p.startsWith('/ems/')) return '/ems'
  if (p === '/system' || p.startsWith('/system/')) return '/projects'
  return p
})
const ready = ref(false)
const features = getEditionFeatures()
const allowDroopSlices = ref(features.allowDroopSlices)
const allowMainline3d = ref(features.allowMainline3d)
const allowTopologyEditor = ref(features.allowTopologyEditor)
const alert = reactive({ isActive: false, message: '', detail: '', secondsUntilExit: 0 })
const faultDialog = reactive({ visible: false, items: [] })
let lastAlarmSnap = null
let alarmTimer = null
let alarmInFlight = false

async function pollHealth() {
  try {
    const h = await getHealth()
    ready.value = !!h.ready
    if (ready.value) {
      try { await getHub() } catch { /* 后端刚起来时 SignalR 稍后重试 */ }
    }
  } catch { /* ignore */ }
}

async function pollAlarms() {
  if (alarmInFlight || !ready.value)
    return
  alarmInFlight = true
  try {
    const next = await getAlarms()
    const added = diffNewFaults(lastAlarmSnap, next)
    lastAlarmSnap = next
    if (added.length) {
      faultDialog.items = mergeFaultItems(faultDialog.items, added)
      faultDialog.visible = true
    }
  } catch { /* ignore */ }
  finally {
    alarmInFlight = false
  }
}

function ackFaultDialog() {
  faultDialog.visible = false
  faultDialog.items = []
}

function goAlarmsPage() {
  ackFaultDialog()
  router.push('/alarms')
}

onMounted(async () => {
  if (isStandalone.value)
    return

  await pollHealth()
  setInterval(pollHealth, 3000)

  try {
    const f = await loadEditionFeatures()
    allowDroopSlices.value = f.allowDroopSlices
    allowMainline3d.value = f.allowMainline3d
    allowTopologyEditor.value = f.allowTopologyEditor
  } catch { /* ignore */ }

  try {
    const a = await getAlert()
    Object.assign(alert, a)
  } catch { /* ignore */ }

  try {
    // 回调不得带返回值：SignalR 客户端会尝试把返回值回传给服务端并告警
    onHubMethod(RealtimeMethods.ReceiveAlert, a => { Object.assign(alert, a) })
    await getHub()
  } catch { /* ignore */ }

  await pollAlarms()
  alarmTimer = setInterval(pollAlarms, 2000)
})

onBeforeUnmount(() => {
  if (alarmTimer)
    clearInterval(alarmTimer)
})
</script>
