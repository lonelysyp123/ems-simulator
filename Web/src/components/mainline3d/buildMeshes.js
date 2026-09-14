import * as THREE from 'three'
import { Y } from './layout.js'
import { createPowerCable } from './powerFlow.js'
import { pvArrayFieldSize, pvArrayRowXs, PV_PANEL_W, PV_PANEL_D, PV_PANEL_GAP_X, PV_ROW_PITCH } from './pvArrayLayout.js'

/**
 * 材质库：参考公共数字孪生/工业可视化中储能站设备配色
 * （油浸灰、PCS 蓝灰柜、集装箱黄/白、绝缘子釉色等）
 */
const MAT = {
  steel: () => new THREE.MeshStandardMaterial({ color: 0xa8b0bc, metalness: 0.62, roughness: 0.38 }),
  darkSteel: () => new THREE.MeshStandardMaterial({ color: 0x3e4654, metalness: 0.55, roughness: 0.42 }),
  galvanized: () => new THREE.MeshStandardMaterial({ color: 0x9aa3ad, metalness: 0.7, roughness: 0.32 }),
  concrete: () => new THREE.MeshStandardMaterial({ color: 0x7a8494, metalness: 0.05, roughness: 0.9 }),
  insulator: () => new THREE.MeshStandardMaterial({ color: 0xd4c4a0, metalness: 0.05, roughness: 0.55 }),
  insulatorBrown: () => new THREE.MeshStandardMaterial({ color: 0x8b5a2b, metalness: 0.08, roughness: 0.5 }),
  pcsBody: () => new THREE.MeshStandardMaterial({ color: 0xf5f7fa, metalness: 0.2, roughness: 0.55 }),
  pcsDoor: () => new THREE.MeshStandardMaterial({ color: 0xffffff, metalness: 0.18, roughness: 0.5 }),
  pcsAccent: () => new THREE.MeshStandardMaterial({ color: 0xe4e7ed, metalness: 0.25, roughness: 0.48 }),
  bmsWall: () => new THREE.MeshStandardMaterial({ color: 0xf5f7fa, metalness: 0.15, roughness: 0.6 }),
  bmsCorrugation: () => new THREE.MeshStandardMaterial({ color: 0xffffff, metalness: 0.18, roughness: 0.55 }),
  bmsDoor: () => new THREE.MeshStandardMaterial({ color: 0xf0f2f5, metalness: 0.2, roughness: 0.52 }),
  bmsRoof: () => new THREE.MeshStandardMaterial({ color: 0xe4e7ed, metalness: 0.3, roughness: 0.45 }),
  xfTank: () => new THREE.MeshStandardMaterial({ color: 0x6b7a88, metalness: 0.5, roughness: 0.4 }),
  xfRadiator: () => new THREE.MeshStandardMaterial({ color: 0x5a6878, metalness: 0.55, roughness: 0.38 }),
  boxXf: () => new THREE.MeshStandardMaterial({ color: 0xd8dde4, metalness: 0.25, roughness: 0.55 }),
  boxXfAccent: () => new THREE.MeshStandardMaterial({ color: 0x4a90c8, metalness: 0.3, roughness: 0.5 }),
  open: () => new THREE.MeshStandardMaterial({
    color: 0xf56c6c, metalness: 0.28, roughness: 0.4, emissive: 0x5c1a1a, emissiveIntensity: 0.3
  }),
  accent220: () => new THREE.MeshStandardMaterial({ color: 0xe0b83a, metalness: 0.45, roughness: 0.38 }),
  accent35: () => new THREE.MeshStandardMaterial({ color: 0x4fb5ba, metalness: 0.4, roughness: 0.4 }),
  ledGreen: () => new THREE.MeshStandardMaterial({
    color: 0x67c23a, emissive: 0x3a9a20, emissiveIntensity: 0.85, metalness: 0.1, roughness: 0.4
  }),
  ledAmber: () => new THREE.MeshStandardMaterial({
    color: 0xe6a23c, emissive: 0xb87a20, emissiveIntensity: 0.7, metalness: 0.1, roughness: 0.4
  }),
  glass: () => new THREE.MeshStandardMaterial({
    color: 0x88aacc, metalness: 0.1, roughness: 0.15, transparent: true, opacity: 0.45
  }),
  blackRubber: () => new THREE.MeshStandardMaterial({ color: 0x2a2a2a, metalness: 0.1, roughness: 0.85 }),
  pvFrame: () => new THREE.MeshStandardMaterial({ color: 0x4a5560, metalness: 0.55, roughness: 0.4 }),
  pvPost: () => new THREE.MeshStandardMaterial({ color: 0x6b7280, metalness: 0.45, roughness: 0.5 }),
  pvInvBody: () => new THREE.MeshStandardMaterial({ color: 0xe8edf2, metalness: 0.22, roughness: 0.52 })
}

function box(w, h, d, mat, y = h / 2) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat)
  m.position.y = y
  m.castShadow = true
  m.receiveShadow = true
  return m
}

function cyl(rTop, rBot, h, mat, y = h / 2, seg = 16) {
  const m = new THREE.Mesh(new THREE.CylinderGeometry(rTop, rBot, h, seg), mat)
  m.position.y = y
  m.castShadow = true
  m.receiveShadow = true
  return m
}

