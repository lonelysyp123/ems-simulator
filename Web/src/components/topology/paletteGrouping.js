/** 组态左侧调色板：模板按分类分组、搜索过滤。 */

export const PALETTE_CATEGORY_ORDER = ['电源', '母线', '开关', '变电', '测量', '负荷', '无功', '储能', '光伏']

function norm(s) {
  return String(s || '').trim().toLowerCase()
}

function matchesQuery(text, query) {
  if (!query) return true
  return norm(text).includes(query)
}

/** 按固定分类顺序分组；未知分类按名称排在末尾。空分组不返回。 */
export function groupTemplatesByCategory(templates, query = '') {
  const q = norm(query)
  const filtered = (templates || []).filter(t =>
    t.id !== 'pv_unit'
    && (matchesQuery(t.name, q) || matchesQuery(t.category, q) || matchesQuery(t.id, q))
  )

  const buckets = new Map(PALETTE_CATEGORY_ORDER.map(c => [c, []]))
  const extra = new Map()
  for (const t of filtered) {
    const cat = t.category || '其他'
    if (buckets.has(cat)) buckets.get(cat).push(t)
    else {
      if (!extra.has(cat)) extra.set(cat, [])
      extra.get(cat).push(t)
    }
  }

  const groups = []
  for (const category of PALETTE_CATEGORY_ORDER) {
    const items = buckets.get(category)
    if (items.length) groups.push({ category, items })
  }
  for (const category of [...extra.keys()].sort()) {
    groups.push({ category, items: extra.get(category) })
  }
  return groups
}

/** 设备库按名称过滤；空查询返回原列表。 */
export function filterLibraryItems(items, query = '') {
  const q = norm(query)
  if (!q) return items || []
  return (items || []).filter(it => matchesQuery(it.name, q) || matchesQuery(it.templateId, q))
}
