/**
 * 由组态工程图推导 3D 场站布局。
 * 站侧结构、设备台数、电压/容量全部来自节点与连线，不写死主变台数或单元内部路数。
 */
import { buildTopologyMainLineLayout, makeGraph, findBmsForPcs } from '../topology/topologyMainLineLayout.js'
import { pvArrayFieldSize } from './pvArrayLayout.js'

/** 单线图像素 → 3D 米 */
export const PX = 0.06
export const CABLE_Y = 0.55
export const LABEL_Y = 6.5

/**
 * 组态储能支路与 2D 单线图同构：断路器引线段 → PCS 卡片行 → 直流母线 → BMS 卡片行。
 * 3D 设备摆放优先取单元实际几何（绑断路器时 PCS 行下移），缺省回落下列 2D 基准值。
 */

/** 槽位序号（0 起）→ 侧位字母（A~Z） */
function sideLetter(i) {
  return String.fromCharCode(65 + Math.max(0, Math.min(25, i | 0)))
}

export function paramNum(node, key, fallback = null) {
  const v = Number(node?.parameters?.[key])
  return Number.isFinite(v) ? v : fallback
}

export function fmtVolt(v) {
  const n = Number(v)
  if (!Number.isFinite(n) || n <= 0) return '—'
  return n >= 1000
    ? `${(n / 1000).toFixed(n % 1000 === 0 ? 0 : 1)} kV`
    : `${n.toFixed(0)} V`
}

/**
 * 组态模板与 3D 模型对应。复合模板（pv_unit）在布局中按内部设备展开，
 * 不发明台数；primitive 模板各有独立网格；EMU 为虚拟节点不进布局。
 */
export const TOPOLOGY_TEMPLATE_3D = {
  grid: 'primitive',
  ac_bus: 'primitive',
  ac_breaker: 'primitive',
  transformer: 'primitive',
  split_transformer: 'primitive',
  ac_meter: 'primitive',
  load: 'primitive',
  bms: 'primitive',
  dc_bus: 'primitive',
  pcs: 'primitive',
  pv_unit: 'composite'
}

function splitCount(n) {
  const total = Math.max(0, Math.round(Number(n) || 0))
  const a = Math.ceil(total / 2)
  return { a, b: total - a }
}

/**
 * 沿母线均匀排开 count 台设备；count=1 居中。
 * @param {number} cx
 * @param {number} count
 * @param {number} pitch
 */
export function slotXs(cx, count, pitch = 5.5) {
  const n = Math.max(0, count | 0)
  if (n <= 0) return []
  if (n === 1) return [cx]
  const span = (n - 1) * pitch
  const x0 = cx - span / 2
  const xs = []
  for (let i = 0; i < n; i++) xs.push(x0 + i * pitch)
  return xs
}

function fingerprintNode(n) {
  const p = n?.parameters || {}
  const keys = [
    'inverterCount', 'pcsCount', 'primaryVoltage', 'secondaryVoltage',
    'nominalVoltage', 'outputVoltage', 'unitXfPrimaryV', 'unitXfSecondaryV',
    'unitXfRatedKva', 'ratedPowerKva', 'stringCount', 'modulesPerString'
  ]
  const sig = keys.map(k => `${k}=${p[k] ?? ''}`).join(',')
  return `${n.id}:${n.templateId}:${Math.round(n.x || 0)}:${Math.round(n.y || 0)}:${sig}`
}

/**
 * 组态或运行时变化时重建 3D。
 * @param {object} snap
 */
export function stationKey(snap) {
  const topo = snap?.topology
  if (topo?.nodes?.length) {
    const nodes = [...topo.nodes].map(fingerprintNode).sort().join('|')
    const edges = [...(topo.edges || [])]
      .map(e => `${e.fromNodeId}>${e.toNodeId}`)
      .sort()
      .join(',')
    // 布局语义变更时抬版本，已打开的 3D 场景会按新 unitIndex 重建网格
    return `t:v2-emu-unit:${nodes}#${edges}`
  }
  const ess = snap?.units?.length || 0
  const pv = snap?.pvUnits?.length || 0
  return `s:${ess}:${pv}`
}

function originOf(sld) {
  const xs = []
  const ys = []
  if (sld.gridX != null) xs.push(sld.gridX)
  for (const b of sld.buses || []) {
    xs.push(b.x1, b.x2)
    ys.push(b.y)
  }
  for (const t of sld.transformers || []) {
    xs.push(t.x)
    ys.push(t.y)
  }
  for (const u of sld.units || []) {
    xs.push(u.cx)
    ys.push(u.originY)
  }
  const x0 = xs.length ? Math.min(...xs) : 0
  const y0 = sld.yGrid ?? (ys.length ? Math.min(...ys) : 0)
  return { x0, y0 }
}

function toX(px, origin) {
  return (px - origin.x0) * PX
}

function toZ(py, origin) {
  return (py - origin.y0) * PX
}

function addItem(items, item) {
  items.push(item)
  return item
}

function addCable(cables, a, b, extra = {}) {
  cables.push({
    ax: a.x, ay: a.y ?? CABLE_Y, az: a.z,
    bx: b.x, by: b.y ?? CABLE_Y, bz: b.z,
    ...extra
  })
}

/** 直流侧模板：PCS/BMS/直流母线之间的边属于直流连线，其余为交流馈线 */
const DC_SIDE_TEMPLATES = new Set(['dc_bus', 'bms', 'pcs'])

/**
 * 统一出线口：每个设备模型最多前/后两个出线口（front = +z 面，back = -z 面），
 * 电缆端点只能落在出线口上，不从设备中心或其他位置走线；
 * BMS 与电表为单口设备（电表 3D 不绘制）：BMS 出线口在面向直流母线/PCS 的一面（-z）。
 * 变压器偏移随模型 scale 缩放；箱变（boxType）与油浸变外形尺寸不同，偏移分开定义。
 */
export const DEVICE_PORTS = {
  grid: { front: 1.4, back: 1.4 },
  ac_breaker: { front: 1.5, back: 0.9 },
  transformer: { front: 1.6, back: 1.6, boxFront: 1.25, boxBack: 1.25, scaled: true },
  split_transformer: { front: 1.6, back: 1.6, boxFront: 1.25, boxBack: 1.25, scaled: true },
  pcs: { front: 1.05, back: 1.0 },
  bms: { only: -1.45 },
  load: { front: 0.75, back: 0.75 }
}

/** 端口 z 偏移（含方向）：单口设备恒定；双口设备按对端方位选择面向对端的口 */
function portDz(item, facingZ) {
  if (item.templateId === 'pv_inverter') {
    const half = pvInverterRowHalfDepth(item.inverterCount) + 0.2
    return facingZ >= item.z ? half : -half
  }
  const spec = DEVICE_PORTS[item.templateId]
  if (!spec) return null
  if (spec.only != null) return spec.only
  const s = spec.scaled ? (item.scale ?? 1) : 1
  const front = facingZ >= item.z
  const dz = (front
    ? (item.boxType && spec.boxFront != null ? spec.boxFront : spec.front)
    : (item.boxType && spec.boxBack != null ? spec.boxBack : spec.back)) * s
  return front ? dz : -dz
}

/** 设备出线口锚点：无出线口定义的模板（母线汇流点等）返回 null */
export function devicePort(item, facingZ) {
  const dz = portDz(item, facingZ)
  if (dz == null) return null
  return { x: item.x, y: 0.5, z: item.z + dz }
}

