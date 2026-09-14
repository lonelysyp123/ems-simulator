import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import {
  TOPOLOGY_TEMPLATE_3D,
  buildStation3dLayout,
  stationKey,
  slotXs,
  paramNum,
  devicePort
} from './project3dLayout.js'
import { pvArrayFieldSize } from './pvArrayLayout.js'
import { createPvStringLeads, createStaticCable } from './buildMeshes.js'
import { SLD_ROLES } from '../topology/nodeLayout.js'

function node(id, templateId, label, x, parameters = {}) {
  return { id, templateId, label, x, y: 0, parameters }
}

function edge(fromNodeId, toNodeId) {
  return { fromNodeId, toNodeId, fromPortId: 'ac_a', toPortId: 'a' }
}

function byTemplate(layout, templateId) {
  return layout.items.filter(i => i.templateId === templateId)
}

describe('slotXs', () => {
  it('returns empty when count is 0 and centers a single slot', () => {
    assert.deepEqual(slotXs(10, 0), [])
    assert.deepEqual(slotXs(10, 1), [10])
    const xs = slotXs(10, 3, 4)
    assert.equal(xs.length, 3)
    assert.equal(xs[1], 10)
    assert.equal(xs[2] - xs[0], 8)
  })
})

describe('buildStation3dLayout from topology', () => {
  it('does not invent a station transformer when the project has none', () => {
    const snap = {
      engineeringMode: true,
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 400, { outputVoltage: 132000 }),
          node('bus', 'ac_bus', '33kV', 400, { nominalVoltage: 33000 }),
          node('pv1', 'pv_unit', '光伏-1', 200, { inverterCount: 8, unitXfPrimaryV: 33000, unitXfSecondaryV: 800 })
        ],
        edges: [edge('pv1', 'bus'), edge('grid', 'bus')]
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    const stationXf = layout.items.filter(i => i.kind === 'station-xf')
    assert.equal(stationXf.length, 0)
    const grid = layout.items.find(i => i.templateId === 'grid')
    assert.equal(paramNum(grid.node, 'outputVoltage'), 132000)
    const pvXf = layout.items.find(i => i.kind === 'pv-xf')
    assert.equal(paramNum(pvXf.node, 'primaryVoltage'), 33000)
    assert.equal(paramNum(pvXf.node, 'secondaryVoltage'), 800)
  })

  it('places every station transformer from the graph, not a hardcoded single main xf', () => {
    const snap = {
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 400, { outputVoltage: 220000 }),
          node('hv', 'ac_bus', '220kV', 400, { nominalVoltage: 220000 }),
          node('xf1', 'transformer', '主变1', 200, { primaryVoltage: 220000, secondaryVoltage: 35000, ratedPowerKva: 25000 }),
          node('xf2', 'transformer', '主变2', 800, { primaryVoltage: 220000, secondaryVoltage: 35000, ratedPowerKva: 25000 }),
          node('lv1', 'ac_bus', '35kV-1', 200, { nominalVoltage: 35000 }),
          node('lv2', 'ac_bus', '35kV-2', 800, { nominalVoltage: 35000 }),
          node('pv1', 'pv_unit', 'PV-1', 120, { inverterCount: 4 }),
          node('pv2', 'pv_unit', 'PV-2', 720, { inverterCount: 4 })
        ],
        edges: [
          edge('hv', 'xf1'),
          edge('hv', 'xf2'),
          edge('xf1', 'lv1'),
          edge('xf2', 'lv2'),
          edge('lv1', 'pv1'),
          edge('lv2', 'pv2')
        ]
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    const xfs = layout.items.filter(i => i.kind === 'station-xf')
    assert.equal(xfs.length, 2)
    assert.ok(xfs[0].x !== xfs[1].x)
    assert.equal(byTemplate(layout, 'pv_array').length, 8, 'one array per inverter (4 inverters x 2 pv units)')
  })

  it('draws inverter / array count from node inverterCount, including a single-array unit', () => {
    const snap = {
      topology: {
        nodes: [
          node('bus', 'ac_bus', '35kV', 0, { nominalVoltage: 35000 }),
          node('pv1', 'pv_unit', 'PV', 0, { inverterCount: 1, stringCount: 10, modulesPerString: 20 })
        ],
        edges: [edge('pv1', 'bus')]
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    const arrays = layout.items.filter(i => i.templateId === 'pv_array')
    const inv = layout.items.filter(i => i.templateId === 'pv_inverter')
    assert.equal(arrays.length, 1)
    assert.equal(inv.length, 1)
    assert.equal(arrays[0].inverterCount, 1)
    assert.equal(arrays[0].stringCount, 10)
    assert.equal(arrays[0].modulesPerString, 20)
  })

  it('draws one cabinet per pcs node and only topology-connected bms containers', () => {
    // 新模型：EMU 虚拟不连线；PCS 经 emuId 归属，DC 侧由 PCS 连 BMS
    const snap = {
      topology: {
        nodes: [
          node('emu1', 'emu', 'EMU-1', 0),
          node('pcs1', 'pcs', 'PCS-1', 0, { emuId: 'emu1' }),
          node('bms1', 'bms', 'BMS-1', 0, { clusterCount: 8 })
        ],
        edges: [
          { fromNodeId: 'pcs1', toNodeId: 'bms1', fromPortId: 'dc_pos', toPortId: 'dc_pos' }
        ]
      },
      units: [{ unitIndex: 0, unitNumber: 1, channels: [{ pcsNumber: 1 }] }]
    }
    const layout = buildStation3dLayout(snap)
    const pcs = layout.items.filter(i => i.templateId === 'pcs')
    const bms = layout.items.filter(i => i.templateId === 'bms')
    assert.equal(pcs.length, 1)
    assert.equal(bms.length, 1)
  })

  it('does not invent pcs / inverter / array counts from defaults', () => {
    const snap = {
      topology: {
        nodes: [
          node('emu1', 'emu', 'EMU-1', 0, {}),
          node('pv1', 'pv_unit', 'PV', 200, {})
        ],
        edges: []
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    assert.equal(byTemplate(layout, 'pcs').length, 0)
    assert.equal(byTemplate(layout, 'pv_inverter').length, 0)
    assert.equal(byTemplate(layout, 'pv_array').length, 0)
  })

  it('draws every pcs node as a cabinet beyond two', () => {
    const snap = {
      topology: {
        nodes: [
          node('emu1', 'emu', 'EMU-1', 0),
          node('pcs1', 'pcs', 'PCS-1', -80, { emuId: 'emu1' }),
          node('pcs2', 'pcs', 'PCS-2', 0, { emuId: 'emu1' }),
          node('pcs3', 'pcs', 'PCS-3', 80, { emuId: 'emu1' })
        ],
        edges: []
      },
      units: []
    }
    const layout = buildStation3dLayout(snap)
    assert.equal(byTemplate(layout, 'pcs').length, 3)
  })

  it('places a dc bus only when the graph has one and more than one bms hang', () => {
    const snap = {
      topology: {
        nodes: [
          node('emu1', 'emu', 'EMU-1', 0, { pcsCount: 2 }),
          node('dc1', 'dc_bus', 'DC', 0, { nominalVoltage: 1500 }),
          node('bms1', 'bms', 'BMS-1', -80, { clusterCount: 8 }),
          node('bms2', 'bms', 'BMS-2', 80, { clusterCount: 6 })
        ],
        edges: [
          { fromNodeId: 'emu1', toNodeId: 'dc1', fromPortId: 'dc_pos', toPortId: 'pos_t' },
          { fromNodeId: 'dc1', toNodeId: 'bms1', fromPortId: 'pos_b', toPortId: 'dc_pos' },
          { fromNodeId: 'dc1', toNodeId: 'bms2', fromPortId: 'pos_b', toPortId: 'dc_pos' }
        ]
      },
      units: []
    }
    const layout = buildStation3dLayout(snap)
    assert.equal(byTemplate(layout, 'dc_bus').length, 1)
    assert.equal(byTemplate(layout, 'bms').length, 2)
    assert.equal(paramNum(layout.items.find(i => i.templateId === 'dc_bus').node, 'nominalVoltage'), 1500)
  })

  it('omits meters but places loads that exist on the bus, and omits a single-hang bus bar', () => {
    const snap = {
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 0, { outputVoltage: 110000 }),
          node('bus', 'ac_bus', '110kV', 0, { nominalVoltage: 110000 }),
          node('m1', 'ac_meter', 'PCC', 40, { isPccMeter: true }),
          node('l1', 'load', '站用', 80, { ratedVoltage: 110000 })
        ],
        edges: [edge('grid', 'bus'), edge('m1', 'bus'), edge('l1', 'bus')]
      },
      units: []
    }
    const layout = buildStation3dLayout(snap)
    // 3D 视图不绘制电表（含兜底），且不产生连向电表的悬空电缆
    assert.equal(byTemplate(layout, 'ac_meter').length, 0)
    assert.ok(!layout.cables.some(c =>
      [c.ax, c.bx, c.az, c.bz].some(v => !Number.isFinite(v))), 'no dangling cable')
    assert.equal(byTemplate(layout, 'load').length, 1)
    assert.equal(byTemplate(layout, 'grid').length, 1)
  })
})

