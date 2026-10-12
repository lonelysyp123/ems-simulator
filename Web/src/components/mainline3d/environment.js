import * as THREE from 'three'

/** 程序化水泥/混凝土贴图（含噪点 + 模板缝） */
function makeConcreteMaps(size = 512) {
  if (typeof document === 'undefined') {
    return { map: null, roughMap: null }
  }
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = size
  const ctx = canvas.getContext('2d')
  ctx.fillStyle = '#8b9199'
  ctx.fillRect(0, 0, size, size)

  // 细粒噪点
  for (let i = 0; i < 9000; i++) {
    const x = Math.random() * size
    const y = Math.random() * size
    const v = 95 + Math.random() * 70
    const a = 0.08 + Math.random() * 0.22
    ctx.fillStyle = `rgba(${v},${v - 2},${v - 6},${a})`
    ctx.fillRect(x, y, 1 + Math.random() * 2.5, 1 + Math.random() * 2.5)
  }

  // 浅色骨料斑点
  for (let i = 0; i < 400; i++) {
    const x = Math.random() * size
    const y = Math.random() * size
    const r = 1 + Math.random() * 3
    const v = 140 + Math.random() * 50
    ctx.fillStyle = `rgba(${v},${v},${v - 8},0.25)`
    ctx.beginPath()
    ctx.arc(x, y, r, 0, Math.PI * 2)
    ctx.fill()
  }

  // 模板分缝
  ctx.strokeStyle = 'rgba(60,65,72,0.28)'
  ctx.lineWidth = 2
  const step = size / 4
  for (let i = 1; i < 4; i++) {
    ctx.beginPath()
    ctx.moveTo(i * step + (Math.random() - 0.5) * 4, 0)
    ctx.lineTo(i * step + (Math.random() - 0.5) * 4, size)
    ctx.stroke()
    ctx.beginPath()
    ctx.moveTo(0, i * step + (Math.random() - 0.5) * 4)
    ctx.lineTo(size, i * step + (Math.random() - 0.5) * 4)
    ctx.stroke()
  }

  // 细裂纹
  ctx.strokeStyle = 'rgba(55,58,64,0.18)'
  ctx.lineWidth = 1
  for (let i = 0; i < 12; i++) {
    let x = Math.random() * size
    let y = Math.random() * size
    ctx.beginPath()
    ctx.moveTo(x, y)
    for (let j = 0; j < 6; j++) {
      x += (Math.random() - 0.5) * 40
      y += (Math.random() - 0.5) * 40
      ctx.lineTo(x, y)
    }
    ctx.stroke()
  }

  const map = new THREE.CanvasTexture(canvas)
  map.wrapS = map.wrapT = THREE.RepeatWrapping
  map.colorSpace = THREE.SRGBColorSpace
  map.anisotropy = 4

  // 粗糙度图：偏高，缝处更糙
  const roughCanvas = document.createElement('canvas')
  roughCanvas.width = roughCanvas.height = size
  const rctx = roughCanvas.getContext('2d')
  rctx.fillStyle = '#b0b0b0'
  rctx.fillRect(0, 0, size, size)
  for (let i = 0; i < 5000; i++) {
    const v = 140 + Math.random() * 100
    rctx.fillStyle = `rgb(${v},${v},${v})`
    rctx.fillRect(Math.random() * size, Math.random() * size, 2, 2)
  }
  rctx.strokeStyle = '#e8e8e8'
  rctx.lineWidth = 3
  for (let i = 1; i < 4; i++) {
    rctx.beginPath()
    rctx.moveTo(i * step, 0)
    rctx.lineTo(i * step, size)
    rctx.stroke()
    rctx.beginPath()
    rctx.moveTo(0, i * step)
    rctx.lineTo(size, i * step)
    rctx.stroke()
  }
  const roughMap = new THREE.CanvasTexture(roughCanvas)
  roughMap.wrapS = roughMap.wrapT = THREE.RepeatWrapping

  return { map, roughMap }
}

