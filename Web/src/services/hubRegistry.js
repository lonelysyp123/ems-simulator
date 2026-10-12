/**
 * SignalR 订阅登记：频道与回调与当前连接解耦。
 * 后端重启后连接会换 ConnectionId，groups 丢失；登记表用于 onreconnected / 重建连接时补 Join、补绑定。
 */
export function createHubRegistry() {
  const channels = new Set()
  const handlers = new Map()

  return {
    addChannel(channel) {
      const name = String(channel || '').trim()
      if (name) channels.add(name)
    },
    removeChannel(channel) {
      channels.delete(channel)
    },
    listChannels() {
      return [...channels]
    },
    addHandler(method, fn) {
      if (!method || typeof fn !== 'function') return
      if (!handlers.has(method)) handlers.set(method, new Set())
      handlers.get(method).add(fn)
    },
    removeHandler(method, fn) {
      if (!method) return
      if (typeof fn === 'function') {
        handlers.get(method)?.delete(fn)
        if (handlers.get(method)?.size === 0) handlers.delete(method)
      } else {
        handlers.delete(method)
      }
    },
    listHandlers() {
      const list = []
      for (const [method, set] of handlers) {
        for (const fn of set) list.push({ method, fn })
      }
      return list
    }
  }
}

/**
 * 先登记回调和频道，再 Join。Join 失败（重连中）不得丢掉订阅，否则一次图不再更新。
 */
export async function activateHubSubscription(registry, {
  channel,
  handlers,
  invokeJoin,
  bindHandler
} = {}) {
  for (const [method, fn] of Object.entries(handlers || {})) {
    registry.addHandler(method, fn)
    bindHandler?.(method, fn)
  }
  if (channel) {
    registry.addChannel(channel)
    try {
      await invokeJoin?.(channel)
    } catch {
      /* 由调用方在重连后按 listChannels() 再 Join */
    }
  }
}
