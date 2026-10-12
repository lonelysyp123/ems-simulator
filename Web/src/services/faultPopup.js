/** 从告警快照抽出 PCS / BMS 当前已触发的故障位。 */
export function collectActiveFaults(snap) {
  const items = []
  for (const d of snap?.devices || []) {
    const type = d.deviceType || ''
    if (type !== 'pcs' && !type.startsWith('bms'))
      continue
    for (const f of d.flags || []) {
      if (!f.active || f.kind !== 'fault')
        continue
      items.push({
        key: `${d.deviceId}:${f.name}`,
        deviceId: d.deviceId,
        deviceType: type,
        title: d.title || d.deviceId,
        name: f.name,
        label: f.label || f.name
      })
    }
  }
  return items
}

/** 相对上一拍新出现的故障（上升沿）。首次 prev 为空时返回空，避免刷新页面立刻弹窗。 */
export function diffNewFaults(prevSnap, nextSnap) {
  if (!prevSnap)
    return []
  const prev = new Set(collectActiveFaults(prevSnap).map(x => x.key))
  return collectActiveFaults(nextSnap).filter(x => !prev.has(x.key))
}

export function mergeFaultItems(existing, incoming) {
  const byKey = new Map((existing || []).map(x => [x.key, x]))
  for (const item of incoming || []) {
    if (!byKey.has(item.key))
      byKey.set(item.key, item)
  }
  return [...byKey.values()]
}