/** 盘形绝缘子串（公共变电站模型常见） */
function createInsulatorString(height = 1.4, scale = 1, brown = false) {
  const g = new THREE.Group()
  const mat = brown ? MAT.insulatorBrown() : MAT.insulator()
  const n = Math.max(3, Math.round(height / 0.28))
  for (let i = 0; i < n; i++) {
    const disc = cyl(0.22 * scale, 0.14 * scale, 0.1 * scale, mat, (i + 0.5) * (height / n), 12)
    g.add(disc)
  }
  const rod = cyl(0.04 * scale, 0.04 * scale, height, MAT.galvanized(), height / 2, 8)
  g.add(rod)
  return g
}

/** 220kV 门型进线构架 + 三相绝缘子串（位置由组态布局指定） */
export function createGridGantry(_x = 0) {
  const g = new THREE.Group()

  // 双柱门架
  for (const sx of [-2.8, 2.8]) {
    const leg = box(0.45, 11, 0.45, MAT.galvanized(), 5.5)
    leg.position.x = sx
    g.add(leg)
    // 斜撑
    const brace = box(0.12, 4.5, 0.12, MAT.steel(), 3.2)
    brace.position.set(sx * 0.55, 0, 0)
    brace.rotation.z = sx > 0 ? 0.35 : -0.35
    g.add(brace)
  }
  // 横梁 + 上弦
  g.add(box(6.2, 0.4, 0.4, MAT.galvanized(), 11))
  g.add(box(6.2, 0.18, 0.18, MAT.steel(), 10.35))
  // 挂点横担
  const crossArm = box(5.5, 0.2, 0.55, MAT.accent220(), 9.6)
  g.add(crossArm)

  // 三相绝缘子 + 线夹示意
  for (let i = -1; i <= 1; i++) {
    const ins = createInsulatorString(1.5, 1.05, true)
    ins.position.set(i * 1.55, 8.0, 0)
    g.add(ins)
    const clamp = box(0.35, 0.18, 0.35, MAT.darkSteel(), 9.7)
    clamp.position.x = i * 1.55
    g.add(clamp)
  }

  // 基础墩
  for (const sx of [-2.8, 2.8]) {
    const pier = box(1.1, 0.55, 1.1, MAT.concrete(), 0.28)
    pier.position.x = sx
    g.add(pier)
  }

  g.userData.kind = 'grid'
  return g
}

/**
 * 户外断路器（三相柱式）：参考公共变电站模型
 * — 三相灭弧室柱 + 支撑绝缘子 + 机构箱
 */
export function createBreakerMesh(id, unitIndex = null) {
  const g = new THREE.Group()
  const bodyMat = MAT.open()

  const frame = box(2.8, 0.18, 1.6, MAT.galvanized(), 0.35)
  g.add(frame)
  const pad = box(3.2, 0.18, 2.0, MAT.concrete(), 0.09)
  g.add(pad)

  // 三相柱
  for (let i = -1; i <= 1; i++) {
    const pole = new THREE.Group()
    pole.position.x = i * 0.85

    const support = createInsulatorString(1.1, 0.85)
    support.position.y = 0.45
    pole.add(support)

    // 灭弧室筒
    const chamber = cyl(0.22, 0.26, 1.15, bodyMat, 2.15, 14)
    chamber.userData.isBreakerBody = true
    pole.add(chamber)

    // 上下出线绝缘子
    const topBush = createInsulatorString(0.7, 0.7)
    topBush.position.y = 2.7
    pole.add(topBush)
    const topCap = cyl(0.08, 0.1, 0.2, MAT.accent220(), 3.45, 8)
    pole.add(topCap)

    g.add(pole)
  }

  // 操作机构箱
  const mech = box(0.9, 1.1, 0.7, MAT.darkSteel(), 0.9)
  mech.position.set(0, 0, 1.05)
  g.add(mech)
  const mechDoor = box(0.75, 0.85, 0.05, MAT.steel(), 0.95)
  mechDoor.position.set(0, 0, 1.42)
  g.add(mechDoor)

  // 状态指示灯条（随合分变色的主材质取三相中柱）
  const indicator = box(0.5, 0.12, 0.08, MAT.ledAmber(), 1.55)
  indicator.position.set(0, 0, 1.45)
  g.add(indicator)

  g.userData.kind = 'breaker'
  g.userData.pickId = id
  g.userData.unitIndex = unitIndex
  g.userData.bodyMat = bodyMat
  g.userData.indicatorMat = indicator.material

  g.traverse(o => {
    if (o.isMesh) {
      o.userData.pickId = id
      o.userData.unitIndex = unitIndex
      if (o.userData.isBreakerBody) o.material = bodyMat
    }
  })
  return g
}

export function setBreakerVisual(group, { closed, tripped }) {
  const mat = group?.userData?.bodyMat
  const ind = group?.userData?.indicatorMat
  if (!mat) return
  if (tripped) {
    mat.color.setHex(0xf56c6c)
    mat.emissive.setHex(0x7a1f1f)
    mat.emissiveIntensity = 0.55
    if (ind) {
      ind.color.setHex(0xf56c6c)
      ind.emissive.setHex(0xaa2020)
      ind.emissiveIntensity = 1.0
    }
  } else if (closed) {
    mat.color.setHex(0x67c23a)
    mat.emissive.setHex(0x1a5c2a)
    mat.emissiveIntensity = 0.4
    if (ind) {
      ind.color.setHex(0x67c23a)
      ind.emissive.setHex(0x3a9a20)
      ind.emissiveIntensity = 0.9
    }
  } else {
    mat.color.setHex(0xf56c6c)
    mat.emissive.setHex(0x5c1a1a)
    mat.emissiveIntensity = 0.3
    if (ind) {
      ind.color.setHex(0xe6a23c)
      ind.emissive.setHex(0xb87a20)
      ind.emissiveIntensity = 0.7
    }
  }
  group.userData.tripped = !!tripped
}

