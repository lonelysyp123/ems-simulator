import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import {
  boundDeviceLabel,
  buildEmuTree,
  highlightIdsForSelection
} from './emuTree.js'

function node(id, templateId, extras = {}) {
  return {
    id,
    templateId,
    label: extras.label || id,
    parameters: { ...(extras.parameters || {}) }
  }
}

function plant() {
  return [
    node('e1', 'emu', { label: 'EMU-1' }),
    node('g1', 'emu_group', { label: '分组A', parameters: { emuId: 'e1' } }),
    node('g2', 'emu_group', { label: '分组B', parameters: { emuId: 'e1' } }),
    node('p1', 'pcs', { label: 'PCS-1', parameters: { emuId: 'e1', groupId: 'g1' } }),
    node('p2', 'pcs', { label: 'PCS-2', parameters: { emuId: 'e1', groupId: 'g1' } }),
    node('p3', 'pcs', { label: 'PCS-3', parameters: { emuId: 'e1', groupId: 'g2' } }),
    node('cb1', 'ac_breaker', { label: 'CB-1', parameters: { emuId: 'e1' } }),
    node('m1', 'ac_meter', { label: 'M-1', parameters: { emuId: 'e1', groupId: 'g1' } }),
    node('t1', 'transformer', { label: 'T-1', parameters: { emuId: 'e1' } }),
    node('s1', 'split_transformer', { label: '双耳1', parameters: { emuId: 'e1' } }),
    node('grid', 'grid', { label: '电网' })
  ]
}

describe('buildEmuTree', () => {
  it('builds emu → groups → bindings with counts', () => {
    const tree = buildEmuTree(plant())
    assert.equal(tree.length, 1)
    const emu = tree[0]
    assert.equal(emu.id, 'e1')
    assert.equal(emu.pcsCount, 3)
    assert.deepEqual(emu.groups.map(g => [g.id, g.pcsCount]), [['g1', 2], ['g2', 1]])
    const byRole = Object.fromEntries(emu.bindings.map(b => [b.role, b.label]))
    assert.equal(byRole['断路器'], 'CB-1')
    assert.equal(byRole['电表'], 'M-1')
    assert.equal(byRole['变压器'], 'T-1')
    assert.equal(byRole['双耳变压器'], '双耳1')
  })

  it('prefers unit-level breaker over group-only binding', () => {
    const nodes = [
      node('e1', 'emu', { label: 'EMU-1' }),
      node('g1', 'emu_group', { label: '组', parameters: { emuId: 'e1' } }),
      node('unitCb', 'ac_breaker', { label: '单元断路器', parameters: { emuId: 'e1' } }),
      node('groupCb', 'ac_breaker', { label: '组断路器', parameters: { emuId: 'e1', groupId: 'g1' } })
    ]
    assert.equal(boundDeviceLabel(nodes, 'e1', 'ac_breaker'), '单元断路器')
    const breaker = buildEmuTree(nodes)[0].bindings.find(b => b.templateId === 'ac_breaker')
    assert.deepEqual(breaker.nodes.map(n => n.id), ['unitCb'])
  })

  it('treats group-only binding as bound', () => {
    const nodes = [
      node('e1', 'emu', { label: 'EMU-1' }),
      node('g1', 'emu_group', { label: '组', parameters: { emuId: 'e1' } }),
      node('groupCb', 'ac_breaker', { label: '组断路器', parameters: { emuId: 'e1', groupId: 'g1' } })
    ]
    assert.equal(boundDeviceLabel(nodes, 'e1', 'ac_breaker'), '组断路器')
  })

  it('returns an empty tree when there is no EMU', () => {
    assert.deepEqual(buildEmuTree([node('pcs1', 'pcs')]), [])
  })
})

describe('highlightIdsForSelection', () => {
  it('highlights every assignable device of the selected EMU', () => {
    const ids = highlightIdsForSelection(plant(), 'e1')
    assert.deepEqual(ids.sort(), ['cb1', 'm1', 'p1', 'p2', 'p3', 's1', 't1'])
  })

  it('highlights only devices of the selected group', () => {
    const ids = highlightIdsForSelection(plant(), 'g1')
    assert.deepEqual(ids.sort(), ['m1', 'p1', 'p2'])
  })

  it('returns empty for a normal canvas node or missing id', () => {
    assert.deepEqual(highlightIdsForSelection(plant(), 'p1'), [])
    assert.deepEqual(highlightIdsForSelection(plant(), null), [])
    assert.deepEqual(highlightIdsForSelection([], 'e1'), [])
  })
})