/** 逆变器排在 3D 中的半深（与 createPvInverterRow 的排布公式一致） */
export function pvInverterRowHalfDepth(count) {
  const n = Math.max(1, Math.round(Number(count) || 1))
  return Math.ceil(n / 8) * 0.35
}

/**
 * 电缆锚点：
 * - 母线横杠（有 x1/x2）：取对端 x 的夹取点（横段由母线承担，只画竖段）；
 * - 设备模型：统一出线口（面向对端方向选口）；
 * - 母线汇流点等无出线口模板：取自身 (x, z)。
 */
function portAnchor(item, other) {
  if ((item.templateId === 'dc_bus' || item.templateId === 'ac_bus')
    && Number.isFinite(item.x1) && Number.isFinite(item.x2)) {
    const lo = Math.min(item.x1, item.x2)
    const hi = Math.max(item.x1, item.x2)
    return { x: Math.max(lo, Math.min(hi, other.x)), y: item.y ?? CABLE_Y, z: item.z }
  }
  return devicePort(item, other.z) || { x: item.x, y: item.y ?? CABLE_Y, z: item.z }
}

function edgeDeviceOf(ia, ib) {
  return [ia, ib].find(i =>
    (i.templateId === 'pcs' || i.templateId === 'bms') && Number.isFinite(i.unitIndex)) || null
}

/** 角色与 _syncState 通电规则一致：单元设备随单元断路器，其余随主断 */
function classifyEdge(ia, ib) {
  const dev = edgeDeviceOf(ia, ib)
  if (dev) {
    const other = dev === ia ? ib : ia
    const meta = { unitIndex: dev.unitIndex, side: dev.side }
    if (dev.templateId === 'bms') return { role: 'dc-link', meta }
    return { role: DC_SIDE_TEMPLATES.has(other.templateId) ? 'dc-link' : 'pcs-feed', meta }
  }
  const brk = [ia, ib].find(i => i.kind === 'unit-breaker' && Number.isFinite(i.unitIndex))
  if (brk) return { role: 'unit-drop', meta: { unitIndex: brk.unitIndex } }
  return { role: 'sld-wire', meta: {} }
}

/**
 * 连线重绘核心：一根连线 ⇔ 组态中存在对应节点边。
 * - 同一对节点的并行边（三相多线）去重为一根；
 * - 模板不在 3D 绘制（电表/EMU/光伏复合）或节点未放置的边跳过；
 * - 返回已出线的节点对（[idA, idB] 数组），供归属兜底判断覆盖。
 */
function buildEdgeCables(topology, items, cables) {
  const placed = new Map()
  for (const it of items) {
    const id = it.node?.id
    if (id && !placed.has(id)) placed.set(id, it)
  }
  const byId = new Map((topology?.nodes || []).map(n => [n.id, n]))
  const seen = new Set()
  const emittedPairs = []
  for (const e of topology?.edges || []) {
    const a = e.fromNodeId
    const b = e.toNodeId
    if (!a || !b || a === b) continue
    const key = a < b ? `${a}|${b}` : `${b}|${a}`
    if (seen.has(key)) continue
    seen.add(key)
    const na = byId.get(a)
    const nb = byId.get(b)
    if (!na || !nb) continue
    if (TOPOLOGY_TEMPLATE_3D[na.templateId] !== 'primitive') continue
    if (TOPOLOGY_TEMPLATE_3D[nb.templateId] !== 'primitive') continue
    const ia = placed.get(a)
    const ib = placed.get(b)
    if (!ia || !ib) continue
    const { role, meta } = classifyEdge(ia, ib)
    addCable(cables, portAnchor(ia, ib), portAnchor(ib, ia), {
      role,
      ...meta,
      static: true,
      midY: 0.35 + (cables.length % 8) * 0.05
    })
    emittedPairs.push([a, b])
  }
  return emittedPairs
}

/**
 * 归属兜底：组态没画边的设备按单元归属补线（真实工程边齐全时不会触发）。
 * plan.cover = { id, side: 'ac'|'dc'|'any' }：已出线边覆盖该侧时跳过，避免重复。
 */
function buildFillInCables(wirePlans, emittedPairs, cables, nodeTemplate) {
  const covered = (id, side) => emittedPairs.some(([a, b]) => {
    if (a !== id && b !== id) return false
    if (side === 'any') return true
    const otherT = nodeTemplate.get(a === id ? b : a)
    const dc = DC_SIDE_TEMPLATES.has(otherT)
    return side === 'dc' ? dc : !dc
  })
  for (const plan of wirePlans || []) {
    if (plan.cover && covered(plan.cover.id, plan.cover.side)) continue
    const { cover, a, b, ...extra } = plan
    addCable(cables, a, b, extra)
  }
}

function xfBoxType(node) {
  const sec = paramNum(node, 'secondaryVoltage', 0) || 0
  const pri = paramNum(node, 'primaryVoltage', 0) || 0
  return sec > 0 && sec < 3000 && pri > 0 && pri < 80000
}

function xfScale(node) {
  const kva = paramNum(node, 'ratedPowerKva', 0) || paramNum(node, 'unitXfRatedKva', 0) || 0
  if (kva <= 0) return 1
  return Math.max(0.55, Math.min(1.7, Math.cbrt(kva / 10000)))
}

/** 组态参数优先；缺省不发明台数。 */
function countFromParam(node, key, liveCount = 0) {
  const n = paramNum(node, key, null)
  if (n != null && n >= 0) return Math.max(0, Math.round(n))
  return Math.max(0, liveCount | 0)
}

/**
 * 运行时通道按设备编号索引：与 2D 单线图绑定规则一致，
 * PCS 卡片编号 N ↔ channel.pcsNumber，BMS 卡片编号 N ↔ channel.compartmentNumber。
 */
function buildLiveDeviceMaps(unitsSnap) {
  const pcsByNum = new Map()
  const bmsByNum = new Map()
  for (const u of unitsSnap || []) {
    const unitIndex = u?.unitIndex ?? 0
    const channels = (u?.channels || [u?.channelA, u?.channelB]).filter(Boolean)
    channels.forEach((ch, i) => {
      const slot = Number(ch?.slotInUnit)
      const slotIdx = Number.isFinite(slot) ? slot : i
      if (ch?.pcsNumber != null && !pcsByNum.has(ch.pcsNumber)) {
        pcsByNum.set(ch.pcsNumber, { ch, unitIndex, slotIdx })
      }
      if (ch?.compartmentNumber != null && !bmsByNum.has(ch.compartmentNumber)) {
        bmsByNum.set(ch.compartmentNumber, { ch, unitIndex, slotIdx })
      }
    })
  }
  return { pcsByNum, bmsByNum }
}

