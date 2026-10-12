import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { resolveFlow, FLOW } from './powerFlow.js'

describe('resolveFlow pv export', () => {
  it('treats positive pv power as discharge toward the grid', () => {
    const flow = resolveFlow(320, { energized: true })
    assert.equal(flow.mode, FLOW.DISCHARGE)
    assert.equal(flow.direction, -1)
    assert.equal(flow.live, true)
  })
})
