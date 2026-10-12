/**
 * 光伏方阵显示规模与占地计算。
 * 与 buildMeshes / project3dLayout 共用，保证“建模样子”和“布局避让”使用同一套尺寸，
 * 避免逆变器排与方阵在 z/x 方向上重叠。
 *
 * 按组态配置 1:1 复刻组件（stringCount × modulesPerString），不截断；
 * 数量多时占地自然变大，场地环境由 bounds 自适应。
 */

/** 单块组件宽（米） */
export const PV_PANEL_W = 0.82
/** 单块组件深（米） */
export const PV_PANEL_D = 1.28
/** 同排组件横向间隙（米） */
export const PV_PANEL_GAP_X = 0.07
/** 相邻排纵向行距（米，不小于倾斜投影深以便行间错落） */
export const PV_ROW_PITCH = 1.05

/**
 * 按组态串数/组件数推算方阵占地尺寸（1:1，不设行列上限）。
 * @param {number|string} [stringCount]
 * @param {number|string} [modulesPerString]
 * @returns {{ rows: number, cols: number, fieldW: number, fieldD: number }}
 */
export function pvArrayFieldSize(stringCount = 0, modulesPerString = 0) {
  const rows = Math.max(0, Math.round(Number(stringCount) || 0))
  const cols = Math.max(0, Math.round(Number(modulesPerString) || 0))
  if (rows <= 0 || cols <= 0) return { rows, cols, fieldW: 1.6, fieldD: 1.2 }
  return {
    rows,
    cols,
    fieldW: cols * (PV_PANEL_W + PV_PANEL_GAP_X) + 0.5,
    fieldD: rows * PV_ROW_PITCH + 0.8
  }
}

/**
 * 每行（串）组件中心 x 位置（相对方阵中心），用于组串出线定位。
 * @param {number} cols 每串组件数（列数）
 * @param {number} fieldW 方阵宽（米）
 * @returns {number[]}
 */
export function pvArrayRowXs(cols, fieldW) {
  const xs = []
  const pitch = PV_PANEL_W + PV_PANEL_GAP_X
  for (let c = 0; c < cols; c++) {
    xs.push(-fieldW / 2 + 0.25 + pitch / 2 + c * pitch)
  }
  return xs
}

/**
 * 第 r 行（串）中心 z 位置（相对方阵中心）。
 * @param {number} rows 串数
 * @param {number} r 行号
 * @returns {number}
 */
export function pvArrayRowZ(rows, r) {
  return (r - (rows - 1) / 2) * PV_ROW_PITCH
}

