import * as THREE from 'three'

/**
 * PCS / BMS 设备详情剖切网格（独立于全站场景）
 */

const MAT = {
  pcsBody: () => new THREE.MeshStandardMaterial({ color: 0xf5f7fa, metalness: 0.2, roughness: 0.55 }),
  pcsDoor: () => new THREE.MeshStandardMaterial({ color: 0xffffff, metalness: 0.18, roughness: 0.5 }),
  pcsAccent: () => new THREE.MeshStandardMaterial({ color: 0xe4e7ed, metalness: 0.25, roughness: 0.48 }),
  darkSteel: () => new THREE.MeshStandardMaterial({ color: 0x3e4654, metalness: 0.55, roughness: 0.42 }),
  galvanized: () => new THREE.MeshStandardMaterial({ color: 0x9aa3ad, metalness: 0.7, roughness: 0.32 }),
  steel: () => new THREE.MeshStandardMaterial({ color: 0xa8b0bc, metalness: 0.62, roughness: 0.38 }),
  floor: () => new THREE.MeshStandardMaterial({ color: 0x8e959e, metalness: 0.06, roughness: 0.92 }),
  concrete: () => new THREE.MeshStandardMaterial({ color: 0x9aa1aa, metalness: 0.05, roughness: 0.9 }),
  module: () => new THREE.MeshStandardMaterial({ color: 0x2c3e50, metalness: 0.45, roughness: 0.4 }),
  heatsink: () => new THREE.MeshStandardMaterial({ color: 0x6b7a88, metalness: 0.65, roughness: 0.35 }),
  busDc: () => new THREE.MeshStandardMaterial({
    color: 0xe6a23c, metalness: 0.5, roughness: 0.35, emissive: 0x5a3a10, emissiveIntensity: 0.15
  }),
  busAc: () => new THREE.MeshStandardMaterial({
    color: 0x409eff, metalness: 0.5, roughness: 0.35, emissive: 0x103a5a, emissiveIntensity: 0.15
  }),
  ledIdle: () => new THREE.MeshStandardMaterial({
    color: 0xe6a23c, emissive: 0xb87a20, emissiveIntensity: 0.55, metalness: 0.1, roughness: 0.4
  }),
  ledRun: () => new THREE.MeshStandardMaterial({
    color: 0x67c23a, emissive: 0x3a9a20, emissiveIntensity: 0.9, metalness: 0.1, roughness: 0.4
  }),
  ledCharge: () => new THREE.MeshStandardMaterial({
    color: 0x38bdf8, emissive: 0x0284c7, emissiveIntensity: 0.85, metalness: 0.1, roughness: 0.4
  }),
  ledDischarge: () => new THREE.MeshStandardMaterial({
    color: 0xfbbf24, emissive: 0xd97706, emissiveIntensity: 0.85, metalness: 0.1, roughness: 0.4
  }),
  glass: () => new THREE.MeshStandardMaterial({
    color: 0x88aacc, metalness: 0.1, roughness: 0.15, transparent: true, opacity: 0.35
  }),
  bmsWall: () => new THREE.MeshStandardMaterial({ color: 0xf5f7fa, metalness: 0.15, roughness: 0.6 }),
  bmsDoor: () => new THREE.MeshStandardMaterial({ color: 0xf0f2f5, metalness: 0.2, roughness: 0.52 }),
  bmsRoof: () => new THREE.MeshStandardMaterial({ color: 0xe4e7ed, metalness: 0.3, roughness: 0.45 }),
  bmsCorrugation: () => new THREE.MeshStandardMaterial({ color: 0xffffff, metalness: 0.18, roughness: 0.55 }),
  rackFrame: () => new THREE.MeshStandardMaterial({ color: 0x4a5568, metalness: 0.4, roughness: 0.5 }),
  packBase: () => new THREE.MeshStandardMaterial({ color: 0x2d3748, metalness: 0.35, roughness: 0.45 })
}
function box(w, h, d, mat, y = h / 2) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat)
  m.position.y = y
  m.castShadow = true
  m.receiveShadow = true
  return m
}

function cyl(rTop, rBot, h, mat, y = h / 2, seg = 12) {
  const m = new THREE.Mesh(new THREE.CylinderGeometry(rTop, rBot, h, seg), mat)
  m.position.y = y
  m.castShadow = true
  m.receiveShadow = true
  return m
}

function disposeObject3D(root) {
  if (!root) return
  root.traverse(obj => {
    if (obj.geometry) obj.geometry.dispose?.()
    if (obj.material) {
      if (Array.isArray(obj.material)) obj.material.forEach(m => m.dispose?.())
      else obj.material.dispose?.()
    }
  })
}