/**
 * 油浸主变：油箱 + 片式散热器 + 油枕 + 三相套管
 * （公共变电站/储能升压变模型常见结构）
 */
export function createTransformer(scale = 1, { boxType = false } = {}) {
  if (boxType) return createBoxTransformer(scale)
  const g = new THREE.Group()
  const s = scale

  // 油箱（矩形更接近真实油浸变外观）
  const tank = box(2.6 * s, 2.8 * s, 2.2 * s, MAT.xfTank(), 1.55 * s)
  g.add(tank)

  // 两侧散热器翅片
  for (const side of [-1, 1]) {
    for (let i = 0; i < 7; i++) {
      const fin = box(0.08 * s, 2.2 * s, 1.6 * s, MAT.xfRadiator(), 1.4 * s)
      fin.position.set(side * (1.4 * s + 0.06), 0, (i - 3) * 0.22 * s)
      g.add(fin)
    }
    const header = box(0.22 * s, 2.4 * s, 1.7 * s, MAT.darkSteel(), 1.45 * s)
    header.position.x = side * 1.55 * s
    g.add(header)
  }

  // 油枕
  const conservator = cyl(0.42 * s, 0.42 * s, 2.0 * s, MAT.darkSteel(), 3.55 * s, 16)
  conservator.rotation.z = Math.PI / 2
  conservator.position.set(0, 0.15 * s, 0)
  g.add(conservator)
  const pipe = cyl(0.08 * s, 0.08 * s, 0.7 * s, MAT.steel(), 3.15 * s, 8)
  pipe.position.x = 0.9 * s
  g.add(pipe)

  // HV 三相套管（顶部）
  for (let i = -1; i <= 1; i++) {
    const bush = createInsulatorString(1.35 * s, 0.95 * s, true)
    bush.position.set(i * 0.55 * s, 2.85 * s, -0.55 * s)
    g.add(bush)
    const tip = cyl(0.06 * s, 0.08 * s, 0.18 * s, MAT.accent220(), 4.3 * s, 8)
    tip.position.set(i * 0.55 * s, 0, -0.55 * s)
    g.add(tip)
  }
  // LV 套管（侧向较低）
  for (let i = -1; i <= 1; i++) {
    const bush = createInsulatorString(0.75 * s, 0.7 * s)
    bush.position.set(i * 0.4 * s, 2.6 * s, 0.85 * s)
    g.add(bush)
  }

  // 基础 + 滚轮示意
  const pad = box(3.6 * s, 0.28 * s, 3.0 * s, MAT.concrete(), 0.14 * s)
  g.add(pad)
  for (const wx of [-1.1, 1.1]) {
    for (const wz of [-0.9, 0.9]) {
      const wheel = cyl(0.18 * s, 0.18 * s, 0.12 * s, MAT.blackRubber(), 0.22 * s, 12)
      wheel.rotation.z = Math.PI / 2
      wheel.position.set(wx * s, 0, wz * s)
      g.add(wheel)
    }
  }

  // 铭牌区
  const plate = box(0.7 * s, 0.4 * s, 0.04 * s, MAT.steel(), 1.6 * s)
  plate.position.z = 1.14 * s
  g.add(plate)

  g.userData.kind = 'transformer'
  return g
}

/** 单元箱变（预制舱/美式箱变外观） */
function createBoxTransformer(scale = 1) {
  const g = new THREE.Group()
  const s = scale
  const body = box(3.2 * s, 2.4 * s, 2.0 * s, MAT.boxXf(), 1.35 * s)
  g.add(body)
  // 高压 / 低压仓分隔线
  const divider = box(0.06 * s, 2.2 * s, 2.02 * s, MAT.boxXfAccent(), 1.35 * s)
  g.add(divider)
  // 百叶通风
  for (let i = 0; i < 5; i++) {
    const louver = box(1.1 * s, 0.08 * s, 0.05 * s, MAT.darkSteel(), 0.9 * s + i * 0.28 * s)
    louver.position.set(-0.85 * s, 0, 1.02 * s)
    g.add(louver)
  }
  // 仓门把手
  for (const sx of [-0.9, 0.9]) {
    const handle = box(0.08 * s, 0.35 * s, 0.08 * s, MAT.galvanized(), 1.3 * s)
    handle.position.set(sx * s, 0, 1.05 * s)
    g.add(handle)
  }
  // 顶部散热罩
  const hood = box(3.0 * s, 0.25 * s, 1.8 * s, MAT.darkSteel(), 2.65 * s)
  g.add(hood)
  const pad = box(3.5 * s, 0.25 * s, 2.3 * s, MAT.concrete(), 0.12 * s)
  g.add(pad)
  // 出线套管小柱
  for (let i = -1; i <= 1; i++) {
    const bush = createInsulatorString(0.55 * s, 0.55 * s)
    bush.position.set(i * 0.45 * s, 2.55 * s, 0)
    g.add(bush)
  }
  g.userData.kind = 'box-transformer'
  return g
}

/**
 * PCS 柜：工业变流器柜常见造型
 * — 双开门、多道百叶、状态指示灯、底部进线、底座
 */
