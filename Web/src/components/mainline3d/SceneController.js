import * as THREE from 'three'
import { CSS2DRenderer, CSS2DObject } from 'three/examples/jsm/renderers/CSS2DRenderer.js'
import { createApp, h, reactive } from 'vue'
import { buildStation3dLayout, stationKey, paramNum } from './project3dLayout.js'
import { makeGraph, runtimeUnitIndexOfEmu } from '../topology/topologyMainLineLayout.js'
import { buildStation, setBreakerVisual, setPvArrayVisual } from './buildMeshes.js'
import { buildEnvironment } from './environment.js'
import { updateCableState, tickCable } from './powerFlow.js'
import { createInteraction } from './interaction.js'
import {
  buildDeviceDetail,
  updateDeviceDetailVisual,
  disposeDeviceDetail,
  setBmsClusterHover,
  setBmsClusterSelected
} from './deviceDetail.js'
import DevicePanel from './DevicePanel.vue'
import { getConfig, getBattery } from '@/services/api.js'

function fmtVolt(v) {
  if (v == null) return '—'
  return v >= 1000 ? `${(v / 1000).toFixed(1)} kV` : `${(v || 0).toFixed(1)} V`
}
function fmtHz(v) {
  if (v == null) return '—'
  return `${Number(v).toFixed(2)} Hz`
}
function fmtBreaker(closed, tripped) {
  return tripped ? '跳闸' : closed ? '合' : '分'
}

/** 断路器遥信：优先按 emuId 对齐运行时单元，避免馈线序号把分闸画成组态默认「合」。 */
function liveUnitForBreaker(snap, item) {
  const emuId = item?.emuId || item?.node?.parameters?.emuId
  let idx = Number.isFinite(item?.unitIndex) ? item.unitIndex : null
  if (emuId && snap?.topology?.nodes) {
    const runtime = runtimeUnitIndexOfEmu(
      makeGraph(snap.topology.nodes, snap.topology.edges),
      String(emuId)
    )
    if (runtime != null) idx = runtime
  }
  if (idx == null) return null
  return (snap.units || []).find(u => u.unitIndex === idx) || (snap.units || [])[idx] || null
}

/** PCS 实时有功：优先 ActualActivePowerKw（>0 放电，<0 充电） */
function channelPowerKw(ch) {
  if (!ch) return 0
  const v = ch.actualActivePowerKw
  if (v != null && Number.isFinite(Number(v))) return Number(v)
  // 兼容旧快照：从 "P实:12.3kW" 解析
  const m = String(ch.pcsActualP || '').match(/P实:(-?\d+(?:\.\d+)?)/)
  return m ? Number(m[1]) : 0
}

function pvUnitPowerKw(pv) {
  if (!pv) return 0
  const v = pv.activePowerKw
  return v != null && Number.isFinite(Number(v)) ? Number(v) : 0
}

function pvArrayOf(pv, side) {
  if (!pv) return null
  return side === 'B' ? (pv.arrayB || pv.ArrayB) : (pv.arrayA || pv.ArrayA)
}

/** 单元运行时通道列表：优先逐槽位 channels，兼容旧 A/B 双通道字段 */
function unitChannels(unit) {
  if (!unit) return []
  return (unit.channels || [unit.channelA, unit.channelB]).filter(Boolean)
}

/** 侧位字母（A=槽位 0）→ 单元对应运行时通道 */
function channelOfSide(unit, side) {
  const c = String(side || '').toUpperCase().charCodeAt(0)
  if (!Number.isFinite(c) || c < 65) return null
  return unitChannels(unit)[c - 65] || null
}

/** 单元全部通道实时有功合计 */
function unitPowerKw(unit) {
  return unitChannels(unit).reduce((s, ch) => s + channelPowerKw(ch), 0)
}

/**
 * Three.js 主接线场景控制器
 */
