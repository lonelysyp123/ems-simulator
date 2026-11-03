import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { buildPvuTree } from './pvuTree.js'

function node(id, templateId, label, parameters = {}) {
  return { id, templateId, label, parameters }
}

describe('buildPvuTree', () => {
  it('lists a photovoltaic unit and its two branches', () => {
    const tree = buildPvuTree([
      node('u1', 'pvu', '光伏单元-1'),
      node('c1', 'pv_pcs', '光伏PCS-1', { pvuId: 'u1' }),
      node('c2', 'pv_pcs', '光伏PCS-2', { pvuId: 'u1' }),
      node('e1', 'emu', 'EMU-1'),
      node('p1', 'pcs', 'PCS-1', { emuId: 'e1' })
    ])
    assert.equal(tree.length, 1)
    assert.equal(tree[0].id, 'u1')
    assert.equal(tree[0].label, '光伏单元-1')
    assert.equal(tree[0].branchCount, 2)
    assert.equal(tree[0].transformer, null)
    assert.deepEqual(tree[0].branches.map(b => b.id), ['c1', 'c2'])
  })

  it('lists the transformer bound to the photovoltaic unit', () => {
    const tree = buildPvuTree([
      node('u1', 'pvu', '光伏单元-1'),
      node('xf', 'transformer', '光伏箱变', { pvuId: 'u1', primaryVoltage: 35000, secondaryVoltage: 690 }),
      node('c1', 'pv_pcs', '光伏PCS-1', { pvuId: 'u1' }),
      node('other', 'transformer', '主变', { primaryVoltage: 220000 })
    ])
    assert.equal(tree[0].transformer.id, 'xf')
    assert.equal(tree[0].transformer.label, '光伏箱变')
  })
})