function expandEmu(unit, origin, items, cables, ctx, graph = null, live = null) {
  const cx = toX(unit.cx, origin)
  const busCx = unit.busCx != null ? toX(unit.busCx, origin) : cx
  const zBus = toZ(unit.originY, origin)
  const unitIndex = unit.unitSnap?.unitIndex ?? unit.unitIndex ?? unit.index
  const pcsNodes = unit.pcsNodes || []
  // 组态模式（有 pcs 节点）：按物理拓扑逐设备展开，不合成单元标题/单元断/单元变/690 母线；
  // 运行时兑底单元（无组态节点）保留原合成表示（仅 fromSnapFallback 路径）
  const deviceLevel = pcsNodes.length > 0
  const node = unit.emu
  const bmsNodes = unit.bmsNodes || []

  // 方阵区需在 z 方向避开所有单元（含储能 BMS/直流母线深度），记录单元底边；
  // 同时记录储能设备列的 x 范围（按设备实际占地），供方阵区在 x 方向避让
  if (ctx) {
    ctx.maxUnitBottomZ = Math.max(ctx.maxUnitBottomZ, toZ(unit.originY + unit.bottom, origin))
    const devXs = slotXs(cx, Math.max(pcsNodes.length, bmsNodes.length), 5.5)
    if (!deviceLevel && devXs.length === 0) devXs.push(cx)
    if (devXs.length) {
      const pad = 3
      const lo = Math.min(...devXs) - pad
      const hi = Math.max(...devXs) + pad
      ctx.emuMinX = ctx.emuMinX == null ? lo : Math.min(ctx.emuMinX, lo)
      ctx.emuMaxX = ctx.emuMaxX == null ? hi : Math.max(ctx.emuMaxX, hi)
    }
  }

  if (deviceLevel) {
    expandEmuDevices(unit, origin, items, cables, graph, live, {
      cx, busCx, zBus, unitIndex, pcsNodes, bmsNodes
    }, ctx)
    return
  }

  // —— 运行时兑底单元：保持原合成单元显示（标题/单元断/单元变/690 母线）——
  const zBr = toZ(unit.originY + unit.unitBrkMid, origin)
  const zXf = toZ(unit.originY + unit.unitXfmrTop + 8, origin)
  const z690 = toZ(unit.originY + unit.unitBus690Y, origin)
  const zPcs = toZ(unit.originY + unit.pcsTop, origin)
  const zBms = toZ(unit.originY + unit.bmsTop, origin)
  // 运行时通道列表：优先逐槽位通道，兼容旧 A/B 双通道字段
  const liveChannelList = (unit.pcsChannels || [unit.pcsA, unit.pcsB]).filter(Boolean)
  const livePcs = liveChannelList.length
  const pcsCount = countFromParam(node, 'pcsCount', livePcs)
  const firstPcs = null
  const pcsXs = slotXs(cx, pcsCount, 5.5)
  const bmsCount = bmsNodes.length
  const bmsXs = slotXs(cx, bmsCount, 5.5)

  let busX = cx
  let busZ = z690
  // 为 true 时每台 PCS 馈线直接接上游供电点（绑定断路器或母线汇流点）
  let feedAllFromBus = false

  {
    addItem(items, {
      key: `emu-title-${unit.index}`,
      templateId: 'label',
      kind: 'unit-title',
      x: cx - 3,
      z: zBus + 0.4,
      y: LABEL_Y,
      text: [unit.label],
      title: true,
      unitIndex
    })

    addItem(items, {
      key: `emu-br-${unit.index}`,
      templateId: 'ac_breaker',
      kind: 'unit-breaker',
      x: cx,
      z: zBr,
      node,
      pickId: `unit-${unitIndex}`,
      unitIndex,
      label: '单元断',
      labelOffset: { x: 2.4, y: 3.4, z: 0 }
    })
    // 支路电缆：单元断路器出线口 → 母线汇流点（星型接线，先南北到母线带再东西汇入，y 分层避免重合）
    addCable(cables, devicePort({ templateId: 'ac_breaker', x: cx, z: zBr }, zBus), { x: busCx, y: 0.5, z: zBus }, {
      role: 'unit-drop',
      unitIndex,
      static: true,
      midY: 0.35 + (unit.index % 8) * 0.05
    })

    const xfNode = {
      parameters: {
        primaryVoltage: paramNum(node, 'unitXfPrimaryV', paramNum(firstPcs, 'acVoltage')),
        secondaryVoltage: paramNum(node, 'unitXfSecondaryV'),
        ratedPowerKva: paramNum(node, 'unitXfRatedKva', 0)
          || paramNum(firstPcs, 'pcsRatedPowerKw', 0) * Math.max(0, pcsCount)
      },
      label: node?.label
    }
    const xfScaleV = xfScale(xfNode)
    addItem(items, {
      key: `emu-xf-${unit.index}`,
      templateId: 'transformer',
      kind: 'unit-xf',
      x: cx,
      z: zXf,
      node: xfNode,
      boxType: true,
      scale: xfScaleV,
      label: `单元变 ${unit.unitXfLabel || ''}`.trim(),
      labelOffset: { x: 2.8, y: 3.8, z: 0 },
      unitIndex
    })
    addCable(cables,
      devicePort({ templateId: 'ac_breaker', x: cx, z: zBr }, zXf),
      devicePort({ templateId: 'transformer', boxType: true, scale: xfScaleV, x: cx, z: zXf }, zBr), {
        role: 'unit-xf',
        unitIndex,
        static: true
      })

    if (!unit.omitBus690) {
      const lvV = paramNum(node, 'unitXfSecondaryV')
      const bus = addItem(items, {
        key: `emu-lv-${unit.index}`,
        templateId: 'ac_bus',
        kind: 'bus-node',
        x: cx,
        z: z690,
        y: CABLE_Y,
        node: {
          label: lvV != null ? fmtVolt(lvV) : '低压母线',
          parameters: { nominalVoltage: lvV }
        },
        radius: 0.22,
        busRole: 'unit-lv-bus',
        unitIndex
      })
      busX = bus.x
      busZ = bus.z
      addCable(cables,
        devicePort({ templateId: 'transformer', boxType: true, scale: xfScaleV, x: cx, z: zXf }, busZ),
        { x: busX, y: 0.5, z: busZ }, {
          role: 'unit-690',
          unitIndex,
          static: true
        })
    } else {
      busZ = zXf
      addCable(cables,
        devicePort({ templateId: 'transformer', boxType: true, scale: xfScaleV, x: cx, z: zXf }, zPcs),
        devicePort({ templateId: 'pcs', x: pcsXs[0] ?? cx, z: zPcs }, zXf), {
          role: 'unit-690',
          unitIndex,
          static: true
        })
    }
  }

  const channels = liveChannelList
  pcsXs.forEach((x, i) => {
    const ch = channels[i] || null
    const side = sideLetter(i)
    addItem(items, {
      key: `pcs-${unitIndex}-${side}`,
      templateId: 'pcs',
      kind: 'pcs',
      x,
      z: zPcs,
      node: null,
      panelKey: `pcs-${unitIndex}-${side}`,
      panelType: 'pcs',
      unitIndex,
      side,
      channel: ch,
      labelOffset: { x: 0, y: 4.4, z: 0.3 }
    })
    if (!unit.omitBus690 || i > 0) {
      addCable(cables, { x: busX, y: 0.5, z: busZ },
        devicePort({ templateId: 'pcs', x, z: zPcs }, busZ), {
          role: 'pcs-feed',
          unitIndex,
          side,
          static: true
        })
    }
  })

  // 直流母线横杠仅在并联多 BMS 时画（与 2D 同规则）；提前判定供 BMS 引线选择
  const hasDcBar = !!unit.dcBus && !unit.omitDcBus && bmsCount > 1
  const zDc = hasDcBar ? toZ(unit.originY + (unit.dcBusY ?? 0), origin) : 0

  bmsXs.forEach((x, i) => {
    const bmsNode = bmsNodes[i] || null
    const side = sideLetter(i)
    const pairedIdx = Math.min(i, Math.max(0, pcsXs.length - 1))
    const pcsX = pcsXs[pairedIdx] ?? x
    addItem(items, {
      key: `bms-${unitIndex}-${side}`,
      templateId: 'bms',
      kind: 'bms',
      x,
      z: zBms,
      node: bmsNode,
      panelKey: `bms-${unitIndex}-${side}`,
      panelType: 'bms',
      unitIndex,
      side,
      channel: channels[i] || null,
      labelOffset: { x: 0, y: 3.6, z: 0.3 }
    })
    // 直流引线：组态画了边时由边生成器出线，此处登记归属兜底（有横杠出竖段，无横杠直连配对 PCS）
    ctx?.wirePlans.push({
      a: hasDcBar ? { x, y: 0.5, z: zDc } : devicePort({ templateId: 'pcs', x: pcsX, z: zPcs }, zBms),
      b: devicePort({ templateId: 'bms', x, z: zBms }, zDc),
      cover: { id: bmsNode?.id, side: 'dc' },
      role: 'dc-link',
      unitIndex,
      side,
      static: true
    })
  })

  if (hasDcBar) {
    const xs = bmsXs.length ? bmsXs : [cx]
    const x1 = Math.min(...xs) - 1.2
    const x2 = Math.max(...xs) + 1.2
    addItem(items, {
      key: `node-${unit.dcBus.id || `emu-dc-${unit.index}`}`,
      templateId: 'dc_bus',
      kind: 'dc-bus',
      x: (x1 + x2) / 2,
      z: zDc,
      y: CABLE_Y,
      x1,
      x2,
      // 与交流母线同一规则：半径随挂接规模自适应
      radius: Math.max(0.26, Math.min(0.5, Math.abs(x2 - x1) * 0.03)),
      node: unit.dcBus,
      voltage: paramNum(unit.dcBus, 'nominalVoltage'),
      label: unit.dcBus.label,
      labelOffset: { x: 1.2, y: 1.1, z: -0.8 }
    })
  }
}

