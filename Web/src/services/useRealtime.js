import { onMounted, onBeforeUnmount, ref } from 'vue'
import { joinHubChannel, leaveHubChannel, offHubMethod, onHubMethod } from './api.js'
import { RealtimeMethods, RealtimeChannels } from './constants.js'

/// 订阅 SignalR 频道并绑定回调，组件卸载时自动退订。
/// 回调与频道登记在连接对象之外：后端重启换 ConnectionId 后会自动补 Join。
/// channel: 频道名（RealtimeChannels）；extra: 额外 group 后缀（如电池单元号）
/// handlers: { methodName: callback }
export function useRealtime(channel, handlers, extra = '') {
  const connected = ref(false)
  const groupName = extra ? `${channel}.${extra}` : channel
  const entries = Object.entries(handlers || {})

  onMounted(async () => {
    try {
      for (const [method, cb] of entries) onHubMethod(method, cb)
      await joinHubChannel(groupName)
      connected.value = true
    } catch (e) {
      console.warn('SignalR 连接失败', e)
    }
  })

  onBeforeUnmount(async () => {
    for (const [method, cb] of entries) offHubMethod(method, cb)
    await leaveHubChannel(groupName)
  })

  return { connected }
}

export { RealtimeMethods, RealtimeChannels }