const MAT = {
  grass: () => new THREE.MeshStandardMaterial({ color: 0x4a6b48, metalness: 0.02, roughness: 0.92 }),
  grassDark: () => new THREE.MeshStandardMaterial({ color: 0x3a5638, metalness: 0.02, roughness: 0.94 }),
  grassLight: () => new THREE.MeshStandardMaterial({ color: 0x5a7e52, metalness: 0.02, roughness: 0.9 }),
  asphalt: () => new THREE.MeshStandardMaterial({ color: 0x3d424a, metalness: 0.12, roughness: 0.88 }),
  asphaltLine: () => new THREE.MeshStandardMaterial({ color: 0xc9c4a8, metalness: 0.05, roughness: 0.7 }),
  curb: () => new THREE.MeshStandardMaterial({ color: 0x8a909a, metalness: 0.08, roughness: 0.85 }),
  concrete: (maps) => new THREE.MeshStandardMaterial({
    color: 0xa0a6ae,
    map: maps?.map || null,
    roughnessMap: maps?.roughMap || null,
    metalness: 0.04,
    roughness: 0.92,
    envMapIntensity: 0.35
  }),
  trunk: () => new THREE.MeshStandardMaterial({ color: 0x5c4030, metalness: 0.05, roughness: 0.9 }),
  foliage: () => new THREE.MeshStandardMaterial({ color: 0x3f7a45, metalness: 0.02, roughness: 0.85 }),
  foliage2: () => new THREE.MeshStandardMaterial({ color: 0x2f6a38, metalness: 0.02, roughness: 0.88 }),
  foliage3: () => new THREE.MeshStandardMaterial({ color: 0x4a8a4e, metalness: 0.02, roughness: 0.82 }),
  hedge: () => new THREE.MeshStandardMaterial({ color: 0x457a42, metalness: 0.02, roughness: 0.9 }),
  pole: () => new THREE.MeshStandardMaterial({ color: 0x4a505a, metalness: 0.55, roughness: 0.4 }),
  lamp: () => new THREE.MeshStandardMaterial({
    color: 0xffe6a8,
    emissive: 0xffcc66,
    emissiveIntensity: 1.2,
    metalness: 0.1,
    roughness: 0.35
  }),
  building: () => new THREE.MeshStandardMaterial({
    color: 0x6a7382,
    metalness: 0.15,
    roughness: 0.85,
    transparent: true,
    opacity: 0.42
  }),
  buildingFar: () => new THREE.MeshStandardMaterial({
    color: 0x5a6575,
    metalness: 0.1,
    roughness: 0.9,
    transparent: true,
    opacity: 0.18
  }),
  fence: () => new THREE.MeshStandardMaterial({ color: 0x7a8494, metalness: 0.4, roughness: 0.5 })
}

function box(w, h, d, mat, y = h / 2) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat)
  m.position.y = y
  m.castShadow = true
  m.receiveShadow = true
  return m
}

function cyl(rTop, rBot, h, mat, y = h / 2, segments = 10) {
  const m = new THREE.Mesh(new THREE.CylinderGeometry(rTop, rBot, h, segments), mat)
  m.position.y = y
  m.castShadow = true
  m.receiveShadow = true
  return m
}

/** 简单乔木 */
function createTree(scale = 1, foliageVariant = 0) {
  const g = new THREE.Group()
  const trunk = cyl(0.18 * scale, 0.28 * scale, 2.2 * scale, MAT.trunk(), 1.1 * scale, 8)
  const foliageMat = foliageVariant === 1 ? MAT.foliage2() : foliageVariant === 2 ? MAT.foliage3() : MAT.foliage()
  const crown1 = cyl(0.05, 1.6 * scale, 2.4 * scale, foliageMat, 3.0 * scale, 8)
  const crown2 = cyl(0.05, 1.1 * scale, 1.6 * scale, MAT.foliage2(), 4.2 * scale, 8)
  g.add(trunk, crown1, crown2)
  return g
}

/** 灌木球 */
function createBush(scale = 1) {
  const g = new THREE.Group()
  const b = new THREE.Mesh(
    new THREE.SphereGeometry(0.7 * scale, 10, 8),
    MAT.hedge()
  )
  b.position.y = 0.55 * scale
  b.castShadow = true
  b.receiveShadow = true
  g.add(b)
  // 次级簇叶，增加层次
  const b2 = new THREE.Mesh(
    new THREE.SphereGeometry(0.45 * scale, 8, 6),
    MAT.foliage3()
  )
  b2.position.set(0.35 * scale, 0.4 * scale, 0.2 * scale)
  b2.castShadow = true
  g.add(b2)
  return g
}

/** 路灯（每隔一盏带点光，控制性能） */
function createStreetLamp(withLight = true) {
  const g = new THREE.Group()
  const pole = cyl(0.08, 0.12, 5.2, MAT.pole(), 2.6, 8)
  const arm = box(1.4, 0.08, 0.08, MAT.pole(), 5.25)
  arm.position.x = 0.55
  const head = box(0.55, 0.12, 0.35, MAT.pole(), 5.15)
  head.position.x = 1.15
  const bulb = new THREE.Mesh(
    new THREE.SphereGeometry(0.14, 10, 8),
    MAT.lamp()
  )
  bulb.position.set(1.15, 5.0, 0)
  g.add(pole, arm, head, bulb)

  if (withLight) {
    const light = new THREE.PointLight(0xffd89a, 0.45, 20, 2)
    light.position.set(1.15, 4.9, 0)
    g.add(light)
  }
  return g
}

