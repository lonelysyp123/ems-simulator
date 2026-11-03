import { describe, it } from 'node:test'
import assert from 'node:assert/strict'

import {
  PV_PANEL_W,
  PV_PANEL_GAP_X,
  PV_ROW_PITCH,
  pvArrayFieldSize,
  pvArrayRowXs,
  pvArrayRowZ
} from './pvArrayLayout.js'

const PITCH_X = PV_PANEL_W + PV_PANEL_GAP_X

describe('pvArrayFieldSize', () => {
  it('按组串串数与每串组件数还原占地', () => {
    const size = pvArrayFieldSize(20, 26)
    assert.equal(size.rows, 20)
    assert.equal(size.cols, 26)
    assert.equal(size.fieldW, 26 * PITCH_X + 0.5)
    assert.equal(size.fieldD, 20 * PV_ROW_PITCH + 0.8)
  })

  it('组件越多方阵越大（不截断）', () => {
    const small = pvArrayFieldSize(10, 10)
    const big = pvArrayFieldSize(20, 20)
    assert.ok(big.fieldW > small.fieldW)
    assert.ok(big.fieldD > small.fieldD)
  })

  it('串数或组件数为 0 时退回默认场地，不产生零尺寸', () => {
    for (const [strings, modules] of [[0, 0], [0, 26], [20, 0]]) {
      const size = pvArrayFieldSize(strings, modules)
      assert.ok(size.fieldW > 0)
      assert.ok(size.fieldD > 0)
      assert.equal(size.fieldW, 1.6)
      assert.equal(size.fieldD, 1.2)
    }
  })

  it('负值与非数字按 0 处理，不抛异常', () => {
    for (const bad of [-5, 'abc', null, undefined, NaN]) {
      const size = pvArrayFieldSize(bad, bad)
      assert.ok(size.rows >= 0)
      assert.ok(size.cols >= 0)
      assert.ok(Number.isFinite(size.fieldW))
    }
  })
})

describe('pvArrayRowXs', () => {
  it('列数与间距与组件尺寸一致', () => {
    const { cols, fieldW } = pvArrayFieldSize(10, 26)
    const xs = pvArrayRowXs(cols, fieldW)

    assert.equal(xs.length, cols)
    for (let i = 1; i < xs.length; i++)
      assert.ok(Math.abs(xs[i] - xs[i - 1] - PITCH_X) < 1e-9, `第 ${i} 列间距应为 ${PITCH_X}`)
  })

  it('整排组件落在方阵边界内', () => {
    const { cols, fieldW } = pvArrayFieldSize(10, 26)
    const xs = pvArrayRowXs(cols, fieldW)
    const half = fieldW / 2

    // 组件有实际宽度，中心点到边界至少要留半个组件
    assert.ok(xs[0] - PV_PANEL_W / 2 >= -half - 1e-9)
    assert.ok(xs[xs.length - 1] + PV_PANEL_W / 2 <= half + 1e-9)
  })

  it('0 列返回空数组', () => {
    assert.deepEqual(pvArrayRowXs(0, 10), [])
  })
})

describe('pvArrayRowZ', () => {
  it('各行关于方阵中心对称', () => {
    const rows = 20
    let sum = 0
    for (let r = 0; r < rows; r++)
      sum += pvArrayRowZ(rows, r)

    assert.ok(Math.abs(sum) < 1e-9, `行中心之和应为 0，实际 ${sum}`)
  })

  it('行间距等于 PV_ROW_PITCH，行号从负到正递增', () => {
    const rows = 10
    assert.ok(Math.abs(pvArrayRowZ(rows, 1) - pvArrayRowZ(rows, 0) - PV_ROW_PITCH) < 1e-9)
    assert.ok(pvArrayRowZ(rows, 9) > pvArrayRowZ(rows, 0))
  })

  it('单行时居中在 0', () => {
    assert.equal(pvArrayRowZ(1, 0), 0)
  })
})
