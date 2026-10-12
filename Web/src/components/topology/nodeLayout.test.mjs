import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import {
  formatVoltage,
  isSplitTransformer,
  isTransformerLike,
  nodeSize,
  portPosition,
  sldRole,
  snapToGrid,
  templateColor
} from './nodeLayout.js'

describe('nodeLayout split transformer editor geometry', () => {
  it('gives the dual-ear box a wider canvas than a two-winding transformer', () => {
    assert.deepEqual(nodeSize('transformer'), { w: 100, h: 120 })
    assert.deepEqual(nodeSize('split_transformer'), { w: 160, h: 120 })
    assert.equal(sldRole('split_transformer'), 'coupling')
    assert.equal(sldRole('transformer'), 'coupling')
    assert.ok(isTransformerLike('split_transformer'))
    assert.ok(isSplitTransformer('split_transformer'))
    assert.equal(isSplitTransformer('transformer'), false)
    assert.equal(templateColor('split_transformer'), templateColor('transformer'))
  })

  it('places left and right ear ports on the bottom at distinct x', () => {
    const node = { id: 's1', templateId: 'split_transformer', x: 100, y: 200 }
    const left = portPosition(node, { id: 'ear_l_a', side: 'bottom', offset: 0.15 })
    const right = portPosition(node, { id: 'ear_r_a', side: 'bottom', offset: 0.65 })
    const pri = portPosition(node, { id: 'pri_a', side: 'top', offset: 0.2 })
    assert.equal(left.y, 200 + 120)
    assert.equal(right.y, 200 + 120)
    assert.equal(pri.y, 200)
    assert.ok(left.x < right.x)
    assert.equal(left.x, 100 + 160 * 0.15)
    assert.equal(right.x, 100 + 160 * 0.65)
  })

  it('snaps coordinates and formats voltages used by the inspector', () => {
    assert.equal(snapToGrid(27), 20)
    assert.equal(snapToGrid(31), 40)
    assert.equal(formatVoltage(35000), '35kV')
    assert.equal(formatVoltage(690), '690V')
    assert.equal(formatVoltage(0), '—')
  })
})
