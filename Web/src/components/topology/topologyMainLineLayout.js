/**
 * 由组态工程图（节点 + 连线）推导电气主接线单线图。
 * 站侧结构随工程变化：母线、变压器、电表、负载按连通递归布局；
 * 储能支路按物理拓扑全量绘制（PCS → 直流母线 → BMS，每台设备一张静态卡片，
 * 不引入 EMU / EMU 分组等虚拟概念，暂不绑定运行时实时数据），
 * 光伏单元按设备类型展开内部图例，不按某个具体工程写死站侧骨架。
 *
 * 耦合器件（变压器 / 双耳 / 未来 N 绕组）一律展开为 downstreams[]：
 * 遍历、测宽、落位必须走同一列表，禁止再为某模板单开 extra 字段。
 * 器件如何入图由 nodeLayout.sldRole 登记；未登记的模板按 unknown 挂件画出。
 */

import { isSplitTransformer, isTransformerLike, sldRole } from './nodeLayout.js'

const CARD_W = 132
const CARD_GAP = 20

function truthy(v) {
  return v === true || v === 'true' || v === 1
}

function paramNum(node, key, fallback = 0) {
  const v = Number(node?.parameters?.[key])
  return Number.isFinite(v) ? v : fallback
}

function paramStr(node, key, fallback = '') {
  const v = node?.parameters?.[key]
  if (v == null || String(v).trim() === '') return fallback
  return String(v)
}

/**
 * 运行时单元号：与 TopologyRuntimeConverter 一致，含 PCS 的 EMU 按画布 (Y,X) 排序。
 * 馈线排布序号（placement index）会因每单元多组直流母线而错位，不能用来对遥信。
 */
export function runtimeUnitIndexOfEmu(graph, emuId) {
  if (!emuId || !graph?.nodes) return null
  const emus = graph.nodes
    .filter(n => n.templateId === 'emu')
    .sort((a, b) => (a.y - b.y) || (a.x - b.x) || String(a.id).localeCompare(String(b.id)))
  let runtime = 0
  for (const e of emus) {
    const hasPcs = graph.nodes.some(n => n.templateId === 'pcs' && paramStr(n, 'emuId') === e.id)
    if (!hasPcs) continue
    if (e.id === emuId) return runtime
    runtime++
  }
  return null
}

function splitCount(n) {
  const total = Math.max(0, Math.round(Number(n) || 0))
  const a = Math.ceil(total / 2)
  return { a, b: total - a }
}

function fmtKv(v) {
  const n = Number(v)
  if (!Number.isFinite(n) || n <= 0) return '—'
  if (n >= 1000) return `${(n / 1000).toFixed(n % 1000 === 0 ? 0 : 1)}kV`
  return `${n.toFixed(0)}V`
}

function neighborsOf(adj, nodeId) {
  return adj.get(nodeId) || []
}

export function makeGraph(nodes, edges) {
  const byId = new Map((nodes || []).map(n => [n.id, n]))
  const adj = new Map()
  for (const n of nodes || []) adj.set(n.id, new Set())
  for (const e of edges || []) {
    if (!adj.has(e.fromNodeId) || !adj.has(e.toNodeId)) continue
    adj.get(e.fromNodeId).add(e.toNodeId)
    adj.get(e.toNodeId).add(e.fromNodeId)
  }
  const listAdj = new Map()
  for (const [id, set] of adj) listAdj.set(id, [...set])
  return { nodes: nodes || [], edges: edges || [], byId, adj: listAdj }
}

/** 穿过一台断路器后的对端设备；对端仍是断路器再跳一层，避免双开关串联丢变压器/母线。 */
function farSideOfBreaker(graph, breakerNode, fromId, skipIds) {
  const far = []
  for (const farId of neighborsOf(graph.adj, breakerNode.id)) {
    if (farId === fromId || skipIds.has(farId)) continue
    const n = graph.byId.get(farId)
    if (!n) continue
    if (n.templateId === 'ac_breaker') {
      for (const far2Id of neighborsOf(graph.adj, n.id)) {
        if (far2Id === breakerNode.id || far2Id === fromId || skipIds.has(far2Id)) continue
        const n2 = graph.byId.get(far2Id)
        if (!n2 || n2.templateId === 'ac_breaker') continue
        far.push(n2)
      }
    } else {
      far.push(n)
    }
  }
  return far
}

/** 相邻设备；断路器视为透明，取其另一侧 */
function acHops(graph, fromId, skipIds = new Set()) {
  const hops = []
  const seen = new Set()
  for (const nid of neighborsOf(graph.adj, fromId)) {
    if (skipIds.has(nid)) continue
    const n = graph.byId.get(nid)
    if (!n) continue
    if (n.templateId === 'ac_breaker') {
      for (const far of farSideOfBreaker(graph, n, fromId, skipIds)) {
        if (seen.has(far.id)) continue
        seen.add(far.id)
        hops.push({ node: far, viaBreaker: n })
      }
    } else if (!seen.has(n.id)) {
      seen.add(n.id)
      hops.push({ node: n, viaBreaker: null })
    }
  }
  return hops
}

function xfmrConnectedBuses(graph, xfmrId) {
  const buses = []
  const seen = new Set()
  for (const hop of acHops(graph, xfmrId)) {
    if (hop.node.templateId !== 'ac_bus' || seen.has(hop.node.id)) continue
    seen.add(hop.node.id)
    buses.push({ bus: hop.node, viaBreaker: hop.viaBreaker || null })
  }
  return buses
}

function emuOwnsBreakerIn(idSet, graph, emuId) {
  if (!emuId || !idSet?.size) return false
  for (const id of idSet) {
    const nd = graph.byId.get(id)
    if (nd && paramStr(nd, 'emuId') === emuId) return true
  }
  return false
}

function branchBreakerRecord(brk, x, yTop, brkSpan) {
  return {
    id: brk.id,
    node: brk,
    x,
    yTop,
    y: yTop + brkSpan / 2,
    yBottom: yTop + brkSpan,
    label: brk.label || brk.parameters?.name || '断路器',
    emuId: paramStr(brk, 'emuId') || null,
    unitIndex: null,
    closed: truthy(brk.parameters?.closed),
    tripped: truthy(brk.parameters?.tripped)
  }
}

/** 耦合器件的全部下游母线帧（两绕组 1 路，双耳 2 路，未来 N 绕组同此列表）。 */
function couplingDownstreams(xf) {
  return (xf?.downstreams || []).filter(Boolean)
}

function blocksStemWalk(node) {
  const role = sldRole(node.templateId)
  return role === 'coupling' || role === 'feeder' || role === 'tap'
    || role === 'dc' || role === 'virtual' || role === 'unknown'
}

