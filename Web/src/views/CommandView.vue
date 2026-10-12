<template>
  <div>
    <div class="card">
      <p class="card-title">命令输入</p>
      <el-input
        v-model="input" placeholder="输入命令：esscmd / breaker / dpc / dpctest，回车执行"
        @keyup.enter="run"
        clearable
      >
        <template #append>
          <el-button @click="run" :loading="loading">执行</el-button>
        </template>
      </el-input>
      <div style="margin-top:8px;color:#909399;font-size:12px">
        示例：
        <el-link @click="setInput('esscmd link status')">esscmd link status</el-link> ·
        <el-link @click="setInput('esscmd setLoad activePower -500')">esscmd setLoad activePower -500</el-link> ·
        <el-link @click="setInput('breaker set true')">breaker set true</el-link> ·
        <el-link @click="setInput('dpc simEmu1.yt0 set 1000')">dpc simEmu1.yt0 set 1000</el-link> ·
        <el-link @click="setInput('dpc simEmu1.yk3 set 1')">dpc simEmu1.yk3 set 1</el-link> ·
        <el-link @click="setInput('dpctest list')">dpctest list</el-link>
      </div>
      <div v-if="history.length" class="cmd-history">
        <div class="cmd-history-head">
          <span>历史指令（最近 {{ history.length }} 条，点击填入）</span>
          <el-button link type="info" size="small" @click="clearHistory">清空</el-button>
        </div>
        <div class="cmd-history-list">
          <button
            v-for="(cmd, i) in history"
            :key="`${i}-${cmd}`"
            type="button"
            class="cmd-history-item"
            :title="cmd"
            @click="setInput(cmd)"
          >{{ cmd }}</button>
        </div>
      </div>
    </div>

    <div class="card">
      <p class="card-title">快捷操作</p>
      <el-space wrap>
        <el-button type="primary" @click="quick('breaker set true')">主断合闸</el-button>
        <el-button type="warning" @click="quick('breaker set false')">主断分闸</el-button>
        <el-button @click="quick('esscmd link status')">链路状态</el-button>
        <el-button @click="quick('dpctest list')">测试用例列表</el-button>
      </el-space>
    </div>

    <div class="card">
      <p class="card-title">自动化测试（autotest.json）</p>
      <el-table :data="tests" size="small" border stripe>
        <el-table-column prop="name" label="测试名" width="200" />
        <el-table-column prop="description" label="说明" />
        <el-table-column label="操作" width="160">
          <template #default="{ row }">
            <el-button size="small" type="primary" @click="runDpcTest(row.name)" :loading="testing === row.name">执行</el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <div class="card">
      <p class="card-title">执行输出</p>
      <div class="log-view" style="height:380px">
        <div v-for="(line, i) in output" :key="i" class="log-line" :class="line.cls">{{ line.text }}</div>
      </div>
    </div>

    <div class="card" v-if="progress.length">
      <p class="card-title">dpctest 进度</p>
      <div class="log-view" style="height:240px">
        <div v-for="(line, i) in progress" :key="i" class="log-line">{{ line }}</div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { postCommand, postDpcTest, getAutoTest, joinHubChannel, leaveHubChannel, onHubMethod, offHubMethod } from '@/services/api.js'
import { RealtimeMethods, RealtimeChannels } from '@/services/constants.js'

const HISTORY_KEY = 'ess-simulator.command-history'
const HISTORY_MAX = 10

const input = ref('')
const loading = ref(false)
const output = ref([])
const progress = ref([])
const tests = ref([])
const testing = ref('')
const history = ref(loadHistory())

function loadHistory() {
  try {
    const raw = localStorage.getItem(HISTORY_KEY)
    if (!raw) return []
    const list = JSON.parse(raw)
    return Array.isArray(list) ? list.filter(x => typeof x === 'string' && x.trim()).slice(0, HISTORY_MAX) : []
  } catch {
    return []
  }
}

function persistHistory() {
  try {
    localStorage.setItem(HISTORY_KEY, JSON.stringify(history.value))
  } catch { /* ignore quota */ }
}

function pushHistory(cmd) {
  const next = [cmd, ...history.value.filter(x => x !== cmd)].slice(0, HISTORY_MAX)
  history.value = next
  persistHistory()
}

function clearHistory() {
  history.value = []
  try { localStorage.removeItem(HISTORY_KEY) } catch { /* ignore */ }
}

function pushOut(text, ok = true) {
  output.value.unshift({ text, cls: ok ? 'level-INFO' : 'level-ERROR' })
  if (output.value.length > 500) output.value.length = 500
}

async function run() {
  const cmd = input.value.trim()
  if (!cmd) return
  pushHistory(cmd)
  loading.value = true
  try {
    const r = await postCommand(cmd)
    const text = r.success ? `✓ ${r.message}` : `✗ ${r.message}`
    pushOut(text, r.success)
  } catch (e) {
    pushOut(`✗ ${e.message}`, false)
  } finally {
    loading.value = false
  }
}

function quick(cmd) { input.value = cmd; run() }
function setInput(cmd) { input.value = cmd }

async function runDpcTest(name) {
  testing.value = name
  progress.value = []
  try {
    const r = await postDpcTest(name)
    pushOut(r.success ? `✓ ${r.message}` : `✗ ${r.message}`, r.success)
  } catch (e) {
    pushOut(`✗ ${e.message}`, false)
  } finally {
    testing.value = ''
  }
}

function onCommandProgress(p) {
  progress.value.push(`${p.time} | ${p.message}`)
  if (progress.value.length > 500) progress.value.length = 500
}

onMounted(async () => {
  try {
    const r = await getAutoTest()
    if (r.ok) tests.value = r.tests || []
  } catch { /* ignore */ }
  try {
    onHubMethod(RealtimeMethods.ReceiveCommandProgress, onCommandProgress)
    await joinHubChannel(RealtimeChannels.CommandProgress)
  } catch { /* ignore */ }
})

onBeforeUnmount(() => {
  offHubMethod(RealtimeMethods.ReceiveCommandProgress, onCommandProgress)
  leaveHubChannel(RealtimeChannels.CommandProgress)
})
</script>

<style scoped>
.cmd-history {
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid #ebeef5;
}
.cmd-history-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: #909399;
  font-size: 12px;
  margin-bottom: 8px;
}
.cmd-history-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.cmd-history-item {
  max-width: 100%;
  padding: 4px 10px;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  background: #f5f7fa;
  color: #606266;
  font-size: 12px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  cursor: pointer;
  text-align: left;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.cmd-history-item:hover {
  border-color: #409eff;
  color: #409eff;
  background: #ecf5ff;
}
</style>
