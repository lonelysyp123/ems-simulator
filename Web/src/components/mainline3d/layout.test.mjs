import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { computeLayout, channelX, pvChannelX, stationKey, UNIT_SPACING } from './layout.js'

describe('computeLayout mixed ess/pv station', () => {
  it('places ess then pv along the 35kV bus and keeps backbone at first feeder', () => {
    const layout = computeLayout({ essCount: 2, pvCount: 3 })
    assert.equal(layout.essCount, 2)
    assert.equal(layout.pvCount, 3)
    assert.equal(layout.unitXs.length, 2)
    assert.equal(layout.pvXs.length, 3)
    assert.equal(layout.unitXs[0], 0)
    assert.equal(layout.unitXs[1], UNIT_SPACING)
    assert.equal(layout.pvXs[0], UNIT_SPACING * 2)
    assert.equal(layout.pvXs[2], UNIT_SPACING * 4)
    assert.equal(layout.mainX, 0)
    assert.ok(layout.busEndX > layout.pvXs[2])
  })

  it('builds a pv-only plant without fabricating ess slots', () => {
    const layout = computeLayout({ essCount: 0, pvCount: 2 })
    assert.equal(layout.essCount, 0)
    assert.deepEqual(layout.unitXs, [])
    assert.equal(layout.pvXs.length, 2)
    assert.equal(layout.pvXs[0], 0)
    assert.equal(layout.mainX, 0)
  })

  it('accepts legacy numeric ess-only argument', () => {
    const layout = computeLayout(4)
    assert.equal(layout.essCount, 4)
    assert.equal(layout.pvCount, 0)
    assert.equal(layout.unitXs.length, 4)
    assert.deepEqual(layout.pvXs, [])
  })

  it('uses a wider pv channel offset than ess pcs/bms', () => {
    assert.equal(channelX(0, 'A'), -5.5)
    assert.equal(channelX(0, 'B'), 5.5)
    assert.ok(Math.abs(pvChannelX(0, 'A')) > Math.abs(channelX(0, 'A')))
    assert.equal(pvChannelX(10, 'A') + pvChannelX(10, 'B'), 20)
  })
})

describe('stationKey', () => {
  it('changes when pv unit count changes so the 3d scene rebuilds', () => {
    assert.equal(stationKey({ units: [{}, {}], pvUnits: [] }), 's:2:0')
    assert.equal(stationKey({ units: [], pvUnits: [{}, {}, {}] }), 's:0:3')
    assert.notEqual(
      stationKey({ units: [], pvUnits: [{}] }),
      stationKey({ units: [], pvUnits: [{}, {}] })
    )
  })
})