/**
 * 组态模式：按物理拓扑逐设备绘制（与 2D 单线图卡片同构）：
 * 母线汇流点 →（绑定断路器）→ PCS 柜 →（直流母线）→ BMS 电池堆，
 * 每个组态节点一个模型，不合成任何 EMU 虚拟概念；
 * 运行时通道按设备编号绑定（pcsNumber / compartmentNumber），与 2D 卡片一致。
 */
function expandEmuDevices(unit, origin, items, cables, graph, live, base, ctx) {
  const { cx, busCx, zBus, unitIndex, pcsNodes, bmsNodes } = base
  // 纵向层次与 2D 卡片几何同源：断路器/卡顶/直流母线/BMS 位置随单元实际布局取值（绑断路器时 PCS 行下移）
  const zBr = toZ(unit.originY + (unit.brkMid ?? 32), origin)
  const zPcs = toZ(unit.originY + (unit.pcsTop ?? 18) + 114, origin)
  const zDc = toZ(unit.originY + (unit.dcBusY ?? 282), origin)
  const zBms = toZ(unit.originY + (unit.bmsTop ?? 318) + 107, origin)
  const pcsXs = slotXs(cx, pcsNodes.length, 5.5)
  const bmsXs = slotXs(cx, bmsNodes.length, 5.5)

  // 馈线接入点：绑定断路器时自断路器取馈线；未绑定直连母线汇流点（星型接线，y 分层避免重合）
  let feedX = busCx
  let feedZ = zBus
  let feedFromBrk = false
  if (unit.unitBreakerNode && !unit.unitBreakerOnBus && !unit.unitBreakerOnXfmr) {
    const brk = unit.unitBreakerNode
    addItem(items, {
      key: `node-${brk.id}`,
      templateId: 'ac_breaker',
      kind: 'unit-breaker',
      x: cx,
      z: zBr,
      node: brk,
      pickId: `brk-${brk.id}`,
      unitIndex,
      label: brk.label || brk.parameters?.name || '断路器',
      labelOffset: { x: 2.4, y: 3.4, z: 0 }
    })
    // 馈线引下线：组态画了边时由边生成器出线，此处登记归属兜底
    ctx?.wirePlans.push({
      a: devicePort({ templateId: 'ac_breaker', x: cx, z: zBr }, zBus),
      b: { x: busCx, y: 0.5, z: zBus },
      cover: { id: brk.id, side: 'any' },
      role: 'unit-drop',
      unitIndex,
      static: true,
      midY: 0.35 + (unit.index % 8) * 0.05
    })
    feedX = cx
    feedZ = zBr
    feedFromBrk = true
  }

  // 直流母线横杠仅在并联多设备时画（与 2D 同规则）；提前判定供 PCS 列引下线使用
  const hasDcBar = !!unit.dcBus && (pcsNodes.length > 1 || bmsNodes.length > 1)

  // PCS：每台组态节点一个机柜，通道按卡片编号（全局秩）命中 channel.pcsNumber
  const pcsNums = unit.pcsNums || pcsNodes.map((_, i) => i + 1)
  pcsNodes.forEach((p, i) => {
    const x = pcsXs[i]
    const liveRec = live?.pcsByNum.get(pcsNums[i]) || null
    const chUnitIndex = liveRec?.unitIndex ?? unitIndex
    const side = sideLetter(liveRec?.slotIdx ?? i)
    addItem(items, {
      key: `pcs-${p.id}`,
      templateId: 'pcs',
      kind: 'pcs',
      x,
      z: zPcs,
      node: p,
      panelKey: `pcs-${chUnitIndex}-${side}`,
      panelType: 'pcs',
      unitIndex: chUnitIndex,
      side,
      channel: liveRec?.ch || null,
      labelOffset: { x: 0, y: 4.4, z: 0.3 }
    })
    ctx?.wirePlans.push({
      a: feedFromBrk
        ? devicePort({ templateId: 'ac_breaker', x: feedX, z: feedZ }, zPcs)
        : { x: feedX, y: 0.5, z: feedZ },
      b: devicePort({ templateId: 'pcs', x, z: zPcs }, feedZ),
      cover: { id: p.id, side: 'ac' },
      role: 'pcs-feed',
      unitIndex: chUnitIndex,
      side,
      static: true,
      midY: 0.35 + ((unit.index + i) % 8) * 0.05
    })
    if (hasDcBar) {
      // 每台 PCS 都接入直流母线：机柜列竖段下至母线横管（贴母线高度走线）
      ctx?.wirePlans.push({
        a: devicePort({ templateId: 'pcs', x, z: zPcs }, zDc),
        b: { x, y: 0.5, z: zDc },
        cover: { id: p.id, side: 'dc' },
        role: 'dc-link',
        unitIndex: chUnitIndex,
        side,
        static: true,
        midY: CABLE_Y
      })
    }
  })

  // BMS 配对：按组态 edges 连通关系（PCS—DC—BMS），单链直连时决定接哪台 PCS
  const pcsIndexOfBms = new Map()
  if (graph) {
    pcsNodes.forEach((p, pi) => {
      for (const b of findBmsForPcs(graph, p.id)) {
        if (!pcsIndexOfBms.has(b.id)) pcsIndexOfBms.set(b.id, pi)
      }
    })
  }

  const bmsNums = unit.bmsNums || bmsNodes.map((_, j) => j + 1)
  bmsNodes.forEach((b, j) => {
    const x = bmsXs[j]
    const liveRec = live?.bmsByNum.get(bmsNums[j]) || null
    const chUnitIndex = liveRec?.unitIndex ?? unitIndex
    const side = sideLetter(liveRec?.slotIdx ?? j)
    const pairedIdx = pcsIndexOfBms.has(b.id)
      ? pcsIndexOfBms.get(b.id)
      : Math.min(j, Math.max(0, pcsXs.length - 1))
    const pcsX = pcsXs[pairedIdx] ?? x
    addItem(items, {
      key: `bms-${b.id}`,
      templateId: 'bms',
      kind: 'bms',
      x,
      z: zBms,
      node: b,
      panelKey: `bms-${chUnitIndex}-${side}`,
      panelType: 'bms',
      unitIndex: chUnitIndex,
      side,
      channel: liveRec?.ch || null,
      labelOffset: { x: 0, y: 3.6, z: 0.3 }
    })
    if (hasDcBar) {
      // 电池堆列竖段自母线横管引出（贴母线高度走线），横段由母线横管模型承担
      ctx?.wirePlans.push({
        a: { x, y: 0.5, z: zDc },
        b: devicePort({ templateId: 'bms', x, z: zBms }, zDc),
        cover: { id: b.id, side: 'dc' },
        role: 'dc-link',
        unitIndex: chUnitIndex,
        side,
        static: true,
        midY: CABLE_Y
      })
    } else {
      ctx?.wirePlans.push({
        a: devicePort({ templateId: 'pcs', x: pcsX, z: zPcs }, zBms),
        b: devicePort({ templateId: 'bms', x, z: zBms }, zPcs),
        cover: { id: b.id, side: 'dc' },
        role: 'dc-link',
        unitIndex: chUnitIndex,
        side,
        static: true
      })
    }
  })

  if (hasDcBar) {
    const xs = [...pcsXs, ...bmsXs]
    const x1 = Math.min(...xs) - 1.2
    const x2 = Math.max(...xs) + 1.2
    addItem(items, {
      key: `node-${unit.dcBus.id}`,
      templateId: 'dc_bus',
      kind: 'dc-bus',
      x: (x1 + x2) / 2,
      z: zDc,
      y: CABLE_Y,
      x1,
      x2,
      radius: Math.max(0.26, Math.min(0.5, Math.abs(x2 - x1) * 0.03)),
      node: unit.dcBus,
      voltage: paramNum(unit.dcBus, 'nominalVoltage'),
      label: unit.dcBus.label,
      labelOffset: { x: 1.2, y: 1.1, z: -0.8 }
    })
  }

}

