/** 光伏虚拟单元树：供右侧列表与归属下拉共用。 */

function nodeLabel(n) {
  return n?.label || n?.parameters?.name || n?.id || ''
}

/** 编号从 1 起。缺省或小于 1 视为未编号。 */
export function pvuUnitNumber(node) {
  const value = Number(node?.parameters?.unitNumber)
  if (!Number.isFinite(value) || value < 1) return Number.POSITIVE_INFINITY
  return Math.round(value)
}

/** 与运行时端口次序一致：先编号，再从上到下、从左到右。 */
export function comparePvu(a, b) {
  const byNumber = pvuUnitNumber(a) - pvuUnitNumber(b)
  if (byNumber) return byNumber
  return (a.y - b.y) || (a.x - b.x) || String(a.id).localeCompare(String(b.id))
}

export function pvuNodesOf(nodes) {
  return (nodes || []).filter(n => n.templateId === 'pvu').sort(comparePvu)
}

export function branchesOfPvu(nodes, pvuId) {
  return (nodes || []).filter(n => n.templateId === 'pv_pcs' && n.parameters?.pvuId === pvuId)
}

export function transformerOfPvu(nodes, pvuId) {
  return (nodes || []).find(n => n.templateId === 'transformer' && n.parameters?.pvuId === pvuId) || null
}

export function buildPvuTree(nodes) {
  const list = nodes || []
  return pvuNodesOf(list).map(unit => {
    const branches = branchesOfPvu(list, unit.id)
    const transformer = transformerOfPvu(list, unit.id)
    const number = pvuUnitNumber(unit)
    const name = nodeLabel(unit)
    return {
      id: unit.id,
      label: Number.isFinite(number) ? `编号 ${number} · ${name}` : name,
      node: unit,
      branchCount: branches.length,
      transformer: transformer
        ? { id: transformer.id, label: nodeLabel(transformer), node: transformer }
        : null,
      branches: branches.map(n => ({ id: n.id, label: nodeLabel(n), node: n }))
    }
  })
}
