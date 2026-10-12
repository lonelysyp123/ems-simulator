<template>
  <div class="ems-hub">
    <div class="card ems-hub-banner">
      <div class="ems-hub-bar">
        <span class="card-title" style="margin:0">EMS</span>
        <el-tag size="small" :type="ownerTagType" effect="plain">{{ ownerLabel }}</el-tag>
        <span class="meta">同一时刻只能有一个外部控制源（电站策略 / 第三方遥控）。</span>
        <el-button
          v-if="canRelease"
          size="small"
          :loading="busy"
          @click="release"
        >释放占用</el-button>
        <el-button
          v-if="tab === 'third-party'"
          size="small"
          type="primary"
          plain
          @click="openPopup"
        >新窗口打开</el-button>
      </div>
    </div>

    <el-tabs :model-value="tab" class="ems-hub-tabs" @tab-change="onTab">
      <el-tab-pane label="电站策略" name="strategy" />
      <el-tab-pane label="第三方遥控" name="third-party" />
    </el-tabs>

    <router-view />
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { getEmsStrategy, postEmsStrategyEnable, postThirdPartyEmsDisconnect } from '@/services/api.js'

const route = useRoute()
const router = useRouter()
const busy = ref(false)
const gateOwner = ref('None')
let timer = null

const tab = computed(() => (route.path.includes('third-party') ? 'third-party' : 'strategy'))
const canRelease = computed(() => gateOwner.value === 'EmsStrategy' || gateOwner.value === 'ThirdPartyEms')
const ownerLabel = computed(() => {
  if (gateOwner.value === 'EmsStrategy') return '电站策略占用中'
  if (gateOwner.value === 'ThirdPartyEms') return '第三方 EMS 占用中'
  return '未占用'
})
const ownerTagType = computed(() => {
  if (gateOwner.value === 'EmsStrategy') return 'warning'
  if (gateOwner.value === 'ThirdPartyEms') return 'success'
  return 'info'
})

function onTab(name) {
  router.push(name === 'third-party' ? '/ems/third-party' : '/ems/strategy')
}

function openPopup() {
  const w = window.open('/third-party-ems', 'thirdPartyEms', 'popup=yes,width=1400,height=900,noopener')
  if (!w)
    ElMessage.warning('弹窗被浏览器拦截，请允许后重试')
}

async function refresh() {
  const s = await getEmsStrategy()
  gateOwner.value = s?.gateOwner || 'None'
}

async function release() {
  busy.value = true
  try {
    if (gateOwner.value === 'EmsStrategy') {
      const res = await postEmsStrategyEnable(false)
      ElMessage.success(res.message || '已关闭电站策略')
    } else if (gateOwner.value === 'ThirdPartyEms') {
      const res = await postThirdPartyEmsDisconnect()
      ElMessage.success(res.message || '已释放第三方占用')
    }
    await refresh()
  } catch (e) {
    ElMessage.error(e.message || '释放失败')
  } finally {
    busy.value = false
  }
}

onMounted(async () => {
  try {
    await refresh()
  } catch { /* ignore */ }
  timer = setInterval(() => { refresh().catch(() => {}) }, 1000)
})

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>