function pathToFirstBus(graph, startId) {
  const q = [{ id: startId, breakers: [] }]
  const seen = new Set([startId])
  while (q.length) {
    const cur = q.shift()
    const node = graph.byId.get(cur.id)
    if (node?.templateId === 'ac_bus') return { bus: node, breakers: cur.breakers }
    for (const nid of neighborsOf(graph.adj, cur.id)) {
      if (seen.has(nid)) continue
      const nb = graph.byId.get(nid)
      if (!nb || blocksStemWalk(nb)) continue
      seen.add(nid)
      q.push({
        id: nid,
        breakers: nb.templateId === 'ac_breaker' ? [...cur.breakers, nb] : cur.breakers
      })
    }
  }
  return { bus: null, breakers: [] }
}

function hangKind(node) {
  if (sldRole(node.templateId) === 'feeder') return 'feeder'
  return node.templateId
}

function buildBusFrame(graph, bus, incomingXfmrId, visitedBuses, visitedXfmrs) {
  visitedBuses.add(bus.id)
  const skip = new Set([incomingXfmrId].filter(Boolean))
  const hangs = []
  const xfmrs = []
  const busLinks = []
  for (const hop of acHops(graph, bus.id, skip)) {
    const n = hop.node
    if (!n) continue
    const role = sldRole(n.templateId)
    if (role === 'source' || role === 'dc' || role === 'virtual') continue
    if (role === 'coupling' || isTransformerLike(n.templateId)) {
      if (visitedXfmrs.has(n.id)) continue
      visitedXfmrs.add(n.id)
      const others = xfmrConnectedBuses(graph, n.id)
        .filter(b => b.bus.id !== bus.id && !visitedBuses.has(b.bus.id))
        .sort((a, b) => (a.bus.x - b.bus.x) || (a.bus.y - b.bus.y) || String(a.bus.id).localeCompare(String(b.bus.id)))
      const downstreams = others.map(far => {
        const frame = buildBusFrame(graph, far.bus, n.id, visitedBuses, visitedXfmrs)
        frame.incomingBreaker = far.viaBreaker || null
        return frame
      })
      xfmrs.push({
        xfmr: n,
        downstreams,
        split: isSplitTransformer(n.templateId),
        viaBreaker: hop.viaBreaker || null
      })
      continue
    }
    if (role === 'bus' || n.templateId === 'ac_bus') {
      // 母线联络（经分段断路器透明 hop 或直接相连）：邻接母线递归为子帧，中间断路器随子帧绘制
      if (visitedBuses.has(n.id)) continue
      const downstream = buildBusFrame(graph, n, null, visitedBuses, visitedXfmrs)
      downstream.viaBusLink = true
      busLinks.push({ breaker: hop.viaBreaker || null, downstream })
      continue
    }
    if (role === 'feeder' || role === 'tap' || role === 'unknown') {
      // 已归入 EMU 的电表在单元框内绘制，不再作为母线挂件重复入图
      if (n.templateId === 'ac_meter' && paramStr(n, 'emuId')) continue
      hangs.push({ node: n, viaBreaker: hop.viaBreaker || null })
    }
  }
  return { node: bus, incomingXfmrId, hangs, xfmrs, busLinks, synthetic: false }
}

function walkFrames(frame, visit) {
  if (!frame) return
  visit(frame)
  for (const xf of frame.xfmrs || []) {
    for (const d of couplingDownstreams(xf)) walkFrames(d, visit)
  }
  for (const bl of frame.busLinks || []) {
    if (bl.downstream) walkFrames(bl.downstream, visit)
  }
}

export function findBmsForPcs(graph, pcsId) {
  const neighborIds = new Set(neighborsOf(graph.adj, pcsId))
  const dcBuses = graph.nodes
    .filter(n => n.templateId === 'dc_bus' && neighborIds.has(n.id))
    .map(n => n.id)
  const dcSet = new Set(dcBuses)
  return graph.nodes
    .filter(n => {
      if (n.templateId !== 'bms') return false
      const nb = new Set(neighborsOf(graph.adj, n.id))
      return nb.has(pcsId) || [...nb].some(id => dcSet.has(id))
    })
    .sort((a, b) => (a.x - b.x) || (a.y - b.y))
}

function findDcBusForPcs(graph, pcsNodes) {
  for (const p of pcsNodes || []) {
    const neighborIds = new Set(neighborsOf(graph.adj, p.id))
    const bus = graph.nodes.find(n => n.templateId === 'dc_bus' && neighborIds.has(n.id))
    if (bus) return bus
  }
  return null
}

/** 储能支路簇：同一台直流母线上挂接的 PCS 为一簇（物理并联）；未接直流母线的 PCS 自成一簇 */
function clusterOfPcs(graph, pcsNode) {
  const dcBus = findDcBusForPcs(graph, [pcsNode])
  let key
  let pcsNodes
  if (dcBus) {
    key = `dc:${dcBus.id}`
    const near = new Set(neighborsOf(graph.adj, dcBus.id))
    pcsNodes = graph.nodes.filter(n => n.templateId === 'pcs' && near.has(n.id))
  } else {
    key = `pcs:${pcsNode.id}`
    pcsNodes = [pcsNode]
  }
  pcsNodes.sort((a, b) => (a.x - b.x) || (a.y - b.y))
  const bmsSeen = new Set()
  const bmsNodes = []
  for (const p of pcsNodes) {
    for (const b of findBmsForPcs(graph, p.id)) {
      if (bmsSeen.has(b.id)) continue
      bmsSeen.add(b.id)
      bmsNodes.push(b)
    }
  }
  return { key, leader: pcsNodes[0] || pcsNode, pcsNodes, dcBus, bmsNodes }
}

function pcsHangOf(graph, pcsNode) {
  const c = clusterOfPcs(graph, pcsNode)
  return { node: c.leader, pcsCluster: c, clusterKey: c.key }
}

/** 帧内多台 PCS 按直流母线簇合并为一个支路挂点（每台设备仍全量绘制） */
function groupFramePcsHangs(graph, frame) {
  if (!frame.hangs.some(h => h.node.templateId === 'pcs')) return
  const others = []
  const merged = new Map()
  for (const h of frame.hangs) {
    if (h.node.templateId !== 'pcs') { others.push(h); continue }
    const hang = pcsHangOf(graph, h.node)
    if (h.viaBreaker) hang.viaBreaker = hang.viaBreaker || h.viaBreaker
    if (!merged.has(hang.clusterKey)) {
      merged.set(hang.clusterKey, hang)
      others.push(hang)
    }
  }
  frame.hangs = others.sort((a, b) =>
    (a.node.x - b.node.x) || String(a.node.id).localeCompare(String(b.node.id)))
}