/** SOC 0–100 → 红→黄→绿 */
function socColor(soc) {
  const t = Math.max(0, Math.min(100, Number(soc) || 0)) / 100
  const c = new THREE.Color()
  if (t < 0.5) c.setRGB(0.9, 0.25 + t * 1.1, 0.15)
  else c.setRGB(0.9 - (t - 0.5) * 1.4, 0.8, 0.2 + (t - 0.5) * 0.6)
  return c
}

function powerMode(channel) {
  const p = Number(channel?.actualActivePowerKw)
  if (!Number.isFinite(p) || Math.abs(p) < 0.05) return 'idle'
  return p > 0 ? 'discharge' : 'charge'
}

function isPcsRunning(channel) {
  const state = String(channel?.pcsDeviceState || '')
  const start = String(channel?.pcsStartStop || '')
  if (/停|off|停止/i.test(state) || /停|off/i.test(start)) return false
  if (/运|run|启|on/i.test(state) || /启|on|运/i.test(start)) return true
  return Math.abs(Number(channel?.actualActivePowerKw) || 0) > 0.05
}

/**
 * PCS 柜剖切详情
 * @param {object} channel
 * @returns {THREE.Group}
 */
export function buildPcsDetail(channel) {
  const g = new THREE.Group()
  g.userData.kind = 'pcs-detail'
  g.userData.detailType = 'pcs'

  const W = 2.6
  const H = 3.9
  const D = 1.8

  // 地板垫
  g.add(box(4.2, 0.08, 3.5, MAT.floor(), 0.04))

  // 柜体：后板 + 左右 + 顶 + 底（前侧开门剖切）
  const back = box(W, H, 0.08, MAT.pcsBody(), H / 2 + 0.15)
  back.position.z = -D / 2
  g.add(back)
  const left = box(0.08, H, D, MAT.pcsBody(), H / 2 + 0.15)
  left.position.x = -W / 2
  g.add(left)
  const right = box(0.08, H, D, MAT.pcsBody(), H / 2 + 0.15)
  right.position.x = W / 2
  g.add(right)
  const roof = box(W, 0.1, D, MAT.pcsAccent(), H + 0.2)
  g.add(roof)
  const floor = box(W, 0.12, D, MAT.darkSteel(), 0.21)
  g.add(floor)

  // 右侧门打开（铰链在右）
  const door = box(W * 0.48, H * 0.88, 0.06, MAT.pcsDoor(), H / 2 + 0.15)
  door.position.set(W / 2 + 0.02, 0, D / 2 - 0.3)
  door.rotation.y = -Math.PI * 0.72
  g.add(door)
  const handle = box(0.06, 0.3, 0.08, MAT.galvanized(), H / 2 + 0.15)
  handle.position.copy(door.position)
  handle.position.x -= 0.9
  handle.position.z += 0.05
  g.add(handle)

  // 左侧半开门（略开）
  const doorL = box(W * 0.48, H * 0.88, 0.06, MAT.pcsDoor(), H / 2 + 0.15)
  doorL.position.set(-W / 2 - 0.02, 0, D / 2 - 0.15)
  doorL.rotation.y = Math.PI * 0.35
  g.add(doorL)

  // 内部层架 + 功率模块
  const moduleMats = []
  for (let row = 0; row < 4; row++) {
    const shelfY = 0.55 + row * 0.85
    const shelf = box(W - 0.25, 0.05, D - 0.25, MAT.steel(), shelfY)
    g.add(shelf)
    for (let col = 0; col < 2; col++) {
      const mod = box(0.95, 0.55, 0.7, MAT.module(), shelfY + 0.35)
      mod.position.set(-0.55 + col * 1.1, 0, -0.15)
      g.add(mod)
      moduleMats.push(mod.material)
      // 散热器翅片
      for (let f = 0; f < 5; f++) {
        const fin = box(0.04, 0.45, 0.55, MAT.heatsink(), shelfY + 0.35)
        fin.position.set(-0.55 + col * 1.1 + 0.35, 0, 0.25)
        fin.position.x += f * 0.06
        g.add(fin)
      }
    }
  }

  // DC 母排（后上方）
  const dcBusMat = MAT.busDc()
  const dcBus = box(W - 0.4, 0.08, 0.12, dcBusMat, H - 0.35)
  dcBus.position.z = -D / 2 + 0.25
  g.add(dcBus)

  // AC 母排（前下方）
  const acBusMat = MAT.busAc()
  const acBus = box(W - 0.4, 0.08, 0.12, acBusMat, 0.55)
  acBus.position.z = D / 2 - 0.35
  g.add(acBus)

  // 三相出线柱
  for (let i = -1; i <= 1; i++) {
    const post = cyl(0.05, 0.05, 0.35, MAT.galvanized(), 0.45)
    post.position.set(i * 0.45, 0, D / 2 - 0.2)
    g.add(post)
  }

  // 顶部状态灯
  const ledMat = MAT.ledIdle()
  const led = cyl(0.1, 0.1, 0.08, ledMat, H + 0.35, 10)
  led.rotation.x = Math.PI / 2
  led.position.set(0, 0, D / 2 - 0.15)
  g.add(led)

  // 功率色带
  const stripMat = new THREE.MeshStandardMaterial({
    color: 0x67c23a, emissive: 0x1a5c2a, emissiveIntensity: 0.4, metalness: 0.2, roughness: 0.5
  })
  const strip = box(0.12, H * 0.7, 0.06, stripMat, H / 2 + 0.2)
  strip.position.set(-W / 2 + 0.08, 0, D / 2 - 0.05)
  g.add(strip)

  // 控制盒
  const ctrl = box(0.7, 0.5, 0.25, MAT.darkSteel(), 3.5)
  ctrl.position.set(0.7, 0, 0.2)
  g.add(ctrl)
  const screen = box(0.45, 0.28, 0.04, MAT.glass(), 3.52)
  screen.position.set(0.7, 0, 0.34)
  g.add(screen)

  const base = box(W + 0.15, 0.15, D + 0.1, MAT.concrete(), 0.12)
  g.add(base)

  g.userData.ledMat = ledMat
  g.userData.stripMat = stripMat
  g.userData.dcBusMat = dcBusMat
  g.userData.acBusMat = acBusMat
  g.userData.moduleMats = moduleMats

  updatePcsDetailVisual(g, channel)
  return g
}