describe('pv array vs inverter spacing', () => {
  it('keeps one array per inverter, all arrays behind and clear of inverter rows', () => {
    const snap = {
      topology: {
        nodes: [node('pv1', 'pv_unit', 'PV', 0, {
          inverterCount: 8, stringCount: 16, modulesPerString: 30
        })],
        edges: []
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    const arrs = layout.items.filter(i => i.templateId === 'pv_array')
    const invs = layout.items.filter(i => i.templateId === 'pv_inverter')
    assert.equal(arrs.length, 8, 'one array per inverter')
    assert.equal(invs.length, 2, 'A/B inverter rows')
    for (const arr of arrs) {
      const inv = invs.find(i => i.panelKey === arr.panelKey)
      assert.ok(inv, 'inverter row exists for the array')
      assert.ok(arr.z - inv.z >= 4, `array stays behind inverter: arr.z=${arr.z} inv.z=${inv.z}`)
    }
    // 网格内相邻方阵不重叠
    const xs = [...arrs].map(a => a.x).sort((a, b) => a - b)
    for (let i = 1; i < xs.length; i++) {
      assert.ok(xs[i] - xs[i - 1] >= 8, `arrays in grid do not overlap: ${xs[i] - xs[i - 1]}`)
    }
  })

  it('keeps inverter rows of adjacent pv units apart', () => {
    const snap = {
      topology: {
        nodes: [
          node('pv1', 'pv_unit', 'PV-1', 0, { inverterCount: 20, stringCount: 16, modulesPerString: 30 }),
          node('pv2', 'pv_unit', 'PV-2', 480, { inverterCount: 20, stringCount: 16, modulesPerString: 30 })
        ],
        edges: []
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    const invs = layout.items.filter(i => i.templateId === 'pv_inverter')
    assert.equal(invs.length, 4, 'A/B rows for each of 2 units')
    const xs = invs.map(i => i.x).sort((a, b) => a - b)
    for (let i = 1; i < xs.length; i++) {
      assert.ok(xs[i] - xs[i - 1] >= 6, `inverter rows do not overlap: ${xs[i] - xs[i - 1]}`)
    }
    assert.equal(layout.items.filter(i => i.templateId === 'pv_array').length, 40, '40 arrays for 40 inverters')
  })
})

describe('pv array 1:1 footprint & scene bounds', () => {
  it('renders every module per topology config without capping', () => {
    const f = pvArrayFieldSize(16, 30)
    assert.equal(f.rows, 16)
    assert.equal(f.cols, 30)
    assert.ok(f.fieldW > 20, `wide field for 30 modules in a row (${f.fieldW})`)
    assert.ok(f.fieldD > 15, `deep field for 16 strings (${f.fieldD})`)
  })

  it('expands scene bounds to contain the array footprint', () => {
    const snap = {
      topology: {
        nodes: [node('pv1', 'pv_unit', 'PV', 0, {
          inverterCount: 8, stringCount: 16, modulesPerString: 30
        })],
        edges: []
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    const { fieldW, fieldD } = pvArrayFieldSize(16, 30)
    assert.ok(layout.bounds.maxX - layout.bounds.minX >= fieldW, 'bounds wide enough on x')
    assert.ok(layout.bounds.maxZ - layout.bounds.minZ >= fieldD, 'bounds deep enough on z')
  })
})

describe('pv inverter<->box transformer cable routing', () => {
  it('uses a main trunk per side plus a short branch per inverter', () => {
    const snap = {
      topology: {
        nodes: [node('pv1', 'pv_unit', 'PV', 0, {
          inverterCount: 4, stringCount: 16, modulesPerString: 30
        })],
        edges: []
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    const mains = layout.cables.filter(c => c.role === 'pv-inv-main')
    const branches = layout.cables.filter(c => c.role === 'pv-inv-branch')
    assert.equal(mains.length, 2, 'one main trunk per A/B side')
    assert.equal(branches.length, 4, 'one short branch per inverter (4)')
    for (const m of mains) {
      assert.equal(m.static, true)
      assert.equal(m.radius, 0.07)
      assert.ok(m.ay <= 0.6 && m.by <= 0.6, 'low')
      // 主干从箱变 +z 侧出线，终点在逆变器排前方（zInv-0.9）
      const row = layout.items.find(i => i.templateId === 'pv_inverter' && i.pvIndex === m.pvIndex && i.side === m.side)
      assert.ok(m.bz < row.z, `main ends before row: bz=${m.bz} rowZ=${row.z}`)
      assert.ok(m.az > layout.items.find(i => i.templateId === 'transformer' && i.kind === 'pv-xf').z, 'starts at xf +z side')
    }
    for (const b of branches) {
      assert.equal(b.static, true)
      assert.equal(b.radius, 0.045)
      assert.ok(b.ay <= 0.6 && b.by <= 0.6, 'low')
      const row = layout.items.find(i => i.templateId === 'pv_inverter' && i.pvIndex === b.pvIndex && i.side === b.side)
      assert.ok(b.bz <= row.z + 0.36 && b.bz >= row.z - 0.72, `branch near row: bz=${b.bz} rowZ=${row.z}`)
    }
    // 每台逆变器分支终点独立（x,z 唯一，不重合）
    const ends = branches.map(c => `${c.bx.toFixed(3)},${c.bz.toFixed(3)}`)
    assert.equal(new Set(ends).size, branches.length, 'distinct per-cabinet branch endpoints')
  })
})

describe('pv string leads are axis-aligned', () => {
  it('builds orthogonal segments only (north-south or east-west, no diagonals)', () => {
    const leads = createPvStringLeads({ rows: 16, cols: 30, fieldW: 27.2, fieldD: 17.6 })
    const line = leads.children.find(o => o.isLineSegments)
    const pos = line.geometry.attributes.position.array
    assert.ok(pos.length >= 16 * 2 * 6, '16 strings x 2 segments x 2 vertices')
    for (let i = 0; i < pos.length; i += 6) {
      const dx = Math.abs(pos[i + 3] - pos[i])
      const dz = Math.abs(pos[i + 5] - pos[i + 2])
      assert.ok(dx < 1e-6 || dz < 1e-6, `segment axis-aligned: dx=${dx} dz=${dz}`)
    }
    // 各串东西段 y 分层，避免同平面重合
    const ys = new Set()
    for (let i = 0; i < pos.length; i += 6) {
      ys.add(pos[i + 1])
      ys.add(pos[i + 4])
    }
    assert.ok(ys.size >= 16, `per-string y layers: ${ys.size}`)
  })
})

describe('pv static dc cable is axis-aligned and ground-hugging', () => {
  it('builds only axis-aligned segments (no diagonals)', () => {
    const cable = createStaticCable({ ax: 0, ay: 0.9, az: 0, bx: 12, by: 0.5, bz: -5, midY: 0.35 })
    assert.ok(cable.children.length >= 4, `segments: ${cable.children.length}`)
    for (const seg of cable.children) {
      const rx = Math.abs(seg.rotation.x)
      const rz = Math.abs(seg.rotation.z)
      const axisY = rx < 1e-6 && rz < 1e-6
      const axisX = Math.abs(rz - Math.PI / 2) < 1e-6 && rx < 1e-6
      const axisZ = Math.abs(rx - Math.PI / 2) < 1e-6 && rz < 1e-6
      assert.ok(axisY || axisX || axisZ, `axis-aligned segment: rx=${rx} rz=${rz}`)
    }
    // 贴地：所有段中点 y 不超过 midY + 端点允许立管高度
    for (const seg of cable.children) {
      assert.ok(seg.position.y <= 0.9 + 1e-6, `segment low: ${seg.position.y}`)
    }
  })
})

describe('fallback drawing for every placed base template node', () => {
  it('draws independent bms / dc_bus / branch breaker not placed by the sld', () => {
    const snap = {
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 0, { outputVoltage: 35000 }),
          node('bus', 'ac_bus', '35kV', 0, { nominalVoltage: 35000 }),
          node('load1', 'load', '站用', 300, { ratedVoltage: 35000 }),
          node('dc1', 'dc_bus', 'DC母线', 200, { nominalVoltage: 1200 }),
          node('bms1', 'bms', '独立BMS', 260, { clusterCount: 8 }),
          node('brk1', 'ac_breaker', '支路断路器', 100, {})
        ],
        edges: [
          edge('grid', 'bus'),
          edge('load1', 'bus'),
          { fromNodeId: 'dc1', toNodeId: 'bms1', fromPortId: 'pos_b', toPortId: 'dc_pos' },
          { fromNodeId: 'brk1', toNodeId: 'bus', fromPortId: 'ac_a', toPortId: 'a' }
        ]
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    const kinds = layout.items.map(i => i.templateId)
    assert.ok(kinds.includes('dc_bus'), 'independent dc_bus drawn')
    assert.ok(kinds.includes('bms'), 'independent bms drawn')
    assert.ok(kinds.includes('ac_breaker'), 'branch breaker drawn')
    // 兜底连线（两个未精确定位节点之间）
    assert.ok(layout.cables.filter(c => c.role === 'sld-wire').length >= 1, 'fallback cable present')
  })

  it('keeps fallback items axis-aligned and low like every cable', () => {
    const snap = {
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 0, { outputVoltage: 35000 }),
          node('bus', 'ac_bus', '35kV', 0, { nominalVoltage: 35000 }),
          node('dc1', 'dc_bus', 'DC母线', 200, { nominalVoltage: 1200 }),
          node('bms1', 'bms', '独立BMS', 260, { clusterCount: 8 })
        ],
        edges: [
          edge('grid', 'bus'),
          { fromNodeId: 'dc1', toNodeId: 'bms1', fromPortId: 'pos_b', toPortId: 'dc_pos' }
        ]
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    for (const c of layout.cables) {
      assert.equal(c.static, true, 'all cables static')
      assert.ok(c.ay <= 0.6 && c.by <= 0.6, 'all cables low')
    }
  })
})

describe('bus is drawn as a node (star wiring rule)', () => {
  it('draws main bus as bus-node and routes feeder drops into the hub', () => {
    // 组态模式：EMU 绑定断路器时支路馈线由真实断路器节点接入母线汇流点
    const snap = {
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 0, { outputVoltage: 35000 }),
          node('bus', 'ac_bus', '35kV', 0, { nominalVoltage: 35000 }),
          node('emu1', 'emu', 'EMU-1', 300),
          node('pcs1', 'pcs', 'PCS-1', 300, { emuId: 'emu1' }),
          node('brk1', 'ac_breaker', '单元断-1', 300, { emuId: 'emu1' }),
          node('emu2', 'emu', 'EMU-2', 500),
          node('pcs2', 'pcs', 'PCS-2', 500, { emuId: 'emu2' }),
          node('brk2', 'ac_breaker', '单元断-2', 500, { emuId: 'emu2' }),
          node('pv1', 'pv_unit', 'PV-1', 700, { inverterCount: 4 })
        ],
        edges: [
          edge('grid', 'bus'),
          edge('brk1', 'bus'),
          edge('brk2', 'bus'),
          edge('pv1', 'bus')
        ]
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    const bus = layout.items.find(i => i.templateId === 'ac_bus')
    assert.ok(bus, 'bus item exists')
    assert.equal(bus.kind, 'bus-node', 'bus drawn as a node, not a bar')
    assert.ok(bus.radius >= 0.26, `radius adapts to size: ${bus.radius}`)
    // 支路电缆汇聚到母线点 x，y 分层避免重合
    const drops = layout.cables.filter(c => c.role === 'unit-drop' || c.role === 'pv-drop')
    assert.equal(drops.length, 3, '3 feeder drops')
    for (const d of drops) {
      assert.equal(d.bx, bus.x, `drop ends at hub x (${bus.x})`)
      assert.ok(d.midY >= 0.35, `layered midY: ${d.midY}`)
    }
    assert.equal(new Set(drops.map(d => d.midY)).size, drops.length, 'per-drop y layers')
  })

  it('expands emu units device by device without synthetic unit title / xf / 690 bus', () => {
    const snap = {
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 0, { outputVoltage: 35000 }),
          node('bus', 'ac_bus', '35kV', 0, { nominalVoltage: 35000 }),
          node('emu1', 'emu', 'EMU-1', 300),
          node('pcs1', 'pcs', 'PCS-1', 260, { emuId: 'emu1' }),
          node('pcs2', 'pcs', 'PCS-2', 340, { emuId: 'emu1' }),
          node('brk1', 'ac_breaker', '单元断-1', 300, { emuId: 'emu1' }),
          node('meter1', 'ac_meter', '单元电表', 300, { emuId: 'emu1' }),
          node('bms1', 'bms', 'BMS-1', 260, { clusterCount: 8 }),
          node('bms2', 'bms', 'BMS-2', 340, { clusterCount: 8 })
        ],
        edges: [
          edge('grid', 'bus'),
          edge('brk1', 'bus'),
          { fromNodeId: 'pcs1', toNodeId: 'bms1', fromPortId: 'dc_pos', toPortId: 'dc_pos' },
          { fromNodeId: 'pcs2', toNodeId: 'bms2', fromPortId: 'dc_pos', toPortId: 'dc_pos' }
        ]
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    // 组态模式不再合成 EMU 概念（单元标题/单元变/690 母线）
    for (const kind of ['unit-title', 'unit-xf']) {
      assert.ok(!layout.items.some(i => i.kind === kind), `no synthetic ${kind}`)
    }
    assert.ok(!layout.items.some(i => i.busRole === 'unit-lv-bus'), 'no synthetic 690 bus')
    // 逐设备出 item：绑定断路器用真实组态节点身份；电表不在 3D 绘制
    const brk = layout.items.find(i => i.templateId === 'ac_breaker' && i.kind === 'unit-breaker')
    assert.ok(brk, 'bound breaker drawn')
    assert.equal(brk.node?.id, 'brk1', 'breaker identity from topology node')
    assert.ok(!layout.items.some(i => i.templateId === 'ac_meter'), 'bound meter not drawn in 3D')
    assert.equal(byTemplate(layout, 'pcs').length, 2, 'one item per pcs node')
    assert.equal(byTemplate(layout, 'bms').length, 2, 'one item per bms node')
  })

  it('omits breaker / meter items and unit-drop when the emu has no bound device', () => {
    const snap = {
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 0, { outputVoltage: 35000 }),
          node('bus', 'ac_bus', '35kV', 0, { nominalVoltage: 35000 }),
          node('emu1', 'emu', 'EMU-1', 300),
          node('pcs1', 'pcs', 'PCS-1', 300, { emuId: 'emu1' })
        ],
        edges: [edge('grid', 'bus')]
      },
      units: [],
      pvUnits: []
    }
    const layout = buildStation3dLayout(snap)
    assert.ok(!layout.items.some(i => i.kind === 'unit-breaker'), 'no breaker drawn')
    assert.ok(!layout.items.some(i => i.kind === 'meter'), 'no meter drawn')
    assert.equal(layout.cables.filter(c => c.role === 'unit-drop').length, 0, 'no unit-drop without breaker')
    assert.equal(layout.cables.filter(c => c.role === 'pcs-feed').length, 1, 'pcs fed directly from bus hub')
  })

  it('fallback layout also draws the bus as a node', () => {
    const layout = buildStation3dLayout({
      units: [{ unitIndex: 0, unitNumber: 1, channels: [{ pcsNumber: 1 }] }, { unitIndex: 1, unitNumber: 2, channels: [{ pcsNumber: 3 }] }],
      pvUnits: []
    })
    const bus = layout.items.find(i => i.templateId === 'ac_bus')
    assert.ok(bus, 'fallback bus exists')
    assert.equal(bus.kind, 'bus-node', 'fallback bus drawn as node')
  })
})

describe('TOPOLOGY_TEMPLATE_3D', () => {
  it('covers every topology editor template as primitive or composite', () => {
    const visible = Object.entries(SLD_ROLES)
      .filter(([, role]) => role !== 'virtual')
      .map(([id]) => id)
      .sort()
    assert.deepEqual(Object.keys(TOPOLOGY_TEMPLATE_3D).sort(), visible)
    assert.equal(TOPOLOGY_TEMPLATE_3D.pcs, 'primitive')
    assert.equal(TOPOLOGY_TEMPLATE_3D.pv_unit, 'composite')
    assert.equal(TOPOLOGY_TEMPLATE_3D.split_transformer, 'primitive')
    assert.equal(TOPOLOGY_TEMPLATE_3D.dc_bus, 'primitive')
    assert.equal(TOPOLOGY_TEMPLATE_3D.emu, undefined)
  })
})

describe('stationKey', () => {
  it('changes when inverterCount on a topology node changes', () => {
    const a = {
      topology: {
        nodes: [node('pv1', 'pv_unit', 'PV', 0, { inverterCount: 4 })],
        edges: []
      }
    }
    const b = {
      topology: {
        nodes: [node('pv1', 'pv_unit', 'PV', 0, { inverterCount: 8 })],
        edges: []
      }
    }
    assert.notEqual(stationKey(a), stationKey(b))
  })
})

describe('sectional bus breaker in 3d', () => {
  // 3D 此前完全不渲染 sld.tieBreakers，绑定到 EMU 的中压断路器只出现在单元馈线上
  function sectionSnap() {
    return {
      topology: {
        nodes: [
          node('main', 'ac_bus', '35kV主母线', 400, { nominalVoltage: 35000 }),
          node('sec', 'ac_breaker', '中压三相断路器', 300, { emuId: 'emu1', closed: true }),
          node('sub', 'ac_bus', '35kV分段', 200, { nominalVoltage: 35000 }),
          node('emu1', 'emu', 'EMU-1', 200),
          node('xf', 'transformer', '级变', 200, { primaryVoltage: 35000, secondaryVoltage: 690 }),
          node('lv', 'ac_bus', '690V', 200, { nominalVoltage: 690 }),
          node('pcs1', 'pcs', 'PCS-1', 160, { emuId: 'emu1' }),
          node('dc1', 'dc_bus', '直流母线', 200, { nominalVoltage: 800 }),
          node('bms1', 'bms', 'BMS-1', 200, {})
        ],
        edges: [
          edge('main', 'sec'), edge('sec', 'sub'), edge('sub', 'xf'), edge('xf', 'lv'),
          edge('lv', 'pcs1'), edge('pcs1', 'dc1'), edge('dc1', 'bms1'), edge('emu1', 'lv')
        ]
      },
      units: []
    }
  }

  it('renders the section breaker once, on the bus, not inside the unit', () => {
    const layout = buildStation3dLayout(sectionSnap())
    const brk = byTemplate(layout, 'ac_breaker')
    assert.equal(brk.length, 1, 'drawn exactly once')
    assert.equal(brk[0].kind, 'tie-breaker', 'placed on the bus, not as a unit breaker')
    assert.equal(brk[0].node.id, 'sec')
    assert.equal(brk[0].unitIndex, 0, 'carries the emu binding for live telemetry')

    const hubs35 = byTemplate(layout, 'ac_bus')
      .filter(b => b.node?.parameters?.nominalVoltage === 35000)
    assert.equal(hubs35.length, 2, 'main bus plus one section')
    // 分段断路器对齐子分段母线列，并落在两段母线之间
    assert.ok(hubs35.some(h => Math.abs(h.x - brk[0].x) < 0.01),
      'sits on one of the 35kV hub columns')
    const zs = hubs35.map(h => h.z).sort((a, b) => a - b)
    assert.ok(brk[0].z > zs[0] && brk[0].z < zs[1],
      'sits between the two 35kV hubs')

    const unitZ = byTemplate(layout, 'pcs')[0].z
    assert.ok(brk[0].z < unitZ, 'stays upstream of the pcs row')
  })

  it('binds two-feeder emu section breakers to runtime unit index', () => {
    const n = (id, templateId, label, x, y, parameters = {}) =>
      ({ id, templateId, label, x, y, parameters })
    const snap = {
      topology: {
        nodes: [
          n('grid', 'grid', '电网', 400, 0, { outputVoltage: 35000 }),
          n('main', 'ac_bus', '35kV主', 400, 40, { nominalVoltage: 35000 }),
          n('emu1', 'emu', 'EMU-1', 200, 10),
          n('brk1', 'ac_breaker', '中压断1', 200, 80, { emuId: 'emu1', closed: true }),
          n('sub1', 'ac_bus', '35kV-1', 200, 120, { nominalVoltage: 35000 }),
          n('xf1', 'transformer', '变1', 200, 160, { primaryVoltage: 35000, secondaryVoltage: 690 }),
          n('lv1', 'ac_bus', '690V-1', 200, 200, { nominalVoltage: 690 }),
          n('pcs1a', 'pcs', 'PCS1A', 100, 240, { emuId: 'emu1' }),
          n('dc1a', 'dc_bus', 'DC1A', 100, 280, { nominalVoltage: 800 }),
          n('pcs1b', 'pcs', 'PCS1B', 300, 240, { emuId: 'emu1' }),
          n('dc1b', 'dc_bus', 'DC1B', 300, 280, { nominalVoltage: 800 }),
          n('emu2', 'emu', 'EMU-2', 600, 20),
          n('brk2', 'ac_breaker', '中压断2', 600, 80, { emuId: 'emu2', closed: true }),
          n('sub2', 'ac_bus', '35kV-2', 600, 120, { nominalVoltage: 35000 }),
          n('xf2', 'transformer', '变2', 600, 160, { primaryVoltage: 35000, secondaryVoltage: 690 }),
          n('lv2', 'ac_bus', '690V-2', 600, 200, { nominalVoltage: 690 }),
          n('pcs2a', 'pcs', 'PCS2A', 500, 240, { emuId: 'emu2' }),
          n('dc2a', 'dc_bus', 'DC2A', 500, 280, { nominalVoltage: 800 }),
          n('pcs2b', 'pcs', 'PCS2B', 700, 240, { emuId: 'emu2' }),
          n('dc2b', 'dc_bus', 'DC2B', 700, 280, { nominalVoltage: 800 })
        ],
        edges: [
          edge('grid', 'main'),
          edge('main', 'brk1'), edge('brk1', 'sub1'), edge('sub1', 'xf1'), edge('xf1', 'lv1'),
          edge('lv1', 'pcs1a'), edge('pcs1a', 'dc1a'),
          edge('lv1', 'pcs1b'), edge('pcs1b', 'dc1b'),
          edge('emu1', 'lv1'),
          edge('main', 'brk2'), edge('brk2', 'sub2'), edge('sub2', 'xf2'), edge('xf2', 'lv2'),
          edge('lv2', 'pcs2a'), edge('pcs2a', 'dc2a'),
          edge('lv2', 'pcs2b'), edge('pcs2b', 'dc2b'),
          edge('emu2', 'lv2')
        ]
      },
      units: [
        { unitIndex: 0, unitNumber: 1, unitBreakerClosed: true, channels: [{ pcsNumber: 1 }, { pcsNumber: 2 }] },
        { unitIndex: 1, unitNumber: 2, unitBreakerClosed: false, channels: [{ pcsNumber: 3 }, { pcsNumber: 4 }] }
      ]
    }
    const layout = buildStation3dLayout(snap)
    const brk2 = layout.items.find(i => i.node?.id === 'brk2')
    assert.ok(brk2, 'emu2 section breaker drawn')
    assert.equal(brk2.kind, 'tie-breaker')
    assert.equal(brk2.unitIndex, 1, 'live telemetry keys off runtime unit 2')
  })
})

describe('transformer-branch series breaker in 3d', () => {
  it('places the MV breaker with the dual-ear transformer, not on the PCS row', () => {
    const snap = {
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 400, { outputVoltage: 35000 }),
          node('hv', 'ac_bus', '35kV', 400, { nominalVoltage: 35000 }),
          node('cb', 'ac_breaker', '中压三相断路器', 400, { emuId: 'emu1', closed: true }),
          node('split', 'split_transformer', '双耳1', 400, {
            primaryVoltage: 35000, secondaryVoltage: 690, ratedPowerKva: 6300, emuId: 'emu1'
          }),
          node('busL', 'ac_bus', '左690', 200, { nominalVoltage: 690 }),
          node('busR', 'ac_bus', '右690', 600, { nominalVoltage: 690 }),
          node('emu1', 'emu', 'EMU-1', 400),
          node('pcsL', 'pcs', 'PCS-L', 200, { emuId: 'emu1' }),
          node('pcsR', 'pcs', 'PCS-R', 600, { emuId: 'emu1' })
        ],
        edges: [
          edge('grid', 'hv'),
          edge('hv', 'cb'), edge('cb', 'split'),
          edge('split', 'busL'), edge('split', 'busR'),
          edge('busL', 'pcsL'), edge('busR', 'pcsR')
        ]
      },
      units: [{ unitIndex: 0, unitNumber: 1, unitBreakerClosed: true, channels: [{ pcsNumber: 1 }, { pcsNumber: 2 }] }]
    }
    const layout = buildStation3dLayout(snap)
    const brk = layout.items.filter(i => i.templateId === 'ac_breaker')
    assert.equal(brk.length, 1, 'drawn exactly once')
    assert.equal(brk[0].kind, 'branch-breaker')
    assert.equal(brk[0].node.id, 'cb')
    const xf = layout.items.find(i => i.kind === 'station-xf')
    const hv = layout.items.find(i => i.templateId === 'ac_bus' && i.node?.id === 'hv')
    const pcs = byTemplate(layout, 'pcs')
    assert.ok(xf && hv && pcs.length === 2)
    assert.ok(brk[0].z > hv.z, 'downstream of the 35kV hub')
    assert.ok(brk[0].z < xf.z, 'upstream of the dual-ear transformer')
    assert.ok(pcs.every(p => brk[0].z < p.z), 'not on the PCS row')
    assert.ok(!layout.items.some(i => i.kind === 'unit-breaker'), 'not redrawn inside the unit')
  })
})

