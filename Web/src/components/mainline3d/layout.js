/** 3D 主接线布局常量与坐标计算（Y 向上） */

export const UNIT_SPACING = 22
export const CHANNEL_OFFSET_X = 5.5
/** 光伏方阵比 PCS/BMS 更宽，左右路间距加大避免叠板 */
export const PV_CHANNEL_OFFSET_X = 7.2

/** 主干沿 -Z → +Z：电网 → 主断 → 主变 → 35kV 母线 */
export const Z = {
  grid: -22,
  mainBreaker: -14,
  mainXf: -7,
  bus35: 0,
  unitBreaker: 4,
  unitXf: 9,
  bus690: 13,
  pcs: 18,
  bms: 26,
  pvInverter: 18,
  pvArray: 26
}

export const Y = {
  ground: 0,
  /**
   * 设备间电缆贴地走线高度（管心）
   * 须高于水泥垫/基座顶面 + 最大管径半径，避免刚体段陷入地面
   */
  cable: 0.55,
  bus35: 4.2,
  bus690: 2.4,
  equipment: 1.2,
  label: 6.5
}

/**
 * @param {number|{ essCount?: number, pvCount?: number }} essCountOrOpts
 * @param {number} [pvCount]
 * @returns {{
 *   mainX: number, busStartX: number, busEndX: number,
 *   unitXs: number[], pvXs: number[], spacing: number,
 *   essCount: number, pvCount: number
 * }}
 */
export function computeLayout(essCountOrOpts, pvCount = 0) {
  let essCount = 0
  let pv = pvCount
  if (essCountOrOpts && typeof essCountOrOpts === 'object') {
    essCount = essCountOrOpts.essCount ?? 0
    pv = essCountOrOpts.pvCount ?? 0
  } else {
    essCount = essCountOrOpts
  }
  const e = Math.max(0, essCount | 0)
  const p = Math.max(0, pv | 0)
  const n = e + p
  // 单元多时略压缩间距，避免场景过宽难适配
  const spacing = n >= 16 ? 16 : n >= 10 ? 18 : UNIT_SPACING
  const unitXs = []
  const pvXs = []
  for (let i = 0; i < e; i++) unitXs.push(i * spacing)
  for (let i = 0; i < p; i++) pvXs.push((e + i) * spacing)
  const allXs = unitXs.length || pvXs.length ? [...unitXs, ...pvXs] : [0]
  const busStartX = allXs[0] - 2
  const busEndX = allXs[allXs.length - 1] + 2
  const mainX = allXs[0]
  return { mainX, busStartX, busEndX, unitXs, pvXs, spacing, essCount: e, pvCount: p }
}

export { stationKey } from './project3dLayout.js'

/**
 * @param {number} unitX
 * @param {'A'|'B'} side
 */
export function channelX(unitX, side) {
  return unitX + (side === 'A' ? -CHANNEL_OFFSET_X : CHANNEL_OFFSET_X)
}

/**
 * @param {number} unitX
 * @param {'A'|'B'} side
 */
export function pvChannelX(unitX, side) {
  return unitX + (side === 'A' ? -PV_CHANNEL_OFFSET_X : PV_CHANNEL_OFFSET_X)
}