/**
 * @param {THREE.Group} root
 * @param {object} channel
 */
export function updatePcsDetailVisual(root, channel) {
  if (!root || root.userData.detailType !== 'pcs') return
  const mode = powerMode(channel)
  const running = isPcsRunning(channel)
  const led = root.userData.ledMat
  const strip = root.userData.stripMat
  if (!led || !strip) return

  if (!running || mode === 'idle') {
    led.color.setHex(0xe6a23c)
    led.emissive.setHex(0xb87a20)
    led.emissiveIntensity = running ? 0.7 : 0.35
    strip.color.setHex(0x909399)
    strip.emissive.setHex(0x303030)
    strip.emissiveIntensity = 0.15
  } else if (mode === 'discharge') {
    led.color.setHex(0xfbbf24)
    led.emissive.setHex(0xd97706)
    led.emissiveIntensity = 0.95
    strip.color.setHex(0xfbbf24)
    strip.emissive.setHex(0xd97706)
    strip.emissiveIntensity = 0.7
  } else {
    led.color.setHex(0x38bdf8)
    led.emissive.setHex(0x0284c7)
    led.emissiveIntensity = 0.95
    strip.color.setHex(0x38bdf8)
    strip.emissive.setHex(0x0284c7)
    strip.emissiveIntensity = 0.7
  }

  const mag = Math.min(1, Math.abs(Number(channel?.actualActivePowerKw) || 0) / 500)
  if (root.userData.dcBusMat) {
    root.userData.dcBusMat.emissiveIntensity = 0.12 + mag * 0.55
  }
  if (root.userData.acBusMat) {
    root.userData.acBusMat.emissiveIntensity = 0.12 + mag * 0.55
  }
  for (const m of root.userData.moduleMats || []) {
    m.emissive = m.emissive || new THREE.Color(0x000000)
    if (running && mode !== 'idle') {
      m.emissive.setHex(mode === 'discharge' ? 0x3a2a08 : 0x082a3a)
      m.emissiveIntensity = 0.15 + mag * 0.35
    } else {
      m.emissive.setHex(0x000000)
      m.emissiveIntensity = 0
    }
  }
}

function makeTextSprite(text, color = '#ffffff', fontSize = 32) {
  if (typeof document === 'undefined') {
    return new THREE.Group()
  }
  const canvas = document.createElement('canvas')
  canvas.width = 512
  canvas.height = 128
  const ctx = canvas.getContext('2d')
  ctx.clearRect(0, 0, canvas.width, canvas.height)
  ctx.fillStyle = 'rgba(20,28,38,0.72)'
  ctx.fillRect(8, 16, canvas.width - 16, canvas.height - 32)
  ctx.font = `bold ${fontSize}px sans-serif`
  ctx.fillStyle = color
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillText(String(text), canvas.width / 2, canvas.height / 2)
  const tex = new THREE.CanvasTexture(canvas)
  tex.needsUpdate = true
  const mat = new THREE.SpriteMaterial({ map: tex, transparent: true, depthTest: true })
  return new THREE.Sprite(mat)
}

