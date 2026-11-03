/** 组态节点几何与端口坐标（画布本地坐标，单位 px） */

export const GRID_SIZE = 20

export function snapToGrid(v, grid = GRID_SIZE) {
  return Math.round(Number(v) / grid) * grid
}

const SIZE = {
  grid: { w: 120, h: 72 },
  ac_bus: { w: 220, h: 36 },
  ac_breaker: { w: 100, h: 110 },
  transformer: { w: 100, h: 120 },
  split_transformer: { w: 160, h: 120 },
  ac_meter: { w: 110, h: 72 },
  load: { w: 110, h: 72 },
  svg: { w: 120, h: 84 },
  emu: { w: 140, h: 96 },
  pcs: { w: 120, h: 96 },
  pv_unit: { w: 140, h: 96 },
  pvu: { w: 0, h: 0 },
  pv_pcs: { w: 120, h: 96 },
  pv_array: { w: 110, h: 72 },
  bms: { w: 110, h: 72 },
  dc_bus: { w: 160, h: 48 }
}

export function nodeSize(templateId) {
  return SIZE[templateId] || { w: 120, h: 80 }
}

export function portPosition(node, portDef) {
  const { w, h } = nodeSize(node.templateId)
  const t = Math.min(0.95, Math.max(0.05, portDef.offset ?? 0.5))
  let lx = w / 2
  let ly = h / 2
  switch (portDef.side) {
    case 'top':
      lx = w * t
      ly = 0
      break
    case 'bottom':
      lx = w * t
      ly = h
      break
    case 'left':
      lx = 0
      ly = h * t
      break
    case 'right':
      lx = w
      ly = h * t
      break
  }
  return { x: node.x + lx, y: node.y + ly, localX: lx, localY: ly }
}

export function formatVoltage(v) {
  const n = Number(v)
  if (!Number.isFinite(n) || n <= 0) return '—'
  if (n >= 1000) return `${(n / 1000).toFixed(n % 1000 === 0 ? 0 : 1)}kV`
  return `${n.toFixed(0)}V`
}

/**
 * 组态模板在电站概览一次图中的角色。新器件必须在此登记，否则按 unknown 仍会画成母线挂件，而不会从接线图蒸发。
 * - source: 电网
 * - bus: 交流母线
 * - passthrough: 断路器（电气透明，沿连通继续）
 * - coupling: 变压器等绕组设备（沿其余绕组分叉；下游可以是母线、发电设备或另一台耦合设备）
 * - feeder: 发电支路（PCS / 光伏），挂在母线或耦合绕组下
 * - tap: 电表 / 负载 / SVG 等母线挂件
 * - dc / virtual: 直流侧或虚拟节点，一次图站侧不直接遍历
 * - unknown: 未登记模板，仍作为挂件画出
 */
export const SLD_ROLES = {
  grid: 'source',
  ac_bus: 'bus',
  ac_breaker: 'passthrough',
  transformer: 'coupling',
  split_transformer: 'coupling',
  ac_meter: 'tap',
  load: 'tap',
  svg: 'tap',
  pcs: 'feeder',
  pv_unit: 'feeder',
  pv_pcs: 'feeder',
  pv_array: 'dc',
  bms: 'dc',
  dc_bus: 'dc',
  emu: 'virtual',
  emu_group: 'virtual',
  pvu: 'virtual'
}

export function sldRole(templateId) {
  return SLD_ROLES[templateId] || 'unknown'
}

export function isTransformerLike(templateId) {
  return sldRole(templateId) === 'coupling'
}

export function isSplitTransformer(templateId) {
  return templateId === 'split_transformer'
}

/** 端口 id → 绕组名。同一绕组的三相合成一条下游。未登记的端口不归入任何绕组。 */
const PHASES = ['a', 'b', 'c']

function windingPortMap(names) {
  const map = {}
  for (const name of names) {
    for (const phase of PHASES) map[`${name}_${phase}`] = name
  }
  return map
}

export const SLD_WINDINGS = {
  transformer: windingPortMap(['pri', 'sec']),
  split_transformer: windingPortMap(['pri', 'ear_l', 'ear_r'])
}

export function sldWinding(templateId, portId) {
  if (!templateId || portId == null || portId === '') return ''
  return SLD_WINDINGS[templateId]?.[portId] || ''
}

/** 除进来的那组绕组以外的绕组名，顺序与登记顺序一致。未知模板返回空数组。 */
export function sldOtherWindings(templateId, incomingWinding) {
  const map = SLD_WINDINGS[templateId]
  if (!map) return []
  const names = []
  for (const name of Object.values(map)) {
    if (!names.includes(name)) names.push(name)
  }
  if (!incomingWinding) return names
  return names.filter(name => name !== incomingWinding)
}

/**
 * 一次图里一台设备自己怎么画。组织遍历只拿这个名字，不在遍历里按模板写分支。
 * - pvLegend: 光伏单元图例（单元断、箱变卡片、方阵 A/B）
 * - pcsChain: 储能链（PCS → 直流母线 → BMS）
 */
export const SLD_DRAW = {
  pv_unit: 'pvLegend',
  pcs: 'pcsChain',
  pv_pcs: 'pvChain'
}

export function sldDraw(templateId) {
  if (!templateId) return ''
  return SLD_DRAW[templateId] || ''
}

export function templateColor(templateId) {
  switch (templateId) {
    case 'grid': return '#c0392b'
    case 'ac_bus': return '#1a1a1a'
    case 'ac_breaker': return '#e67e22'
    case 'transformer':
    case 'split_transformer':
      return '#2980b9'
    case 'ac_meter': return '#8e44ad'
    case 'load': return '#c0392b'
    case 'svg': return '#3d9a6a'
    case 'emu': return '#16a085'
    case 'emu_group': return '#0f8a9d'
    case 'pcs': return '#0e7a6a'
    case 'pv_unit':
    case 'pv_pcs': return '#e6a817'
    case 'pv_array': return '#f0c14a'
    case 'pvu': return '#b8860b'
    case 'bms': return '#27ae60'
    case 'dc_bus': return '#d35400'
    default: return '#606266'
  }
}
