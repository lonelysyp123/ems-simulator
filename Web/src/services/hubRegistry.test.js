import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { activateHubSubscription, createHubRegistry } from './hubRegistry.js'

describe('hubRegistry', () => {
  it('JoinChannel 失败时仍登记频道和回调，供重连后补订阅', async () => {
    const registry = createHubRegistry()
    const bound = []
    const receive = () => {}

    await activateHubSubscription(registry, {
      channel: 'mainline',
      handlers: { ReceiveMainLine: receive },
      invokeJoin: async () => {
        throw new Error('Cannot send data if the connection is not in the Connected state')
      },
      bindHandler: (method, fn) => bound.push({ method, fn })
    })

    assert.deepEqual(registry.listChannels(), ['mainline'])
    assert.equal(bound.length, 1)
    assert.equal(bound[0].method, 'ReceiveMainLine')
    assert.equal(bound[0].fn, receive)
    const listed = registry.listHandlers()
    assert.equal(listed.length, 1)
    assert.equal(listed[0].method, 'ReceiveMainLine')
    assert.equal(listed[0].fn, receive)
  })

  it('leave 后重连补 Join 不再包含该频道', () => {
    const registry = createHubRegistry()
    registry.addChannel('mainline')
    registry.addChannel('connections')
    registry.removeChannel('mainline')
    assert.deepEqual(registry.listChannels(), ['connections'])
  })

  it('removeHandler 只去掉指定回调', () => {
    const registry = createHubRegistry()
    const a = () => {}
    const b = () => {}
    registry.addHandler('ReceiveMainLine', a)
    registry.addHandler('ReceiveMainLine', b)
    registry.removeHandler('ReceiveMainLine', a)
    const listed = registry.listHandlers()
    assert.equal(listed.length, 1)
    assert.equal(listed[0].fn, b)
  })
})