/**
 * BMS 集装箱剖切详情：单排缩小簇架；舱体透明度高于簇；支持悬停高亮
 */
export function buildBmsDetail(channel, topology = {}, batteryOverview = null) {
  const g = new THREE.Group()
  g.userData.kind = 'bms-detail'
  g.userData.detailType = 'bms'

  const clusterCount = Math.max(0, Number(topology.clusterCount) || 0)
  const packCount = Math.max(0, Number(topology.packCount) || 0)
  const cellSeries = Math.max(0, Number(topology.cellSeriesCount) || 0)
  const cellParallel = Math.max(1, Number(topology.cellParallelCount) || 1)

  const SCALE = 0.58
  const CABIN_OPACITY = 0.28
  const CLUSTER_OPACITY = 0.58
  const CLUSTER_HOVER_OPACITY = 0.92

  const cellR = 0.028 * SCALE
  const cellH = 0.07 * SCALE
  const cellGapX = 0.01 * SCALE
  const cellGapZ = 0.008 * SCALE
  const seriesCols = Math.max(1, Math.ceil(Math.sqrt(cellSeries * 1.4)))
  const seriesRows = Math.max(1, Math.ceil(cellSeries / seriesCols))

  const packInnerW = seriesCols * (cellR * 2 + cellGapX) + 0.06 * SCALE
  const packInnerD = cellParallel * (seriesRows * (cellR * 2 + cellGapZ) + 0.03 * SCALE) + 0.05 * SCALE
  const packHActual = 0.16
  const packGapY = 0.055
  const rackPad = 0.06
  const rackW = packInnerW + rackPad * 2
  const rackD = packInnerD + rackPad * 2
  const rackH = packCount * (packHActual + packGapY) + 0.22

  const clustersPerRow = clusterCount
  const clusterGapX = 0.14
  const marginX = 0.35
  const marginZ = 0.28
  const headroom = 0.35
  const contentL = clustersPerRow * rackW + Math.max(0, clustersPerRow - 1) * clusterGapX
  const contentW = rackD
  const L = contentL + marginX * 2
  const W = contentW + marginZ * 2
  const H = rackH + headroom + 0.28

  g.userData.topology = { clusterCount, packCount, cellSeries, cellParallel }
  g.userData.layout = { L, W, H, rackW, rackD, rackH }
  g.userData.opacity = {
    cabin: CABIN_OPACITY,
    cluster: CLUSTER_OPACITY,
    clusterHover: CLUSTER_HOVER_OPACITY
  }
  g.userData.hoveredCluster = -1
  g.userData.selectedCluster = -1
  g.userData.selectZOffset = 0.45

  const cabinMats = []
  function cabinMat(factory) {
    const m = factory()
    m.transparent = true
    m.opacity = CABIN_OPACITY
    m.depthWrite = false
    cabinMats.push(m)
    return m
  }

  const wallT = 0.07
  const floorH = 0.1
  const floorY = floorH / 2
  // 不透明实心底板：底部干净收口，避免透明排序造成“悬空腿/重影”
  const floorMat = MAT.concrete()
  floorMat.transparent = false
  floorMat.opacity = 1
  floorMat.depthWrite = true
  const cabinFloor = box(L + 0.02, floorH, W + 0.02, floorMat, floorY)
  cabinFloor.receiveShadow = true
  cabinFloor.userData.isCabinShell = true
  g.add(cabinFloor)

  const wallBottom = floorH
  // 仅保留后墙+顶板+实心底板；去掉两端侧墙与白色波纹筋
  // （透明端墙/波纹筋在俯视透视下会投影到地板下方，形成白色竖条与灰色凸块）
  const back = box(L, H, wallT, cabinMat(MAT.bmsWall), wallBottom + H / 2)
  back.position.z = -W / 2
  back.userData.isCabinShell = true
  g.add(back)

  const roof = box(L + 0.08, 0.08, W + 0.08, cabinMat(MAT.bmsRoof), wallBottom + H + 0.04)
  roof.userData.isCabinShell = true
  g.add(roof)

  const hvacW = Math.min(1.4, L * 0.18)
  const hvac = box(hvacW, 0.38, Math.min(0.85, W * 0.45), cabinMat(MAT.darkSteel), wallBottom + H + 0.35)
  hvac.position.set(-L / 2 + hvacW * 0.55 + 0.25, 0, 0)
  hvac.userData.isCabinShell = true
  g.add(hvac)

  const title = makeTextSprite(
    `舱${channel?.compartmentNumber ?? ''}  ${clusterCount}簇×${packCount}包×${cellSeries}S${cellParallel > 1 ? `${cellParallel}P` : ''}`,
    '#e8f0fa',
    28
  )
  title.position.set(0, wallBottom + H + 0.65, W / 2 - 0.08)
  title.scale.set(Math.min(4.2, Math.max(2.4, L * 0.28)), 0.55, 1)
  g.add(title)

  // 簇架落在实心底板上，不再用通高四角立柱（避免底部条形码悬空腿）
  const packBaseY = floorH + 0.04
  const stackH = packCount * (packHActual + packGapY)
  const rackFrameH = stackH + 0.06

  const cellGeo = new THREE.CylinderGeometry(cellR, cellR, cellH, 8)
  const clusterSocMats = []
  const clusterGroups = []
  const packFrameMats = []
  const startX = -((clustersPerRow - 1) * (rackW + clusterGapX)) / 2
  const dummy = new THREE.Object3D()

  for (let ci = 0; ci < clusterCount; ci++) {
    const cx = startX + ci * (rackW + clusterGapX)
    const frameColor = new THREE.Color(0x6a7d8c)
    const frameMat = new THREE.MeshStandardMaterial({
      color: frameColor,
      metalness: 0.35,
      roughness: 0.45,
      emissive: new THREE.Color(0x1a2430),
      emissiveIntensity: 0.12,
      transparent: true,
      opacity: CLUSTER_OPACITY
    })

    const rack = new THREE.Group()
    rack.position.set(cx, 0, 0)
    rack.userData.clusterIndex = ci
    rack.userData.isClusterRoot = true
    const clusterMats = [frameMat]

    const tagCluster = (obj) => {
      obj.userData.clusterIndex = ci
      obj.userData.isClusterPart = true
    }

    // 底座贴地板
    const baseMesh = box(rackW, 0.04, rackD, frameMat, packBaseY)
    tagCluster(baseMesh)
    rack.add(baseMesh)
    // 仅左右侧板（短），不再做四角通高立柱
    for (const dx of [-rackW / 2 + 0.02, rackW / 2 - 0.02]) {
      const side = box(0.03, rackFrameH, rackD * 0.92, frameMat, packBaseY + rackFrameH / 2)
      side.position.x = dx
      tagCluster(side)
      rack.add(side)
    }
    const topBeam = box(rackW, 0.03, rackD, frameMat, packBaseY + rackFrameH)
    tagCluster(topBeam)
    rack.add(topBeam)

    const cLabel = makeTextSprite(`簇${ci + 1}`, '#ffffff', 36)
    cLabel.position.set(0, packBaseY + rackFrameH + 0.16, rackD / 2 + 0.04)
    cLabel.scale.set(0.55, 0.22, 1)
    cLabel.userData.clusterIndex = ci
    rack.add(cLabel)

    const cellsInCluster = packCount * cellSeries * cellParallel
    const cellMat = new THREE.MeshStandardMaterial({
      color: 0x3a9a20,
      metalness: 0.25,
      roughness: 0.45,
      emissive: 0x1a5c2a,
      emissiveIntensity: 0.2,
      transparent: true,
      opacity: CLUSTER_OPACITY
    })
    clusterSocMats.push(cellMat)
    clusterMats.push(cellMat)
    const cellInst = new THREE.InstancedMesh(cellGeo, cellMat, cellsInCluster)
    cellInst.castShadow = true
    cellInst.receiveShadow = true
    tagCluster(cellInst)
    let instanceId = 0

    for (let pi = 0; pi < packCount; pi++) {
      const py = packBaseY + 0.03 + pi * (packHActual + packGapY)
      const trayMat = new THREE.MeshStandardMaterial({
        color: 0x2d3748,
        metalness: 0.4,
        roughness: 0.5,
        transparent: true,
        opacity: CLUSTER_OPACITY
      })
      packFrameMats.push(trayMat)
      clusterMats.push(trayMat)
      const tray = box(packInnerW + 0.04, 0.03, packInnerD + 0.04, trayMat, py)
      tagCluster(tray)
      rack.add(tray)

      const rim = box(packInnerW + 0.05, packHActual * 0.8, 0.025, frameMat, py + packHActual * 0.35)
      rim.position.z = packInnerD / 2 + 0.015
      tagCluster(rim)
      rack.add(rim)
      const rimBack = box(packInnerW + 0.05, packHActual * 0.8, 0.025, frameMat, py + packHActual * 0.35)
      rimBack.position.z = -packInnerD / 2 - 0.015
      tagCluster(rimBack)
      rack.add(rimBack)

      const pLabel = makeTextSprite(`P${pi + 1}`, '#fbbf24', 26)
      pLabel.position.set(packInnerW / 2 + 0.08, py + packHActual * 0.4, 0)
      pLabel.scale.set(0.28, 0.13, 1)
      pLabel.userData.clusterIndex = ci
      rack.add(pLabel)

      for (let par = 0; par < cellParallel; par++) {
        const zBase = -packInnerD / 2 + 0.04 + par * (seriesRows * (cellR * 2 + cellGapZ) + 0.03)
        for (let s = 0; s < cellSeries; s++) {
          const sx = s % seriesCols
          const sz = Math.floor(s / seriesCols)
          const x = -packInnerW / 2 + 0.04 + cellR + sx * (cellR * 2 + cellGapX)
          const z = zBase + cellR + sz * (cellR * 2 + cellGapZ)
          dummy.position.set(x, py + 0.05 + cellH / 2, z)
          dummy.rotation.set(0, 0, 0)
          dummy.scale.set(1, 1, 1)
          dummy.updateMatrix()
          cellInst.setMatrixAt(instanceId++, dummy.matrix)
        }
      }
    }

    cellInst.count = instanceId
    cellInst.instanceMatrix.needsUpdate = true
    rack.add(cellInst)

    const pickCenterY = packBaseY + rackFrameH / 2
    const pickGeo = new THREE.BoxGeometry(rackW + 0.04, rackFrameH + 0.08, rackD + 0.04)
    const pickMat = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0, depthWrite: false })
    const pickMesh = new THREE.Mesh(pickGeo, pickMat)
    pickMesh.position.y = pickCenterY
    pickMesh.userData.clusterIndex = ci
    pickMesh.userData.isClusterPick = true
    pickMesh.userData.isClusterPart = true
    rack.add(pickMesh)

    const hlGeo = new THREE.BoxGeometry(rackW + 0.08, rackFrameH + 0.1, rackD + 0.08)
    const hlEdges = new THREE.EdgesGeometry(hlGeo)
    const hlMat = new THREE.LineBasicMaterial({ color: 0x7dd3fc, transparent: true, opacity: 0.95 })
    const hl = new THREE.LineSegments(hlEdges, hlMat)
    hl.position.y = pickCenterY
    hl.visible = false
    hl.userData.isClusterHighlight = true
    rack.add(hl)

    const glowMat = new THREE.MeshBasicMaterial({
      color: 0x38bdf8, transparent: true, opacity: 0, depthWrite: false
    })
    const glow = new THREE.Mesh(new THREE.BoxGeometry(rackW + 0.1, 0.03, rackD + 0.1), glowMat)
    glow.position.y = packBaseY
    glow.visible = false
    glow.userData.isClusterGlow = true
    rack.add(glow)

    rack.userData.clusterMats = clusterMats
    rack.userData.highlight = hl
    rack.userData.glow = glow
    rack.userData.pickMesh = pickMesh
    rack.userData.baseZ = 0
    rack.userData.cellLayout = {
      packCount,
      cellSeries,
      cellParallel,
      seriesCols,
      seriesRows,
      packInnerW,
      packInnerD,
      packHActual,
      packGapY,
      cellR,
      cellH,
      cellGapX,
      cellGapZ,
      packBaseY: packBaseY + 0.03
    }
    g.add(rack)
    clusterGroups.push(rack)
  }

  const dcMat = MAT.busDc()
  dcMat.transparent = true
  dcMat.opacity = Math.min(0.85, CLUSTER_OPACITY + 0.15)
  const bus = box(L - 0.5, 0.05, 0.08, dcMat, wallBottom + H - 0.15)
  bus.position.z = 0.04
  bus.userData.isCabinShell = true
  g.add(bus)

  g.userData.clusterSocMats = clusterSocMats
  g.userData.packFrameMats = packFrameMats
  g.userData.clusterGroups = clusterGroups
  g.userData.cabinMats = cabinMats
  g.userData.dcBusMat = dcMat
  g.userData.cellGeo = cellGeo

  updateBmsDetailVisual(g, channel, batteryOverview)
  return g
}