function pvGroupsFromUnit(unit) {
  const inv = countFromParam(unit.pv, 'inverterCount', 0)
  const split = splitCount(inv)
  const groups = []
  if (split.a > 0) groups.push({ side: 'A', inverterCount: split.a, group: unit.groupA })
  if (split.b > 0) groups.push({ side: 'B', inverterCount: split.b, group: unit.groupB })
  return { inverterCount: inv, groups }
}

function expandPv(unit, origin, items, cables, pvSnap, ctx) {
  const cx = toX(unit.cx, origin)
  const busCx = unit.busCx != null ? toX(unit.busCx, origin) : cx
  const zBus = toZ(unit.originY, origin)
  const zBr = toZ(unit.originY + unit.unitBrkMid, origin)
  const zXf = toZ(unit.originY + unit.xfmrCardTop + 20, origin)
  const zInv = toZ(unit.originY + unit.arraySplitY, origin)
  const node = unit.pv
  const pvIndex = unit.pvIndex
  const live = (pvSnap || []).find(p => (p.pvIndex ?? -1) === pvIndex) || (pvSnap || [])[pvIndex] || null

  // 逆变器台数按组态 inverterCount（A/B 分组）；未配置时不发明台数
  const invTotal = countFromParam(node, 'inverterCount', 0)
  if (invTotal <= 0) return
  const split = splitCount(invTotal)
  const groups = [
    { side: 'A', count: Number(unit.groupA?.inverterCount) || split.a },
    { side: 'B', count: Number(unit.groupB?.inverterCount) || split.b }
  ].filter(g => g.count > 0)
  if (!groups.length) groups.push({ side: 'A', count: invTotal })

  const stringCount = countFromParam(node, 'stringCount', 0)
  const modulesPerString = countFromParam(node, 'modulesPerString', 0)
  if (ctx) {
    ctx.unitCxs.push(cx)
    ctx.stringCount = Math.max(ctx.stringCount, stringCount)
    ctx.modulesPerString = Math.max(ctx.modulesPerString, modulesPerString)
    ctx.maxInvZ = Math.max(ctx.maxInvZ, zInv)
    ctx.maxUnitBottomZ = Math.max(ctx.maxUnitBottomZ, toZ(unit.originY + unit.bottom, origin))
  }

  addItem(items, {
    key: `pv-${pvIndex}`,
    templateId: 'label',
    kind: 'pv-title',
    x: cx - 3,
    z: zBus + 0.4,
    y: LABEL_Y,
    text: [unit.label],
    title: true,
    pvIndex
  })

  addItem(items, {
    key: `pv-br-${pvIndex}`,
    templateId: 'ac_breaker',
    kind: 'pv-breaker',
    x: cx,
    z: zBr,
    node,
    pickId: `pv-br-${pvIndex}`,
    pvIndex,
    label: '单元断',
    labelOffset: { x: 2.4, y: 3.4, z: 0 }
  })
  // 支路电缆：光伏断路器出线口 → 母线汇流点（星型接线，y 分层避免重合）
  addCable(cables, devicePort({ templateId: 'ac_breaker', x: cx, z: zBr }, zBus), { x: busCx, y: 0.5, z: zBus }, {
    role: 'pv-drop',
    pvIndex,
    static: true,
    midY: 0.35 + ((unit.index ?? 0) % 8) * 0.05
  })

  const xfNode = {
    parameters: {
      primaryVoltage: paramNum(node, 'unitXfPrimaryV', paramNum(node, 'acVoltage')),
      secondaryVoltage: paramNum(node, 'unitXfSecondaryV'),
      ratedPowerKva: paramNum(node, 'unitXfRatedKva', 0)
        || (paramNum(node, 'inverterRatedPowerKw', 0) * invTotal)
    },
    label: node?.label
  }
  const pvXfScale = xfScale(xfNode)
  addItem(items, {
    key: `pvxf-${pvIndex}`,
    templateId: 'transformer',
    kind: 'pv-xf',
    x: cx,
    z: zXf,
    node: xfNode,
    boxType: true,
    scale: pvXfScale,
    panelKey: `pvxf-${pvIndex}`,
    panelType: 'pv',
    pvIndex,
    pvUnit: live,
    label: `箱变 ${unit.unitXfLabel || ''}`.trim(),
    labelOffset: { x: 2.8, y: 3.8, z: 0 }
  })
  addCable(cables,
    devicePort({ templateId: 'ac_breaker', x: cx, z: zBr }, zXf),
    devicePort({ templateId: 'transformer', boxType: true, scale: pvXfScale, x: cx, z: zXf }, zBr), {
      role: 'pv-xf',
      pvIndex,
      static: true
    })

  // —— 逆变器排：A/B 分居单元中心两侧，紧凑排布（不随方阵宽度拉开，避免跨单元重叠）——
  const invOffset = 5.5
  groups.forEach((g, i) => {
    const invX = cx + (i === 0 ? -invOffset : invOffset)
    const arrKey = `pvarr-${pvIndex}-${g.side}`
    addItem(items, {
      key: `pvinv-${pvIndex}-${g.side}`,
      templateId: 'pv_inverter',
      kind: 'pv-inverter',
      x: invX,
      z: zInv,
      panelKey: arrKey,
      panelType: 'pv-array',
      pvIndex,
      side: g.side,
      inverterCount: g.count,
      pvUnit: live,
      pvArray: g.side === 'B' ? live?.arrayB : live?.arrayA,
      labelOffset: { x: 0, y: 4.4, z: 0.3 }
    })
    // —— 主干：箱变前出线口（A/B 错开）→ 逆变器排后出线口，贴地正交一条线 ——
    const mainX = cx + i * 0.15
    const rowPortZ = zInv - (pvInverterRowHalfDepth(g.count) + 0.2)
    addCable(cables, { x: mainX, y: 0.5, z: zXf + 1.25 * pvXfScale }, { x: invX, y: 0.5, z: rowPortZ }, {
      role: 'pv-inv-main',
      pvIndex,
      side: g.side,
      static: true,
      radius: 0.07
    })
    // —— 分支：排后出线口 → 每台逆变器柜面向箱变侧（-z）出线口，短段可分辨 ——
    for (let k = 0; k < g.count; k++) {
      const off = inverterCabinetOffset(k, g.count)
      addCable(cables, { x: invX, y: 0.5, z: rowPortZ }, { x: invX + off.x, y: 0.5, z: zInv + off.z - 0.24 }, {
        role: 'pv-inv-branch',
        pvIndex,
        side: g.side,
        static: true,
        radius: 0.045
      })
      // 每台逆变器请求一块方阵（1:1），方阵位置由 placePvArrays 统一排布
      if (ctx) {
        ctx.requests.push({
          pvIndex,
          side: g.side,
          invIdx: k,
          invCount: g.count,
          invX,
          zInv,
          pvUnit: live,
          pvArray: g.side === 'B' ? live?.arrayB : live?.arrayA
        })
      }
    }
  })
}