export function createPcsCabinet(panelKey) {
  const g = new THREE.Group()

  const body = box(2.5, 3.8, 1.7, MAT.pcsBody(), 2.05)
  g.add(body)
  const skirting = box(2.55, 0.25, 1.75, MAT.pcsAccent(), 0.2)
  g.add(skirting)

  // 双开门
  for (const sx of [-0.58, 0.58]) {
    const door = box(1.05, 3.2, 0.07, MAT.pcsDoor(), 2.0)
    door.position.set(sx, 0, 0.9)
    g.add(door)
    const handle = box(0.08, 0.35, 0.1, MAT.galvanized(), 2.0)
    handle.position.set(sx + (sx > 0 ? -0.35 : 0.35), 0, 0.98)
    g.add(handle)
    // 观察窗
    const win = box(0.45, 0.55, 0.04, MAT.glass(), 2.7)
    win.position.set(sx, 0, 0.94)
    g.add(win)
  }

  // 顶部与侧向百叶
  for (let i = 0; i < 6; i++) {
    const topLouver = box(2.1, 0.06, 0.05, MAT.darkSteel(), 3.55 - i * 0.08)
    topLouver.position.z = 0.88
    g.add(topLouver)
  }
  for (let i = 0; i < 8; i++) {
    const sideLouver = box(0.05, 0.12, 1.2, MAT.darkSteel(), 1.0 + i * 0.28)
    sideLouver.position.set(1.28, 0, 0)
    g.add(sideLouver)
  }

  // 状态灯带
  const ledBar = box(1.6, 0.1, 0.06, MAT.ledGreen(), 3.7)
  ledBar.position.z = 0.9
  g.add(ledBar)
  for (let i = 0; i < 3; i++) {
    const led = cyl(0.06, 0.06, 0.05, i === 0 ? MAT.ledGreen() : MAT.ledAmber(), 3.55, 10)
    led.rotation.x = Math.PI / 2
    led.position.set(-0.5 + i * 0.5, 0, 0.92)
    g.add(led)
  }

  // 铭牌 / 品牌条
  const brand = box(1.4, 0.28, 0.04, MAT.pcsAccent(), 3.25)
  brand.position.z = 0.9
  g.add(brand)

  // 底部电缆沟盖板
  const cableEntry = box(1.8, 0.12, 0.5, MAT.darkSteel(), 0.35)
  cableEntry.position.z = 1.05
  g.add(cableEntry)

  const base = box(2.7, 0.18, 1.9, MAT.concrete(), 0.09)
  g.add(base)

  g.userData.kind = 'pcs'
  tagPanelPick(g, panelKey)
  return g
}

/**
 * 储能集装箱：ISO 箱体 + 波纹侧板 + 端门锁杆 + 顶部空调
 * （公共储能数字孪生资源中最常见外观）
 */
export function createBmsContainer(panelKey) {
  const g = new THREE.Group()
  const L = 5.2
  const H = 2.7
  const W = 2.5

  // 主箱体
  const shell = box(L, H, W, MAT.bmsWall(), H / 2 + 0.15)
  g.add(shell)

  // 波纹侧板
  for (const side of [-1, 1]) {
    for (let i = 0; i < 14; i++) {
      const rib = box(0.12, H * 0.92, 0.08, MAT.bmsCorrugation(), H / 2 + 0.15)
      rib.position.set(-L / 2 + 0.35 + i * 0.35, 0, side * (W / 2 + 0.02))
      g.add(rib)
    }
  }

  // 角件
  for (const sx of [-1, 1]) {
    for (const sz of [-1, 1]) {
      for (const sy of [0.35, H + 0.05]) {
        const corner = box(0.22, 0.22, 0.22, MAT.darkSteel(), sy)
        corner.position.set(sx * (L / 2 - 0.05), 0, sz * (W / 2 - 0.05))
        g.add(corner)
      }
    }
  }

  // 端门（双开）+ 锁杆
  for (const sx of [-0.65, 0.65]) {
    const door = box(1.15, H * 0.88, 0.08, MAT.bmsDoor(), H / 2 + 0.15)
    door.position.set(sx, 0, W / 2 + 0.05)
    g.add(door)
    const bar = cyl(0.04, 0.04, H * 0.75, MAT.galvanized(), H / 2 + 0.15, 8)
    bar.position.set(sx, 0, W / 2 + 0.12)
    g.add(bar)
    for (const hy of [0.7, 1.9]) {
      const latch = box(0.25, 0.1, 0.1, MAT.darkSteel(), hy)
      latch.position.set(sx, 0, W / 2 + 0.14)
      g.add(latch)
    }
  }

  // 屋顶 + HVAC
  const roof = box(L + 0.1, 0.12, W + 0.1, MAT.bmsRoof(), H + 0.2)
  g.add(roof)
  const hvac = box(1.6, 0.55, 1.1, MAT.darkSteel(), H + 0.55)
  hvac.position.set(-1.2, 0, 0)
  g.add(hvac)
  for (let i = 0; i < 4; i++) {
    const grill = box(1.3, 0.06, 0.04, MAT.steel(), H + 0.45 + i * 0.1)
    grill.position.set(-1.2, 0, 0.58)
    g.add(grill)
  }

  // 侧向检修门 / 铭牌
  const sideDoor = box(0.9, 1.6, 0.06, MAT.bmsDoor(), 1.2)
  sideDoor.position.set(-L / 2 - 0.02, 0, 0)
  g.add(sideDoor)
  const nameplate = box(1.0, 0.35, 0.04, MAT.steel(), 2.2)
  nameplate.position.set(0, 0, W / 2 + 0.06)
  g.add(nameplate)

  // 底座梁
  const base = box(L + 0.2, 0.16, W + 0.15, MAT.concrete(), 0.08)
  g.add(base)
  for (const sx of [-1.6, 0, 1.6]) {
    const beam = box(0.25, 0.22, W + 0.1, MAT.darkSteel(), 0.2)
    beam.position.x = sx
    g.add(beam)
  }

  g.userData.kind = 'bms'
  tagPanelPick(g, panelKey)
  return g
}

