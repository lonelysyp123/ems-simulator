import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { nodeSize, sldDraw, sldOtherWindings, sldRole, sldWinding } from './nodeLayout.js'

describe('sld windings', () => {
  it('groups transformer phases into pri and sec', () => {
    assert.equal(sldWinding('transformer', 'pri_b'), 'pri')
    assert.equal(sldWinding('transformer', 'sec_c'), 'sec')
    assert.deepEqual(sldOtherWindings('transformer', 'pri'), ['sec'])
    assert.deepEqual(sldOtherWindings('transformer', 'sec'), ['pri'])
  })

  it('groups a split transformer into primary and two ears', () => {
    assert.equal(sldWinding('split_transformer', 'ear_l_a'), 'ear_l')
    assert.equal(sldWinding('split_transformer', 'ear_r_c'), 'ear_r')
    assert.deepEqual(sldOtherWindings('split_transformer', 'pri'), ['ear_l', 'ear_r'])
  })

  it('returns empty for unknown templates and ports', () => {
    assert.equal(sldWinding('transformer', 'ac_a'), '')
    assert.equal(sldWinding('pv_unit', 'ac_a'), '')
    assert.equal(sldWinding('', null), '')
    assert.equal(sldWinding('transformer', ''), '')
    assert.deepEqual(sldOtherWindings('pv_unit', 'pri'), [])
    assert.deepEqual(sldOtherWindings('nope'), [])
  })

  it('names a draw scheme per device and leaves other templates unnamed', () => {
    assert.equal(sldDraw('pv_unit'), 'pvLegend')
    assert.equal(sldDraw('pcs'), 'pcsChain')
    assert.equal(sldDraw('pv_pcs'), 'pvChain')
    assert.equal(nodeSize('pv_pcs').w, 120)
    assert.equal(nodeSize('pv_array').h, 72)
    assert.deepEqual(nodeSize('pvu'), { w: 0, h: 0 })
    assert.equal(sldRole('pvu'), 'virtual')
    assert.equal(sldRole('pv_pcs'), 'feeder')
    assert.equal(sldRole('pv_array'), 'dc')
    assert.equal(sldDraw('ac_bus'), '')
    assert.equal(sldDraw(''), '')
    assert.equal(sldDraw(undefined), '')
  })
})