/**
 * 逆变器柜在排内的位置偏移（与 createPvInverterRow 的排布公式一致），
 * 用于把每根主 dc 电缆接到排内对应柜位，避免线束在排中心重合。
 */
function inverterCabinetOffset(invIdx, count) {
  const n = Math.max(1, count | 0)
  const cols = Math.min(n, 8)
  const rows = Math.ceil(n / cols)
  const col = invIdx % cols
  const row = Math.floor(invIdx / cols)
  const pitch = n > 8 ? 0.48 : 0.72
  const rowPitch = 0.7
  return {
    x: (col - (cols - 1) / 2) * pitch,
    z: (row - (rows - 1) / 2) * rowPitch
  }
}

/**
 * 光伏方阵区：所有单元每台逆变器一块方阵（1:1），集中排在设备区后方网格，
 * 场地尺寸随方阵数量/大小自适应。每块方阵的组串出线（每串一根单独出线 → 汇流母线）
 * 由 buildStation 生成；此处生成方阵 item 与“汇流母线 → 逆变器柜”静态 dc 电缆。
 */
function placePvArrays(ctx, items, cables) {
  const reqs = ctx.requests || []
  if (!reqs.length) return
  const { fieldW, fieldD } = pvArrayFieldSize(ctx.stringCount, ctx.modulesPerString)
  const perRow = 8
  const gapX = 3
  const gapZ = 3
  const meanCx = ctx.unitCxs.length ? ctx.unitCxs.reduce((a, b) => a + b, 0) / ctx.unitCxs.length : 0
  let startX = meanCx - ((perRow - 1) * (fieldW + gapX)) / 2
  // 方阵区与储能单元列在 x 方向重叠时，整体平移到储能单元靠光伏一侧，
  // 避免方阵 dc 出线穿越储能单元 BMS/直流母线区域
  if (ctx.emuMinX != null && ctx.emuMaxX != null) {
    const span = (perRow - 1) * (fieldW + gapX)
    const fieldLeft = startX - fieldW / 2
    const fieldRight = startX + span + fieldW / 2
    if (fieldLeft < ctx.emuMaxX && fieldRight > ctx.emuMinX) {
      const edgeGap = 6
      if (meanCx >= (ctx.emuMinX + ctx.emuMaxX) / 2) startX = ctx.emuMaxX + edgeGap + fieldW / 2
      else startX = ctx.emuMinX - edgeGap - fieldW / 2 - span
    }
  }
  // 方阵区排在所有单元设备（含储能 BMS/直流母线）后方，避免与储能单元重叠
  const startZ = Math.max(ctx.maxInvZ, ctx.maxUnitBottomZ) + fieldD / 2 + 10

  let x = startX
  let z = startZ
  let col = 0
  for (const req of reqs) {
    // key 每块方阵唯一；panelKey 同组共享一个面板（pvarr-{pvIndex}-{side}）
    const key = `pvarr-${req.pvIndex}-${req.side}-${req.invIdx ?? 0}`
    const panelKey = `pvarr-${req.pvIndex}-${req.side}`
    addItem(items, {
      key,
      templateId: 'pv_array',
      kind: 'pv-array',
      x,
      z,
      // 真实占地：供 finalize 推算场景边界，场地环境随方阵区自适应
      footprint: { w: fieldW, d: fieldD },
      panelKey,
      panelType: 'pv-array',
      pvIndex: req.pvIndex,
      side: req.side,
      inverterCount: 1,
      stringCount: ctx.stringCount,
      modulesPerString: ctx.modulesPerString,
      pvUnit: req.pvUnit,
      pvArray: req.pvArray,
      labelOffset: { x: req.side === 'A' ? -2.2 : 2.2, y: 2.6, z: 0 }
    })
    // 静态 dc 电缆：方阵汇流竖排底部（贴地）→ 逆变器柜朝光伏侧（+z）出线口，
    // 全程贴地正交走线，终点分散到各柜位
    const off = inverterCabinetOffset(req.invIdx ?? 0, req.invCount ?? 1)
    addCable(cables, { x, y: 0.35, z: z - fieldD / 2 - 0.35 }, { x: req.invX + off.x, y: 0.5, z: req.zInv + off.z + 0.24 }, {
      role: 'pv-dc',
      pvIndex: req.pvIndex,
      side: req.side,
      static: true
    })
    col++
    if (col >= perRow) {
      col = 0
      x = startX
      z += fieldD + gapZ
    } else {
      x += fieldW + gapX
    }
  }
}

function makePvCtx() {
  return {
    requests: [],
    unitCxs: [],
    maxInvZ: 0,
    maxUnitBottomZ: 0,
    emuMinX: null,
    emuMaxX: null,
    stringCount: 0,
    modulesPerString: 0,
    // 归属兜底连线候选：组态未画边时按单元归属补线，由 buildFillInCables 条件执行
    wirePlans: []
  }
}

