import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import {
  toggleNodeSelection,
  unionIds,
  normalizeRect,
  nodesIntersectingRect,
  libraryEligibleNodes,
  libraryPayloadsFromNodes,
  assignableNodes,
  mixedParam,
  applyEmuId,
  applyGroupId,
  captureComposite,
  instantiateComposite,
  isCompositeLibraryItem,
  compositeDropSize
} from './batchEdit.js'

function node(id, templateId, extras = {}) {
  return {
    id,
    templateId,
    label: extras.label || id,
    x: extras.x ?? 0,
    y: extras.y ?? 0,
    parameters: { ...(extras.parameters || {}) }
  }
}

describe('toggleNodeSelection', () => {
  it('adds a node that was not selected', () => {
    assert.deepEqual(toggleNodeSelection(['a'], 'b'), ['a', 'b'])
  })

  it('removes a node that was already selected', () => {
    assert.deepEqual(toggleNodeSelection(['a', 'b'], 'a'), ['b'])
  })
})

describe('unionIds', () => {
  it('merges without duplicates and keeps existing order', () => {
    assert.deepEqual(unionIds(['a', 'b'], ['b', 'c']), ['a', 'b', 'c'])
  })
})

describe('nodesIntersectingRect', () => {
  it('keeps nodes whose box overlaps the marquee', () => {
    const nodes = [
      node('in', 'pcs', { x: 10, y: 10 }),
      node('out', 'pcs', { x: 400, y: 400 })
    ]
    const hit = nodesIntersectingRect(
      nodes,
      normalizeRect(0, 0, 80, 80),
      () => ({ w: 40, h: 40 })
    )
    assert.deepEqual(hit.map(n => n.id), ['in'])
  })

  it('normalizes inverted drag (bottom-right to top-left)', () => {
    const rect = normalizeRect(80, 80, 0, 0)
    assert.deepEqual(rect, { x: 0, y: 0, w: 80, h: 80 })
  })
})

describe('libraryPayloadsFromNodes', () => {
  it('skips virtual EMU nodes and strips instance binding', () => {
    const payloads = libraryPayloadsFromNodes([
      node('e1', 'emu', { label: '单元1', parameters: { name: 'EMU' } }),
      node('g1', 'emu_group', { label: '组1' }),
      node('p1', 'pcs', {
        label: 'PCS-1',
        parameters: { emuId: 'e1', groupId: 'g1', pcsRatedPowerKw: 1725 }
      }),
      node('b1', 'ac_breaker', {
        label: '断路器',
        parameters: { emuId: 'e1', ratedVoltage: 35000 }
      })
    ])
    assert.equal(payloads.length, 2)
    assert.equal(payloads[0].nodeId, 'p1')
    assert.equal(payloads[0].name, 'PCS-1')
    assert.equal(payloads[0].templateId, 'pcs')
    assert.equal(payloads[0].parameters.pcsRatedPowerKw, 1725)
    assert.equal(payloads[0].parameters.emuId, undefined)
    assert.equal(payloads[0].parameters.groupId, undefined)
    assert.equal(payloads[1].nodeId, 'b1')
    assert.equal(payloads[1].parameters.ratedVoltage, 35000)
    assert.equal(payloads[1].parameters.emuId, undefined)
  })

  it('does not mutate the source node parameters', () => {
    const n = node('p1', 'pcs', { parameters: { emuId: 'e1', pcsRatedPowerKw: 100 } })
    libraryPayloadsFromNodes([n])
    assert.equal(n.parameters.emuId, 'e1')
    assert.equal(n.parameters.pcsRatedPowerKw, 100)
  })

  it('applies a name prefix when saving a batch', () => {
    const payloads = libraryPayloadsFromNodes(
      [node('p1', 'pcs', { label: 'PCS-1' })],
      { namePrefix: '1250kW-' }
    )
    assert.equal(payloads[0].name, '1250kW-PCS-1')
  })

  it('libraryEligibleNodes matches payloads', () => {
    const nodes = [node('e1', 'emu'), node('p1', 'pcs'), node('grid', 'grid')]
    assert.deepEqual(libraryEligibleNodes(nodes).map(n => n.id), ['p1', 'grid'])
  })
})

describe('batch unit/group assignment', () => {
  it('assignableNodes only keeps PCS / breaker / meter / transformer / split transformer', () => {
    const nodes = [
      node('p1', 'pcs'),
      node('br', 'ac_breaker'),
      node('m', 'ac_meter'),
      node('xf', 'transformer'),
      node('split', 'split_transformer'),
      node('grid', 'grid'),
      node('bus', 'ac_bus')
    ]
    assert.deepEqual(assignableNodes(nodes).map(n => n.id), ['p1', 'br', 'm', 'xf', 'split'])
  })

  it('mixedParam reports a common value or mixed', () => {
    const same = [node('a', 'pcs', { parameters: { emuId: 'e1' } }), node('b', 'pcs', { parameters: { emuId: 'e1' } })]
    assert.deepEqual(mixedParam(same, 'emuId'), { value: 'e1', mixed: false })
    const mixed = [node('a', 'pcs', { parameters: { emuId: 'e1' } }), node('b', 'pcs', { parameters: { emuId: 'e2' } })]
    assert.deepEqual(mixedParam(mixed, 'emuId'), { value: '', mixed: true })
  })

  it('applyEmuId writes emuId on split transformers and clears groupId', () => {
    const split = node('s1', 'split_transformer', { parameters: { emuId: 'old', groupId: 'g1' } })
    applyEmuId([split], 'e2')
    assert.equal(split.parameters.emuId, 'e2')
    assert.equal(split.parameters.groupId, '')
  })

  it('applyEmuId writes emuId and clears groupId on assignable nodes only', () => {
    const pcs = node('p1', 'pcs', { parameters: { emuId: 'old', groupId: 'g1' } })
    const grid = node('grid', 'grid', { parameters: { name: '电网' } })
    applyEmuId([pcs, grid], 'e2')
    assert.equal(pcs.parameters.emuId, 'e2')
    assert.equal(pcs.parameters.groupId, '')
    assert.equal(grid.parameters.emuId, undefined)
  })

  it('applyGroupId writes groupId and copies the group emuId', () => {
    const pcs = node('p1', 'pcs', { parameters: { emuId: '', groupId: '' } })
    const group = node('g1', 'emu_group', { parameters: { emuId: 'e1' } })
    applyGroupId([pcs], 'g1', [pcs, group])
    assert.equal(pcs.parameters.groupId, 'g1')
    assert.equal(pcs.parameters.emuId, 'e1')
  })

  it('clearing groupId does not change emuId', () => {
    const pcs = node('p1', 'pcs', { parameters: { emuId: 'e1', groupId: 'g1' } })
    applyGroupId([pcs], '', [])
    assert.equal(pcs.parameters.groupId, '')
    assert.equal(pcs.parameters.emuId, 'e1')
  })
})

