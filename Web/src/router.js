import { createRouter, createWebHistory } from 'vue-router'
import { isSystemLocked } from './services/systemLock.js'
import { isEditionRouteAllowed, loadEditionFeatures } from './services/editionFeatures.js'

const routes = [
  { path: '/', redirect: '/mainline' },
  { path: '/mainline', name: 'mainline', component: () => import('./views/MainLineView.vue'), meta: { title: '电站概览' } },
  { path: '/mainline-3d', name: 'mainline-3d', component: () => import('./views/MainLine3dView.vue'), meta: { title: '数字孪生' } },
  { path: '/topology', name: 'topology', component: () => import('./views/TopologyView.vue'), meta: { title: '组态编辑' } },
  { path: '/projects', name: 'projects', component: () => import('./views/ProjectHubView.vue'), meta: { title: '工程配置' } },
  { path: '/system', redirect: '/projects' },
  { path: '/battery', name: 'battery', component: () => import('./views/BatteryView.vue'), meta: { title: '电池堆簇信息' } },
  { path: '/cells', name: 'cells', component: () => import('./views/CellsView.vue'), meta: { title: '电池单体信息' } },
  { path: '/thresholds', name: 'thresholds', component: () => import('./views/ThresholdsView.vue'), meta: { title: 'BMS 告警门限' } },
  { path: '/alarms', name: 'alarms', component: () => import('./views/AlarmsView.vue'), meta: { title: '设备告警' } },
  { path: '/command', name: 'command', component: () => import('./views/CommandView.vue'), meta: { title: '命令输入' } },
  {
    path: '/ems',
    component: () => import('./views/EmsHubView.vue'),
    meta: { title: 'EMS' },
    redirect: '/ems/strategy',
    children: [
      { path: 'strategy', name: 'ems-strategy', component: () => import('./views/EmsStrategyView.vue'), meta: { title: 'EMS 策略' } },
      { path: 'third-party', name: 'ems-third-party', component: () => import('./views/ThirdPartyEmsView.vue'), meta: { title: '第三方 EMS' } }
    ]
  },
  { path: '/ems-strategy', redirect: '/ems/strategy' },
  { path: '/droop-slices', name: 'droop-slices', component: () => import('./views/DroopSlicesView.vue'), meta: { title: '白盒切片' } },
  { path: '/connections', name: 'connections', component: () => import('./views/ConnectionsView.vue'), meta: { title: '连接信息' } },
  { path: '/iec61850', name: 'iec61850', component: () => import('./views/Iec61850View.vue'), meta: { title: 'IEC 61850' } },
  { path: '/protocol-ports', name: 'protocol-ports', component: () => import('./views/ProtocolPortsView.vue'), meta: { title: '协议端口' } },
  { path: '/third-party-ems', name: 'third-party-ems', component: () => import('./views/ThirdPartyEmsView.vue'), meta: { title: '第三方 EMS', standalone: true } }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach(async (to, from, next) => {
  if (isSystemLocked() && to.path !== from.path) {
    next(false)
    return
  }

  await loadEditionFeatures()
  if (!isEditionRouteAllowed(to.path)) {
    next('/mainline')
    return
  }

  next()
})

export default router