/** 悬停簇：提高不透明度 + 高亮边框（选中簇保持强调） */
export function setBmsClusterHover(root, clusterIndex) {
  if (!root || root.userData.detailType !== 'bms') return
  const groups = root.userData.clusterGroups || []
  const base = root.userData.opacity?.cluster ?? 0.58
  const hover = root.userData.opacity?.clusterHover ?? 0.92
  const selected = root.userData.selectedCluster
  if (root.userData.hoveredCluster === clusterIndex) return
  root.userData.hoveredCluster = clusterIndex

  for (const rack of groups) {
    const ci = rack.userData.clusterIndex
    const active = ci === clusterIndex || ci === selected
    const opacity = active ? hover : base
    for (const m of rack.userData.clusterMats || []) {
      if (!m) continue
      m.opacity = opacity
      m.transparent = opacity < 0.99
    }
    if (rack.userData.highlight) {
      rack.userData.highlight.visible = ci === clusterIndex || ci === selected
    }
    if (rack.userData.glow) {
      const show = ci === clusterIndex || ci === selected
      rack.userData.glow.visible = show
      if (rack.userData.glow.material) rack.userData.glow.material.opacity = show ? 0.35 : 0
    }
  }
}

function cellLocalPos(layout, flatId) {
  const series = Math.max(1, layout.cellSeries || 1)
  const pack = Math.floor(flatId / series)
  const cellInPack = flatId % series
  const sx = cellInPack % layout.seriesCols
  const sz = Math.floor(cellInPack / layout.seriesCols)
  const py = layout.packBaseY + pack * (layout.packHActual + layout.packGapY)
  const x = -layout.packInnerW / 2 + 0.04 + layout.cellR + sx * (layout.cellR * 2 + layout.cellGapX)
  const zBase = -layout.packInnerD / 2 + 0.04
  const z = zBase + layout.cellR + sz * (layout.cellR * 2 + layout.cellGapZ)
  const y = py + 0.05 + layout.cellH / 2
  return new THREE.Vector3(x, y, z)
}