describe('captureComposite / instantiateComposite', () => {
  const pcsA = () => node('pA', 'pcs', {
    label: 'PCS-A',
    x: 200,
    y: 400,
    parameters: { emuId: 'e1', groupId: 'g1', pcsRatedPowerKw: 1725 }
  })
  const pcsB = () => node('pB', 'pcs', {
    label: 'PCS-B',
    x: 360,
    y: 400,
    parameters: { emuId: 'e1', pcsRatedPowerKw: 1725 }
  })
  const bms = () => node('bmsA', 'bms', { label: 'BMS-A', x: 200, y: 560 })
  const emu = () => node('e1', 'emu', { label: 'EMU-1', x: 0, y: 0 })
  const internal = { id: 'e-dc', fromNodeId: 'pA', fromPortId: 'dc_pos', toNodeId: 'bmsA', toPortId: 'dc_pos' }
  const toBus = { id: 'e-ac', fromNodeId: 'pA', fromPortId: 'ac_a', toNodeId: 'bus', toPortId: 'a2' }

  it('returns null when fewer than two drawable devices', () => {
    assert.equal(captureComposite([pcsA(), emu()], []), null)
  })

  it('omits the station grid from a composite', () => {
    const item = captureComposite(
      [pcsA(), bms(), node('g', 'grid', { label: '电网', x: 0, y: 0 })],
      []
    )
    assert.equal(item.nodes.length, 2)
    assert.ok(!item.nodes.some(n => n.templateId === 'grid'))
  })

  it('stores relative positions, internal edges only, and strips unit/group ids', () => {
    const item = captureComposite(
      [pcsA(), pcsB(), bms(), emu()],
      [internal, toBus],
      { name: '5.5MW 馈线' }
    )
    assert.equal(item.kind, 'composite')
    assert.equal(item.name, '5.5MW 馈线')
    assert.equal(item.nodes.length, 3)
    assert.deepEqual(item.nodes.map(n => n.id), ['pA', 'pB', 'bmsA'])
    assert.equal(item.nodes[0].x, 0)
    assert.equal(item.nodes[0].y, 0)
    assert.equal(item.nodes[1].x, 160)
    assert.equal(item.nodes[2].y, 160)
    assert.equal(item.nodes[0].parameters.pcsRatedPowerKw, 1725)
    assert.equal(item.nodes[0].parameters.emuId, undefined)
    assert.equal(item.nodes[0].parameters.groupId, undefined)
    assert.equal(item.edges.length, 1)
    assert.equal(item.edges[0].fromNodeId, 'pA')
    assert.equal(item.edges[0].toNodeId, 'bmsA')
    assert.ok(isCompositeLibraryItem(item))
    assert.equal(isCompositeLibraryItem({ templateId: 'pcs', parameters: {} }), false)
  })

  it('instantiateComposite remaps ids, keeps layout, and does not reuse edge ids', () => {
    const item = captureComposite([pcsA(), bms()], [internal], { name: '支路' })
    item.id = 'lib-1'
    let n = 0
    const uid = () => `n${++n}`
    const first = instantiateComposite(item, { x: 100, y: 50, uid, snap: v => v })
    assert.equal(first.nodes.length, 2)
    assert.equal(first.edges.length, 1)
    assert.equal(first.nodes[0].id, 'n1')
    assert.equal(first.nodes[0].x, 100)
    assert.equal(first.nodes[0].y, 50)
    assert.equal(first.nodes[1].x, 100)
    assert.equal(first.nodes[1].y, 210)
    assert.equal(first.nodes[0].libraryItemId, 'lib-1')
    assert.equal(first.edges[0].fromNodeId, 'n1')
    assert.equal(first.edges[0].toNodeId, 'n2')
    assert.equal(first.edges[0].fromPortId, 'dc_pos')
    assert.notEqual(first.edges[0].id, 'e-dc')

    const second = instantiateComposite(item, { x: 400, y: 50, uid, snap: v => v })
    assert.equal(second.nodes[0].id, 'n4')
    assert.notEqual(second.nodes[0].id, first.nodes[0].id)
    assert.equal(second.nodes[0].x, 400)
  })

  it('compositeDropSize is the bounding box of relative nodes', () => {
    const item = captureComposite(
      [node('a', 'pcs', { x: 10, y: 20 }), node('b', 'pcs', { x: 130, y: 20 })],
      []
    )
    const size = compositeDropSize(item, () => ({ w: 120, h: 96 }))
    assert.equal(size.w, 240)
    assert.equal(size.h, 96)
  })
})
