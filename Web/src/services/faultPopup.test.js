import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { collectActiveFaults, diffNewFaults, mergeFaultItems } from './faultPopup.js'

function snap(devices) {
  return { devices }
}

function flag(name, kind, active, label) {
  return { name, kind, active, label: label || name }
}

describe('faultPopup', () => {
  it('collectActiveFaults 只收 PCS/BMS 已触发的 fault 位', () => {
    const items = collectActiveFaults(snap([
      {
        deviceId: 'simEmu1.pcs0',
        deviceType: 'pcs',
        title: 'EMU1 PCS0',
        flags: [
          flag('DriveFault', 'fault', true, '驱动故障'),
          flag('SmokeAlarm', 'alarm', true, '烟雾告警'),
          flag('OverloadProtection', 'protection', true)
        ]
      },
      {
        deviceId: 'simBms1.r0',
        deviceType: 'bms-rack',
        title: 'BMS1 簇0',
        flags: [flag('OvervoltageFault', 'fault', true, '簇过压故障')]
      },
      {
        deviceId: 'other',
        deviceType: 'meter',
        flags: [flag('XFault', 'fault', true)]
      }
    ]))
    assert.deepEqual(items.map(x => x.key), [
      'simEmu1.pcs0:DriveFault',
      'simBms1.r0:OvervoltageFault'
    ])
  })

  it('diffNewFaults 首次快照不弹窗', () => {
    const next = snap([{
      deviceId: 'simEmu1.pcs0',
      deviceType: 'pcs',
      title: 'EMU1 PCS0',
      flags: [flag('DriveFault', 'fault', true, '驱动故障')]
    }])
    assert.deepEqual(diffNewFaults(null, next), [])
  })

  it('diffNewFaults 只报告新出现的故障', () => {
    const prev = snap([{
      deviceId: 'simEmu1.pcs0',
      deviceType: 'pcs',
      title: 'EMU1 PCS0',
      flags: [
        flag('DriveFault', 'fault', true, '驱动故障'),
        flag('IdConflict', 'fault', false, 'ID 冲突')
      ]
    }])
    const next = snap([{
      deviceId: 'simEmu1.pcs0',
      deviceType: 'pcs',
      title: 'EMU1 PCS0',
      flags: [
        flag('DriveFault', 'fault', true, '驱动故障'),
        flag('IdConflict', 'fault', true, 'ID 冲突')
      ]
    }, {
      deviceId: 'simBms1.stack',
      deviceType: 'bms-stack',
      title: 'BMS1 堆',
      flags: [flag('InsulationFault', 'fault', true, '绝缘故障')]
    }])
    const added = diffNewFaults(prev, next)
    assert.deepEqual(added.map(x => x.key).sort(), [
      'simBms1.stack:InsulationFault',
      'simEmu1.pcs0:IdConflict'
    ])
  })

  it('diffNewFaults 故障消失后再出现会再次报告', () => {
    const withFault = snap([{
      deviceId: 'simEmu1.pcs0',
      deviceType: 'pcs',
      flags: [flag('DriveFault', 'fault', true, '驱动故障')]
    }])
    const cleared = snap([{
      deviceId: 'simEmu1.pcs0',
      deviceType: 'pcs',
      flags: [flag('DriveFault', 'fault', false, '驱动故障')]
    }])
    assert.equal(diffNewFaults(withFault, cleared).length, 0)
    const again = diffNewFaults(cleared, withFault)
    assert.equal(again.length, 1)
    assert.equal(again[0].key, 'simEmu1.pcs0:DriveFault')
  })

  it('mergeFaultItems 去重追加', () => {
    const merged = mergeFaultItems(
      [{ key: 'a', label: 'A' }],
      [{ key: 'a', label: 'A2' }, { key: 'b', label: 'B' }]
    )
    assert.deepEqual(merged.map(x => x.key), ['a', 'b'])
    assert.equal(merged[0].label, 'A')
  })
})