function clearTempMarkers(rack) {
  if (!rack?.userData?.tempMarkers) return
  for (const m of rack.userData.tempMarkers) {
    rack.remove(m)
    m.geometry?.dispose?.()
    if (m.material) {
      m.material.map?.dispose?.()
      m.material.dispose?.()
    }
  }
  rack.userData.tempMarkers = null
}

function addTempMarker(rack, layout, flatId, colorHex, label) {
  const pos = cellLocalPos(layout, flatId)
  const mat = new THREE.MeshStandardMaterial({
    color: colorHex,
    emissive: colorHex,
    emissiveIntensity: 0.85,
    metalness: 0.1,
    roughness: 0.35,
    transparent: true,
    opacity: 0.98
  })
  const r = Math.max(layout.cellR * 1.55, 0.035)
  const mesh = new THREE.Mesh(new THREE.CylinderGeometry(r, r, layout.cellH * 1.35, 10), mat)
  mesh.position.copy(pos)
  mesh.position.y += layout.cellH * 0.15
  mesh.userData.isTempMarker = true
  rack.add(mesh)

  const sprite = makeTextSprite(label, colorHex === 0xf56c6c ? '#ffb4b4' : '#b8f0c8', 30)
  sprite.position.set(pos.x, pos.y + 0.18, pos.z + layout.packInnerD * 0.15)
  sprite.scale.set(0.55, 0.22, 1)
  rack.add(sprite)
  return [mesh, sprite]
}