function tagPanelPick(group, panelKey) {
  if (!panelKey) return
  group.userData.panelKey = panelKey
  group.traverse(o => {
    if (o.isMesh || o.isInstancedMesh) o.userData.panelKey = panelKey
  })
}

/**
 * 组串逆变器排：柜数取组态 inverterCount（多台时分行排布，不写死 4/16）
 */
export function createPvInverterRow(panelKey, { count = 0 } = {}) {
  const g = new THREE.Group()
  const n = Math.max(0, Math.round(Number(count) || 0))
  if (n <= 0) {
    g.userData.kind = 'pv-inverter'
    tagPanelPick(g, panelKey)
    return g
  }
  const cols = Math.min(n, 8)
  const rows = Math.ceil(n / cols)
  const pitch = n > 8 ? 0.48 : 0.72
  const rowPitch = 0.7
  for (let i = 0; i < n; i++) {
    const col = i % cols
    const row = Math.floor(i / cols)
    const cab = box(0.55, 1.45, 0.38, MAT.pvInvBody(), 0.88)
    cab.position.x = (col - (cols - 1) / 2) * pitch
    cab.position.z = (row - (rows - 1) / 2) * rowPitch
    g.add(cab)
    const door = box(0.42, 1.05, 0.04, MAT.pcsDoor(), 0.9)
    door.position.set(cab.position.x, 0, cab.position.z + 0.22)
    g.add(door)
  }
  const pad = box(cols * pitch + 0.4, 0.12, rows * rowPitch + 0.5, MAT.concrete(), 0.06)
  g.add(pad)
  g.userData.kind = 'pv-inverter'
  tagPanelPick(g, panelKey)
  return g
}

/**
 * 光伏方阵：组件行列由 stringCount / modulesPerString 决定（有上限以免面数爆炸）
 */
export function createPvArray(panelKey, { stringCount = 0, modulesPerString = 0 } = {}) {
  const g = new THREE.Group()
  g.userData.kind = 'pv-array'
  tagPanelPick(g, panelKey)
  const { rows: visRows, cols: visCols, fieldW, fieldD } = pvArrayFieldSize(stringCount, modulesPerString)
  if (visRows <= 0 || visCols <= 0) {
    g.add(box(1.6, 0.1, 1.2, MAT.concrete(), 0.05))
    return g
  }
  const tilt = (28 * Math.PI) / 180
  const panelW = PV_PANEL_W
  const panelH = 0.025
  const panelD = PV_PANEL_D
  const gapX = PV_PANEL_GAP_X
  const rowPitch = PV_ROW_PITCH

  const pad = box(fieldW, 0.1, fieldD, MAT.concrete(), 0.05)
  g.add(pad)

  const panelMat = new THREE.MeshStandardMaterial({
    color: 0x17365a,
    metalness: 0.38,
    roughness: 0.26,
    emissive: 0x0c2748,
    emissiveIntensity: 0.18
  })
  g.userData.panelMat = panelMat
  // 每行（串）组件 x 中心：供组串出线（createPvStringLeads）定位串首
  g.userData.rowXs = pvArrayRowXs(visCols, fieldW)

  const geo = new THREE.BoxGeometry(panelW, panelH, panelD)
  const count = visRows * visCols
  const mesh = new THREE.InstancedMesh(geo, panelMat, count)
  mesh.castShadow = true
  mesh.receiveShadow = true
  const dummy = new THREE.Object3D()
  const originX = -((visCols - 1) * (panelW + gapX)) / 2
  const originZ = -((visRows - 1) * rowPitch) / 2
  let idx = 0
  for (let r = 0; r < visRows; r++) {
    for (let c = 0; c < visCols; c++) {
      dummy.position.set(originX + c * (panelW + gapX), 0.92, originZ + r * rowPitch)
      dummy.rotation.x = -tilt
      dummy.updateMatrix()
      mesh.setMatrixAt(idx++, dummy.matrix)
    }
  }
  mesh.instanceMatrix.needsUpdate = true
  g.add(mesh)

  const frameMat = MAT.pvFrame()
  const postMat = MAT.pvPost()
  for (let r = 0; r < visRows; r++) {
    const z = originZ + r * rowPitch
    const rail = box(fieldW - 0.4, 0.05, 0.06, frameMat, 0.72)
    rail.position.z = z
    rail.rotation.x = -tilt
    g.add(rail)
    for (const sx of [-fieldW * 0.32, fieldW * 0.32]) {
      const post = box(0.08, 0.85, 0.08, postMat, 0.42)
      post.position.set(sx, 0, z + 0.28)
      g.add(post)
    }
  }

  g.userData.kind = 'pv-array'
  tagPanelPick(g, panelKey)
  return g
}

/**
 * 组串出线：每串一根单独出线（30 块串联为一行），正交走线——
 * 段1 南北（行首 → 方阵前缘母线带）、段2 东西（→ 前缘汇流竖排，各串 y 分层避免重合）。
 * 16 串并联后由主 dc 电缆接入逆变器。无能量流光。
 * 坐标相对方阵中心，调用方通过 group.position 平移到方阵位置。
 * @param {{ rows: number, cols: number, fieldW: number, fieldD: number }} opts
 */