/** 支路簇占位宽度：PCS / BMS 两行卡片中较宽的一行 */
function clusterSpan(hang) {
  const c = hang.pcsCluster
  const n = Math.max(1, c?.pcsNodes.length || 1)
  const m = c?.bmsNodes.length || 0
  const cols = Math.max(n, m)
  return cols * CARD_W + (cols - 1) * CARD_GAP
}

function canvasX(slotItem) {
  if (slotItem.kind === 'xfmr') return slotItem.xf.xfmr.x || 0
  if (slotItem.kind === 'buslink') return slotItem.bl.downstream?.node?.x || 0
  return slotItem.hang.node.x || 0
}

function busLabel(bus, fallbackV = 0) {
  if (!bus) return `${fmtKv(fallbackV)} 母线`
  const v = paramNum(bus, 'nominalVoltage', fallbackV)
  return `${bus.label || 'AC母线'} ${fmtKv(v)}`
}

function expandFeederUnit(opts) {
  const {
    feeder, cx, xfmrId, originY, index, pvRank, graph, scene,
    busCx, UNIT_W, LINK_STUB, BRK_SPAN, pcsCluster, pcsRank, bmsRank, boundClaim,
    sectionBreakerIds, xfmrBranchBreakerIds, stemBreakerIds, viaBreaker
  } = opts
  const kind = feeder?.templateId === 'pv_unit' ? 'pv' : 'emu'

  if (kind === 'emu') {
    // 储能支路：按物理拓扑全量绘制 PCS → 直流母线 → BMS 设备链；
    // 不引入 EMU / EMU 分组 / 单元断 / 单元变等虚拟概念；卡片绑定运行时实时数据与控制
    const c = pcsCluster || { pcsNodes: [], dcBus: null, bmsNodes: [] }
    const pcsNodes = c.pcsNodes.length ? c.pcsNodes : (feeder ? [feeder] : [])
    const n = Math.max(1, pcsNodes.length)
    const m = c.bmsNodes.length
    const cols = Math.max(n, m)
    const span = cnt => cnt * CARD_W + (cnt - 1) * CARD_GAP
    const cardX = (cnt, i) => -span(cnt) / 2 + CARD_W / 2 + i * (CARD_W + CARD_GAP)

    // 单元归属的 EMU 虚拟节点（经 PCS 的 emuId 反查），供 3D 等下游识别绑定设备；
    // 绑定断路器（如中压三相断路器）画在母线与 PCS 卡之间的引线段，电表随单元下发供 3D 使用。
    // 同 emu 多支路时按序逐个认领，2D 母线挂件不重复绘制
    const emuId = paramStr(pcsNodes[0], 'emuId')
    const emu = emuId ? graph.byId.get(emuId) || null : null
    const unitIndex = runtimeUnitIndexOfEmu(graph, emuId) ?? index
    const claimed = boundClaim || new Set()
    const placedIds = new Set([
      ...(sectionBreakerIds || []),
      ...(xfmrBranchBreakerIds || []),
      ...(stemBreakerIds || [])
    ])
    const seriesBreaker = viaBreaker && !placedIds.has(viaBreaker.id) ? viaBreaker : null
    if (seriesBreaker) claimed.add(seriesBreaker.id)
    const pickBound = tpl => {
      if (!emuId) return null
      const cand = graph.nodes
        .filter(nd => nd.templateId === tpl && paramStr(nd, 'emuId') === emuId && !claimed.has(nd.id))
        .sort((a, b) => (a.y - b.y) || (a.x - b.x))
      if (cand[0]) claimed.add(cand[0].id)
      return cand[0] || null
    }
    const unitBreakerNode = pickBound('ac_breaker') || seriesBreaker
    const unitMeterNode = pickBound('ac_meter')
    // 该断路器若为母线分段断路器（两端皆母线），已按 tieBreaker 画在母线上：
    // 单元内不再重复绘制，仅保留 unitBreakerNode 作为实时遥信的绑定关系。
    // 母线—变压器支路上的串联断同理，已按 branchBreaker 画在变压器引线。
    const unitBreakerOnBus = !!(unitBreakerNode && sectionBreakerIds?.has(unitBreakerNode.id))
      || (!unitBreakerNode && emuOwnsBreakerIn(sectionBreakerIds, graph, emuId))
    const unitBreakerOnXfmr = !!(unitBreakerNode && xfmrBranchBreakerIds?.has(unitBreakerNode.id))
      || (!!emuId && emuOwnsBreakerIn(xfmrBranchBreakerIds, graph, emuId)
        && (!unitBreakerNode || placedIds.has(unitBreakerNode.id)))
    const drawnBreakerNode = seriesBreaker
      || (unitBreakerNode && !placedIds.has(unitBreakerNode.id) ? unitBreakerNode : null)

    // 绑断路器时引线段加高：母线 → 断路器 → PCS 卡；未绑定维持短引线
    const brkTop = LINK_STUB
    const brkMid = brkTop + BRK_SPAN / 2
    const brkBottom = brkTop + BRK_SPAN
    const pcsTop = drawnBreakerNode ? brkBottom + LINK_STUB : LINK_STUB
    // 卡片高度需容纳运行时数据行与设定/启停控件（对齐旧版交互卡）
    const pcsH = 228
    const dcBusY = pcsTop + pcsH + LINK_STUB * 2
    const bmsTop = dcBusY + LINK_STUB * 2
    const bmsH = 214

    const cards = []
    const wires = []
    const labels = []
    if (drawnBreakerNode) {
      // 主干：母线 → 断路器，再经汇流横线接入各 PCS 卡顶（经典馈线画法）
      wires.push({ x1: cx, y1: originY, x2: cx, y2: originY + brkBottom })
      if (n > 1) {
        wires.push({ x1: cx, y1: originY + brkBottom, x2: cx, y2: originY + pcsTop })
        wires.push({ x1: cx + cardX(n, 0), y1: originY + pcsTop, x2: cx + cardX(n, n - 1), y2: originY + pcsTop })
      } else {
        wires.push({ x1: cx, y1: originY + brkBottom, x2: cx + cardX(1, 0), y2: originY + pcsTop })
      }
    }
    pcsNodes.forEach((p, i) => {
      const x = cardX(n, i)
      cards.push({
        id: `pcs-${p.id}`,
        tone: 'pcs',
        num: (pcsRank.get(p.id) ?? i) + 1,
        x, y: pcsTop, w: CARD_W, h: pcsH,
        title: `PCS${(pcsRank.get(p.id) ?? i) + 1}`,
        lines: [
          `额定 ${paramNum(p, 'pcsRatedPowerKw')} kW`,
          `最大 ${paramNum(p, 'pcsMaxPowerKw')} kW`,
          `交流 ${paramNum(p, 'acVoltage')} V`,
          `直流 ${paramNum(p, 'dcVoltageMin')}~${paramNum(p, 'dcVoltageMax')} V`
        ]
      })
      if (!drawnBreakerNode) {
        wires.push({ x1: cx + x, y1: originY, x2: cx + x, y2: originY + pcsTop })
      }
    })
    c.bmsNodes.forEach((b, j) => {
      const x = cardX(m, j)
      cards.push({
        id: `bms-${b.id}`,
        tone: 'bms',
        num: (bmsRank.get(b.id) ?? j) + 1,
        x, y: bmsTop, w: CARD_W, h: bmsH,
        title: `BMS${(bmsRank.get(b.id) ?? j) + 1}`,
        lines: [
          `簇 ${paramNum(b, 'clusterCount')} × 包 ${paramNum(b, 'packCount')}`,
          `串 ${paramNum(b, 'cellSeriesCount')} × 并 ${paramNum(b, 'cellParallelCount')}`,
          `容量 ${paramNum(b, 'cellNominalCapacity')} Ah`,
          `初始SOC ${Math.round(paramNum(b, 'cellInitialSoc', 0) * 100)}%`
        ]
      })
    })

    let bottom
    if (c.dcBus && (n > 1 || m > 1)) {
      // 并联共直流母线：PCS ↓ 直流母线 ↓ BMS
      const halfSpan = span(cols) / 2
      for (let i = 0; i < n; i++) {
        const x = cardX(n, i)
        wires.push({ x1: cx + x, y1: originY + pcsTop + pcsH, x2: cx + x, y2: originY + dcBusY })
      }
      wires.push({ x1: cx - halfSpan - 16, y1: originY + dcBusY, x2: cx + halfSpan + 16, y2: originY + dcBusY, thick: true })
      labels.push({ x: -halfSpan - 16, y: dcBusY - 6, text: `${c.dcBus.label || '直流母线'} ${fmtKv(paramNum(c.dcBus, 'nominalVoltage', 0))}` })
      for (let j = 0; j < m; j++) {
        const x = cardX(m, j)
        wires.push({ x1: cx + x, y1: originY + dcBusY, x2: cx + x, y2: originY + bmsTop })
      }
      bottom = bmsTop + bmsH
    } else if (m > 0) {
      // 单链：PCS 直连 BMS，引线旁标注直流母线电压
      for (let j = 0; j < m; j++) {
        const x = cardX(m, j)
        wires.push({ x1: cx + x, y1: originY + pcsTop + pcsH, x2: cx + x, y2: originY + bmsTop })
      }
      if (c.dcBus) {
        labels.push({ x: cardX(m, 0) + 10, y: (pcsTop + pcsH + bmsTop) / 2 + 4, text: fmtKv(paramNum(c.dcBus, 'nominalVoltage', 0)) })
      }
      bottom = bmsTop + bmsH
    } else {
      bottom = pcsTop + pcsH
    }
    // unitWire 标记：这些连线仅用于 2D 单线图卡片绘制；3D 由逐设备展开自行布线，转换时应跳过
    scene.wires.push(...wires.map(w => ({ ...w, unitWire: true })))

    return {
      unit: {
        index, unitIndex, kind, cx, originY, xfmrId,
        busCx: busCx ?? cx,
        emu,
        pcsNodes,
        pcsNums: pcsNodes.map((p, i) => (pcsRank.get(p.id) ?? i) + 1),
        bmsNodes: c.bmsNodes,
        bmsNums: c.bmsNodes.map((b, j) => (bmsRank.get(b.id) ?? j) + 1),
        dcBus: c.dcBus,
        unitBreakerNode,
        unitBreakerOnBus,
        unitBreakerOnXfmr,
        unitMeterNode,
        brkTop,
        brkMid,
        brkBottom,
        pcsTop,
        dcBusY,
        bmsTop,
        cards, labels,
        halfSpan: span(cols) / 2,
        bottom: bottom + 16
      },
      unitBottom: bottom + 16
    }
  }

  // 光伏支路：箱变 + 方阵 A/B 图例（保留实时数据与控制）
  const pv = feeder
  const pvIndex = pvRank.get(pv?.id) ?? -1
  const unitBrkTop = LINK_STUB
  const unitBrkMid = unitBrkTop + BRK_SPAN / 2
  const unitBrkBottom = unitBrkTop + BRK_SPAN
  const unitXfmrTop = unitBrkBottom + LINK_STUB
  const channelX = 92
  const xfmrCardH = 160
  const xfmrCardTop = unitXfmrTop
  const bmsTop = xfmrCardTop + xfmrCardH + LINK_STUB * 2
  const bmsH = 118
  const arraySplitY = xfmrCardTop + xfmrCardH + LINK_STUB
  const unitBottom = bmsTop + bmsH + 16

  const inverterCount = Math.max(1, Math.round(paramNum(pv, 'inverterCount', 16)))
  const inverterRatedKw = paramNum(pv, 'inverterRatedPowerKw', 320)
  const stringCount = Math.max(1, Math.round(paramNum(pv, 'stringCount', 16)))
  const modulesPerString = Math.max(1, Math.round(paramNum(pv, 'modulesPerString', 30)))
  const xfPrimary = paramNum(pv, 'unitXfPrimaryV', 35000)
  const xfSecondary = paramNum(pv, 'unitXfSecondaryV', 690)
  const split = splitCount(inverterCount)
  const moduleModel = paramStr(pv, 'moduleModel', 'TSM-NEG21C.20Q')
  const dcVoltageMin = paramNum(pv, 'dcVoltageMin', 500)
  const dcVoltageMax = paramNum(pv, 'dcVoltageMax', 1500)
  const inverterEfficiency = paramNum(pv, 'inverterEfficiency', 0.99)
  const inverterAcVoltage = paramNum(pv, 'inverterAcVoltage', xfSecondary)
  const makePvGroup = (side, count) => ({
    side,
    inverterCount: count,
    inverterRatedKw,
    inverterMaxKw: paramNum(pv, 'inverterMaxPowerKw', inverterRatedKw),
    inverterEfficiency,
    inverterAcVoltage,
    totalRatedKw: count * inverterRatedKw,
    moduleModel,
    modulesPerString,
    stringCount,
    totalModuleCount: count * stringCount * modulesPerString,
    dcVoltageMin,
    dcVoltageMax
  })

  const unit = {
    index,
    kind,
    cx,
    busCx: busCx ?? cx,
    originY,
    xfmrId,
    pv,
    pvIndex,
    channelX,
    unitBrkTop,
    unitBrkMid,
    unitBrkBottom,
    unitXfmrTop,
    bmsTop,
    bmsH,
    bottom: unitBottom,
    halfSpan: UNIT_W / 2,
    label: pv?.label || `PV ${pvIndex + 1}`,
    unitXfLabel: `${fmtKv(xfPrimary)}/${fmtKv(xfSecondary)}`,
    inverterCount,
    inverterRatedKw,
    inverterMaxKw: paramNum(pv, 'inverterMaxPowerKw', inverterRatedKw),
    inverterEfficiency,
    inverterAcVoltage,
    totalRatedKw: inverterCount * inverterRatedKw,
    xfRatedKva: paramNum(pv, 'unitXfRatedKva', inverterCount * inverterRatedKw),
    moduleModel,
    modulesPerString,
    stringCount,
    totalModuleCount: inverterCount * stringCount * modulesPerString,
    dcVoltageMin,
    dcVoltageMax,
    groupA: makePvGroup('A', split.a),
    groupB: makePvGroup('B', split.b),
    xfmrCardTop,
    xfmrCardH,
    arraySplitY
  }

  return { unit, unitBottom }
}

