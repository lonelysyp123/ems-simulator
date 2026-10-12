/** 组态批量选择 / 入库 / 归属：纯函数，供画布与属性面板共用。 */

export const LIBRARY_STRIP_KEYS = ['emuId', 'groupId']

export const ASSIGNABLE_TEMPLATES = new Set(['pcs', 'ac_breaker', 'ac_meter', 'transformer', 'split_transformer'])

export const VIRTUAL_TEMPLATES = new Set(['emu', 'emu_group'])

export const COMPOSITE_SKIP_TEMPLATES = new Set(['emu', 'emu_group', 'grid'])

export function toggleNodeSelection(ids, nodeId) {
  const next = [...ids]
  const i = next.indexOf(nodeId)
  if (i >= 0) next.splice(i, 1)
  else next.push(nodeId)
  return next
}

export function unionIds(current, added) {
  const set = new Set(current)
  const next = [...current]
  for (const id of added) {
    if (set.has(id)) continue
    set.add(id)
    next.push(id)
  }
  return next
}

export function normalizeRect(x0, y0, x1, y1) {
  return {
    x: Math.min(x0, x1),
    y: Math.min(y0, y1),
    w: Math.abs(x1 - x0),
    h: Math.abs(y1 - y0)
  }
}

function rectsIntersect(a, b) {
  return a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y
}

export function nodesIntersectingRect(nodes, rect, sizeOf) {
  if (!rect || rect.w <= 0 || rect.h <= 0) return []
  return (nodes || []).filter(n => {
    const s = sizeOf(n) || { w: 0, h: 0 }
    return rectsIntersect(rect, { x: n.x, y: n.y, w: s.w, h: s.h })
  })
}

export function libraryEligibleNodes(nodes) {
  return (nodes || []).filter(n => n && n.templateId && !VIRTUAL_TEMPLATES.has(n.templateId))
}

export function libraryPayloadFromNode(node, { name, stripKeys = LIBRARY_STRIP_KEYS } = {}) {
  const parameters = { ...(node.parameters || {}) }
  for (const k of stripKeys) delete parameters[k]
  return {
    nodeId: node.id,
    name: name || node.label || '未命名设备',
    templateId: node.templateId,
    parameters
  }
}

export function libraryPayloadsFromNodes(nodes, { namePrefix = '', stripKeys = LIBRARY_STRIP_KEYS } = {}) {
  return libraryEligibleNodes(nodes).map(n => libraryPayloadFromNode(n, {
    name: namePrefix ? `${namePrefix}${n.label || '未命名设备'}` : undefined,
    stripKeys
  }))
}

export function assignableNodes(nodes) {
  return (nodes || []).filter(n => ASSIGNABLE_TEMPLATES.has(n.templateId))
}

export function mixedParam(nodes, key) {
  const values = (nodes || []).map(n => n.parameters?.[key] || '')
  if (!values.length) return { value: '', mixed: false }
  const first = values[0]
  const mixed = values.some(v => v !== first)
  return { value: mixed ? '' : first, mixed }
}

export function applyEmuId(nodes, emuId) {
  const next = emuId || ''
  for (const n of nodes || []) {
    if (!ASSIGNABLE_TEMPLATES.has(n.templateId)) continue
    if (!n.parameters) n.parameters = {}
    n.parameters.emuId = next
    n.parameters.groupId = ''
  }
}

export function applyGroupId(nodes, groupId, allNodes = []) {
  const next = groupId || ''
  const group = next ? (allNodes || []).find(n => n.id === next) : null
  const emuFromGroup = group?.parameters?.emuId || ''
  for (const n of nodes || []) {
    if (!ASSIGNABLE_TEMPLATES.has(n.templateId)) continue
    if (!n.parameters) n.parameters = {}
    n.parameters.groupId = next
    if (emuFromGroup) n.parameters.emuId = emuFromGroup
  }
}

export function isCompositeLibraryItem(item) {
  if (!item) return false
  if (item.kind === 'composite') return true
  return Array.isArray(item.nodes) && item.nodes.length >= 2
}

function cloneParamsStripped(parameters, stripKeys = LIBRARY_STRIP_KEYS) {
  const next = { ...(parameters || {}) }
  for (const k of stripKeys) delete next[k]
  return next
}

export function compositeEligibleNodes(nodes) {
  return (nodes || []).filter(n => n && n.templateId && !COMPOSITE_SKIP_TEMPLATES.has(n.templateId))
}

export function captureComposite(nodes, edges, { name, stripKeys = LIBRARY_STRIP_KEYS } = {}) {
  const eligible = compositeEligibleNodes(nodes)
  if (eligible.length < 2) return null
  const ids = new Set(eligible.map(n => n.id))
  const originX = Math.min(...eligible.map(n => Number(n.x) || 0))
  const originY = Math.min(...eligible.map(n => Number(n.y) || 0))
  const capturedNodes = eligible.map(n => ({
    id: n.id,
    templateId: n.templateId,
    label: n.label || n.templateId,
    x: (Number(n.x) || 0) - originX,
    y: (Number(n.y) || 0) - originY,
    parameters: cloneParamsStripped(n.parameters, stripKeys)
  }))
  const capturedEdges = (edges || [])
    .filter(e => ids.has(e.fromNodeId) && ids.has(e.toNodeId))
    .map(e => ({
      id: e.id,
      fromNodeId: e.fromNodeId,
      fromPortId: e.fromPortId,
      toNodeId: e.toNodeId,
      toPortId: e.toPortId
    }))
  return {
    kind: 'composite',
    name: name || '未命名组合',
    templateId: capturedNodes[0].templateId,
    nodes: capturedNodes,
    edges: capturedEdges
  }
}

export function instantiateComposite(item, { x = 0, y = 0, uid, snap = v => v } = {}) {
  if (!isCompositeLibraryItem(item) || typeof uid !== 'function') return { nodes: [], edges: [] }
  const idMap = {}
  const nodes = (item.nodes || []).map(n => {
    const id = uid()
    idMap[n.id] = id
    return {
      id,
      templateId: n.templateId,
      libraryItemId: item.id || null,
      label: n.label || n.templateId,
      x: snap(x + (Number(n.x) || 0)),
      y: snap(y + (Number(n.y) || 0)),
      parameters: cloneParamsStripped(n.parameters)
    }
  })
  const edges = (item.edges || [])
    .filter(e => idMap[e.fromNodeId] && idMap[e.toNodeId])
    .map(e => ({
      id: uid(),
      fromNodeId: idMap[e.fromNodeId],
      fromPortId: e.fromPortId,
      toNodeId: idMap[e.toNodeId],
      toPortId: e.toPortId
    }))
  return { nodes, edges }
}

export function compositeDropSize(item, sizeOf) {
  if (!isCompositeLibraryItem(item)) return { w: 120, h: 80 }
  const nodes = item.nodes || []
  if (!nodes.length) return { w: 120, h: 80 }
  let maxX = 0
  let maxY = 0
  for (const n of nodes) {
    const s = sizeOf(n) || { w: 0, h: 0 }
    maxX = Math.max(maxX, (Number(n.x) || 0) + s.w)
    maxY = Math.max(maxY, (Number(n.y) || 0) + s.h)
  }
  return { w: maxX, h: maxY }
}