describe('edge-derived cable redraw', () => {
  // 一根连线 ⇔ 组态中存在对应节点边：并行边去重、未绘制模板跳过、端点必落在设备锚点上
  function stationSnap() {
    return {
      topology: {
        nodes: [
          node('grid', 'grid', '电网', 400, { outputVoltage: 220000 }),
          node('main', 'ac_breaker', '主断路器', 400, { isMainBreaker: true }),
          node('hv', 'ac_bus', '220kV母线', 400, { nominalVoltage: 220000 }),
          node('xf', 'transformer', '主变', 400, { primaryVoltage: 220000, secondaryVoltage: 35000 }),
          node('mv', 'ac_bus', '35kV母线', 400, { nominalVoltage: 35000 }),
          node('uxf', 'transformer', '2级变', 300, { primaryVoltage: 35000, secondaryVoltage: 690 }),
          node('lv', 'ac_bus', '690V母线', 300, { nominalVoltage: 690 }),
          node('emu1', 'emu', 'EMU-1', 300),
          node('pcs1', 'pcs', 'PCS-1', 260, { emuId: 'emu1' }),
          node('pcs2', 'pcs', 'PCS-2', 340, { emuId: 'emu1' }),
          node('dc1', 'dc_bus', '直流母线', 300, { nominalVoltage: 1200 }),
          node('bms1', 'bms', 'BMS-1', 260, {}),
          node('bms2', 'bms', 'BMS-2', 340, {}),
          node('m1', 'ac_meter', '三相电表', 500, {})
        ],
        edges: [
          // 三相并行边：同一对节点 3 条边只画 1 根线
          edge('grid', 'main'), edge('grid', 'main'), edge('grid', 'main'),
          edge('main', 'hv'), edge('hv', 'xf'), edge('xf', 'mv'),
          edge('mv', 'uxf'), edge('uxf', 'lv'),
          edge('lv', 'pcs1'), edge('lv', 'pcs2'),
          edge('pcs1', 'dc1'), edge('pcs2', 'dc1'),
          edge('dc1', 'bms1'), edge('dc1', 'bms2'),
          edge('mv', 'm1')
        ]
      },
      units: []
    }
  }

  /** 电缆端点必须落在某个已放置节点的出线口 / 母线汇流点 / 直流母线横杠夹取点上 */
  function onPort(layout, x, z) {
    return layout.items.some(it => {
      if ((it.templateId === 'dc_bus' || it.templateId === 'ac_bus')
        && Number.isFinite(it.x1) && Number.isFinite(it.x2)) {
        return Math.abs(it.z - z) < 1e-6
          && x >= Math.min(it.x1, it.x2) - 1e-6
          && x <= Math.max(it.x1, it.x2) + 1e-6
      }
      const front = devicePort(it, it.z + 10)
      const back = devicePort(it, it.z - 10)
      const ports = [front, back].filter(Boolean)
      if (ports.length) {
        return ports.some(p => Math.abs(p.x - x) < 1e-6 && Math.abs(p.z - z) < 1e-6)
      }
      return Math.abs(it.x - x) < 1e-6 && Math.abs(it.z - z) < 1e-6
    })
  }

  it('dedupes parallel three-phase edges into one cable per node pair', () => {
    const layout = buildStation3dLayout(stationSnap())
    const grid = layout.items.find(i => i.templateId === 'grid')
    const main = layout.items.find(i => i.node?.id === 'main')
    // 出线口：电网前口 → 主断后口（面向对端选口）
    const gp = devicePort(grid, main.z)
    const mp = devicePort(main, grid.z)
    const between = layout.cables.filter(c =>
      Math.abs(c.ax - gp.x) < 1e-6 && Math.abs(c.az - gp.z) < 1e-6
      && Math.abs(c.bx - mp.x) < 1e-6 && Math.abs(c.bz - mp.z) < 1e-6)
      .concat(layout.cables.filter(c =>
        Math.abs(c.ax - mp.x) < 1e-6 && Math.abs(c.az - mp.z) < 1e-6
        && Math.abs(c.bx - gp.x) < 1e-6 && Math.abs(c.bz - gp.z) < 1e-6))
    assert.equal(between.length, 1, '3 parallel edges → exactly 1 cable')
  })

  it('draws exactly one cable per placed node pair, every endpoint on a port', () => {
    const layout = buildStation3dLayout(stationSnap())
    // 14 个唯一节点对 − 电表对（3D 不画电表）= 13 条… 其中 pcs/bms 全覆盖不触发兜底
    // 逐对核对：电网—主断—220kV—主变—35kV—2级变—690V—2PCS—直流母线—2BMS
    assert.equal(layout.cables.length, 12, `cable count = placed pairs, got ${layout.cables.length}`)
    for (const c of layout.cables) {
      assert.equal(c.static, true, 'edge cables are static')
      assert.ok(onPort(layout, c.ax, c.az), `a-end on a port: (${c.ax}, ${c.az})`)
      assert.ok(onPort(layout, c.bx, c.bz), `b-end on a port: (${c.bx}, ${c.bz})`)
    }
  })

  it('terminates every BMS cable on the single BMS port', () => {
    const layout = buildStation3dLayout(stationSnap())
    for (const bms of layout.items.filter(i => i.templateId === 'bms')) {
      const port = devicePort(bms, bms.z - 10)
      const touches = layout.cables.filter(c =>
        (Math.abs(c.ax - bms.x) < 1e-6 && Math.abs(c.az - port.z) < 1e-6)
        || (Math.abs(c.bx - bms.x) < 1e-6 && Math.abs(c.bz - port.z) < 1e-6))
      assert.ok(touches.length >= 1, `bms ${bms.key} has its port cable`)
      for (const c of touches) {
        const end = Math.abs(c.ax - bms.x) < 1e-6 && Math.abs(c.az - port.z) < 1e-6
          ? [c.ax, c.az] : [c.bx, c.bz]
        assert.equal(end[1], port.z, 'BMS cables only use the single port')
      }
    }
  })

  it('classifies roles so energization sync keeps working', () => {
    const layout = buildStation3dLayout(stationSnap())
    const feeds = layout.cables.filter(c => c.role === 'pcs-feed')
    assert.equal(feeds.length, 2, '690V bus → each PCS')
    for (const f of feeds) {
      assert.ok(Number.isFinite(f.unitIndex), 'pcs-feed carries unitIndex')
      assert.ok(f.side, 'pcs-feed carries side')
    }
    const dcs = layout.cables.filter(c => c.role === 'dc-link')
    assert.equal(dcs.length, 4, 'PCS→dc bar drops + dc bar→BMS rises')
    assert.equal(layout.cables.filter(c => c.role === 'sld-wire').length, 6,
      'grid/main breaker/hv bus/main xf/35kV bus/2级变 chain')
    assert.equal(layout.cables.filter(c => c.role === 'unit-drop').length, 0)
  })

  it('clamps dc bus bar anchors when the counterpart sits beyond the bar span', () => {
    const snap = {
      topology: {
        nodes: [
          node('dc1', 'dc_bus', '直流母线', 0, { nominalVoltage: 1200 }),
          node('bms1', 'bms', '远端BMS', 600, {})
        ],
        edges: [edge('dc1', 'bms1')]
      },
      units: []
    }
    const layout = buildStation3dLayout(snap)
    const bar = layout.items.find(i => i.templateId === 'dc_bus')
    assert.ok(bar, 'fallback dc bar drawn')
    const cable = layout.cables.find(c => c.role !== 'pv-dc')
    assert.ok(cable, 'edge cable drawn')
    const barEndX = Math.max(bar.x1, bar.x2)
    // BMS 远在横杠右端之外：横杠侧锚点夹取到端点而不是越过横杠
    const onBarX = Math.abs(cable.ax - barEndX) < 1e-6 ? cable.ax : cable.bx
    assert.ok(Math.abs(onBarX - barEndX) < 1e-6, `clamped to bar end: ${onBarX} vs ${barEndX}`)
  })
})