/**
 * 选中/取消选中簇：前移 + 高低温单体着色标记
 * @param {THREE.Group} root
 * @param {number} clusterIndex -1 取消
 * @param {object|null} clusterDto
 */
export function setBmsClusterSelected(root, clusterIndex, clusterDto = null) {
  if (!root || root.userData.detailType !== 'bms') return null
  const groups = root.userData.clusterGroups || []
  const offset = root.userData.selectZOffset ?? 0.45
  const prev = root.userData.selectedCluster
  // 再次点击同一簇 → 取消
  if (clusterIndex >= 0 && clusterIndex === prev) clusterIndex = -1
  root.userData.selectedCluster = clusterIndex

  let selectedRack = null
  for (const rack of groups) {
    const ci = rack.userData.clusterIndex
    const selected = ci === clusterIndex
    rack.position.z = selected ? offset : (rack.userData.baseZ ?? 0)
    clearTempMarkers(rack)

    if (selected && clusterDto && rack.userData.cellLayout) {
      selectedRack = rack
      const layout = rack.userData.cellLayout
      const series = Math.max(1, layout.cellSeries)
      let maxId = Number(clusterDto.maxCellTempId ?? clusterDto.MaxCellTempId)
      let minId = Number(clusterDto.minCellTempId ?? clusterDto.MinCellTempId)
      if (!Number.isFinite(maxId)) {
        const p = Number(clusterDto.maxCellTempPackId ?? clusterDto.MaxCellTempPackId) || 0
        const c = Number(clusterDto.maxCellTempCellId ?? clusterDto.MaxCellTempCellId) || 0
        maxId = p * series + c
      }
      if (!Number.isFinite(minId)) {
        const p = Number(clusterDto.minCellTempPackId ?? clusterDto.MinCellTempPackId) || 0
        const c = Number(clusterDto.minCellTempCellId ?? clusterDto.MinCellTempCellId) || 0
        minId = p * series + c
      }
      const maxT = Number(clusterDto.maxCellTemp ?? clusterDto.MaxCellTemp)
      const minT = Number(clusterDto.minCellTemp ?? clusterDto.MinCellTemp)
      const maxLabel = `最高温 ${Number.isFinite(maxT) ? maxT.toFixed(1) : '—'}℃`
      const minLabel = `最低温 ${Number.isFinite(minT) ? minT.toFixed(1) : '—'}℃`
      const markers = [
        ...addTempMarker(rack, layout, maxId, 0xf56c6c, maxLabel),
        ...addTempMarker(rack, layout, minId, 0x67c23a, minLabel)
      ]
      rack.userData.tempMarkers = markers
    }
  }

  // 同步透明度/高亮
  const hover = root.userData.hoveredCluster
  root.userData.hoveredCluster = -2 // force refresh
  setBmsClusterHover(root, hover)

  return selectedRack
}

