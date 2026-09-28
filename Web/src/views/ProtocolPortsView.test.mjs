import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const source = readFileSync(new URL('./ProtocolPortsView.vue', import.meta.url), 'utf8')
const start = source.indexOf('const TYPE_LABELS = {')
const end = source.indexOf('function typeLabel', start)
const labels = source.slice(start, end)

describe('ProtocolPortsView type labels', () => {
  it('names the photovoltaic inverter enum value', () => {
    assert.match(labels, /7:\s*'光伏逆变器'/)
    assert.match(labels, /4:\s*'光伏 Logger'/)
    assert.match(labels, /5:\s*'光伏电表'/)
  })
})
