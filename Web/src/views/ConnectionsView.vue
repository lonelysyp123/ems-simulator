<template>
  <div>
    <div class="card">
      <p class="card-title">本机网络接口</p>
      <el-table :data="data?.networkInterfaces || []" size="small" border stripe>
        <el-table-column prop="name" label="接口" width="200" />
        <el-table-column prop="address" label="IPv4 地址" />
      </el-table>
    </div>

    <div class="card">
      <div class="card-title" style="display:flex;align-items:center;justify-content:space-between;gap:12px">
        <span>IEC 61850</span>
        <el-button type="primary" size="small" @click="$router.push('/iec61850')">打开 IEC 61850</el-button>
      </div>
      <p class="muted" style="margin:0 0 8px;font-size:13px">
        {{ data?.iec61850Summary?.headline || '加载中…' }}
      </p>
      <p class="muted" style="margin:0;font-size:12px">
        详细 IED 状态、GOOSE 入向报文与解码请到「IEC 61850」页；端口开关在「协议端口」。
      </p>
    </div>

    <div class="card">
      <p class="card-title">Modbus 服务监听</p>
      <el-table :data="data?.servers || []" size="small" border stripe>
        <el-table-column prop="server" label="服务" width="160" />
        <el-table-column prop="listenInfo" label="监听信息" />
      </el-table>
    </div>

    <div class="card">
      <p class="card-title">协议链路状态</p>
      <el-table :data="data?.linkStatus || []" size="small" border stripe>
        <el-table-column prop="label" label="目标" width="160" />
        <el-table-column prop="serverName" label="服务名" width="140" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.online ? 'success' : 'danger'" size="small">{{ row.online ? '在线' : '离线' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="listenInfo" label="监听" />
        <el-table-column prop="extra" label="备注" />
        <el-table-column label="操作" width="160">
          <template #default="{ row }">
            <el-button size="small" :disabled="row.online" @click="toggle(row.target, 'on')">上线</el-button>
            <el-button size="small" type="danger" :disabled="!row.online" @click="toggle(row.target, 'off')">离线</el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <div class="card">
      <p class="card-title">客户端连接</p>
      <el-table :data="data?.clients || []" size="small" border stripe>
        <el-table-column prop="client" label="客户端" />
        <el-table-column prop="state" label="状态" width="120" />
      </el-table>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getConnections, postLink, joinHubChannel, onHubMethod } from '@/services/api.js'
import { RealtimeMethods, RealtimeChannels } from '@/services/constants.js'
import { ElMessage } from 'element-plus'

const data = ref(null)

async function reload() {
  try { data.value = await getConnections() } catch (e) { console.warn(e) }
}

async function toggle(target, state) {
  try {
    const r = await postLink(target, state)
    ElMessage[r.success ? 'success' : 'error'](r.message)
    await reload()
  } catch (e) {
    ElMessage.error(e.message)
  }
}

onMounted(async () => {
  await reload()
  try {
    onHubMethod(RealtimeMethods.ReceiveConnections, d => { data.value = d })
    await joinHubChannel(RealtimeChannels.Connections)
  } catch { /* ignore */ }
})
</script>

<style scoped>
.muted { color: #909399; }
</style>
