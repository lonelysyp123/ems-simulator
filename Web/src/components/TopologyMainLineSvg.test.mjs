import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createContext, runInContext } from 'node:vm'
import { reactive } from 'vue'

const source = readFileSync(new URL('./TopologyMainLineSvg.vue', import.meta.url), 'utf8')
const start = source.indexOf('const powerDrafts = reactive({})')
const end = source.indexOf('const PvXfmrCard =', start)
assert.ok(start >= 0 && end > start)

function createDrafts() {
  const context = createContext({ reactive })
  runInContext(source.slice(start, end), context)
  return context
}

const pcs1 = { pcsNumber: 1, compartmentNumber: 1, slotInUnit: 0 }
const pv1 = { pvNumber: 1 }

describe('TopologyMainLineSvg input drafts', () => {
  for (const kind of ['p', 'q']) {
    it(`${kind}: initializes same-number PCS and PV from their own targets`, () => {
      const { getDraft, draftKey } = createDrafts()
      assert.notEqual(draftKey(pcs1, kind), draftKey(pv1, kind))
      assert.equal(getDraft(pcs1, kind, 100), '100.0')
      assert.equal(getDraft(pv1, kind, 2000), '2000.0')
    })

    it(`${kind}: isolates edits in both directions, including negative and empty input`, () => {
      const { getDraft, setDraft } = createDrafts()
      getDraft(pv1, kind, 2000)
      setDraft(pcs1, kind, '-123.5')
      assert.equal(getDraft(pv1, kind, 2000), '2000.0')
      setDraft(pv1, kind, '345.6')
      assert.equal(getDraft(pcs1, kind, 100), '-123.5')
      setDraft(pcs1, kind, '')
      assert.equal(getDraft(pv1, kind, 2000), '345.6')
      assert.equal(getDraft(pcs1, kind, 100), '')
    })
  }

  it('isolates device numbers and active/reactive drafts', () => {
    const { getDraft, setDraft } = createDrafts()
    setDraft(pcs1, 'p', '123')
    setDraft(pv1, 'p', '456')
    assert.equal(getDraft({ pcsNumber: 2 }, 'p', 200), '200.0')
    assert.equal(getDraft({ pvNumber: 2 }, 'p', 3000), '3000.0')
    assert.equal(getDraft(pcs1, 'q', 10), '10.0')
    assert.equal(getDraft(pv1, 'q', 20), '20.0')
  })

  it('keeps PV array sides and BMS SOC drafts independent', () => {
    const { getDraft, setDraft } = createDrafts()
    const arrayA = { pvNumber: 1, side: 'A' }
    const arrayB = { pvNumber: 1, side: 'B' }
    setDraft(arrayA, 't', '35')
    assert.equal(getDraft(arrayB, 't', 25), '25.0')
    assert.equal(getDraft(arrayA, 'ang', 90), '90.0')
    assert.equal(getDraft(arrayA, 'alb', 0), '0.0')
    setDraft(pcs1, 'soc', '60')
    assert.equal(getDraft(pcs1, 'p', 100), '100.0')
    assert.equal(getDraft(pv1, 'p', 2000), '2000.0')
    assert.equal(getDraft({ pcsNumber: 2, compartmentNumber: 2 }, 'soc', 50), '50.0')
  })

  it('preserves each device draft when a new snapshot arrives', () => {
    const { getDraft, setDraft } = createDrafts()
    setDraft(pcs1, 'p', '123')
    setDraft(pv1, 'p', '456')
    assert.equal(getDraft({ ...pcs1 }, 'p', 789), '123')
    assert.equal(getDraft({ ...pv1 }, 'p', 987), '456')
  })
})