function collectSldNodeIds(sld) {
  const ids = new Set()
  const add = x => { if (x) ids.add(x) }
  add(sld.grid?.id)
  for (const b of sld.stemBreakers || []) add(b.id)
  for (const b of sld.tieBreakers || []) add(b.id)
  for (const b of sld.branchBreakers || []) add(b.id)
  for (const t of sld.transformers || []) add(t.id)
  for (const b of sld.buses || []) add(b.id)
  for (const m of sld.meters || []) add(m.id)
  for (const l of sld.loads || []) add(l.id)
  for (const u of sld.units || []) {
    add(u.emu?.id)
    add(u.pv?.id)
    for (const p of u.pcsNodes || []) add(p.id)
    for (const b of u.bmsNodes || []) add(b.id)
    add(u.dcBus?.id)
    add(u.unitBreakerNode?.id)
    add(u.unitMeterNode?.id)
  }
  return ids
}

/**
 * 兜底绘制：单线图布局未精确定位的基础模板节点（独立 BMS/DC 母线、母线支路断路器、
 * 孤立设备等），只要用户画了且模板有效，就按画布坐标生成基础 3D 模型；
 * 它们之间的连线由 buildEdgeCables 按组态边统一生成。
 */
function drawFallbackNodes(topology, origin, items, placedIds) {
  const placed = new Set(placedIds)
  // 3D 视图不绘制电表：全部电表标记为已放置，兜底不再补画，边生成器也不会为其出线
  for (const n of topology?.nodes || []) {
    if (n.templateId === 'ac_meter') placed.add(n.id)
  }

  for (const n of topology?.nodes || []) {
    if (placed.has(n.id)) continue
    if (TOPOLOGY_TEMPLATE_3D[n.templateId] !== 'primitive') continue
    const x = toX(n.x, origin)
    const z = toZ(n.y, origin)
    const item = {
      key: `node-${n.id}`,
      templateId: n.templateId,
      kind: n.templateId,
      x,
      z,
      node: n,
      label: n.label,
      labelOffset: { x: 1.6, y: 3.2, z: 0 }
    }
    if (n.templateId === 'ac_breaker') {
      item.pickId = `brk-${n.id}`
    } else if (n.templateId === 'ac_bus') {
      item.kind = 'bus-node'
      item.y = CABLE_Y
      item.radius = 0.24
    } else if (n.templateId === 'dc_bus') {
      item.x1 = x - 3
      item.x2 = x + 3
      item.y = CABLE_Y
      item.radius = 0.24
    }
    addItem(items, item)
  }
}

function fromTopology(topology, unitsSnap, pvSnap) {
  const sld = buildTopologyMainLineLayout(topology, unitsSnap || [])
  const origin = originOf(sld)
  const items = []
  const cables = []
  const ctx = makePvCtx()
  const graph = makeGraph(topology?.nodes, topology?.edges)

  if (sld.grid) {
    const x = toX(sld.gridX, origin)
    const z = toZ(sld.yGrid, origin)
    addItem(items, {
      key: `node-${sld.grid.id}`,
      templateId: 'grid',
      kind: 'grid',
      x,
      z,
      node: sld.grid,
      voltage: paramNum(sld.grid, 'outputVoltage'),
      labelOffset: { x: 3.8, y: LABEL_Y, z: 0 }
    })
  }

  for (const br of sld.stemBreakers || []) {
    const x = toX(br.x ?? sld.gridX, origin)
    const z = toZ(br.y, origin)
    addItem(items, {
      key: `node-${br.id}`,
      templateId: 'ac_breaker',
      kind: br.isMain ? 'main-breaker' : 'stem-breaker',
      x,
      z,
      node: br.node,
      pickId: br.isMain ? 'main' : `brk-${br.id}`,
      isMain: !!br.isMain,
      label: br.label,
      labelOffset: { x: 2.4, y: 3.6, z: 0 }
    })
  }

  for (const xf of sld.transformers || []) {
    addItem(items, {
      key: `node-${xf.id}`,
      templateId: xf.node?.templateId || 'transformer',
      kind: 'station-xf',
      x: toX(xf.x, origin),
      z: toZ(xf.y + xf.span / 2, origin),
      node: xf.node,
      boxType: xfBoxType(xf.node),
      scale: xfScale(xf.node),
      label: xf.label,
      ratioLabel: xf.ratioLabel,
      kvaLabel: xf.kvaLabel,
      labelOffset: { x: 3.6, y: 5.0, z: 0 }
    })
  }

  for (const bus of sld.buses || []) {
    const x1 = toX(bus.x1, origin)
    const x2 = toX(bus.x2, origin)
    // 汇流点画在设备汇聚中心（frame.cx，不含挂件外扩），与支路电缆终点一致
    const cx = bus.cx != null ? toX(bus.cx, origin) : (x1 + x2) / 2
    const z = toZ(bus.y, origin)
    // 统一规则：母线绘制为一个汇流点（星型接线），不再画长条母线管；
    // 半径随母线挂接规模（长度）自适应，电压用于配色
    const radius = Math.max(0.26, Math.min(0.5, Math.abs(x2 - x1) * 0.03))
    addItem(items, {
      key: `node-${bus.id}`,
      templateId: 'ac_bus',
      kind: 'bus-node',
      x: cx,
      z,
      y: CABLE_Y,
      node: bus.node,
      voltage: bus.voltage,
      label: bus.label,
      radius,
      busRole: 'ac-bus',
      labelOffset: { x: 1.2, y: 1.1, z: -0.8 }
    })
  }

  for (const br of sld.tieBreakers || []) {
    const x = toX(br.x, origin)
    const z = toZ(br.y, origin)
    addItem(items, {
      key: `node-${br.id}`,
      templateId: 'ac_breaker',
      kind: 'tie-breaker',
      x,
      z,
      node: br.node,
      pickId: `brk-${br.id}`,
      emuId: br.emuId || null,
      unitIndex: br.unitIndex ?? null,
      label: br.label,
      labelOffset: { x: 2.4, y: 3.6, z: 0 }
    })
  }

  for (const br of sld.branchBreakers || []) {
    const x = toX(br.x, origin)
    const z = toZ(br.y, origin)
    addItem(items, {
      key: `node-${br.id}`,
      templateId: 'ac_breaker',
      kind: 'branch-breaker',
      x,
      z,
      node: br.node,
      pickId: `brk-${br.id}`,
      emuId: br.emuId || null,
      unitIndex: br.unitIndex ?? null,
      label: br.label,
      labelOffset: { x: 2.4, y: 3.6, z: 0 }
    })
  }

  // 3D 视图不绘制电表（站侧/单元绑定均不画，仅在 2D 单线图展示）
  for (const l of sld.loads || []) {
    addItem(items, {
      key: `node-${l.id}`,
      templateId: 'load',
      kind: 'load',
      x: toX(l.x, origin),
      z: toZ(l.busY + (l.stub || 0) + (l.symbolH || 0) / 2, origin),
      node: l.node,
      label: l.label,
      labelOffset: { x: 1.6, y: 2.2, z: 0 }
    })
  }

  // 连线不再来自 2D 单线图 wires：节点间电缆统一由组态边推导（见 buildEdgeCables）

  // 运行时通道按设备编号索引，供逐设备绘制时把机柜绑定到对应通道/侧位
  const live = buildLiveDeviceMaps(unitsSnap)
  for (const unit of sld.units || []) {
    if (unit.kind === 'pv') expandPv(unit, origin, items, cables, pvSnap, ctx)
    else expandEmu(unit, origin, items, cables, ctx, graph, live)
  }

  // 方阵区统一排布（每台逆变器一块方阵）
  placePvArrays(ctx, items, cables)

  // 兜底绘制：sld 未精确定位但用户已画的基础模板节点
  drawFallbackNodes(topology, origin, items, collectSldNodeIds(sld))

  // 连线重绘：节点间电缆统一由组态边推导（并行边去重），无边设备按归属兜底
  const emittedPairs = buildEdgeCables(topology, items, cables)
  const nodeTemplate = new Map((topology?.nodes || []).map(n => [n.id, n.templateId]))
  buildFillInCables(ctx.wirePlans, emittedPairs, cables, nodeTemplate)

  return finalize(items, cables)
}