export function createPvStringLeads({ rows = 0, cols = 0, fieldW = 1.6, fieldD = 1.2 } = {}) {
  const g = new THREE.Group()
  if (rows <= 0 || cols <= 0) return g

  const rowXs = pvArrayRowXs(cols, fieldW)
  const frontZ = -fieldD / 2
  const busZ = frontZ - 0.3
  const yBase = 1.05
  // 各串东西段按不同高度分层，同平面线段即使 x 区间重叠也不重合（允许交叉/并行）
  const ySpan = Math.max(0.15, rows * 0.03)
  // —— 串线（正交）：段1 南北（x 不变），段2 东西（z 不变）——
  const positions = []
  for (let r = 0; r < rows; r++) {
    const sx = r % 2 ? rowXs[cols - 1] : rowXs[0]
    const rz = (r - (rows - 1) / 2) * PV_ROW_PITCH
    const y = yBase + (r / Math.max(1, rows - 1)) * ySpan
    // 段1：行首 → 方阵前缘母线带（南北走向）
    positions.push(sx, y, rz, sx, y, busZ)
    // 段2：前缘母线带 → 汇流竖排（东西走向，z 不变）
    positions.push(sx, y, busZ, 0, y, busZ)
  }
  if (positions.length) {
    const geo = new THREE.BufferGeometry()
    geo.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3))
    const line = new THREE.LineSegments(geo, new THREE.LineBasicMaterial({ color: 0x9aa3ad, transparent: true, opacity: 0.85 }))
    line.renderOrder = 3
    line.userData.isPvStringLead = true
    g.add(line)
  }

  // —— 前缘汇流竖排：16 根串线按不同 y 层接入；下端下探贴地，接主 dc 电缆 ——
  const busBottom = 0.3
  const busTop = yBase + ySpan + 0.15
  const bus = new THREE.Mesh(
    new THREE.CylinderGeometry(0.06, 0.06, busTop - busBottom, 8),
    new THREE.MeshStandardMaterial({ color: 0x7d8a9a, metalness: 0.5, roughness: 0.4 })
  )
  bus.position.set(0, (busTop + busBottom) / 2, busZ)
  bus.renderOrder = 3
  bus.userData.isPvBusBar = true
  g.add(bus)
  return g
}

/**
 * 静态直流电缆：贴地正交走线（竖直→南北→东西→竖直），纯直角无斜线，
 * 每段为轴对齐圆柱，避免 TubeGeometry 在尖角处扭曲/斜切。无能量流光。
 */
export function createStaticCable({ ax, ay, az, bx, by, bz, radius = 0.05, color = 0x2c3e50, midY = 0.35 } = {}) {
  const pts = groundRoute(ax, ay, az, bx, by, bz, { midY })
  const g = new THREE.Group()
  const mat = new THREE.MeshStandardMaterial({
    color,
    metalness: 0.45,
    roughness: 0.45,
    polygonOffset: true,
    polygonOffsetFactor: -2,
    polygonOffsetUnits: -2
  })
  for (let i = 0; i < pts.length - 1; i++) {
    const a = pts[i]
    const b = pts[i + 1]
    const dx = b.x - a.x
    const dy = b.y - a.y
    const dz = b.z - a.z
    const len = Math.hypot(dx, dy, dz)
    if (len < 1e-4) continue
    const seg = new THREE.Mesh(new THREE.CylinderGeometry(radius, radius, len, 8), mat)
    seg.position.set((a.x + b.x) / 2, (a.y + b.y) / 2, (a.z + b.z) / 2)
    // 轴对齐：沿 X / Y / Z 之一（严格正交）
    if (Math.abs(dx) > 1e-4) seg.rotation.z = Math.PI / 2
    else if (Math.abs(dz) > 1e-4) seg.rotation.x = Math.PI / 2
    seg.renderOrder = 2
    seg.castShadow = false
    seg.receiveShadow = false
    g.add(seg)
  }
  g.userData.isStaticCable = true
  return g
}

export function setPvArrayVisual(group, { irradianceWm2, running } = {}) {
  const mat = group?.userData?.panelMat
  if (!mat) return
  const k = Math.max(0, Math.min(1, (Number(irradianceWm2) || 0) / 1000))
  const live = !!running && k > 0.02
  mat.emissiveIntensity = live ? 0.15 + 0.75 * k : 0.06
  mat.color.setHex(k > 0.08 ? 0x1c4d82 : 0x1a2838)
}

/** 三相电表柜 */
export function createAcMeter() {
  const g = new THREE.Group()
  g.add(box(1.1, 1.6, 0.7, MAT.pcsBody(), 0.95))
  g.add(box(0.85, 0.55, 0.08, MAT.glass(), 1.35))
  const pad = box(1.3, 0.12, 0.9, MAT.concrete(), 0.06)
  g.add(pad)
  g.userData.kind = 'meter'
  return g
}

/** 站用负荷 */
export function createLoadBank() {
  const g = new THREE.Group()
  g.add(box(1.6, 1.4, 1.1, MAT.darkSteel(), 0.85))
  for (let i = 0; i < 4; i++) {
    const fin = box(1.4, 0.06, 0.9, MAT.steel(), 0.5 + i * 0.22)
    g.add(fin)
  }
  g.add(box(1.8, 0.12, 1.3, MAT.concrete(), 0.06))
  g.userData.kind = 'load'
  return g
}

/** 母线横管（单挂省略时由布局不生成此项） */
export function createBusBar(item) {
  const g = new THREE.Group()
  const x1 = item.x1 ?? item.x
  const x2 = item.x2 ?? item.x
  const len = Math.max(0.6, Math.abs(x2 - x1))
  const tube = new THREE.Mesh(
    new THREE.CylinderGeometry(0.09, 0.09, len, 10),
    new THREE.MeshStandardMaterial({ color: 0xc45c26, metalness: 0.55, roughness: 0.35 })
  )
  tube.rotation.z = Math.PI / 2
  tube.position.y = item.y ?? Y.cable
  g.add(tube)
  const node = createBusNode(0, item.y ?? Y.cable, 0, {
    radius: item.radius ?? 0.22,
    label: item.node?.label || '',
    voltage: item.voltage ?? item.node?.parameters?.nominalVoltage
  })
  g.add(node)
  g.userData.kind = 'bus-bar'
  g.userData.core = node.children?.[0]
  return g
}

