/** EMU 虚拟单元树：供右侧检查器与画布高亮共用。 */

import { ASSIGNABLE_TEMPLATES } from './batchEdit.js'

export const BIND_ROLES = [
  { templateId: 'ac_breaker', role: '断路器' },
  { templateId: 'ac_meter', role: '电表' },
  { templateId: 'transformer', role: '变压器' },
  { templateId: 'split_transformer', role: '双耳变压器' }
]

function nodeLabel(n) {
  return n?.label || n?.parameters?.name || n?.id || ''
}

export function emuNodesOf(nodes) {
  return (nodes || []).filter(n => n.templateId === 'emu')
}

export function groupsOfEmu(nodes, emuId) {
  return (nodes || []).filter(n => n.templateId === 'emu_group' && n.parameters?.emuId === emuId)
}

export function devicesOfEmu(nodes, emuId) {
  return (nodes || []).filter(n => ASSIGNABLE_TEMPLATES.has(n.templateId) && n.parameters?.emuId === emuId)
}

export function devicesOfGroup(nodes, emuId, groupId) {
  return (nodes || []).filter(n =>
    ASSIGNABLE_TEMPLATES.has(n.templateId)
    && n.parameters?.emuId === emuId
    && n.parameters?.groupId === groupId
  )
}

function pcsCountOfEmu(nodes, emuId) {
  return (nodes || []).filter(n => n.templateId === 'pcs' && n.parameters?.emuId === emuId).length
}

function pcsCountOfGroup(nodes, groupId) {
  return (nodes || []).filter(n => n.templateId === 'pcs' && n.parameters?.groupId === groupId).length
}

/** 单元级优先；仅组级绑定时也视为已绑定（未绑定返回空串）。 */
export function boundDeviceLabel(nodes, emuId, templateId) {
  const bound = (nodes || []).filter(x => x.templateId === templateId && x.parameters?.emuId === emuId)
  const unitLevel = bound.filter(x => !x.parameters?.groupId)
  const list = unitLevel.length ? unitLevel : bound
  if (!list.length) return ''
  return list.map(nodeLabel).join('、')
}

function preferredBound(nodes, emuId, templateId) {
  const bound = (nodes || []).filter(x => x.templateId === templateId && x.parameters?.emuId === emuId)
  const unitLevel = bound.filter(x => !x.parameters?.groupId)
  return unitLevel.length ? unitLevel : bound
}

export function buildEmuTree(nodes) {
  const list = nodes || []
  return emuNodesOf(list).map(emu => ({
    id: emu.id,
    kind: 'emu',
    label: nodeLabel(emu),
    node: emu,
    pcsCount: pcsCountOfEmu(list, emu.id),
    groups: groupsOfEmu(list, emu.id).map(g => ({
      id: g.id,
      kind: 'group',
      label: nodeLabel(g),
      node: g,
      pcsCount: pcsCountOfGroup(list, g.id)
    })),
    bindings: BIND_ROLES.map(({ templateId, role }) => {
      const preferred = preferredBound(list, emu.id, templateId)
      return {
        templateId,
        role,
        nodes: preferred,
        label: preferred.length ? preferred.map(nodeLabel).join('、') : ''
      }
    })
  }))
}

/** 当前选中为 EMU 或分组时，画布应高亮的实物设备 id。 */
export function highlightIdsForSelection(nodes, selectedId) {
  if (!selectedId) return []
  const list = nodes || []
  const selected = list.find(n => n.id === selectedId)
  if (!selected) return []
  if (selected.templateId === 'emu')
    return devicesOfEmu(list, selected.id).map(n => n.id)
  if (selected.templateId === 'emu_group')
    return devicesOfGroup(list, selected.parameters?.emuId, selected.id).map(n => n.id)
  return []
}
