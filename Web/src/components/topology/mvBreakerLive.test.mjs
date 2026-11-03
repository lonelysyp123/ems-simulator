import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { mvBreakerCodeMap, protocolBreakerClosed } from './mvBreakerLive.js'

describe('protocol breaker closed', () => {
  it('returns null when the node has no protocol flag', () => {
    assert.equal(protocolBreakerClosed({}, 'b1'), null)
    assert.equal(protocolBreakerClosed({ mvBreakerClosed: { b2: false } }, 'b1'), null)
  })

  it('numbers breakers in the same order as simMv ports', () => {
    const nodes = [
      { id: 'late', templateId: 'ac_breaker', x: 0, y: 30 },
      { id: 'main', templateId: 'ac_breaker', x: 80, y: 0 },
      { id: 'tie-b', templateId: 'ac_breaker', x: 0, y: 40 },
      { id: 'unit', templateId: 'ac_breaker', x: 0, y: 10 },
      { id: 'tie-a', templateId: 'ac_breaker', x: 0, y: 40 },
      { id: 'pcs', templateId: 'pcs', x: 0, y: 0 }
    ]
    assert.deepEqual(mvBreakerCodeMap(nodes), {
      main: 'MV1',
      unit: 'MV2',
      late: 'MV3',
      'tie-a': 'MV4',
      'tie-b': 'MV5'
    })
  })

  it('returns the protocol flag for that node', () => {
    const snap = { mvBreakerClosed: { pv: false, other: true } }
    assert.equal(protocolBreakerClosed(snap, 'pv'), false)
    assert.equal(protocolBreakerClosed(snap, 'other'), true)
  })
})