export class SceneController {
  /**
   * @param {HTMLElement} container
   * @param {{ onEvent: (name: string, payload?: any) => void }} handlers
   */
  constructor(container, handlers) {
    this.container = container
    this.onEvent = handlers?.onEvent || (() => {})
    this.snap = { units: [], pvUnits: [] }
    this.stationKey = ''
    this.stationRoot = null
    this.envRoot = null
    this.refs = null
    this.panelApps = new Map()
    this.labelObjects = new Map()
    this.panelStates = new Map()
    /** @type {'station'|'device'} */
    this.viewMode = 'station'
    this.detailKey = null
    this.detailRoot = null
    this._savedCamera = null
    /** @type {Map<number, object>} compartmentNumber -> topology */
    this._bmsTopologyByCompartment = new Map()
    this._batteryOverview = null
    this._batteryPollTimer = null
    this._raf = 0
    this._lastT = 0
    this._disposed = false

    const w = container.clientWidth || 800
    const h = container.clientHeight || 480

    this.scene = new THREE.Scene()
    this.scene.background = new THREE.Color(0x8a9aab)
    // 近景清晰、远景更快虚化
    this.scene.fog = new THREE.Fog(0x8a9aab, 48, 165)

    this.camera = new THREE.PerspectiveCamera(50, w / h, 0.1, 2000)
    this.camera.position.set(28, 22, 38)

    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false })
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2))
    this.renderer.setSize(w, h)
    this.renderer.shadowMap.enabled = true
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping
    this.renderer.toneMappingExposure = 1.35
    this.renderer.outputColorSpace = THREE.SRGBColorSpace
    container.appendChild(this.renderer.domElement)

    this.labelRenderer = new CSS2DRenderer()
    this.labelRenderer.setSize(w, h)
    this.labelRenderer.domElement.style.position = 'absolute'
    this.labelRenderer.domElement.style.inset = '0'
    this.labelRenderer.domElement.style.pointerEvents = 'none'
    this.labelRenderer.domElement.className = 'mainline3d-labels'
    container.appendChild(this.labelRenderer.domElement)

    this._setupLights()
    this._setupGround()

    this.interaction = createInteraction(this.camera, this.renderer.domElement, this.scene, {
      onBreakerClick: ({ pickId, unitIndex, layoutItem }) => {
        if (this.viewMode !== 'station') return
        if (pickId === 'main') this.onEvent('toggle-main-breaker')
        else {
          const live = liveUnitForBreaker(this.snap, layoutItem)
          const idx = live?.unitIndex ?? unitIndex
          if (typeof idx === 'number') this.onEvent('toggle-unit-breaker', idx)
        }
      },
      onDeviceClick: (panelKey) => {
        if (this.viewMode !== 'station') return
        this.toggleDevicePanel(panelKey)
      },
      onDeviceDblClick: (panelKey) => {
        if (String(panelKey || '').startsWith('bms-')) this.enterDeviceDetail(panelKey)
        else if (this.viewMode === 'station') this.toggleDevicePanel(panelKey)
      },
      onPointerMove: (e) => this._onDetailPointerMove(e),
      onClick: (e) => this._onDetailClick(e)
    })

    this._onResize = () => this.resize()
    window.addEventListener('resize', this._onResize)

    this.rebuild(this.snap)
    this.fitAll()
    this._loop = this._loop.bind(this)
    this._raf = requestAnimationFrame(this._loop)
    this._loadBmsTopology()
  }

  async _loadBmsTopology() {
    try {
      const cfg = await getConfig()
      const list = cfg?.simulator?.bmsTopology || []
      this._bmsTopologyByCompartment.clear()
      for (const t of list) {
        const n = Number(t.compartmentNumber)
        if (Number.isFinite(n)) this._bmsTopologyByCompartment.set(n, t)
      }
      // 若已在 BMS 详情中，用真实拓扑重建
      if (this.viewMode === 'device' && this.detailKey?.startsWith('bms-')) {
        const key = this.detailKey
        this.enterDeviceDetail(key, { skipSaveCamera: true })
      }
    } catch (e) {
      console.warn('load bms topology failed', e)
    }
  }

  _resolveBmsTopology(channel) {
    const n = Number(channel?.compartmentNumber ?? channel?.channelNumber)
    if (Number.isFinite(n) && this._bmsTopologyByCompartment.has(n)) {
      return this._bmsTopologyByCompartment.get(n)
    }
    // 回退：按 unitIndex + slot 匹配
    const ui = Number(channel?.unitIndex)
    const slot = Number(channel?.slotInUnit)
    for (const t of this._bmsTopologyByCompartment.values()) {
      if (t.unitIndex === ui && t.slotInUnit === slot) return t
    }
    return {
      clusterCount: Number(channel?.clusterCount) || 0,
      packCount: Number(channel?.packCount) || 0,
      cellSeriesCount: Number(channel?.cellSeriesCount) || 0,
      cellParallelCount: Number(channel?.cellParallelCount) || 0
    }
  }

  _setupLights() {
    const amb = new THREE.AmbientLight(0xdde5ef, 0.95)
    this.scene.add(amb)
    const hemi = new THREE.HemisphereLight(0xe8f0fa, 0x5a6a4a, 0.9)
    this.scene.add(hemi)
    const sun = new THREE.DirectionalLight(0xfff2dd, 1.45)
    sun.position.set(40, 55, 25)
    sun.castShadow = true
    sun.shadow.mapSize.set(1024, 1024)
    sun.shadow.camera.near = 5
    sun.shadow.camera.far = 200
    sun.shadow.camera.left = -90
    sun.shadow.camera.right = 90
    sun.shadow.camera.top = 90
    sun.shadow.camera.bottom = -90
    sun.shadow.bias = -0.0002
    this.scene.add(sun)
    this.sun = sun
    const fill = new THREE.DirectionalLight(0xb8d4f0, 0.55)
    fill.position.set(-30, 22, -15)
    this.scene.add(fill)
  }

  _setupGround() {
    this.groundMat = new THREE.MeshStandardMaterial({ color: 0x4d6a46, metalness: 0.02, roughness: 0.95 })
    this.ground = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), this.groundMat)
    this.ground.rotation.x = -Math.PI / 2
    this.ground.position.y = -0.02
    this.ground.receiveShadow = true
    this.scene.add(this.ground)
    // 不再叠加 GridHelper：与水泥垫/路面几乎共面时缩放会闪烁
    this.grid = null
    this._resizeGround(280)
  }

  /**
   * 按场景跨度放大地面/网格/雾效，避免单元落在“画布外”感
   * @param {number} span
   */
  _resizeGround(span) {
    const size = Math.max(240, Math.ceil(span / 40) * 40)
    if (this.ground) {
      this.ground.geometry.dispose()
      this.ground.geometry = new THREE.PlaneGeometry(size, size)
    }
    if (this.grid) {
      this.scene.remove(this.grid)
      this.grid.geometry?.dispose?.()
      if (Array.isArray(this.grid.material)) this.grid.material.forEach(m => m.dispose?.())
      else this.grid.material?.dispose?.()
      this.grid = null
    }
    if (this.scene.fog) {
      // 近景清晰、远景快速虚化，营造电站外围模糊感
      this.scene.fog.near = Math.max(36, size * 0.14)
      this.scene.fog.far = Math.max(120, size * 0.55)
    }
  }

  /**
   * 根据 station 包围盒更新相机可拉远距离与地面
   */
  _adaptSceneExtent() {
    if (!this.stationRoot || !this.interaction) return
    const box = new THREE.Box3().setFromObject(this.stationRoot)
    if (box.isEmpty()) return
    const size = box.getSize(new THREE.Vector3())
    const center = box.getCenter(new THREE.Vector3())
    const span = Math.max(size.x, size.z, 40)
    this._resizeGround(span * 1.8)
    // 地面跟随机组中心，避免右侧单元落在地面之外
    if (this.ground) {
      this.ground.position.set(center.x, 0, center.z)
    }
    if (this.grid) {
      this.grid.position.set(center.x, 0.02, center.z)
    }
    const maxDist = Math.max(200, span * 3.5)
    this.interaction.controls.maxDistance = maxDist
    this.camera.far = Math.max(2000, maxDist * 4)
    this.camera.updateProjectionMatrix()
    return { box, size, span, center }
  }

  resize() {
    if (this._disposed) return
    const w = this.container.clientWidth || 1
    const h = this.container.clientHeight || 1
    this.camera.aspect = w / h
    this.camera.updateProjectionMatrix()
    this.renderer.setSize(w, h)
    this.labelRenderer.setSize(w, h)
  }

  /**
   * @param {object} snap
   */
  updateFromSnap(snap) {
    if (this._disposed) return
    this.snap = snap || { units: [], pvUnits: [] }
    const key = stationKey(this.snap)
    if (key !== this.stationKey) {
      const keepDetailKey = this.viewMode === 'device' ? this.detailKey : null
      this.rebuild(this.snap)
      if (keepDetailKey) {
        this.enterDeviceDetail(keepDetailKey, { skipSaveCamera: true })
      } else {
        this.fitAll()
      }
    } else {
      this._syncState(this.snap)
    }
  }

  rebuild(snap) {
    const keepDetailKey = this.viewMode === 'device' ? this.detailKey : null
    if (this.detailKey) this._restorePanelAnchor(this.detailKey)
    this._disposeDetailRoot()
    this._disposeStation()
    this.stationKey = stationKey(snap)
    const layout = buildStation3dLayout(snap)
    this.stationLayout = layout
    const { root, refs } = buildStation(layout)
    this.stationRoot = root
    this.refs = refs
    this.scene.add(root)

    this.envRoot = buildEnvironment(layout)
    this.scene.add(this.envRoot)

    this._mountLabelsAndPanels(snap)
    this._syncState(snap)

    if (keepDetailKey) {
      this.viewMode = 'station'
      this.detailKey = null
    }
  }

  _disposeEnv() {
    if (!this.envRoot) return
    this.scene.remove(this.envRoot)
    this.envRoot.traverse(obj => {
      if (obj.isLight) obj.dispose?.()
      if (obj.geometry) obj.geometry.dispose?.()
      if (obj.material) {
        if (Array.isArray(obj.material)) obj.material.forEach(m => m.dispose?.())
        else obj.material.dispose?.()
      }
    })
    this.envRoot = null
  }

  _disposeStation() {
    for (const [, app] of this.panelApps) {
      try { app.unmount() } catch { /* ignore */ }
    }
    this.panelApps.clear()
    this.panelStates.clear()
    this.labelObjects.clear()

    this._disposeEnv()

    if (this.stationRoot) {
      this.scene.remove(this.stationRoot)
      this.stationRoot.traverse(obj => {
        if (obj.geometry) obj.geometry.dispose?.()
        if (obj.material) {
          if (Array.isArray(obj.material)) obj.material.forEach(m => m.dispose?.())
          else obj.material.dispose?.()
        }
        if (obj.isCSS2DObject && obj.element?.parentNode) {
          obj.element.parentNode.removeChild(obj.element)
        }
      })
      this.stationRoot = null
    }
    this.refs = null
  }

  _mountLabelsAndPanels(snap) {
    if (!this.refs) return

    for (const a of this.refs.labelAnchors) {
      const el = document.createElement('div')
      el.className = 'dt-float-label'
      // 标签纯展示：不拦截鼠标，避免遮挡画布的缩放/旋转/点选操作
      el.style.pointerEvents = 'none'
      const obj = new CSS2DObject(el)
      obj.position.copy(a.position)
      this.stationRoot.add(obj)
      this.labelObjects.set(a.key, { obj, el, kind: a.kind, unitIndex: a.unitIndex })
    }

    for (const a of this.refs.panelAnchors) {
      let panelProps = null
      if (a.type === 'pv' || a.type === 'pv-array') {
        const pv = (snap.pvUnits || []).find(u => (u.pvIndex ?? -1) === a.pvIndex)
          || (snap.pvUnits || [])[a.pvIndex]
        if (!pv) continue
        const state = a.type === 'pv-array'
          ? reactive({ unit: { ...pv }, array: { ...(pvArrayOf(pv, a.side) || {}) } })
          : reactive({ unit: { ...pv } })
        this.panelStates.set(a.key, state)
        panelProps = () => ({
          type: a.type,
          side: a.side || 'A',
          pvUnit: state.unit,
          pvArray: state.array,
          onClose: () => {
            if (this.viewMode === 'device' && this.detailKey === a.key) this.exitDeviceDetail()
            else this.setDevicePanelVisible(a.key, false)
          },
          onPvStart: (n) => this.onEvent('pv-start', n),
          onPvStop: (n) => this.onEvent('pv-stop', n),
          onPvSetPower: (p) => this.onEvent('pv-set-power', p && typeof p === 'object' ? p : {}),
          onPvSetReactive: (p) => this.onEvent('pv-set-reactive', p && typeof p === 'object' ? p : {}),
          onPvSetTemp: (p) => this.onEvent('pv-set-temp', p && typeof p === 'object' ? p : {}),
          onPvSetAngle: (p) => this.onEvent('pv-set-angle', p && typeof p === 'object' ? p : {}),
          onPvSetAlbedo: (p) => this.onEvent('pv-set-albedo', p && typeof p === 'object' ? p : {})
        })
      } else {
        const unit = (snap.units || []).find(u => (u.unitIndex ?? -1) === a.unitIndex)
          || (snap.units || [])[a.unitIndex]
        const channel = channelOfSide(unit, a.side)
        if (!channel) continue

        const state = reactive({ channel: { ...channel } })
        this.panelStates.set(a.key, state)
        panelProps = () => ({
          type: a.type,
          side: a.side,
          channel: state.channel,
          onClose: () => {
            if (this.viewMode === 'device' && this.detailKey === a.key) {
              this.exitDeviceDetail()
            } else {
              this.setDevicePanelVisible(a.key, false)
            }
          },
          onPcsStart: (n) => this.onEvent('pcs-start', n),
          onPcsStop: (n) => this.onEvent('pcs-stop', n),
          onPcsSetPower: (p) => {
            const payload = p && typeof p === 'object' && !('isTrusted' in p) ? p : {}
            this.onEvent('pcs-set-power', payload)
          },
          onPcsSetReactive: (p) => {
            const payload = p && typeof p === 'object' && !('isTrusted' in p) ? p : {}
            this.onEvent('pcs-set-reactive', payload)
          },
          onBmsPowerOn: (n) => this.onEvent('bms-power-on', n),
          onBmsPowerOff: (n) => this.onEvent('bms-power-off', n),
          onBmsFaultClear: (n) => this.onEvent('bms-fault-clear', n)
        })
      }

      const propsFn = panelProps
      const mountEl = document.createElement('div')
      mountEl.className = 'dt-panel-host'
      mountEl.style.pointerEvents = 'none'
      mountEl.style.display = 'none'
      const app = createApp({
        setup: () => () => h(DevicePanel, propsFn())
      })
      app.mount(mountEl)
      this.panelApps.set(a.key, app)

      const obj = new CSS2DObject(mountEl)
      obj.position.copy(a.position)
      obj.visible = false
      this.stationRoot.add(obj)
      this.labelObjects.set(a.key, { obj, el: mountEl, kind: 'panel', visible: false })
    }
  }

  /**
   * 解析 panelKey → { type, unitIndex, side, channel }
   * @param {string} panelKey
   */
  _resolvePanelKey(panelKey) {
    const m = String(panelKey || '').match(/^(pcs|bms)-(\d+)-([A-Z])$/)
    if (!m) return null
    const type = m[1]
    const unitIndex = Number(m[2])
    const side = m[3]
    const unit = (this.snap?.units || []).find(u => u.unitIndex === unitIndex)
      || (this.snap?.units || [])[unitIndex]
    const channel = channelOfSide(unit, side)
    if (!channel) return null
    return { type, unitIndex, side, channel, key: panelKey }
  }

  _disposeDetailRoot() {
    this._hideClusterInfoPanel()
    if (!this.detailRoot) return
    this.scene.remove(this.detailRoot)
    disposeDeviceDetail(this.detailRoot)
    this.detailRoot = null
    if (this.renderer?.domElement) this.renderer.domElement.style.cursor = ''
  }

  /**
   * BMS 详情：鼠标悬停簇高亮
   * @param {PointerEvent} e
   */
  _onDetailPointerMove(e) {
    if (this.viewMode !== 'device' || !this.detailRoot || this.detailRoot.userData.detailType !== 'bms') {
      return
    }
    if (e?.__leave || e?.clientX < 0) {
      setBmsClusterHover(this.detailRoot, -1)
      this.renderer.domElement.style.cursor = ''
      return
    }
    if (!this._hoverRaycaster) {
      this._hoverRaycaster = new THREE.Raycaster()
      this._hoverPointer = new THREE.Vector2()
    }
    const rect = this.renderer.domElement.getBoundingClientRect()
    this._hoverPointer.x = ((e.clientX - rect.left) / rect.width) * 2 - 1
    this._hoverPointer.y = -((e.clientY - rect.top) / rect.height) * 2 + 1
    this._hoverRaycaster.setFromCamera(this._hoverPointer, this.camera)

    const pickTargets = []
    for (const rack of this.detailRoot.userData.clusterGroups || []) {
      if (rack.userData.pickMesh) pickTargets.push(rack.userData.pickMesh)
    }
    const hits = this._hoverRaycaster.intersectObjects(pickTargets, false)
    let clusterIndex = -1
    if (hits.length) {
      clusterIndex = hits[0].object?.userData?.clusterIndex
      if (!Number.isFinite(clusterIndex)) clusterIndex = -1
    }
    setBmsClusterHover(this.detailRoot, clusterIndex)
    this.renderer.domElement.style.cursor = clusterIndex >= 0 ? 'pointer' : ''
  }

  /**
   * BMS 详情：单击选中簇（前移 + 信息 + 高低温单体）
   * @param {PointerEvent} e
   */
  _onDetailClick(e) {
    if (this.viewMode !== 'device' || !this.detailRoot || this.detailRoot.userData.detailType !== 'bms') {
      return
    }
    if (!this._hoverRaycaster) {
      this._hoverRaycaster = new THREE.Raycaster()
      this._hoverPointer = new THREE.Vector2()
    }
    const rect = this.renderer.domElement.getBoundingClientRect()
    this._hoverPointer.x = ((e.clientX - rect.left) / rect.width) * 2 - 1
    this._hoverPointer.y = -((e.clientY - rect.top) / rect.height) * 2 + 1
    this._hoverRaycaster.setFromCamera(this._hoverPointer, this.camera)

    const pickTargets = []
    for (const rack of this.detailRoot.userData.clusterGroups || []) {
      if (rack.userData.pickMesh) pickTargets.push(rack.userData.pickMesh)
    }
    const hits = this._hoverRaycaster.intersectObjects(pickTargets, false)
    if (!hits.length) {
      this._selectBmsCluster(-1)
      return
    }
    const clusterIndex = hits[0].object?.userData?.clusterIndex
    if (!Number.isFinite(clusterIndex)) return
    this._selectBmsCluster(clusterIndex)
  }

  _selectBmsCluster(clusterIndex) {
    if (!this.detailRoot) return
    const clusters = this._batteryOverview?.clusters || this._batteryOverview?.Clusters || []
    const dto = clusterIndex >= 0
      ? (clusters.find(c => (c.clusterId ?? c.ClusterId) === clusterIndex) || clusters[clusterIndex] || null)
      : null
    const rack = setBmsClusterSelected(this.detailRoot, clusterIndex, dto)
    const selected = this.detailRoot.userData.selectedCluster
    if (selected < 0 || !rack) {
      this._hideClusterInfoPanel()
      return
    }
    this._showClusterInfoPanel(rack, dto || { clusterId: selected })
  }

  _ensureClusterInfoPanel() {
    if (this._clusterInfoObj) return
    const el = document.createElement('div')
    el.className = 'dt-cluster-info'
    el.style.pointerEvents = 'none'
    el.style.display = 'none'
    const obj = new CSS2DObject(el)
    obj.visible = false
    this._clusterInfoEl = el
    this._clusterInfoObj = obj
  }

  _showClusterInfoPanel(rack, dto) {
    this._ensureClusterInfoPanel()
    const el = this._clusterInfoEl
    const obj = this._clusterInfoObj
    const id = (dto.clusterId ?? dto.ClusterId ?? 0) + 1
    const fmt = (v, d = 1) => (v != null && Number.isFinite(Number(v)) ? Number(v).toFixed(d) : '—')
    const maxPack = dto.maxCellTempPackId ?? dto.MaxCellTempPackId
    const maxCell = dto.maxCellTempCellId ?? dto.MaxCellTempCellId
    const minPack = dto.minCellTempPackId ?? dto.MinCellTempPackId
    const minCell = dto.minCellTempCellId ?? dto.MinCellTempCellId
    el.innerHTML = [
      `<div class="ci-title">簇 ${id}</div>`,
      `<div>V ${fmt(dto.totalVoltage ?? dto.TotalVoltage, 1)} V · I ${fmt(dto.totalCurrent ?? dto.TotalCurrent, 1)} A</div>`,
      `<div>P ${fmt(dto.powerKw ?? dto.PowerKw, 2)} kW · SOC ${fmt(dto.soc ?? dto.SOC, 1)}%</div>`,
      `<div>SOH ${fmt(dto.soh ?? dto.SOH, 1)}% · 均温 ${fmt(dto.avgCellTemp ?? dto.AvgCellTemp, 1)}℃</div>`,
      `<div class="ci-hot">最高温 包${maxPack ?? '—'} / 单体${maxCell ?? '—'} · ${fmt(dto.maxCellTemp ?? dto.MaxCellTemp, 1)}℃</div>`,
      `<div class="ci-cold">最低温 包${minPack ?? '—'} / 单体${minCell ?? '—'} · ${fmt(dto.minCellTemp ?? dto.MinCellTemp, 1)}℃</div>`
    ].join('')
    el.style.display = 'block'
    obj.visible = true

    const layout = rack.userData.cellLayout
    const h = layout ? layout.packBaseY + layout.packCount * (layout.packHActual + layout.packGapY) + 0.35 : 1.5
    obj.position.set(0, h + 0.35, (this.detailRoot.userData.selectZOffset || 0.45) + 0.2)
    if (obj.parent !== rack) {
      obj.parent?.remove(obj)
      rack.add(obj)
    }
  }

  _hideClusterInfoPanel() {
    if (!this._clusterInfoObj) return
    this._clusterInfoObj.visible = false
    if (this._clusterInfoEl) this._clusterInfoEl.style.display = 'none'
    this._clusterInfoObj.parent?.remove(this._clusterInfoObj)
  }

  _setStationVisible(visible) {
    if (this.stationRoot) this.stationRoot.visible = !!visible
    if (this.envRoot) this.envRoot.visible = !!visible
    if (this.grid) this.grid.visible = !!visible
    if (this.ground) this.ground.visible = !!visible
  }

  _notifyViewMode() {
    this.onEvent('view-mode', {
      mode: this.viewMode,
      detailKey: this.detailKey
    })
  }

  /**
   * 双击设备：切入 BMS 详情 3D（PCS 已禁用）
   * @param {string} panelKey
   * @param {{ skipSaveCamera?: boolean }} [opts]
   */
  enterDeviceDetail(panelKey, opts = {}) {
    const resolved = this._resolvePanelKey(panelKey)
    if (!resolved) return
    if (resolved.type !== 'bms') return

    if (this.viewMode === 'station' && !opts.skipSaveCamera) {
      this._savedCamera = {
        position: this.camera.position.clone(),
        target: this.interaction.controls.target.clone()
      }
    }

    // 关闭其它面板；若从另一设备详情切入，先把面板移回站级根
    for (const [key, e] of this.labelObjects) {
      if (e.kind === 'panel' && key !== panelKey) this.setDevicePanelVisible(key, false)
    }
    if (this.detailKey && this.detailKey !== panelKey) {
      this._restorePanelAnchor(this.detailKey)
    }

    this._disposeDetailRoot()
    const detailOpts = {}
    if (resolved.type === 'bms') {
      detailOpts.topology = this._resolveBmsTopology(resolved.channel)
      detailOpts.batteryOverview = this._batteryOverview
    }
    this.detailRoot = buildDeviceDetail(resolved.type, resolved.channel, detailOpts)
    this.detailRoot.position.set(0, 0, 0)
    this.scene.add(this.detailRoot)

    this._setStationVisible(false)
    this.viewMode = 'device'
    this.detailKey = panelKey

    // 详情模式允许更近观察；关闭远景雾效
    this.interaction.controls.minDistance = 2
    this.interaction.controls.maxDistance = 80
    if (this.scene.fog) {
      this._savedFog = { near: this.scene.fog.near, far: this.scene.fog.far }
      this.scene.fog.near = 120
      this.scene.fog.far = 280
    }

    this._placeDetailPanel(panelKey)
    this.fitDetail()
    this.setDevicePanelVisible(panelKey, true)
    this._notifyViewMode()

    if (resolved.type === 'bms') {
      this._startBatteryPoll(resolved.channel)
    } else {
      this._stopBatteryPoll()
    }
  }

  _stopBatteryPoll() {
    if (this._batteryPollTimer) {
      clearInterval(this._batteryPollTimer)
      this._batteryPollTimer = null
    }
    this._batteryOverview = null
  }

  /**
   * BMS 详情：拉取舱级簇 SOC 并刷新电芯着色
   * @param {object} channel
   */
  _startBatteryPoll(channel) {
    this._stopBatteryPoll()
    const unit = Number(channel?.compartmentNumber ?? channel?.channelNumber)
    if (!Number.isFinite(unit) || unit < 1) return

    const tick = async () => {
      if (this._disposed || this.viewMode !== 'device' || !this.detailKey?.startsWith('bms-')) return
      try {
        const overview = await getBattery(unit)
        this._batteryOverview = overview
        const resolved = this._resolvePanelKey(this.detailKey)
        if (resolved?.channel && this.detailRoot) {
          updateDeviceDetailVisual(this.detailRoot, resolved.channel, overview)
          const sel = this.detailRoot.userData.selectedCluster
          if (sel >= 0) {
            const clusters = overview?.clusters || overview?.Clusters || []
            const dto = clusters.find(c => (c.clusterId ?? c.ClusterId) === sel) || clusters[sel]
            const rack = (this.detailRoot.userData.clusterGroups || [])[sel]
            if (rack && dto) this._showClusterInfoPanel(rack, dto)
          }
        }
      } catch (e) {
        console.warn('battery overview poll failed', e)
      }
    }
    tick()
    this._batteryPollTimer = setInterval(tick, 2000)
  }

  /**
   * 退出详情，回到全站
   * @param {{ restoreCamera?: boolean }} [opts]
   */
  exitDeviceDetail(opts = {}) {
    if (this.viewMode !== 'device') return
    const key = this.detailKey
    // 先切回 station，避免 setDevicePanelVisible(false) 再次触发 exit
    this.viewMode = 'station'
    this.detailKey = null
    this._stopBatteryPoll()

    // 面板先移回站级，再销毁详情根
    if (key) this._restorePanelAnchor(key)

    this._disposeDetailRoot()
    this._setStationVisible(true)

    this.interaction.controls.minDistance = 8
    if (this.scene.fog && this._savedFog) {
      this.scene.fog.near = this._savedFog.near
      this.scene.fog.far = this._savedFog.far
      this._savedFog = null
    }
    this._adaptSceneExtent()

    if (key) {
      const entry = this.labelObjects.get(key)
      if (entry && entry.kind === 'panel') {
        entry.visible = false
        entry.obj.visible = false
        entry.el.style.display = 'none'
        entry.el.style.pointerEvents = 'none'
      }
    }

    const restore = opts.restoreCamera !== false
    if (restore && this._savedCamera) {
      this.camera.position.copy(this._savedCamera.position)
      this.interaction.controls.target.copy(this._savedCamera.target)
      this.interaction.controls.update()
    } else {
      this.fitAll()
    }
    this._savedCamera = null
    this._notifyViewMode()
  }

  /**
   * 双击设备：切换对应 PCS/BMS 面板；打开时关闭其它面板
   * @param {string} panelKey
   */
  toggleDevicePanel(panelKey) {
    const entry = this.labelObjects.get(panelKey)
    if (!entry || entry.kind !== 'panel') return
    const next = !entry.visible
    if (next) {
      for (const [key, e] of this.labelObjects) {
        if (e.kind === 'panel' && key !== panelKey) this.setDevicePanelVisible(key, false)
      }
    }
    this.setDevicePanelVisible(panelKey, next)
  }

  /**
   * @param {string} panelKey
   * @param {boolean} visible
   */
  setDevicePanelVisible(panelKey, visible) {
    const entry = this.labelObjects.get(panelKey)
    if (!entry || entry.kind !== 'panel') return
    entry.visible = !!visible
    entry.obj.visible = !!visible
    entry.el.style.display = visible ? 'block' : 'none'
    entry.el.style.pointerEvents = visible ? 'auto' : 'none'
  }

  /**
   * 将面板锚点挪到详情场景旁，便于阅读
   */
  _placeDetailPanel(panelKey) {
    const entry = this.labelObjects.get(panelKey)
    if (!entry || entry.kind !== 'panel') return
    const isBms = panelKey.startsWith('bms-')
    const layout = this.detailRoot?.userData?.layout
    if (isBms && layout) {
      entry.obj.position.set(layout.L * 0.42, Math.max(2.8, layout.H * 0.7), layout.W * 0.35)
    } else {
      entry.obj.position.set(isBms ? 4.2 : 2.4, isBms ? 2.8 : 3.2, isBms ? 1.5 : 1.2)
    }
    if (this.detailRoot && entry.obj.parent !== this.detailRoot) {
      entry.obj.parent?.remove(entry.obj)
      this.detailRoot.add(entry.obj)
    }
  }

  _restorePanelAnchor(panelKey) {
    const entry = this.labelObjects.get(panelKey)
    if (!entry || entry.kind !== 'panel' || !this.refs) return
    const anchor = this.refs.panelAnchors.find(a => a.key === panelKey)
    if (!anchor) return
    if (this.stationRoot && entry.obj.parent !== this.stationRoot) {
      entry.obj.parent?.remove(entry.obj)
      this.stationRoot.add(entry.obj)
    }
    entry.obj.position.copy(anchor.position)
  }

  _syncState(snap) {
    if (!this.refs || !snap) return

    setBreakerVisual(this.refs.mainBreaker, {
      closed: snap.mainBreakerClosed,
      tripped: snap.mainBreakerTripped
    })

    const mainLive = !!(snap.mainBreakerClosed && !snap.mainBreakerTripped)

    for (const u of snap.units || []) {
      const idx = u.unitIndex
      const br = this.refs.unitBreakers[idx]
      if (br) {
        setBreakerVisual(br, {
          closed: u.unitBreakerClosed,
          tripped: u.unitBreakerTripped
        })
      }
    }

    for (const pv of snap.pvUnits || []) {
      const idx = pv.pvIndex
      const br = this.refs.pvBreakers?.[idx]
      if (br) {
        setBreakerVisual(br, {
          closed: mainLive && (pv.running || pvUnitPowerKw(pv) > 0.5),
          tripped: !!snap.mainBreakerTripped
        })
      }
    }

    for (const br of this.refs.tieBreakers || []) {
      const item = br.userData?.layoutItem || {}
      const live = liveUnitForBreaker(snap, item)
      setBreakerVisual(br, live
        ? { closed: !!live.unitBreakerClosed, tripped: !!live.unitBreakerTripped }
        : {
          closed: !!item.node?.parameters?.closed,
          tripped: !!item.node?.parameters?.tripped
        })
    }

    for (const arr of this.refs.pvArrays || []) {
      const pv = (snap.pvUnits || []).find(x => x.pvIndex === arr.userData.pvIndex)
        || (snap.pvUnits || [])[arr.userData.pvIndex]
      const liveArr = pvArrayOf(pv, arr.userData.side)
      setPvArrayVisual(arr, {
        irradianceWm2: liveArr?.planeOfArrayWm2,
        running: !!(pv?.running || (liveArr?.activePowerKw > 0.5))
      })
    }

    // 全站对外有功：PCS 实发 + 光伏实发（光伏只向电网送电，≥0）
    let plantPowerKw = 0
    for (const u of snap.units || []) {
      plantPowerKw += unitPowerKw(u)
    }
    for (const pv of snap.pvUnits || []) {
      plantPowerKw += pvUnitPowerKw(pv)
    }

    for (const cable of this.refs.cables) {
      const role = cable.userData.cableRole
      const ui = cable.userData.unitIndex
      const pvIndex = cable.userData.pvIndex
      const side = cable.userData.side
      let energized = false
      let tripped = false
      let powerKw = 0

      if (role === 'grid-main' || role === 'main-xf' || role === 'xf-bus35' || role === 'sld-wire') {
        energized = mainLive
        tripped = !!snap.mainBreakerTripped
        powerKw = plantPowerKw
      } else if (role === 'pv-drop' || role === 'pv-xf' || role === 'pv-690') {
        const pv = (snap.pvUnits || []).find(x => x.pvIndex === pvIndex)
          || (snap.pvUnits || [])[pvIndex]
        const pvLive = !!(mainLive && pv)
        tripped = !!snap.mainBreakerTripped
        energized = pvLive
        powerKw = pvUnitPowerKw(pv)
      } else if (role === 'pv-inv' || role === 'pv-dc') {
        const pv = (snap.pvUnits || []).find(x => x.pvIndex === pvIndex)
          || (snap.pvUnits || [])[pvIndex]
        const arr = pvArrayOf(pv, side)
        tripped = !!snap.mainBreakerTripped
        energized = !!(mainLive && pv)
        powerKw = Number(arr?.activePowerKw) || 0
      } else {
        const unit = (snap.units || []).find(x => x.unitIndex === ui)
        const unitLive = !!(unit?.unitBreakerClosed && !unit?.unitBreakerTripped && mainLive)
        tripped = !!(unit?.unitBreakerTripped || snap.mainBreakerTripped)
        const unitPower = unitPowerKw(unit)

        if (role === 'unit-drop' || role === 'unit-xf' || role === 'unit-690') {
          energized = unitLive
          powerKw = unitPower
        } else if (role === 'pcs-feed' || role === 'dc-link') {
          const ch = channelOfSide(unit, side)
          energized = unitLive
          powerKw = channelPowerKw(ch)
        }
      }
      updateCableState(cable, { energized, tripped, powerKw })
    }

    // 母线汇流点通电态（随主断/单元断变化）
    for (const node of this.refs.busNodes || []) {
      const role = node.userData.busRole
      let live = mainLive
      if (role === 'unit-690-bus' || role === 'unit-lv-bus') {
        const unit = (snap.units || []).find(x => x.unitIndex === node.userData.unitIndex)
        live = !!(unit?.unitBreakerClosed && !unit?.unitBreakerTripped && mainLive)
      } else if (role === 'pv-690-bus' || role === 'ac-bus' || node.userData.layoutItem?.templateId === 'dc_bus') {
        live = mainLive
      }
      const core = node.userData?.core || node.children?.[0]
      if (core?.material) {
        core.material.color?.setHex?.(live ? 0xe07a3a : 0x8a9099)
        if (core.material.emissive) core.material.emissiveIntensity = live ? 0.55 : 0.12
      }
    }

    // 面板数据
    for (const [key, state] of this.panelStates) {
      const ess = key.match(/^(pcs|bms)-(\d+)-([A-Z])$/)
      if (ess) {
        const unitIndex = Number(ess[2])
        const side = ess[3]
        const unit = (snap.units || []).find(u => u.unitIndex === unitIndex)
        const ch = channelOfSide(unit, side)
        if (ch) Object.assign(state.channel, ch)
        continue
      }
      const xf = key.match(/^pvxf-(\d+)$/)
      if (xf && state.unit) {
        const pv = (snap.pvUnits || []).find(u => u.pvIndex === Number(xf[1]))
          || (snap.pvUnits || [])[Number(xf[1])]
        if (pv) Object.assign(state.unit, pv)
        continue
      }
      const arr = key.match(/^pvarr-(\d+)-(A|B)$/)
      if (arr && state.unit) {
        const pv = (snap.pvUnits || []).find(u => u.pvIndex === Number(arr[1]))
          || (snap.pvUnits || [])[Number(arr[1])]
        if (pv) {
          Object.assign(state.unit, pv)
          const liveArr = pvArrayOf(pv, arr[2])
          if (liveArr && state.array) Object.assign(state.array, liveArr)
        }
      }
    }

    // 详情外观随快照刷新
    if (this.viewMode === 'device' && this.detailRoot && this.detailKey) {
      const resolved = this._resolvePanelKey(this.detailKey)
      if (resolved?.channel) {
        updateDeviceDetailVisual(
          this.detailRoot,
          resolved.channel,
          this.detailKey.startsWith('bms-') ? this._batteryOverview : null
        )
      }
    }

    // 浮动标签：文案来自组态节点参数，不写死电压等级
    for (const a of this.refs.labelAnchors || []) {
      const item = a.item || {}
      const kind = a.kind
      let lines = item.text || []
      let title = !!item.title
      if (kind === 'grid') {
        const rated = paramNum(item.node, 'outputVoltage')
        lines = [
          item.node?.label || '电网',
          `PCC ${fmtVolt(snap.pccLineVoltageV)} / ${fmtHz(snap.systemFrequencyHz)}`,
          rated != null ? `额定 ${fmtVolt(rated)} / ${fmtHz(snap.gridNominalFrequencyHz)}` : ''
        ]
      } else if (kind === 'main-breaker' || kind === 'stem-breaker') {
        lines = [
          `${item.label || item.node?.label || '断路器'} ${fmtBreaker(snap.mainBreakerClosed, snap.mainBreakerTripped)}`
        ]
      } else if (kind === 'tie-breaker' || kind === 'branch-breaker') {
        const live = liveUnitForBreaker(snap, item)
        const closed = live ? !!live.unitBreakerClosed : !!item.node?.parameters?.closed
        const tripped = live ? !!live.unitBreakerTripped : !!item.node?.parameters?.tripped
        lines = [
          `${item.label || item.node?.label || '断路器'} ${fmtBreaker(closed, tripped)}`
        ]
      } else if (kind === 'station-xf') {
        lines = [
          item.label || item.node?.label || '变压器',
          item.ratioLabel,
          item.kvaLabel,
          fmtVolt(snap.mainTransformerSecondary?.lineVoltageV)
        ]
      } else if (kind === 'bus-bar' || kind === 'bus-node' || kind === 'dc-bus') {
        const v = item.voltage ?? paramNum(item.node, 'nominalVoltage')
        lines = [item.label || `${fmtVolt(v)} 母线`]
      } else if (kind === 'meter') {
        lines = [item.label || item.node?.label || '电表']
      } else if (kind === 'load') {
        lines = [
          item.label || item.node?.label || '负载',
          `${(Number(snap.loadActivePowerKw) || 0).toFixed(1)} kW`
        ]
      } else if (kind === 'unit-title') {
        title = true
        const u = (snap.units || []).find(x => x.unitIndex === a.unitIndex)
        lines = [item.text?.[0] || `UNIT ${u?.unitNumber ?? (a.unitIndex ?? 0) + 1}`]
      } else if (kind === 'unit-breaker') {
        const u = liveUnitForBreaker(snap, item)
          || (snap.units || []).find(x => x.unitIndex === a.unitIndex)
        // 组态绑定的真实断路器（如中压三相断路器）显示节点名；合成兑底单元回落「单元断」
        const name = item.label || item.node?.label || '单元断'
        const state = u?.unitBreakerLabel || fmtBreaker(u?.unitBreakerClosed, u?.unitBreakerTripped)
        lines = [`${name} ${state}`]
      } else if (kind === 'unit-xf') {
        const u = (snap.units || []).find(x => x.unitIndex === a.unitIndex)
        lines = [item.label || '单元变', u?.unitTransformerLine || fmtVolt(u?.unitTransformerSecondary?.lineVoltageV)]
      } else if (kind === 'pv-title') {
        title = true
        lines = item.text || [item.label]
      } else if (kind === 'pv-breaker') {
        const pv = (snap.pvUnits || []).find(x => x.pvIndex === a.pvIndex)
        lines = [`单元断 ${pv?.running ? '合' : '分'}`]
      } else if (kind === 'pv-xf') {
        const pv = (snap.pvUnits || []).find(x => x.pvIndex === a.pvIndex)
        const pKw = pvUnitPowerKw(pv)
        lines = [
          item.label || '箱变',
          `${pKw.toFixed(1)} kW / ${(Number(pv?.reactivePowerKvar) || 0).toFixed(1)} kvar`
        ]
      } else if (kind === 'pv-array') {
        const pv = (snap.pvUnits || []).find(x => x.pvIndex === a.pvIndex)
        const arr = pvArrayOf(pv, a.side)
        const irr = Number(arr?.planeOfArrayWm2)
        lines = [
          `方阵${a.side} ${(Number(arr?.activePowerKw) || 0).toFixed(1)} kW`,
          Number.isFinite(irr) ? `${irr.toFixed(0)} W/㎡` : ''
        ]
      }
      this._setLabel(a.key, lines, title)
    }
  }

  _setLabel(key, lines, title = false) {
    const entry = this.labelObjects.get(key)
    if (!entry || entry.kind === 'panel') return
    entry.el.className = title ? 'dt-float-label dt-unit-title' : 'dt-float-label'
    entry.el.innerHTML = lines.filter(Boolean).map(t => `<div>${t}</div>`).join('')
  }

  fitAll() {
    if (this.viewMode === 'device') {
      this.fitDetail()
      return
    }
    if (!this.stationRoot) return
    const extent = this._adaptSceneExtent()
    if (!extent) return
    const { box, size } = extent
    const center = box.getCenter(new THREE.Vector3())

    // 按 FOV / 宽高比计算刚好装下整站的距离，再留边距
    const fov = this.camera.fov * (Math.PI / 180)
    const fitHeight = size.y / (2 * Math.tan(fov / 2))
    const fitWidth = (size.x / this.camera.aspect) / (2 * Math.tan(fov / 2))
    const fitDepth = (size.z / this.camera.aspect) / (2 * Math.tan(fov / 2))
    let dist = Math.max(fitHeight, fitWidth, fitDepth, size.x * 0.55, size.z * 0.7, 40)
    dist *= 1.35

    const dir = new THREE.Vector3(0.42, 0.38, 0.82).normalize()
    this.camera.position.copy(center).addScaledVector(dir, dist)
    this.interaction.controls.target.copy(center)
    this.interaction.controls.update()
  }

  fitDetail() {
    if (!this.detailRoot) return
    const box = new THREE.Box3().setFromObject(this.detailRoot)
    if (box.isEmpty()) return
    const size = box.getSize(new THREE.Vector3())
    const center = box.getCenter(new THREE.Vector3())
    const fov = this.camera.fov * (Math.PI / 180)
    const fitH = size.y / (2 * Math.tan(fov / 2))
    const fitW = (size.x / this.camera.aspect) / (2 * Math.tan(fov / 2))
    const fitD = (size.z / this.camera.aspect) / (2 * Math.tan(fov / 2))
    let dist = Math.max(fitH, fitW, fitD, 4) * 1.55
    const dir = new THREE.Vector3(0.55, 0.35, 0.75).normalize()
    this.camera.position.copy(center).addScaledVector(dir, dist)
    this.interaction.controls.target.copy(center)
    this.interaction.controls.minDistance = 2
    this.interaction.controls.maxDistance = Math.max(40, dist * 3)
    this.interaction.controls.update()
  }

  setViewPreset(preset) {
    if (this.viewMode === 'device') {
      if (!this.detailRoot) return
      const box = new THREE.Box3().setFromObject(this.detailRoot)
      if (box.isEmpty()) return
      const size = box.getSize(new THREE.Vector3())
      const center = box.getCenter(new THREE.Vector3())
      const span = Math.max(size.x, size.z, 4)
      if (preset === 'top') {
        this.camera.position.set(center.x, center.y + span * 1.4, center.z + 0.01)
      } else if (preset === 'side') {
        this.camera.position.set(center.x + span * 1.5, center.y + span * 0.35, center.z)
      } else {
        this.fitDetail()
        return
      }
      this.interaction.controls.target.copy(center)
      this.interaction.controls.update()
      return
    }
    if (!this.stationRoot) return
    const extent = this._adaptSceneExtent()
    if (!extent) return
    const { box, size } = extent
    const center = box.getCenter(new THREE.Vector3())
    const span = Math.max(size.x, size.z, 40)
    if (preset === 'top') {
      this.camera.position.set(center.x, center.y + span * 1.15, center.z + 0.01)
    } else if (preset === 'side') {
      this.camera.position.set(center.x + span * 1.1, center.y + span * 0.28, center.z)
    } else {
      this.fitAll()
      return
    }
    this.interaction.controls.target.copy(center)
    this.interaction.controls.update()
  }

  _loop(t) {
    if (this._disposed) return
    this._raf = requestAnimationFrame(this._loop)
    const dt = this._lastT ? Math.min(0.05, (t - this._lastT) / 1000) : 0.016
    this._lastT = t

    if (this.refs?.mainBreaker?.userData?.tripped) {
      const pulse = 0.35 + 0.35 * Math.sin(t * 0.008)
      const mat = this.refs.mainBreaker.userData.bodyMat
      if (mat) mat.emissiveIntensity = pulse
    }
    for (const br of this.refs?.unitBreakers || []) {
      if (!br?.userData?.tripped) continue
      const pulse = 0.35 + 0.35 * Math.sin(t * 0.008)
      if (br.userData.bodyMat) br.userData.bodyMat.emissiveIntensity = pulse
    }
    for (const br of this.refs?.pvBreakers || []) {
      if (!br?.userData?.tripped) continue
      const pulse = 0.35 + 0.35 * Math.sin(t * 0.008)
      if (br.userData.bodyMat) br.userData.bodyMat.emissiveIntensity = pulse
    }

    for (const cable of this.refs?.cables || []) tickCable(cable, dt)

    this.interaction.controls.update()
    this.renderer.render(this.scene, this.camera)
    this.labelRenderer.render(this.scene, this.camera)
  }

  dispose() {
    this._disposed = true
    cancelAnimationFrame(this._raf)
    window.removeEventListener('resize', this._onResize)
    this._stopBatteryPoll()
    this._disposeDetailRoot()
    this._disposeStation()
    this.interaction?.dispose()
    this.renderer.dispose()
    if (this.renderer.domElement.parentNode) {
      this.renderer.domElement.parentNode.removeChild(this.renderer.domElement)
    }
    if (this.labelRenderer.domElement.parentNode) {
      this.labelRenderer.domElement.parentNode.removeChild(this.labelRenderer.domElement)
    }
  }
}
