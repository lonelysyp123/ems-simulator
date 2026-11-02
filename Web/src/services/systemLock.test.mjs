import { describe, it } from 'node:test'
import assert from 'node:assert/strict'

import { systemLock, lockSystem, updateSystemProgress, unlockSystem, isSystemLocked } from './systemLock.js'

describe('systemLock', () => {
  it('锁定后阻塞界面并带回默认文案与进度', () => {
    lockSystem()

    assert.equal(isSystemLocked(), true)
    assert.equal(systemLock.locked, true)
    assert.ok(systemLock.message.length > 0)
    assert.equal(systemLock.progress, 5)
  })

  it('进度被夹在 0–100 并取整', () => {
    lockSystem('重建中', -10)
    assert.equal(systemLock.progress, 0)

    updateSystemProgress(150)
    assert.equal(systemLock.progress, 100)

    updateSystemProgress(42.6)
    assert.equal(systemLock.progress, 43)

    updateSystemProgress('abc')
    assert.equal(systemLock.progress, 0)
  })

  it('阶段文案缺省时回落到消息本身', () => {
    lockSystem('等待重启')
    assert.equal(systemLock.stage, '等待重启')

    lockSystem('等待重启', 30, '提交配置')
    assert.equal(systemLock.stage, '提交配置')
  })

  it('更新时只覆盖传入的字段', () => {
    lockSystem('初始化', 10, '提交配置')

    updateSystemProgress(60)
    assert.equal(systemLock.progress, 60)
    assert.equal(systemLock.message, '初始化')

    updateSystemProgress(undefined, '重启中')
    assert.equal(systemLock.message, '重启中')
    assert.equal(systemLock.progress, 60)
  })

  it('解锁后恢复交互', () => {
    lockSystem()
    assert.equal(isSystemLocked(), true)

    unlockSystem()
    assert.equal(isSystemLocked(), false)
    assert.equal(systemLock.locked, false)
  })
})
