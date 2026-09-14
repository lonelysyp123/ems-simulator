import axios from 'axios'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { createHubRegistry } from './hubRegistry.js'

const api = axios.create({ baseURL: '/api', timeout: 10000 })

api.interceptors.response.use(
  r => r,
  err => {
    const msg = err?.response?.data?.message || err.message
    return Promise.reject(new Error(msg))
  }
)

export async function getHealth() { return (await api.get('/health')).data }
export async function getMainLine() { return (await api.get('/mainline')).data }
export async function getBattery(unit) { return (await api.get(`/battery/${unit}`)).data }
export async function getCells(unit, cluster) { return (await api.get(`/cells/${unit}/${cluster}`)).data }
export async function getRackThresholds(unit, rack = 0) {
  return (await api.get(`/bms/${unit}/rack-thresholds`, { params: { rack } })).data
}
export async function postRackThresholds(unit, body) {
  return (await api.post(`/bms/${unit}/rack-thresholds`, body)).data
}
export async function getAlarms() { return (await api.get('/alarms')).data }
export async function getBmsAlarms(unit, rack) {
  return (await api.get(`/alarms/bms/${unit}`, { params: rack == null ? {} : { rack } })).data
}
export async function getConnections() { return (await api.get('/connections')).data }
export async function getAlert() { return (await api.get('/alert')).data }
export async function getConfig() { return (await api.get('/config')).data }
export async function getProtocol() { return (await api.get('/protocol')).data }
export async function getAutoTest() { return (await api.get('/autotest')).data }
export async function getPointMaps() { return (await api.get('/pointmaps')).data }
export async function postCommand(input) { return (await api.post('/command', { input })).data }
export async function postLink(target, state) { return (await api.post(`/link/${target}/${state}`)).data }
export async function postMainBreaker(closed) { return (await api.post(`/breaker/main/${closed}`)).data }
export async function postUnitBreaker(unit, closed) { return (await api.post(`/breaker/unit/${unit}/${closed}`)).data }
export async function postDpcTest(name) { return (await api.post(`/dpctest/${name}`)).data }

export async function getDroopSliceStatus() { return (await api.get('/droop-slices/status')).data }
export async function getDroopSlices(limit = 100, offset = 0) {
  return (await api.get('/droop-slices', { params: { limit, offset } })).data
}
export async function getDroopSlice(id) { return (await api.get(`/droop-slices/${id}`)).data }
export async function clearDroopSlices() { return (await api.post('/droop-slices/clear')).data }
export async function setDroopSliceConfig(body) {
  return (await api.post('/droop-slices/config', body)).data
}

// 组态编辑
export async function getTopologyTemplates() { return (await api.get('/topology/templates')).data }
export async function getTopologyProject() { return (await api.get('/topology/project')).data }
export async function putTopologyProject(project) { return (await api.put('/topology/project', project)).data }
export async function postTopologyValidate(project) { return (await api.post('/topology/validate', project)).data }
export async function postTopologyConnect(body) { return (await api.post('/topology/connect', body)).data }
export async function postTopologyDisconnect(body) { return (await api.post('/topology/disconnect', body)).data }
export async function postTopologyScaffold(body) { return (await api.post('/topology/scaffold', body)).data }
export async function getTopologyLibrary() { return (await api.get('/topology/library')).data }
export async function putTopologyLibrary(item) { return (await api.put('/topology/library', item)).data }
export async function deleteTopologyLibrary(id) { return (await api.delete(`/topology/library/${id}`)).data }
export async function getTopologyPaths() { return (await api.get('/topology/paths')).data }
export async function getTopologyProjects() { return (await api.get('/topology/projects')).data }
export async function getTopologyProjectById(id) { return (await api.get(`/topology/projects/${id}`)).data }
export async function postTopologyProjectNew(body = {}) { return (await api.post('/topology/projects/new', body)).data }
export async function postTopologyProjectOpen(id) { return (await api.post(`/topology/projects/${id}/open`)).data }
export async function postTopologyProjectCopy(id, name) { return (await api.post(`/topology/projects/${id}/copy`, { name: name || undefined })).data }
export async function deleteTopologyProject(id) { return (await api.delete(`/topology/projects/${id}`)).data }
export async function checkTopologyProjectName(name, excludeId) {
  return (await api.get('/topology/projects/check-name', { params: { name, excludeId: excludeId || undefined } })).data
}

export async function getSystemConfig() { return (await api.get('/system/config')).data }
export async function postSystemApply(body) { return (await api.post('/system/apply', body)).data }
export async function getDeviceModels() { return (await api.get('/system/device-models')).data }
export async function postDeviceModelsApply(body) { return (await api.post('/system/device-models/apply', body)).data }