function fromSnapFallback(snap) {
  const items = []
  const cables = []
  const ctx = makePvCtx()
  const units = snap?.units || []
  const pvs = snap?.pvUnits || []
  const n = units.length + pvs.length
  const spacing = n >= 16 ? 16 : n >= 10 ? 18 : 22
  const xs = []
  for (let i = 0; i < n; i++) xs.push(i * spacing)
  const mainX = xs[0] || 0

  const gridV = snap?.gridNominalLineVoltageV
  addItem(items, {
    key: 'grid',
    templateId: 'grid',
    kind: 'grid',
    x: mainX,
    z: -22,
    node: { label: '电网', parameters: { outputVoltage: gridV } },
    voltage: gridV,
    labelOffset: { x: 3.8, y: LABEL_Y, z: 0 }
  })
  addItem(items, {
    key: 'main-breaker',
    templateId: 'ac_breaker',
    kind: 'main-breaker',
    x: mainX,
    z: -14,
    pickId: 'main',
    isMain: true,
    node: { label: snap?.mainBreakerLabel || '主断路器' },
    label: snap?.mainBreakerLabel || '主断',
    labelOffset: { x: 2.4, y: 3.6, z: 0 }
  })
  if (snap?.mainTransformerSecondary || snap?.mainTransformerPrimary) {
    addItem(items, {
      key: 'main-xf',
      templateId: 'transformer',
      kind: 'station-xf',
      x: mainX,
      z: -7,
      node: {
        label: '主变',
        parameters: {
          primaryVoltage: snap?.gridNominalLineVoltageV,
          secondaryVoltage: snap?.stationBus35LineVoltageV
        }
      },
      boxType: false,
      scale: 1.2,
      labelOffset: { x: 3.6, y: 5.0, z: 0 }
    })
  }
  if (n > 1) {
    addItem(items, {
      key: 'bus-lv',
      templateId: 'ac_bus',
      kind: 'bus-node',
      x: mainX,
      z: 0,
      y: CABLE_Y,
      node: { label: '母线', parameters: { nominalVoltage: snap?.stationBus35LineVoltageV } },
      voltage: snap?.stationBus35LineVoltageV,
      radius: 0.32,
      busRole: 'ac-bus',
      labelOffset: { x: 1.2, y: 1.1, z: -0.8 }
    })
  }
  addCable(cables,
    devicePort({ templateId: 'grid', x: mainX, z: -22 }, -14),
    devicePort({ templateId: 'ac_breaker', x: mainX, z: -14 }, -22),
    { role: 'grid-main', static: true })
  if (snap?.mainTransformerSecondary || snap?.mainTransformerPrimary) {
    addCable(cables,
      devicePort({ templateId: 'ac_breaker', x: mainX, z: -14 }, -7),
      devicePort({ templateId: 'transformer', scale: 1.2, x: mainX, z: -7 }, -14),
      { role: 'main-xf', static: true })
    addCable(cables,
      devicePort({ templateId: 'transformer', scale: 1.2, x: mainX, z: -7 }, 0),
      { x: mainX, y: 0.5, z: 0 },
      { role: 'xf-bus35', static: true })
  } else {
    addCable(cables,
      devicePort({ templateId: 'ac_breaker', x: mainX, z: -14 }, 0),
      { x: mainX, y: 0.5, z: 0 },
      { role: 'sld-wire', static: true })
  }

  units.forEach((u, i) => {
    const fake = {
      index: i,
      kind: 'emu',
      cx: (xs[i] || 0) / PX,
      busCx: (xs[0] || 0) / PX,
      originY: 0,
      unitBrkMid: 4 / PX,
      unitXfmrTop: 9 / PX,
      unitBus690Y: 13 / PX,
      pcsTop: 18 / PX,
      bmsTop: 26 / PX,
      drawPcsSlots: (u.channels || [u.channelA, u.channelB]).filter(Boolean).length,
      pcsChannels: (u.channels || [u.channelA, u.channelB]).filter(Boolean),
      bmsNodes: (u.channels || [u.channelA, u.channelB]).filter(Boolean).map((_, k) => ({ id: `ch${k}` })),
      dcParallel: (u.channels || [u.channelA, u.channelB]).filter(Boolean).length > 1,
      omitBus690: (u.channels || [u.channelA, u.channelB]).filter(Boolean).length <= 1,
      emu: { parameters: {}, label: `UNIT ${u.unitNumber ?? i + 1}` },
      unitSnap: u,
      unitXfLabel: u.unitTransformerLine || '',
      label: `UNIT ${u.unitNumber ?? i + 1}`
    }
    expandEmu(fake, { x0: 0, y0: 0 }, items, cables, ctx)
  })
  pvs.forEach((pv, i) => {
    const ux = xs[units.length + i] || 0
    const inv = Math.max(0, Number(pv.gridConnectedDeviceCount) || 0)
    const fakePv = {
      index: i,
      kind: 'pv',
      cx: ux / PX,
      busCx: (xs[0] || 0) / PX,
      originY: 0,
      unitBrkMid: 4 / PX,
      xfmrCardTop: 9 / PX,
      arraySplitY: 18 / PX,
      bmsTop: 26 / PX,
      pvIndex: pv.pvIndex ?? i,
      pv: {
        label: `光伏单元 ${pv.pvNumber ?? i + 1}`,
        parameters: { inverterCount: inv || undefined }
      },
      inverterCount: inv,
      groupA: {},
      groupB: {},
      unitXfLabel: '',
      label: `光伏单元 ${pv.pvNumber ?? i + 1}`
    }
    expandPv(fakePv, { x0: 0, y0: 0 }, items, cables, pvs, ctx)
  })

  // 方阵区统一排布（每台逆变器一块方阵）
  placePvArrays(ctx, items, cables)

  return finalize(items, cables)
}

function finalize(items, cables) {
  let minX = 0
  let maxX = 8
  let minZ = -8
  let maxZ = 8
  for (const it of items) {
    // 用设备真实占地推算边界（方阵按 footprint 半宽/半深，其余按固定 4 米边距），
    // 保证场地环境、地面、相机随设备数量与大小自适应。
    const f = it.footprint
    const hw = f && f.w > 0 ? f.w / 2 : 4
    const hd = f && f.d > 0 ? f.d / 2 : 4
    minX = Math.min(minX, it.x - hw)
    maxX = Math.max(maxX, it.x + hw)
    minZ = Math.min(minZ, it.z - hd)
    maxZ = Math.max(maxZ, it.z + hd)
  }
  return {
    items,
    cables,
    bounds: { minX, maxX, minZ, maxZ, busStartX: minX, busEndX: maxX }
  }
}

/**
 * @param {object} snap MainLineViewModel
 */
export function buildStation3dLayout(snap) {
  const topology = snap?.topology
  if (topology?.nodes?.length) {
    return fromTopology(topology, snap.units || [], snap.pvUnits || [])
  }
  return fromSnapFallback(snap || {})
}