/**
 * 设备间电缆：正交刚体走线（仅东西 / 南北 / 竖直，禁止斜向）
 * 路径：起点立管 → 贴地先南北后东西 → 终点立管
 */
function groundRoute(x0, y0, z0, x1, y1, z1, opts = {}) {
  const gy = opts.midY ?? Y.cable
  const eps = 0.08
  const pts = [new THREE.Vector3(x0, y0, z0)]

  const push = (x, y, z) => {
    const p = new THREE.Vector3(x, y, z)
    const last = pts[pts.length - 1]
    if (last.distanceToSquared(p) > eps * eps) pts.push(p)
  }

  // 起点落到贴地高度（竖直）
  if (Math.abs(y0 - gy) > eps) push(x0, gy, z0)

  // 贴地：先南北（Z），再东西（X）——每段只改一个轴
  if (Math.abs(z1 - z0) > eps) push(x0, gy, z1)
  if (Math.abs(x1 - x0) > eps) push(x1, gy, z1)

  // 终点立管
  if (Math.abs(y1 - gy) > eps) push(x1, y1, z1)
  else push(x1, y1, z1)

  return pts
}

/**
 * 母线汇流点（星型接线节点，统一规则：所有母线在 3D 中绘制为一个点）
 * 各支路电缆汇合于此，不再画长条母线管。
 * radius 随母线规模（挂接设备数/长度）自适应，并按电压等级配色。
 */
export function createBusNode(x, y, z, { radius = 0.28, label = '', voltage = 0 } = {}) {
  const g = new THREE.Group()
  g.position.set(x, y, z)
  g.userData.kind = 'bus-node'
  g.userData.label = label
  g.userData.voltage = voltage

  // 主色：高压(≥35kV)铜色、中压琥珀、低压(≤1kV)橙，弱化色阶干扰时统一铜色
  const v = Number(voltage) || 0
  const bodyColor = v >= 35000 ? 0xc45c26 : v >= 1000 ? 0xd4a017 : 0xe07a3a
  const ringColor = v >= 35000 ? 0xd4a017 : 0xf2c94c

  const core = new THREE.Mesh(
    new THREE.SphereGeometry(radius, 20, 14),
    new THREE.MeshStandardMaterial({
      color: bodyColor,
      metalness: 0.55,
      roughness: 0.35,
      emissive: 0x3a1808,
      emissiveIntensity: 0.25
    })
  )
  core.castShadow = true
  core.receiveShadow = true
  g.add(core)

  // 双层指示环（醒目，便于在远处识别汇流点）
  for (const [r, tube] of [[1.15, 0.045], [1.42, 0.028]]) {
    const ring = new THREE.Mesh(
      new THREE.TorusGeometry(radius * r, tube, 8, 24),
      new THREE.MeshStandardMaterial({
        color: ringColor,
        metalness: 0.7,
        roughness: 0.3,
        emissive: 0x2a1a05,
        emissiveIntensity: 0.35
      })
    )
    ring.rotation.x = Math.PI / 2
    g.add(ring)
  }

  // 顶部亮芯（通电指示）
  const lightCore = new THREE.Mesh(
    new THREE.SphereGeometry(radius * 0.45, 10, 8),
    new THREE.MeshStandardMaterial({
      color: 0xffe6a8,
      emissive: 0xffcc66,
      emissiveIntensity: 0.9,
      metalness: 0.1,
      roughness: 0.35
    })
  )
  lightCore.position.y = radius * 0.75
  g.add(lightCore)

  // 矮底座，强调贴地节点
  const pedestal = new THREE.Mesh(
    new THREE.CylinderGeometry(radius * 0.85, radius * 1.05, 0.08, 12),
    new THREE.MeshStandardMaterial({ color: 0x5a6570, metalness: 0.4, roughness: 0.55 })
  )
  pedestal.position.y = -radius * 0.55
  pedestal.receiveShadow = true
  g.add(pedestal)

  return g
}

/**
 * 每个组态模板对应独立 3D 模型；数量/电压/容量由 layout item 传入。
 */
const TEMPLATE_MODELS = {
  grid: (item) => createGridGantry(),
  ac_breaker: (item) => createBreakerMesh(item.pickId || item.key, item.unitIndex ?? null),
  transformer: (item) => {
    const g = createTransformer(item.scale ?? 1, { boxType: !!item.boxType })
    if (item.panelKey) tagPanelPick(g, item.panelKey)
    return g
  },
  split_transformer: (item) => {
    const g = createTransformer(item.scale ?? 1, { boxType: !!item.boxType })
    if (item.panelKey) tagPanelPick(g, item.panelKey)
    return g
  },
  ac_bus: (item) => (item.kind === 'bus-bar' ? createBusBar(item) : createBusNode(0, item.y ?? Y.cable, 0, {
    radius: item.radius ?? 0.24,
    label: item.node?.label || '',
    voltage: item.voltage ?? item.node?.parameters?.nominalVoltage
  })),
  // 直流母线：有横向跨度（并联多设备）时画横管母线，PCS/BMS 竖段落接在横管上；
  // 无跨度退化为汇流点节点
  dc_bus: (item) => {
    const span = Math.abs((item.x2 ?? item.x) - (item.x1 ?? item.x))
    if (span > 0.5) return createBusBar(item)
    return createBusNode(0, item.y ?? Y.cable, 0, {
      radius: item.radius ?? 0.24,
      label: item.node?.label || '',
      voltage: item.voltage ?? item.node?.parameters?.nominalVoltage
    })
  },
  ac_meter: () => createAcMeter(),
  load: () => createLoadBank(),
  bms: (item) => createBmsContainer(item.panelKey),
  emu: () => null,
  pv_unit: () => null,
  pcs: (item) => createPcsCabinet(item.panelKey),
  pv_inverter: (item) => createPvInverterRow(item.panelKey, { count: item.inverterCount }),
  pv_array: (item) => createPvArray(item.panelKey, {
    stringCount: item.stringCount,
    modulesPerString: item.modulesPerString
  }),
  label: () => null
}

