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
 * - coupling: 变压器等绕组设备（沿其余已接线母线分叉为 N 路下游，不限两绕组）
 * - feeder: 发电支路（PCS / 光伏），在母线下展开
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
  bms: 'dc',
  dc_bus: 'dc',
  emu: 'virtual',
  emu_group: 'virtual'
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
    case 'pv_unit': return '#e6a817'
    case 'bms': return '#27ae60'
    case 'dc_bus': return '#d35400'
    default: return '#606266'
  }
}