/**
 * 在设备区周围铺路面、绿化、路灯，并加远景虚化建筑。
 * 范围取组态 3D 布局包围盒，不写死单元深度。
 */
export function buildEnvironment(layout) {
  const root = new THREE.Group()
  root.name = 'station-environment'

  const b = layout?.bounds || layout || {}
  const x0 = (b.minX ?? b.busStartX ?? 0) - 18
  const x1 = (b.maxX ?? b.busEndX ?? 20) + 18
  const zMin = b.minZ ?? -22
  const zMax = b.maxZ ?? 26
  const cx = (x0 + x1) / 2
  const width = Math.max(40, x1 - x0)
  // 设备区纵深跨度自适应（方阵 1:1 后 z 范围显著增大，侧翼景物随之拉长）
  const zMid = (zMin + zMax) / 2
  const zSpan = Math.max(70, zMax - zMin + 30)

  const concreteMaps = makeConcreteMaps(512)
  if (concreteMaps.map) {
    const repeatX = Math.max(6, Math.round(width / 8))
    concreteMaps.map.repeat.set(repeatX, 10)
    concreteMaps.roughMap.repeat.set(repeatX, 10)
  }

  // —— 设备区水泥地：与前方道路在 Z 向上严格错开，避免共面闪烁 ——
  const roadZ = zMax + 10
  const roadDepth = 7.5
  const roadHalf = roadDepth / 2
  const roadInnerZ = roadZ - roadHalf // 道路靠设备一侧边缘
  const gap = 0.6 // 垫层与道路之间的缝，放一条路缘
  const padFrontZ = roadInnerZ - gap
  const padBackZ = zMin - 6
  const padDepth = Math.max(36, padFrontZ - padBackZ)
  const padCenterZ = (padFrontZ + padBackZ) / 2
  const padH = 0.1
  const pad = box(width + 8, padH, padDepth, MAT.concrete(concreteMaps), padH / 2 + 0.01)
  pad.position.set(cx, 0, padCenterZ)
  pad.receiveShadow = true
  pad.castShadow = false
  root.add(pad)

  // 水泥垫 ↔ 巡视道路 过渡路缘（只占缝隙，不与两侧顶面共面）
  const seamCurb = box(width + 8.2, 0.14, gap * 0.85, MAT.curb(), 0.09)
  seamCurb.position.set(cx, 0, (padFrontZ + roadInnerZ) / 2)
  root.add(seamCurb)

  // —— 前方巡视道路（BMS 外侧；顶面低于水泥垫，避免边缘闪烁）——
  const road = box(width + 24, 0.06, roadDepth, MAT.asphalt(), 0.04)
  road.position.set(cx, 0, roadZ)
  road.receiveShadow = true
  root.add(road)
  // 中线虚线（略高于路面）
  const dashCount = Math.max(6, Math.round(width / 8))
  for (let i = 0; i < dashCount; i++) {
    const t = dashCount === 1 ? 0.5 : i / (dashCount - 1)
    const x = x0 - 6 + t * (width + 12)
    const dash = box(2.2, 0.015, 0.18, MAT.asphaltLine(), 0.085)
    dash.position.set(x, 0, roadZ)
    root.add(dash)
  }
  // 道路外侧路缘（远离设备侧，不与水泥垫相交）
  const curbB = box(width + 24, 0.16, 0.3, MAT.curb(), 0.1)
  curbB.position.set(cx, 0, roadZ + roadHalf + 0.2)
  root.add(curbB)

  // —— 侧向联络道路（草坪带外侧，与草坪严格不重叠）——
  const sideRoad = box(6.5, 0.08, zSpan, MAT.asphalt(), 0.03)
  sideRoad.position.set(x0 - 14.25, 0, zMid)
  root.add(sideRoad)
  const sideRoad2 = box(6.5, 0.08, zSpan, MAT.asphalt(), 0.03)
  sideRoad2.position.set(x1 + 14.25, 0, zMid)
  root.add(sideRoad2)

  // —— 绿化带（道路外侧 + 储能区侧翼）——
  const strip = box(width + 28, 0.06, 5.5, MAT.grass(), 0.02)
  strip.position.set(cx, 0, roadZ + 7.2)
  strip.receiveShadow = true
  root.add(strip)
  const stripBack = box(width + 20, 0.06, 8, MAT.grassDark(), 0.02)
  stripBack.position.set(cx, 0, zMin - 14)
  root.add(stripBack)

  // 储能区两侧草坪带（设备垫与侧路之间，增强绿化包围感）
  const sideGrassL = box(7, 0.05, zSpan - 16, MAT.grassLight(), 0.02)
  sideGrassL.position.set(x0 - 7.5, 0, zMid)
  root.add(sideGrassL)
  const sideGrassR = box(7, 0.05, zSpan - 16, MAT.grass(), 0.02)
  sideGrassR.position.set(x1 + 7.5, 0, zMid)
  root.add(sideGrassR)

  // 绿篱（道路侧 + 侧翼矮篱）
  const hedgeLen = width + 16
  const hedge = box(hedgeLen, 1.1, 0.7, MAT.hedge(), 0.55)
  hedge.position.set(cx, 0, roadZ + 5.2)
  root.add(hedge)
  const hedgeSideL = box(0.55, 0.85, zSpan - 24, MAT.hedge(), 0.42)
  hedgeSideL.position.set(x0 - 3.2, 0, zMid)
  root.add(hedgeSideL)
  const hedgeSideR = box(0.55, 0.85, zSpan - 24, MAT.hedge(), 0.42)
  hedgeSideR.position.set(x1 + 3.2, 0, zMid)
  root.add(hedgeSideR)

  // 树木沿道路外侧（加密）
  const treeStep = 11
  for (let x = x0 - 4; x <= x1 + 4; x += treeStep) {
    const tree = createTree(0.85 + ((Math.abs(x) * 0.01) % 0.35), Math.abs(Math.round(x)) % 3)
    tree.position.set(x, 0, roadZ + 9.5)
    root.add(tree)
    const bush = createBush(0.75 + (Math.abs(x) % 5) * 0.06)
    bush.position.set(x + 2.8, 0, roadZ + 6.6)
    root.add(bush)
    if ((x / treeStep) % 2 < 1) {
      const bush2 = createBush(0.55)
      bush2.position.set(x - 2.2, 0, roadZ + 7.8)
      root.add(bush2)
    }
  }

  // BMS 舱前绿化点缀
  for (let x = x0 + 2; x <= x1 - 2; x += 9) {
    const bush = createBush(0.65 + (Math.abs(x) % 3) * 0.08)
    bush.position.set(x, 0, zMax + 5.5)
    root.add(bush)
  }

  // 侧翼乔木 + 灌木丛
  for (let z = zMin; z <= zMax + 4; z += 12) {
    const treeL = createTree(0.95, 1)
    treeL.position.set(x0 - 7, 0, z)
    root.add(treeL)
    const treeR = createTree(1.05, 2)
    treeR.position.set(x1 + 7, 0, z + 3)
    root.add(treeR)
    const bushL = createBush(0.8)
    bushL.position.set(x0 - 4.5, 0, z + 4)
    root.add(bushL)
    const bushR = createBush(0.7)
    bushR.position.set(x1 + 4.5, 0, z + 5)
    root.add(bushR)
  }

  // 电网侧零星树木
  for (let x = x0; x <= x1; x += 14) {
    const tree = createTree(1.05, Math.abs(Math.round(x)) % 3)
    tree.position.set(x + 2, 0, zMin - 18)
    root.add(tree)
  }

  // —— 路灯（道路两侧，点数控制）——
  const lampStep = Math.max(18, Math.round(width / 4))
  let lampIdx = 0
  for (let x = x0 - 2; x <= x1 + 2; x += lampStep) {
    const lampA = createStreetLamp(lampIdx % 2 === 0)
    lampA.position.set(x, 0, roadZ - 4.2)
    lampA.rotation.y = Math.PI
    root.add(lampA)

    const lampB = createStreetLamp(lampIdx % 2 === 1)
    lampB.position.set(x + lampStep * 0.45, 0, roadZ + 4.2)
    root.add(lampB)
    lampIdx++
  }

  // —— 围栏示意 ——
  const fenceZ = roadZ + 12
  const fence = box(width + 30, 2.2, 0.12, MAT.fence(), 1.1)
  fence.position.set(cx, 0, fenceZ)
  root.add(fence)
  for (let x = x0 - 10; x <= x1 + 10; x += 8) {
    const post = box(0.18, 2.4, 0.18, MAT.pole(), 1.2)
    post.position.set(x, 0, fenceZ)
    root.add(post)
  }

  // —— 天空穹顶（淡色渐变感，配合雾效虚化远景）——
  const sky = new THREE.Mesh(
    new THREE.SphereGeometry(420, 24, 16),
    new THREE.MeshBasicMaterial({
      color: 0x7a8fa8,
      side: THREE.BackSide,
      depthWrite: false,
      fog: false
    })
  )
  sky.position.set(cx, 0, 4)
  root.add(sky)

  // 远景虚化由场景自适应雾效（SceneController._adaptSceneExtent）负责，
  // 不再使用固定半径雾环——场站扩大后固定半径雾环会圈进场站内部形成白色圆环。

  root.userData.center = { x: cx, z: 4 }
  root.userData.width = width
  return root
}