export function updateBmsDetailVisual(root, channel, batteryOverview = null) {
  if (!root || root.userData.detailType !== 'bms') return
  const mats = root.userData.clusterSocMats || []
  const clusters = batteryOverview?.clusters || batteryOverview?.Clusters || null
  const fallbackSoc = Number(channel?.socPercent)
  const baseSoc = Number.isFinite(fallbackSoc) ? fallbackSoc : 50
  const hovered = root.userData.hoveredCluster
  const selected = root.userData.selectedCluster
  const baseOp = root.userData.opacity?.cluster ?? 0.58
  const hoverOp = root.userData.opacity?.clusterHover ?? 0.92

  for (let i = 0; i < mats.length; i++) {
    let soc = baseSoc
    if (Array.isArray(clusters) && clusters[i]) {
      const c = clusters[i]
      const v = c.soc ?? c.SOC
      if (v != null && Number.isFinite(Number(v))) soc = Number(v)
    } else {
      soc = baseSoc + ((i % 5) - 2) * 1.5
    }
    const color = socColor(soc)
    mats[i].color.copy(color)
    mats[i].emissive.copy(color).multiplyScalar(0.2)
    mats[i].emissiveIntensity = 0.18 + Math.max(0, Math.min(100, soc)) / 100 * 0.35
    mats[i].transparent = true
    mats[i].opacity = (i === hovered || i === selected) ? hoverOp : baseOp
  }

  // 选中簇时刷新高低温标记位置/数值
  if (selected >= 0 && Array.isArray(clusters) && clusters[selected]) {
    const rack = (root.userData.clusterGroups || [])[selected]
    if (rack?.userData?.tempMarkers) {
      const keep = selected
      const dto = clusters[selected]
      root.userData.selectedCluster = -1
      setBmsClusterSelected(root, keep, dto)
    }
  }

  if (root.userData.dcBusMat) {
    const idc = Math.abs(Number(channel?.dcCurrent) || 0)
    root.userData.dcBusMat.emissiveIntensity = 0.12 + Math.min(1, idc / 200) * 0.5
  }
}

export function buildDeviceDetail(type, channel, opts = {}) {
  if (type === 'bms') return buildBmsDetail(channel, opts.topology || {}, opts.batteryOverview || null)
  return buildPcsDetail(channel)
}

export function updateDeviceDetailVisual(root, channel, batteryOverview = null) {
  if (!root) return
  if (root.userData.detailType === 'bms') updateBmsDetailVisual(root, channel, batteryOverview)
  else updatePcsDetailVisual(root, channel)
}

export function disposeDeviceDetail(root) {
  if (!root) return
  const cellGeo = root.userData.cellGeo
  root.traverse(obj => {
    if (obj.isInstancedMesh) {
      if (obj.material) {
        if (Array.isArray(obj.material)) obj.material.forEach(m => m.dispose?.())
        else obj.material.dispose?.()
      }
      return
    }
    if (obj.geometry && obj.geometry !== cellGeo) obj.geometry.dispose?.()
    if (obj.material) {
      if (Array.isArray(obj.material)) {
        obj.material.forEach(m => { m.map?.dispose?.(); m.dispose?.() })
      } else {
        obj.material.map?.dispose?.()
        obj.material.dispose?.()
      }
    }
  })
  cellGeo?.dispose?.()
}