// 协议端口配置
export async function getProtocolPorts() { return (await api.get('/protocol-ports')).data }
export async function putProtocolPorts(entries) { return (await api.put('/protocol-ports', { entries })).data }
export async function postProtocolPortsApply() { return (await api.post('/protocol-ports/apply')).data }
export async function postProtocolPortsReset(rebuild = false) {
  return (await api.post('/protocol-ports/reset', { rebuild })).data
}
export async function getModbusAllowlist() { return (await api.get('/protocol-ports/allowlist')).data }
export async function putModbusAllowlist(body) { return (await api.put('/protocol-ports/allowlist', body)).data }

export async function getIec61850() { return (await api.get('/iec61850')).data }
export async function getIec61850Messages(server, limit = 200) {
  return (await api.get('/iec61850/messages', { params: { server: server || undefined, limit } })).data
}
export async function clearIec61850Messages() {
  return (await api.post('/iec61850/messages/clear')).data
}
export async function getProtocolBindings() { return (await api.get('/iec61850/bindings')).data }
export async function putProtocolBindings(entries, rebuild = true) {
  return (await api.put('/iec61850/bindings', { entries, rebuild })).data
}

export async function getThirdPartyEms() { return (await api.get('/third-party-ems')).data }
export async function postThirdPartyEmsConnect(name) {
  return (await api.post('/third-party-ems/connect', { name: name || undefined })).data
}
export async function postThirdPartyEmsDisconnect(name) {
  return (await api.post('/third-party-ems/disconnect', { name: name || undefined })).data
}
export async function postThirdPartyEmsPower(name, activePowerKw, reactivePowerKvar) {
  return (await api.post('/third-party-ems/power', { name, activePowerKw, reactivePowerKvar })).data
}
export async function postThirdPartyEmsRemote(name, enable, mode) {
  return (await api.post('/third-party-ems/remote', { name, enable, mode })).data
}
export async function postThirdPartyEmsOperation(name, operation) {
  return (await api.post('/third-party-ems/operation', { name, operation })).data
}

export async function getEmsStrategy() { return (await api.get('/ems-strategy')).data }
export async function postEmsStrategyEnable(enabled) {
  return (await api.post('/ems-strategy/enable', { enabled })).data
}
export async function postEmsStrategy(body) {
  return (await api.post('/ems-strategy', body)).data
}

const hubRegistry = createHubRegistry()
let connection = null
let startPromise = null
/** 当前已把登记表绑到该连接；避免 App 与页面并行 onMounted 时重复 conn.on */
let handlersBoundTo = null

function buildHubConnection() {
  const conn = new HubConnectionBuilder()
    .withUrl('/hub/realtime')
    // 系统重新初始化会停后端数十秒，默认 4 次重试约 42s 后放弃，一次图就再也收不到推送
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => 2000 })
    .configureLogging(LogLevel.Warning)
    .build()

  conn.onreconnected(async () => {
    await rejoinRegisteredChannels(conn)
  })
  conn.onclose(() => {
    if (connection === conn) {
      connection = null
      startPromise = null
      if (handlersBoundTo === conn) handlersBoundTo = null
    }
  })
  return conn
}

function bindRegisteredHandlers(conn) {
  for (const { method, fn } of hubRegistry.listHandlers()) {
    conn.on(method, fn)
  }
  handlersBoundTo = conn
}

async function rejoinRegisteredChannels(conn) {
  if (!conn || conn.state !== HubConnectionState.Connected) return
  for (const channel of hubRegistry.listChannels()) {
    try {
      await conn.invoke('JoinChannel', channel)
    } catch { /* 下一轮重连再试 */ }
  }
}

export function getHub() {
  if (connection && connection.state === HubConnectionState.Disconnected) {
    connection = null
    startPromise = null
    handlersBoundTo = null
  }
  if (!startPromise) {
    const conn = buildHubConnection()
    connection = conn
    bindRegisteredHandlers(conn)
    startPromise = conn.start()
      .then(async () => {
        await rejoinRegisteredChannels(conn)
        return conn
      })
      .catch(err => {
        if (connection === conn) {
          connection = null
          startPromise = null
          if (handlersBoundTo === conn) handlersBoundTo = null
        }
        throw err
      })
  }
  return startPromise
}

export function onHubMethod(method, fn) {
  hubRegistry.addHandler(method, fn)
  if (connection && handlersBoundTo === connection) {
    connection.on(method, fn)
  }
}

export function offHubMethod(method, fn) {
  hubRegistry.removeHandler(method, fn)
  if (connection) {
    if (fn) connection.off(method, fn)
    else connection.off(method)
  }
}

export async function joinHubChannel(channel) {
  hubRegistry.addChannel(channel)
  try {
    const conn = await getHub()
    if (conn.state === HubConnectionState.Connected) {
      await conn.invoke('JoinChannel', channel)
    }
  } catch { /* 重连后 onreconnected 会按登记表补 Join */ }
}

export async function leaveHubChannel(channel) {
  hubRegistry.removeChannel(channel)
  try {
    if (connection && connection.state === HubConnectionState.Connected) {
      await connection.invoke('LeaveChannel', channel)
    }
  } catch { /* ignore */ }
}

export { api }