/**
 * @param {object} topology TopologyProject
 */
export function buildTopologyMainLineLayout(topology) {
  const graph = makeGraph(topology?.nodes, topology?.edges)
  const nodes = graph.nodes

  const UNIT_W = 360
  const HANG_W = 168
  const MARGIN_X = 48
  const MARGIN_TOP = 24
  const BAY_GAP = 72
  const FEEDER_SLOT_PAD = 80
  const ISLAND_GAP = 72
  const LINK_STUB = 18
  const BRK_SPAN = 28
  const XFMR_SPAN = 44
  const METER_H = 72
  const LOAD_SYMBOL_H = 36
  const METER_HALF_W = 32

  const grid = nodes.find(n => n.templateId === 'grid') || null
  const acBuses = nodes.filter(n => n.templateId === 'ac_bus').sort((a, b) =>
    paramNum(b, 'nominalVoltage') - paramNum(a, 'nominalVoltage') || (a.x - b.x)
  )
  const feeders = nodes
    .filter(n => n.templateId === 'pcs' || n.templateId === 'pv_unit')
    .sort((a, b) => (a.x - b.x) || (a.y - b.y))

  // 设备编号秩：按画布坐标 (Y,X) 排序，与运行时转换器顺序保持一致
  const byYX = (a, b) => (a.y - b.y) || (a.x - b.x)
  const pcsRank = new Map()
  nodes.filter(n => n.templateId === 'pcs').sort(byYX).forEach((n, i) => pcsRank.set(n.id, i))
  const bmsRank = new Map()
  nodes.filter(n => n.templateId === 'bms').sort(byYX).forEach((n, i) => bmsRank.set(n.id, i))
  const pvRank = new Map()
  nodes
    .filter(n => n.templateId === 'pv_unit')
    .sort((a, b) => (a.y - b.y) || (a.x - b.x))
    .forEach((n, i) => pvRank.set(n.id, i))

  const visitedBuses = new Set()
  const visitedXfmrs = new Set()
  const stem = pathToFirstBus(graph, grid?.id)
  const startBus = stem.bus || acBuses[0] || null
  const roots = []
  if (startBus) roots.push(buildBusFrame(graph, startBus, null, visitedBuses, visitedXfmrs))
  for (const bus of acBuses) {
    if (!visitedBuses.has(bus.id)) {
      roots.push(buildBusFrame(graph, bus, null, visitedBuses, visitedXfmrs))
    }
  }

  for (const root of roots) walkFrames(root, fr => groupFramePcsHangs(graph, fr))
  const hangingIds = new Set()
  for (const root of roots) walkFrames(root, fr => {
    for (const h of fr.hangs) {
      hangingIds.add(h.node.id)
      for (const p of h.pcsCluster?.pcsNodes || []) hangingIds.add(p.id)
    }
  })
  const frames = []
  for (const root of roots) walkFrames(root, fr => frames.push(fr))
  // 母线分段/联络断路器：两端皆为母线，已随 tieBreakers 画在母线上
  const sectionBreakerIds = new Set()
  for (const fr of frames) {
    for (const bl of fr.busLinks || []) {
      if (bl.breaker) sectionBreakerIds.add(bl.breaker.id)
    }
  }
  for (const f of feeders) {
    if (hangingIds.has(f.id)) continue
    const hang = f.templateId === 'pcs' ? pcsHangOf(graph, f) : { node: f }
    let best = frames[0] || null
    let bestDist = Infinity
    for (const fr of frames) {
      const d = Math.abs((hang.node.x || 0) - (fr.node?.x || 0))
      if (d < bestDist) {
        best = fr
        bestDist = d
      }
    }
    if (best) {
      best.hangs.push(hang)
      hangingIds.add(hang.node.id)
      for (const p of hang.pcsCluster?.pcsNodes || []) hangingIds.add(p.id)
    }
  }
  if (!roots.length && feeders.length) {
    const hangs = []
    const seenClusters = new Set()
    for (const f of feeders) {
      if (f.templateId === 'pcs') {
        const hang = pcsHangOf(graph, f)
        if (seenClusters.has(hang.clusterKey)) continue
        seenClusters.add(hang.clusterKey)
        hangs.push(hang)
      } else {
        hangs.push({ node: f })
      }
    }
    roots.push({
      node: null,
      incomingXfmrId: null,
      hangs,
      xfmrs: [],
      synthetic: true
    })
  }

  const kept = roots.filter(fr => fr.hangs.length > 0 || fr.xfmrs.length > 0 || fr === roots[0])
  const forest = kept.length ? kept : roots

  function measure(frame) {
    const items = [
      ...frame.xfmrs.map(xf => ({ kind: 'xfmr', xf })),
      ...(frame.busLinks || []).map(bl => ({ kind: 'buslink', bl })),
      ...frame.hangs
        .filter(hang => hangKind(hang.node) === 'feeder')
        .map(hang => ({ kind: 'feeder', hang }))
    ].sort((a, b) => canvasX(a) - canvasX(b) || String(a.hang?.node?.id || a.xf?.xfmr.id || a.bl?.downstream?.node?.id).localeCompare(String(b.hang?.node?.id || b.xf?.xfmr.id || b.bl?.downstream?.node?.id)))
    frame.taps = frame.hangs.filter(hang => {
      const r = sldRole(hang.node.templateId)
      return r === 'tap' || r === 'unknown'
    }).sort((a, b) => (a.node.x - b.node.x) || String(a.node.id).localeCompare(b.node.id))
    const slots = []
    for (const item of items) {
      if (item.kind === 'xfmr') {
        const downs = couplingDownstreams(item.xf)
        downs.forEach(measure)
        const downW = downs.reduce((s, d) => s + (d.width || 0), 0) + BAY_GAP * Math.max(0, downs.length - 1)
        slots.push({ item, w: Math.max(downW, UNIT_W) })
      } else if (item.kind === 'buslink') {
        measure(item.bl.downstream)
        slots.push({ item, w: Math.max(item.bl.downstream.width || 0, UNIT_W) })
      } else {
        // 光伏支路图例按 channelX 两翼展开，槽位至少占一个单元宽，避免相邻光伏单元叠板。
        // 储能馈线槽宽 = 卡片行 + 边距，避免相邻 EMU 卡片贴在一起。
        slots.push({
          item,
          w: item.hang.node.templateId === 'pv_unit'
            ? UNIT_W
            : Math.max(clusterSpan(item.hang) + FEEDER_SLOT_PAD, 240)
        })
      }
    }
    frame.slots = slots
    const tapCount = frame.taps.length
    // 母线联络结构 / 多台 PCS 并联支路必须画出母线横杠，不适用单挂件省略规则
    const pcsTotal = slots.reduce((s, sl) => s + (sl.item.kind === 'feeder'
      ? Math.max(1, sl.item.hang.pcsCluster?.pcsNodes.length || 1)
      : 0), 0)
    const structural = (frame.busLinks || []).length > 0 || !!frame.viaBusLink || pcsTotal > 1
    if (!slots.length) {
      frame.width = Math.max(UNIT_W, tapCount * HANG_W)
      frame.omit = tapCount <= 1 && !structural
      return
    }
    frame.width = slots.reduce((s, sl) => s + sl.w, 0) + BAY_GAP * (slots.length - 1)
    frame.omit = (slots.length + tapCount) <= 1 && !structural
  }
  for (const root of forest) measure(root)

  const scene = {
    wires: [],
    buses: [],
    transformers: [],
    meters: [],
    loads: [],
    unknowns: [],
    tieBreakers: [],
    branchBreakers: [],
    placements: []
  }

  function placeFrame(frame, x0, yBus) {
    frame.x0 = x0
    frame.y = yBus
    frame.width = Math.max(frame.width || UNIT_W, UNIT_W)
    frame.x1 = x0 + 40
    frame.x2 = x0 + frame.width - 40
    frame.cx = x0 + frame.width / 2
    if (frame.node) {
      const v = paramNum(frame.node, 'nominalVoltage', 0)
      scene.buses.push({
        id: frame.node.id,
        node: frame.node,
        x1: frame.x1,
        x2: frame.x2,
        cx: frame.cx,
        y: yBus,
        omit: !!frame.omit,
        label: busLabel(frame.node, v),
        voltage: v
      })
    }

    const slots = frame.slots || []
    const taps = frame.taps || []
    const hasXfmr = slots.some(s => s.item.kind === 'xfmr')
    const hasTieBrk = slots.some(s => s.item.kind === 'buslink' && s.item.bl.breaker)
    const hasMeter = taps.some(t => t.node.templateId === 'ac_meter')
    const hasLoad = taps.some(t => t.node.templateId === 'load')
    const hasUnknown = taps.some(t => sldRole(t.node.templateId) === 'unknown')
    const equipH = Math.max(
      hasXfmr ? XFMR_SPAN : 0,
      hasTieBrk ? BRK_SPAN : 0,
      hasMeter ? METER_H : 0,
      hasUnknown ? METER_H : 0,
      hasLoad ? LOAD_SYMBOL_H + LINK_STUB : 0
    )
    const yEquip = yBus + LINK_STUB
    const structXs = []
    const busRec = frame.node ? scene.buses[scene.buses.length - 1] : null

    let x = x0
    slots.forEach((slot, idx) => {
      const cx = x + slot.w / 2
      const item = slot.item
      const hasRight = idx < slots.length - 1 || taps.length > 0
      if (item.kind === 'xfmr') {
        const xf = item.xf.xfmr
        const downs = couplingDownstreams(item.xf)
        if (downs.length > 1) {
          for (const d of downs) d.omit = false
        }
        const pri = paramNum(xf, 'primaryVoltage', 0)
        const sec = paramNum(xf, 'secondaryVoltage', 0)
        const hvBrk = item.xf.viaBreaker || null
        const lvBrkAny = downs.some(d => d.incomingBreaker)
        // 无串联断：保持原 yEquip 对齐电表/联络；有高压或低压串联断则按跨距叠高
        const yXf = hvBrk ? (yBus + LINK_STUB + BRK_SPAN + LINK_STUB) : yEquip
        if (hvBrk) {
          const yTop = yBus + LINK_STUB
          scene.wires.push({ x1: cx, y1: yBus, x2: cx, y2: yTop })
          scene.branchBreakers.push(branchBreakerRecord(hvBrk, cx, yTop, BRK_SPAN))
          scene.wires.push({ x1: cx, y1: yTop + BRK_SPAN, x2: cx, y2: yXf })
        } else {
          scene.wires.push({ x1: cx, y1: yBus, x2: cx, y2: yXf })
        }
        const rec = {
          id: xf.id,
          node: xf,
          x: cx,
          y: yXf,
          span: XFMR_SPAN,
          windings: item.xf.split ? 3 : 2,
          split: !!item.xf.split,
          label: xf.label || '变压器',
          ratioLabel: `${fmtKv(pri)}/${fmtKv(sec)}`,
          kvaLabel: paramNum(xf, 'ratedPowerKva', 0) > 0
            ? `${paramNum(xf, 'ratedPowerKva', 0).toFixed(0)} kVA${item.xf.split ? ' · 双耳' : ''}`
            : (item.xf.split ? '双耳' : ''),
          labelSide: hasRight ? 'left' : 'right',
          omitBusLv: downs.length === 1 ? !!downs[0].omit : downs.length === 0,
          busLeft: x + 40,
          busRight: x + slot.w - 40,
          incomingBreakerId: hvBrk?.id || null
        }
        scene.transformers.push(rec)
        structXs.push(cx)
        if (downs.length > 0) {
          const fromY = yXf + XFMR_SPAN
          const lvExtra = lvBrkAny ? (BRK_SPAN + LINK_STUB) : 0
          const yChild = hvBrk || lvBrkAny
            ? fromY + LINK_STUB + lvExtra + (downs.length === 1 && downs[0].omit ? LINK_STUB : 24)
            : yEquip + equipH + LINK_STUB + (downs.length === 1 && downs[0].omit ? LINK_STUB : 24)
          let dx = x
          for (const d of downs) {
            placeFrame(d, dx, yChild)
            dx += (d.width || UNIT_W) + BAY_GAP
          }
          rec.busLeft = downs[0].x1
          rec.busRight = downs[downs.length - 1].x2
          rec.omitBusLv = downs.length === 1 ? !!downs[0].omit : false
          if (downs.length === 1) {
            const lvBrk = downs[0].incomingBreaker
            if (lvBrk) {
              const yTop = fromY + LINK_STUB
              scene.wires.push({ x1: cx, y1: fromY, x2: cx, y2: yTop })
              scene.branchBreakers.push(branchBreakerRecord(lvBrk, cx, yTop, BRK_SPAN))
              scene.wires.push({ x1: cx, y1: yTop + BRK_SPAN, x2: cx, y2: yChild })
            } else {
              scene.wires.push({ x1: cx, y1: fromY, x2: cx, y2: yChild })
            }
          } else {
            const cxs = downs.map(d => d.cx)
            const yJoin = lvBrkAny ? fromY + LINK_STUB : (fromY + yChild) / 2
            scene.wires.push({ x1: cx, y1: fromY, x2: cx, y2: yJoin })
            scene.wires.push({ x1: Math.min(...cxs), y1: yJoin, x2: Math.max(...cxs), y2: yJoin })
            for (const d of downs) {
              const lvBrk = d.incomingBreaker
              if (lvBrk) {
                const yTop = yJoin + LINK_STUB
                scene.wires.push({ x1: d.cx, y1: yJoin, x2: d.cx, y2: yTop })
                scene.branchBreakers.push(branchBreakerRecord(lvBrk, d.cx, yTop, BRK_SPAN))
                scene.wires.push({ x1: d.cx, y1: yTop + BRK_SPAN, x2: d.cx, y2: yChild })
              } else {
                scene.wires.push({ x1: d.cx, y1: yJoin, x2: d.cx, y2: yChild })
              }
            }
          }
        }
      } else if (item.kind === 'buslink') {
        // 母线联络：母线 → 分段断路器 → 子帧母线
        const brk = item.bl.breaker
        const downstream = item.bl.downstream
        structXs.push(cx)
        scene.wires.push({ x1: cx, y1: yBus, x2: cx, y2: yEquip })
        if (brk) {
          scene.tieBreakers.push({
            id: brk.id,
            node: brk,
            x: cx,
            yTop: yEquip,
            y: yEquip + BRK_SPAN / 2,
            yBottom: yEquip + BRK_SPAN,
            label: brk.label || brk.parameters?.name || '断路器',
            emuId: paramStr(brk, 'emuId') || null,
            unitIndex: null,
            closed: truthy(brk.parameters?.closed),
            tripped: truthy(brk.parameters?.tripped)
          })
        }
        const yChild = yEquip + equipH + LINK_STUB + (downstream.omit ? LINK_STUB : 24)
        scene.wires.push({ x1: cx, y1: yEquip + (brk ? BRK_SPAN : 0), x2: cx, y2: yChild })
        placeFrame(downstream, x, yChild)
      } else if (item.kind === 'feeder') {
        structXs.push(cx)
        scene.placements.push({
          feeder: item.hang.node,
          cx,
          xfmrId: frame.incomingXfmrId || null,
          originY: yBus,
          busCx: frame.cx,
          pcsCluster: item.hang.pcsCluster || null,
          viaBreaker: item.hang.viaBreaker || null
        })
      }
      x += slot.w + BAY_GAP
    })

    const busX = frame.node?.x ?? frame.cx
    let rightX = Math.max(frame.cx, ...structXs)
    let leftX = Math.min(frame.cx, ...structXs)
    for (const tap of taps) {
      const n = tap.node
      const toRight = (n.x || 0) >= busX
      let tapX
      if (toRight) {
        rightX += HANG_W
        tapX = rightX
      } else {
        leftX -= HANG_W
        tapX = leftX
      }
      if (n.templateId === 'ac_meter') {
        // meterTap 标记：3D 不绘制电表，该挂线在 3D 转换时应跳过，避免悬空线
        scene.wires.push({ x1: tapX, y1: yBus, x2: tapX, y2: yEquip, meterTap: true })
        scene.meters.push({
          id: n.id,
          node: n,
          busId: frame.node?.id || null,
          x: tapX,
          y: yEquip,
          h: METER_H,
          label: n.label || '电表',
          isPcc: truthy(n.parameters?.isPccMeter)
        })
      } else if (n.templateId === 'load') {
        scene.wires.push({ x1: tapX, y1: yBus, x2: tapX, y2: yBus + LINK_STUB })
        scene.loads.push({
          id: n.id,
          node: n,
          busId: frame.node?.id || null,
          x: tapX,
          busY: yBus,
          stub: LINK_STUB,
          symbolH: LOAD_SYMBOL_H,
          label: n.label || n.parameters?.name || '负载'
        })
      } else {
        scene.wires.push({ x1: tapX, y1: yBus, x2: tapX, y2: yEquip })
        scene.unknowns.push({
          id: n.id,
          node: n,
          busId: frame.node?.id || null,
          x: tapX,
          y: yEquip,
          h: METER_H,
          label: n.label || n.templateId || '设备'
        })
      }
      if (busRec) {
        busRec.x1 = Math.min(busRec.x1, tapX - 40)
        busRec.x2 = Math.max(busRec.x2, tapX + 40)
      }
      frame.x1 = Math.min(frame.x1, tapX - 40)
      frame.x2 = Math.max(frame.x2, tapX + 40)
    }
  }

  let y = MARGIN_TOP
  const yGrid = y
  y += 50
  const stemBreakers = []
  let yCursor = y + LINK_STUB
  const pathBreakers = grid ? (stem.breakers || []) : []
  if (pathBreakers.length) {
    for (const br of pathBreakers) {
      const yTop = yCursor
      const yMid = yTop + BRK_SPAN / 2
      const yBot = yTop + BRK_SPAN
      stemBreakers.push({
        id: br.id,
        node: br,
        yTop,
        y: yMid,
        yBottom: yBot,
        isMain: truthy(br.parameters?.isMainBreaker),
        label: br.label || br.parameters?.name || '断路器'
      })
      yCursor = yBot + LINK_STUB
    }
  }
  const yRoot = yCursor

  let cursorX = MARGIN_X
  for (const root of forest) {
    placeFrame(root, cursorX, yRoot)
    cursorX += (root.width || UNIT_W) + ISLAND_GAP
  }

  const gridX = forest[0]?.cx ?? (MARGIN_X + UNIT_W / 2)
  if (grid) {
    const yFrom = yGrid + 8
    if (stemBreakers.length) {
      scene.wires.push({ x1: gridX, y1: yFrom, x2: gridX, y2: stemBreakers[0].yTop })
      for (let i = 0; i < stemBreakers.length; i++) {
        const b = stemBreakers[i]
        const yTo = i < stemBreakers.length - 1 ? stemBreakers[i + 1].yTop : yRoot
        scene.wires.push({ x1: gridX, y1: b.yBottom, x2: gridX, y2: yTo })
      }
    } else {
      scene.wires.push({ x1: gridX, y1: yFrom, x2: gridX, y2: yRoot })
    }
  }
  for (const b of stemBreakers) b.x = gridX

  const unitLayouts = []
  let unitBottom = 400
  const boundClaim = new Set()
  const xfmrBranchBreakerIds = new Set(scene.branchBreakers.map(b => b.id))
  const stemBreakerIds = new Set(stemBreakers.map(b => b.id))
  scene.placements.forEach((p, i) => {
    const built = expandFeederUnit({
      feeder: p.feeder,
      cx: p.cx,
      busCx: p.busCx,
      xfmrId: p.xfmrId,
      originY: p.originY,
      index: i,
      pvRank,
      graph,
      scene,
      UNIT_W,
      LINK_STUB,
      BRK_SPAN,
      pcsCluster: p.pcsCluster,
      pcsRank,
      bmsRank,
      boundClaim,
      sectionBreakerIds,
      xfmrBranchBreakerIds,
      stemBreakerIds,
      viaBreaker: p.viaBreaker || null
    })
    unitLayouts.push(built.unit)
    unitBottom = Math.max(unitBottom, built.unitBottom)
  })

  // 分段 / 变压器支路断路器绑定了 EMU 时，实时分合闸遥信取自该 EMU 对应的运行时单元
  for (const tb of [...scene.tieBreakers, ...scene.branchBreakers]) {
    if (!tb.emuId) continue
    const owner = unitLayouts.find(u => u.emu?.id === tb.emuId)
    if (owner) tb.unitIndex = owner.unitIndex ?? owner.index
  }

  const yUnitTop = unitLayouts[0]?.originY ?? yRoot
  const yBusHv = forest[0]?.y ?? yRoot
  const feederBuses = scene.buses.filter(b => unitLayouts.some(u => Math.abs(u.originY - b.y) < 0.5))
  const omitBusHv = forest[0] ? !!forest[0].omit : true
  const omitBusLv = feederBuses.length
    ? feederBuses.every(b => b.omit)
    : unitLayouts.length <= 1
  const firstXfmr = scene.transformers[0] || null
  const pccMeter = scene.meters.find(m => m.isPcc) || scene.meters[0] || null
  const mainBreakerNode = stemBreakers.find(b => b.isMain)?.node
    || nodes.find(n => n.templateId === 'ac_breaker' && truthy(n.parameters?.isMainBreaker))
    || null
  const firstLoad = scene.loads[0] || null
  const rootBus = forest[0]?.node || null
  const firstLvBus = scene.buses.find(b => b.id && b.id !== rootBus?.id)?.node || null

  let maxX = gridX + 80
  let maxY = yRoot + 80
  for (const b of scene.buses) {
    maxX = Math.max(maxX, b.x2 + 16)
    maxY = Math.max(maxY, b.y + 24)
  }
  for (const t of scene.transformers) {
    maxX = Math.max(maxX, t.x + 80)
    maxY = Math.max(maxY, t.y + t.span + 24)
  }
  for (const m of scene.meters) {
    maxX = Math.max(maxX, m.x + METER_HALF_W + 16)
    maxY = Math.max(maxY, m.y + m.h + 16)
  }
  for (const b of scene.tieBreakers) {
    maxX = Math.max(maxX, b.x + 80)
    maxY = Math.max(maxY, b.yBottom + 24)
  }
  for (const b of scene.branchBreakers) {
    maxX = Math.max(maxX, b.x + 80)
    maxY = Math.max(maxY, b.yBottom + 24)
  }
  for (const u of unitLayouts) {
    maxX = Math.max(maxX, u.cx + (u.halfSpan ?? UNIT_W / 2) + 24)
    maxY = Math.max(maxY, u.originY + u.bottom + 36)
  }
  for (const l of scene.loads) {
    maxX = Math.max(maxX, l.x + 80)
  }
  for (const u of scene.unknowns) {
    maxX = Math.max(maxX, u.x + METER_HALF_W + 16)
    maxY = Math.max(maxY, u.y + u.h + 16)
  }

  const hvV = paramNum(rootBus, 'nominalVoltage', paramNum(grid, 'outputVoltage', 220000))
  const lvV = paramNum(firstLvBus, 'nominalVoltage', paramNum(firstXfmr?.node, 'secondaryVoltage', 35000))
  const mainBrk = stemBreakers.find(b => b.isMain) || stemBreakers[0] || null

  return {
    width: Math.max(720, maxX + MARGIN_X),
    height: Math.max(320, maxY),
    wires: scene.wires,
    buses: scene.buses,
    transformers: scene.transformers,
    meters: scene.meters,
    loads: scene.loads,
    unknowns: scene.unknowns,
    tieBreakers: scene.tieBreakers,
    branchBreakers: scene.branchBreakers,
    stemBreakers,
    gridX,
    stationCenterX: gridX,
    xfmrX: firstXfmr?.x ?? gridX,
    stationXfmrs: scene.transformers,
    meterX: pccMeter?.x ?? gridX,
    rowPeerGap: HANG_W,
    xfmrLabelSide: scene.transformers.length > 1 || pccMeter ? 'left' : 'right',
    busLeft: forest[0]?.x1 ?? MARGIN_X + 40,
    busRight: forest[0]?.x2 ?? MARGIN_X + UNIT_W - 40,
    linkStub: LINK_STUB,
    yGrid,
    yAfterGrid: yGrid + 50,
    yMainBreaker: mainBrk?.y ?? null,
    yBrkTop: mainBrk?.yTop ?? null,
    yBrkBottom: mainBrk?.yBottom ?? null,
    yBusHv,
    omitBusHv,
    yXfmr: firstXfmr?.y ?? yRoot + LINK_STUB,
    xfmrSpan: XFMR_SPAN,
    meterH: METER_H,
    yBusLv: yUnitTop,
    omitBusLv,
    yUnitTop,
    hasMainBreaker: !!mainBreakerNode,
    hasPccMeter: !!pccMeter,
    hasLoad: scene.loads.length > 0,
    load: firstLoad?.node || null,
    loadBus: firstLoad ? graph.byId.get(firstLoad.busId) || null : null,
    loadAttachSide: firstLoad
      ? (firstLoad.busId && firstLoad.busId === rootBus?.id ? 'hv' : 'lv')
      : null,
    loadX: firstLoad?.x ?? null,
    loadStub: LINK_STUB,
    loadSymbolH: LOAD_SYMBOL_H,
    loadLabel: firstLoad?.label || '负载',
    grid,
    mainBreaker: mainBreakerNode,
    pccMeter: pccMeter?.node || null,
    mainXfmr: firstXfmr?.node || null,
    busHv: rootBus,
    busLv: firstLvBus,
    busHvLabel: busLabel(rootBus, hvV),
    busLvLabel: busLabel(firstLvBus, lvV),
    xfmrLabel: firstXfmr
      ? firstXfmr.ratioLabel
      : `${fmtKv(hvV)}/${fmtKv(lvV)}`,
    meterLabel: pccMeter?.label || '并网点电表',
    units: unitLayouts,
    groups: []
  }
}
