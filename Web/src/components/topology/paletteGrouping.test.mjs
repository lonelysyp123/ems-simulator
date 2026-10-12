import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { filterLibraryItems, groupTemplatesByCategory, PALETTE_CATEGORY_ORDER } from './paletteGrouping.js'

function tpl(id, name, category) {
  return { id, name, category }
}

const builtins = [
  tpl('grid', '电网', '电源'),
  tpl('ac_bus', '三相母线', '母线'),
  tpl('dc_bus', 'DC母线', '母线'),
  tpl('ac_breaker', '三相断路器', '开关'),
  tpl('transformer', '变压器', '变电'),
  tpl('split_transformer', '双耳变压器', '变电'),
  tpl('ac_meter', '电表', '测量'),
  tpl('load', '站用负载', '负荷'),
  tpl('svg', 'SVG', '无功'),
  tpl('emu', 'EMU储能单元', '储能'),
  tpl('emu_group', 'EMU分组', '储能'),
  tpl('pcs', 'PCS变流器', '储能'),
  tpl('bms', 'BMS', '储能'),
  tpl('pv_unit', '光伏单元', '光伏')
]

describe('groupTemplatesByCategory', () => {
  it('groups builtins in the locked category order', () => {
    const groups = groupTemplatesByCategory(builtins)
    assert.deepEqual(groups.map(g => g.category), PALETTE_CATEGORY_ORDER)
    assert.deepEqual(groups.find(g => g.category === '母线').items.map(t => t.id), ['ac_bus', 'dc_bus'])
    assert.deepEqual(
      groups.find(g => g.category === '变电').items.map(t => t.id),
      ['transformer', 'split_transformer']
    )
    assert.deepEqual(
      groups.find(g => g.category === '储能').items.map(t => t.id),
      ['emu', 'emu_group', 'pcs', 'bms']
    )
  })

  it('search 双耳 hits the split transformer only', () => {
    const groups = groupTemplatesByCategory(builtins, '双耳')
    assert.equal(groups.length, 1)
    assert.equal(groups[0].category, '变电')
    assert.deepEqual(groups[0].items.map(t => t.id), ['split_transformer'])
  })

  it('search pcs hits the PCS template only', () => {
    const groups = groupTemplatesByCategory(builtins, 'pcs')
    assert.equal(groups.length, 1)
    assert.equal(groups[0].category, '储能')
    assert.deepEqual(groups[0].items.map(t => t.id), ['pcs'])
  })

  it('empty query returns every non-empty group', () => {
    const groups = groupTemplatesByCategory(builtins, '  ')
    const ids = groups.flatMap(g => g.items.map(t => t.id))
    assert.deepEqual(ids, builtins.map(t => t.id))
  })

  it('unknown categories append after the known order and keep all items', () => {
    const extra = tpl('foo', '自定义', '插件')
    const groups = groupTemplatesByCategory([...builtins, extra])
    assert.equal(groups.at(-1).category, '插件')
    assert.deepEqual(groups.at(-1).items.map(t => t.id), ['foo'])
    assert.equal(groups.flatMap(g => g.items).length, builtins.length + 1)
  })
})

describe('filterLibraryItems', () => {
  const lib = [
    { id: 'a', name: '5.5MW 单元馈线', templateId: 'pcs' },
    { id: 'b', name: '大容量 BMS', templateId: 'bms' }
  ]

  it('filters by name', () => {
    assert.deepEqual(filterLibraryItems(lib, 'BMS').map(i => i.id), ['b'])
  })

  it('empty query returns the original list', () => {
    assert.equal(filterLibraryItems(lib, '').length, 2)
  })
})