/**
 * 按组态 3D 布局实例化场景。layout 由 buildStation3dLayout 生成。
 * @returns {{ root: THREE.Group, refs: object }}
 */
export function buildStation(layout) {
  const root = new THREE.Group()
  const refs = {
    mainBreaker: null,
    unitBreakers: [],
    pvBreakers: [],
    tieBreakers: [],
    pvArrays: [],
    cables: [],
    busNodes: [],
    panelAnchors: [],
    labelAnchors: [],
    items: layout?.items || []
  }

  // 布局附加对象（组串出线等非工厂网格）
  for (const extra of layout?.extras || []) {
    if (extra) root.add(extra)
  }

  for (const item of layout?.items || []) {
    const factory = TEMPLATE_MODELS[item.templateId]
    const mesh = factory ? factory(item) : null
    if (mesh) {
      mesh.position.set(item.x, 0, item.z)
      mesh.userData.layoutItem = item
      if (item.busRole) mesh.userData.busRole = item.busRole
      if (item.unitIndex != null) mesh.userData.unitIndex = item.unitIndex
      if (item.pvIndex != null) mesh.userData.pvIndex = item.pvIndex
      if (item.side) mesh.userData.side = item.side
      root.add(mesh)

      if (item.kind === 'main-breaker') refs.mainBreaker = mesh
      if (item.kind === 'unit-breaker' && item.unitIndex != null) refs.unitBreakers[item.unitIndex] = mesh
      if (item.kind === 'pv-breaker' && item.pvIndex != null) refs.pvBreakers[item.pvIndex] = mesh
      if (item.kind === 'tie-breaker' || item.kind === 'branch-breaker') refs.tieBreakers.push(mesh)
      if (item.templateId === 'pv_array') {
        refs.pvArrays.push(mesh)
        // 组串出线：每串（行）一根单独出线 → 方阵前缘汇流点（30 块串联成串，16 串并联）
        if (item.stringCount > 0 && item.modulesPerString > 0) {
          const leads = createPvStringLeads({
            rows: item.stringCount,
            cols: item.modulesPerString,
            fieldW: item.footprint?.w,
            fieldD: item.footprint?.d
          })
          if (leads) {
            leads.position.set(item.x, 0, item.z)
            root.add(leads)
          }
        }
      }
      if (item.templateId === 'ac_bus' || item.templateId === 'dc_bus') refs.busNodes.push(mesh)

      if (item.panelKey && !refs.panelAnchors.some(a => a.key === item.panelKey)) {
        const off = item.labelOffset || { x: 0, y: 4.2, z: 0.3 }
        refs.panelAnchors.push({
          key: item.panelKey,
          type: item.panelType,
          unitIndex: item.unitIndex,
          pvIndex: item.pvIndex,
          side: item.side,
          position: new THREE.Vector3(item.x + (off.x || 0), off.y || 4.2, item.z + (off.z || 0))
        })
      }
    }

    const labelKinds = new Set([
      'grid', 'main-breaker', 'stem-breaker', 'tie-breaker', 'branch-breaker', 'station-xf', 'bus-bar', 'bus-node', 'dc-bus',
      'meter', 'load', 'unit-title', 'unit-breaker', 'unit-xf',
      'pv-title', 'pv-breaker', 'pv-xf', 'pv-array'
    ])
    if (labelKinds.has(item.kind) || item.templateId === 'label') {
      const off = item.labelOffset || { x: 1.6, y: 3.2, z: 0 }
      refs.labelAnchors.push({
        key: item.key,
        kind: item.kind,
        unitIndex: item.unitIndex,
        pvIndex: item.pvIndex,
        side: item.side,
        item,
        position: new THREE.Vector3(
          item.x + (off.x || 0),
          item.y != null && item.templateId === 'label' ? item.y : (off.y || 3.2),
          item.z + (off.z || 0)
        )
      })
    }
  }

  for (const c of layout?.cables || []) {
    // 静态电缆（光伏连接线）：无能量流光，不参与潮流动画；radius 区分主干/分支粗细
    const cable = c.static
      ? createStaticCable({
        ax: c.ax, ay: c.ay, az: c.az, bx: c.bx, by: c.by, bz: c.bz,
        radius: c.radius ?? 0.05,
        midY: c.midY ?? 0.35
      })
      : createPowerCable(
        groundRoute(c.ax, c.ay, c.az, c.bx, c.by, c.bz),
        { radius: 0.08 }
      )
    cable.userData.cableRole = c.role
    cable.userData.unitIndex = c.unitIndex
    cable.userData.pvIndex = c.pvIndex
    cable.userData.side = c.side
    root.add(cable)
    if (!c.static) refs.cables.push(cable)
  }

  return { root, refs }
}
