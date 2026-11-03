import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createContext, runInContext } from 'node:vm'
import { reactive } from 'vue'
import { buildTopologyMainLineLayout } from './topology/topologyMainLineLayout.js'

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

  it('shows photovoltaic pcs and array titles from the pcs chain', () => {
    assert.match(source, /\{\{ c\.title \}\}/)
    assert.match(source, /c\.tone === 'pvArray'/)
    assert.match(source, /最大放电/)
    assert.match(source, /panel\.open/)
    const chain = source.slice(source.indexOf('const PvChainArrayCard'), source.indexOf('const PcsCard'))
    assert.match(chain, /环境℃/)
    assert.match(chain, /入射角°/)
    assert.match(chain, /pv-set-temp/)
    assert.match(chain, /pv-set-angle/)
    assert.match(chain, /climate-locked/)
    assert.match(chain, /disabled: climateLocked/)
    const arrayBranch = source.indexOf("c.tone === 'pvArray'")
    const pcsBranch = source.indexOf("c.tone === 'pvPcs'")
    const unloaded = source.indexOf('运行时未加载')
    assert.ok(arrayBranch >= 0 && unloaded > arrayBranch)
    assert.ok(pcsBranch >= 0 && unloaded > pcsBranch)
    const pcsCard = source.slice(source.indexOf('const PvChainPcsCard'), source.indexOf('const PvChainArrayCard'))
    assert.match(pcsCard, /P设:/)
    assert.match(pcsCard, /P实:/)
    assert.match(pcsCard, /可发:/)
    assert.equal(pcsCard.includes('disabled'), false)
    assert.match(source, /黑启动|pcsBlackStart/)
    const nodes = [
      { id: 'grid', templateId: 'grid', label: '电网', x: 0, y: 0, parameters: { outputVoltage: 220000 } },
      { id: 'bus', templateId: 'ac_bus', label: '35kV', x: 0, y: 0, parameters: { nominalVoltage: 35000 } },
      { id: 'u1', templateId: 'pvu', label: '光伏单元', x: 0, y: 0, parameters: {} }
    ]
    const edges = [{ fromNodeId: 'grid', toNodeId: 'bus', fromPortId: 'ac_a', toPortId: 'a' }]
    for (let i = 0; i < 4; i++) {
      nodes.push({
        id: `c${i}`, templateId: 'pv_pcs', label: `PCS-${i}`, x: i * 40, y: 0,
        parameters: { pvuId: 'u1', pcsRatedPowerKw: 320 }
      })
      nodes.push({
        id: `a${i}`, templateId: 'pv_array', label: `方阵-${i}`, x: i * 40, y: 80,
        parameters: { moduleModel: 'TSM-NEG21C.20Q', stringCount: 16, modulesPerString: 30 }
      })
      edges.push({ fromNodeId: `c${i}`, toNodeId: 'bus', fromPortId: 'ac_a', toPortId: 'a' })
      edges.push({ fromNodeId: `c${i}`, toNodeId: `a${i}`, fromPortId: 'dc_pos', toPortId: 'dc_pos' })
    }
    const layout = buildTopologyMainLineLayout({ nodes, edges }, [])
    const cards = layout.units.filter(u => u.kind === 'pvChain').flatMap(u => u.cards)
    const titles = cards.map(c => c.title).join(' ')
    assert.match(titles, /光伏PCS/)
    assert.match(titles, /方阵/)
    assert.equal(titles.includes('BMS'), false)
    assert.ok(cards.some(c => c.lines.some(ln => String(ln).includes('320'))))
    assert.ok(cards.some(c => c.lines.includes('TSM-NEG21C.20Q')))
    const emu = buildTopologyMainLineLayout({
      nodes: [
        { id: 'grid', templateId: 'grid', label: '电网', x: 0, y: 0, parameters: {} },
        { id: 'bus', templateId: 'ac_bus', label: '35kV', x: 0, y: 0, parameters: { nominalVoltage: 35000 } },
        { id: 'p1', templateId: 'pcs', label: 'PCS', x: 0, y: 40, parameters: { emuId: 'e1', pcsRatedPowerKw: 1725 } },
        { id: 'b1', templateId: 'bms', label: 'BMS', x: 0, y: 80, parameters: {} },
        { id: 'e1', templateId: 'emu', label: 'EMU', x: 0, y: 0, parameters: {} }
      ],
      edges: [
        { fromNodeId: 'p1', toNodeId: 'bus', fromPortId: 'ac_a', toPortId: 'a' },
        { fromNodeId: 'p1', toNodeId: 'b1', fromPortId: 'dc_pos', toPortId: 'dc_pos' }
      ]
    }, [])
    const emuTitles = emu.units.filter(u => u.kind === 'emu').flatMap(u => u.cards).map(c => c.title)
    assert.ok(emuTitles.some(t => t.startsWith('PCS')))
    assert.ok(emuTitles.some(t => t.startsWith('BMS')))
  })

  it('preserves each device draft when a new snapshot arrives', () => {
    const { getDraft, setDraft } = createDrafts()
    setDraft(pcs1, 'p', '123')
    setDraft(pv1, 'p', '456')
    assert.equal(getDraft({ ...pcs1 }, 'p', 789), '123')
    assert.equal(getDraft({ ...pv1 }, 'p', 987), '456')
  })
})
