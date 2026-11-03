/** 与端口计划相同的顺序：画布 Y、X、节点 id。编号 MV1 对应 simMv1。 */
export function mvBreakerCodeMap(nodes) {
  const list = (nodes || []).filter(n => n.templateId === 'ac_breaker')
  list.sort((a, b) => {
    const dy = (Number(a.y) || 0) - (Number(b.y) || 0)
    if (dy) return dy
    const dx = (Number(a.x) || 0) - (Number(b.x) || 0)
    if (dx) return dx
    const ia = String(a.id ?? '')
    const ib = String(b.id ?? '')
    return ia < ib ? -1 : ia > ib ? 1 : 0
  })
  const map = {}
  list.forEach((n, i) => { map[n.id] = `MV${i + 1}` })
  return map
}

/** 协议合闸覆盖。没有该节点时返回 null，调用方继续用组态静态参数。 */
export function protocolBreakerClosed(snap, nodeId) {
  const map = snap?.mvBreakerClosed
  if (!map || nodeId == null || !Object.prototype.hasOwnProperty.call(map, nodeId))
    return null
  return !!map[nodeId]
}
