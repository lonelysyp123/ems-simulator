<template>
  <div>
    <div class="card">
      <p class="card-title">电站 EMS 策略</p>
      <p class="hint">
        站级 PPC：基值经功率路径（斜率 → 限幅 → 滤波 → PID → 分配）下发到各 PCS；一次调频 / 惯量 / 调压为可选叠加。
        启用后占用控制权；与第三方遥控互斥。路径与策略使能立即生效，明细按卡「应用」后热更新并落盘。功率放电为正、充电为负。
      </p>
      <el-alert
        v-if="blockedByThirdParty"
        title="第三方 EMS 占用中，无法启用或改参。请先释放第三方占用。"
        type="warning"
        :closable="false"
        show-icon
      />
      <el-alert
        v-else-if="status.enabled"
        title="EMS 策略已占用控制权：外部下发均被拒绝，直到关闭。"
        type="info"
        :closable="false"
        show-icon
      />
      <div class="toolbar">
        <el-switch
          v-model="enabled"
          active-text="策略启用"
          inactive-text="策略关闭"
          :loading="busy"
          :disabled="busy || blockedByThirdParty"
          @change="onToggleEnabled"
        />
        <el-switch v-model="systemSwitch" active-text="系统开关" :disabled="busy || blockedByThirdParty" @change="onPatch" />
        <el-switch v-model="activeEnable" active-text="有功使能" :disabled="busy || blockedByThirdParty" @change="onPatch" />
        <el-switch v-model="reactiveEnable" active-text="无功使能" :disabled="busy || blockedByThirdParty" @change="onPatch" />
        <el-select v-model="localRemote" size="small" style="width:120px" :disabled="busy || blockedByThirdParty" @change="onPatch">
          <el-option :value="0" label="本地设定" />
          <el-option :value="1" label="远程设定" />
        </el-select>
      </div>
    </div>

    <div class="card">
      <div class="ems-card-head">
        <p class="card-title">功率设定</p>
        <el-button type="primary" size="small" :disabled="paramsLocked" :loading="busy" @click="issueSetpoint">下发</el-button>
      </div>
      <div class="toolbar">
        <span class="meta">有功</span>
        <el-select v-model="activeMode" size="small" style="width:140px" :disabled="paramsLocked">
          <el-option :value="0" label="开环固定值" />
          <el-option :value="1" label="闭环固定值" />
        </el-select>
        <span class="meta">本地 P kW</span>
        <el-input-number v-model="localP" size="small" :step="50" controls-position="right" :disabled="paramsLocked || localRemote === 1" />
        <span class="meta">远程 P kW</span>
        <el-input-number v-model="remoteP" size="small" :step="50" controls-position="right" :disabled="paramsLocked || localRemote === 0" />
      </div>
      <div class="toolbar">
        <span class="meta">无功</span>
        <el-select v-model="reactiveMode" size="small" style="width:140px" :disabled="paramsLocked">
          <el-option :value="0" label="开环固定值" />
          <el-option :value="1" label="闭环固定值" />
          <el-option :value="3" label="功率因数" />
          <el-option :value="4" label="恒压" />
        </el-select>
        <span class="meta">本地 Q kvar</span>
        <el-input-number v-model="localQ" size="small" :step="50" controls-position="right" :disabled="paramsLocked || localRemote === 1" />
        <span class="meta">远程 Q kvar</span>
        <el-input-number v-model="remoteQ" size="small" :step="50" controls-position="right" :disabled="paramsLocked || localRemote === 0" />
        <span class="meta">目标 PF</span>
        <el-input-number v-model="pfSet" size="small" :min="0.1" :max="1" :step="0.01" :disabled="paramsLocked || reactiveMode !== 3" />
        <span class="meta">恒压设定 V</span>
        <el-input-number v-model="voltageSetV" size="small" :step="100" :disabled="paramsLocked || reactiveMode !== 4" />
      </div>
    </div>

    <div class="card">
      <p class="card-title">功率路径</p>
      <p class="hint">关 = 旁路该级。开环不走 PID、不加策略。滤波为视在圆超限时的 0.5 阻尼。</p>
      <div class="ems-path-flow">
        <span class="ems-path-node is-static">设定</span>
        <span class="ems-path-arrow">→</span>
        <button type="button" class="ems-path-node" :class="pathNodeClass('slope', slopeStageOn)" @click="openPath('slope')">斜率</button>
        <span class="ems-path-arrow">→</span>
        <button type="button" class="ems-path-node" :class="pathNodeClass('limit', apparentLimitEnabled)" @click="openPath('limit')">限幅</button>
        <span class="ems-path-arrow">→</span>
        <button type="button" class="ems-path-node" :class="pathNodeClass('damp', dampEnabled)" @click="openPath('damp')">滤波</button>
        <span class="ems-path-arrow">→</span>
        <button type="button" class="ems-path-node" :class="pathNodeClass('pid', pidStageOn)" @click="openPath('pid')">PID</button>
        <span class="ems-path-arrow">→</span>
        <button type="button" class="ems-path-node" :class="pathNodeClass('dist', drafts.distribution.enabled)" @click="openPath('dist')">分配</button>
        <span class="ems-path-arrow">→</span>
        <span class="ems-path-node is-static">PCS</span>
      </div>
      <el-collapse v-model="pathOpen" class="ems-param-collapse">
        <el-collapse-item name="slope">
          <template #title>
            <span>斜率</span>
            <span v-if="dirtySlope" class="ems-dirty">未应用</span>
            <span class="ems-title-switches" @click.stop>
              <el-switch v-model="drafts.slope.enabled" active-text="有功" :disabled="paramsLocked" @change="v => onScalar('slopeEnabled', v, () => { applied.slope.enabled = v })" />
              <el-switch v-model="drafts.reactiveSlope.enabled" active-text="无功" :disabled="paramsLocked" @change="v => onScalar('reactiveSlopeEnabled', v, () => { applied.reactiveSlope.enabled = v })" />
            </span>
          </template>
          <p class="hint">速率与 C 一致：kW/min（JSON 字段名仍是 riseKwPerSec）。基准是并网点/储能实测，不是上次指令。</p>
          <div class="ems-axis-grid">
            <div>
              <p class="ems-axis-label">有功</p>
              <div class="ems-param-grid">
                <label class="ems-param">上升 kW/min<el-input-number v-model="drafts.slope.riseKwPerSec" size="small" :disabled="paramsLocked" /></label>
                <label class="ems-param">下降 kW/min<el-input-number v-model="drafts.slope.fallKwPerSec" size="small" :disabled="paramsLocked" /></label>
              </div>
            </div>
            <div>
              <p class="ems-axis-label">无功</p>
              <div class="ems-param-grid">
                <label class="ems-param">上升 kvar/min<el-input-number v-model="drafts.reactiveSlope.riseKwPerSec" size="small" :disabled="paramsLocked" /></label>
                <label class="ems-param">下降 kvar/min<el-input-number v-model="drafts.reactiveSlope.fallKwPerSec" size="small" :disabled="paramsLocked" /></label>
              </div>
            </div>
          </div>
          <el-button size="small" type="primary" :disabled="paramsLocked || !dirtySlope" :loading="busy" @click="applySlope">应用</el-button>
        </el-collapse-item>

        <el-collapse-item name="limit">
          <template #title>
            <span>视在限幅</span>
            <span v-if="dirtyLimit" class="ems-dirty">未应用</span>
            <span class="ems-title-switches" @click.stop>
              <el-switch v-model="apparentLimitEnabled" active-text="使能" :disabled="paramsLocked" @change="v => onScalar('apparentLimitEnabled', v)" />
            </span>
          </template>
          <div class="ems-param-grid">
            <label class="ems-param">视在额定 kVA<el-input-number v-model="drafts.plant.apparentRatedKva" size="small" :disabled="paramsLocked" /></label>
            <label class="ems-param">站额定 kW<el-input-number v-model="drafts.plant.plantRatedKw" size="small" :disabled="paramsLocked" /></label>
          </div>
          <el-button size="small" type="primary" :disabled="paramsLocked || !dirtyLimit" :loading="busy" @click="applyLimit">应用</el-button>
        </el-collapse-item>

        <el-collapse-item name="damp">
          <template #title>
            <span>滤波</span>
            <span class="ems-title-switches" @click.stop>
              <el-switch v-model="dampEnabled" active-text="使能" :disabled="paramsLocked" @change="v => onScalar('dampEnabled', v)" />
            </span>
          </template>
          <p class="hint">超视在圆且仍远离实测时，按 0.5 阻尼贴近并网点/储能实测。关闭则限幅结果原样下传。</p>
        </el-collapse-item>

        <el-collapse-item name="pid" :disabled="activeMode === 0 && reactiveMode === 0">
          <template #title>
            <span>PID</span>
            <span v-if="dirtyPid" class="ems-dirty">未应用</span>
            <span class="ems-title-switches" @click.stop>
              <el-switch v-model="drafts.activePid.enabled" active-text="有功" :disabled="paramsLocked || activeMode === 0" @change="v => onScalar('activePidEnabled', v, () => { applied.activePid.enabled = v })" />
              <el-switch v-model="drafts.reactivePid.enabled" active-text="无功" :disabled="paramsLocked || reactiveMode === 0" @change="v => onScalar('reactivePidEnabled', v, () => { applied.reactivePid.enabled = v })" />
            </span>
          </template>
          <p class="hint">开环不走 PI。关闭使能时闭环把滤波后目标直接当下发。兼容周期按 Period 积分；Dt 按仿真步长。</p>
          <div class="ems-axis-grid">
            <div>
              <p class="ems-axis-label">有功</p>
              <div class="ems-param-grid">
                <label class="ems-param">Kp<el-input-number v-model="drafts.activePid.kp" size="small" :step="0.05" :disabled="paramsLocked || activeMode === 0" /></label>
                <label class="ems-param">Ki<el-input-number v-model="drafts.activePid.ki" size="small" :step="0.01" :disabled="paramsLocked || activeMode === 0" /></label>
                <label class="ems-param">Kb<el-input-number v-model="drafts.activePid.kb" size="small" :step="0.05" :disabled="paramsLocked || activeMode === 0" /></label>
                <label class="ems-param">周期<el-input v-model="drafts.activePid.period" size="small" :disabled="paramsLocked || activeMode === 0" placeholder="00:00:03" /></label>
                <label class="ems-param">死区 kW<el-input-number v-model="drafts.activePid.deadbandKw" size="small" :disabled="paramsLocked || activeMode === 0" /></label>
                <label class="ems-param">输出下限<el-input-number v-model="drafts.activePid.outMinKw" size="small" :disabled="paramsLocked || activeMode === 0" /></label>
                <label class="ems-param">输出上限<el-input-number v-model="drafts.activePid.outMaxKw" size="small" :disabled="paramsLocked || activeMode === 0" /></label>
                <label class="ems-param">离散化
                  <el-select v-model="drafts.activePid.discretization" size="small" :disabled="paramsLocked || activeMode === 0">
                    <el-option :value="0" label="兼容周期" />
                    <el-option :value="1" label="按 dt" />
                  </el-select>
                </label>
              </div>
            </div>
            <div>
              <p class="ems-axis-label">无功</p>
              <div class="ems-param-grid">
                <label class="ems-param">Kp<el-input-number v-model="drafts.reactivePid.kp" size="small" :step="0.05" :disabled="paramsLocked || reactiveMode === 0" /></label>
                <label class="ems-param">Ki<el-input-number v-model="drafts.reactivePid.ki" size="small" :step="0.01" :disabled="paramsLocked || reactiveMode === 0" /></label>
                <label class="ems-param">Kb<el-input-number v-model="drafts.reactivePid.kb" size="small" :step="0.05" :disabled="paramsLocked || reactiveMode === 0" /></label>
                <label class="ems-param">周期<el-input v-model="drafts.reactivePid.period" size="small" :disabled="paramsLocked || reactiveMode === 0" placeholder="00:00:03" /></label>
                <label class="ems-param">死区<el-input-number v-model="drafts.reactivePid.deadbandKw" size="small" :disabled="paramsLocked || reactiveMode === 0" /></label>
                <label class="ems-param">输出下限<el-input-number v-model="drafts.reactivePid.outMinKw" size="small" :disabled="paramsLocked || reactiveMode === 0" /></label>
                <label class="ems-param">输出上限<el-input-number v-model="drafts.reactivePid.outMaxKw" size="small" :disabled="paramsLocked || reactiveMode === 0" /></label>
                <label class="ems-param">离散化
                  <el-select v-model="drafts.reactivePid.discretization" size="small" :disabled="paramsLocked || reactiveMode === 0">
                    <el-option :value="0" label="兼容周期" />
                    <el-option :value="1" label="按 dt" />
                  </el-select>
                </label>
              </div>
            </div>
          </div>
          <el-button size="small" type="primary" :disabled="paramsLocked || !dirtyPid" :loading="busy" @click="applyPid">应用</el-button>
        </el-collapse-item>

        <el-collapse-item name="dist">
          <template #title>
            <span>功率分配</span>
            <span v-if="dirtyDist" class="ems-dirty">未应用</span>
            <span class="ems-title-switches" @click.stop>
              <el-switch v-model="drafts.distribution.enabled" active-text="使能" :disabled="paramsLocked" @change="v => onScalar('distributionEnabled', v, () => { applied.distribution.enabled = v })" />
            </span>
          </template>
          <p class="hint">关闭时按额定均分到支路；打开后可启用 SOC 均衡。</p>
          <div class="ems-param-grid">
            <label class="ems-param">SOC 均衡<el-switch v-model="drafts.distribution.socBalance" :disabled="paramsLocked || !drafts.distribution.enabled" /></label>
            <label class="ems-param">SOC 下限<el-input-number v-model="drafts.distribution.socMin" size="small" :min="0" :max="1" :step="0.05" :disabled="paramsLocked || !drafts.distribution.enabled" /></label>
            <label class="ems-param">SOC 上限<el-input-number v-model="drafts.distribution.socMax" size="small" :min="0" :max="1" :step="0.05" :disabled="paramsLocked || !drafts.distribution.enabled" /></label>
          </div>
          <el-button size="small" type="primary" :disabled="paramsLocked || !dirtyDist" :loading="busy" @click="applyDist">应用</el-button>
        </el-collapse-item>
      </el-collapse>
    </div>

    <div class="card">
      <p class="card-title">辅助策略</p>
      <el-collapse v-model="strategyOpen" class="ems-param-collapse">
        <el-collapse-item name="pfr" :disabled="activeMode === 0">
          <template #title>
            <span>一次调频</span>
            <span v-if="dirtyPfr" class="ems-dirty">未应用</span>
            <span v-if="inertiaLockingPfr" class="ems-dirty">已被惯量闭锁</span>
            <span class="ems-title-switches" @click.stop>
              <el-switch v-model="pfrEnabled" active-text="使能" :disabled="paramsLocked || activeMode === 0" @change="onPatchStrategy" />
            </span>
          </template>
          <p class="hint">仅闭环有功叠加 ΔP。开环本拍不加调频。</p>
          <div class="ems-param-grid">
            <label class="ems-param">额定频率 Hz<el-input-number v-model="drafts.pfr.ratedFrequencyHz" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">死区1 %<el-input-number v-model="drafts.pfr.deadband1Percent" size="small" :step="0.05" :disabled="paramsLocked" /></label>
            <label class="ems-param">死区2 %<el-input-number v-model="drafts.pfr.deadband2Percent" size="small" :step="0.05" :disabled="paramsLocked" /></label>
            <label class="ems-param">droop %<el-input-number v-model="drafts.pfr.droopPercent" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">droop2 %<el-input-number v-model="drafts.pfr.droop2Percent" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">过频 droop %<el-input-number v-model="drafts.pfr.overDroopPercent" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">欠频 droop %<el-input-number v-model="drafts.pfr.underDroopPercent" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">段数
              <el-select v-model="drafts.pfr.segmentCount" size="small" :disabled="paramsLocked">
                <el-option :value="3" label="三段" />
                <el-option :value="5" label="五段" />
              </el-select>
            </label>
            <label class="ems-param">过频投<el-switch v-model="drafts.pfr.overFreqEnable" :disabled="paramsLocked" /></label>
            <label class="ems-param">欠频投<el-switch v-model="drafts.pfr.underFreqEnable" :disabled="paramsLocked" /></label>
            <label class="ems-param">控制周期<el-input v-model="drafts.pfr.controlCycle" size="small" :disabled="paramsLocked" placeholder="00:00:01" /></label>
            <label class="ems-param">复归时间<el-input v-model="drafts.pfr.resetTime" size="small" :disabled="paramsLocked" placeholder="00:00:02" /></label>
            <label class="ems-param">最大输出 kW<el-input-number v-model="drafts.pfr.maxOutputKw" size="small" :disabled="paramsLocked" /></label>
            <label class="ems-param">最大吸收 kW<el-input-number v-model="drafts.pfr.maxAbsorbKw" size="small" :disabled="paramsLocked" /></label>
            <label class="ems-param">限幅系数<el-input-number v-model="drafts.pfr.limitCoefficient" size="small" :step="0.1" :disabled="paramsLocked" /></label>
          </div>
          <el-button size="small" type="primary" :disabled="paramsLocked || activeMode === 0 || !dirtyPfr" :loading="busy" @click="applyPfr">应用</el-button>
        </el-collapse-item>

        <el-collapse-item name="inertia" :disabled="activeMode === 0">
          <template #title>
            <span>惯量</span>
            <span v-if="dirtyInertia" class="ems-dirty">未应用</span>
            <span class="ems-title-switches" @click.stop>
              <el-switch v-model="inertiaEnabled" active-text="使能" :disabled="paramsLocked || activeMode === 0" @change="onPatchStrategy" />
            </span>
          </template>
          <p class="hint">仅闭环有功叠加 ΔP。ACTION 且勾选闭锁时跳过一次调频。</p>
          <div class="ems-param-grid">
            <label class="ems-param">闭锁一次调频<el-switch v-model="drafts.inertia.lockPrimaryFrequency" :disabled="paramsLocked" /></label>
            <label class="ems-param">额定频率 Hz<el-input-number v-model="drafts.inertia.ratedFrequencyHz" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">惯性时间 Tj s<el-input-number v-model="drafts.inertia.inertiaTimeSec" size="small" :step="0.5" :disabled="paramsLocked" /></label>
            <label class="ems-param">幅度死区 Hz<el-input-number v-model="drafts.inertia.amplitudeDeadbandHz" size="small" :step="0.01" :disabled="paramsLocked" /></label>
            <label class="ems-param">速率死区 Hz/s<el-input-number v-model="drafts.inertia.rateDeadbandHzPerSec" size="small" :step="0.01" :disabled="paramsLocked" /></label>
            <label class="ems-param">频率下限 Hz<el-input-number v-model="drafts.inertia.freqMinHz" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">频率上限 Hz<el-input-number v-model="drafts.inertia.freqMaxHz" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">控制周期<el-input v-model="drafts.inertia.controlCycle" size="small" :disabled="paramsLocked" placeholder="00:00:00.200" /></label>
            <label class="ems-param">复归时间<el-input v-model="drafts.inertia.resetTime" size="small" :disabled="paramsLocked" placeholder="00:00:02" /></label>
          </div>
          <el-button size="small" type="primary" :disabled="paramsLocked || activeMode === 0 || !dirtyInertia" :loading="busy" @click="applyInertia">应用</el-button>
        </el-collapse-item>

        <el-collapse-item name="droop">
          <template #title>
            <span>调压</span>
            <span v-if="dirtyDroop" class="ems-dirty">未应用</span>
            <span class="ems-title-switches" @click.stop>
              <el-switch v-model="droopEnabled" active-text="下垂使能" :disabled="paramsLocked" @change="onPatchStrategy" />
            </span>
          </template>
          <p class="hint">下垂仅在无功闭环固定时叠加 ΔQ；测点按额定电压就近选 PCC 或 35kV 母线。k 为下垂百分数，与一次调频同口径。PF / 恒压 / 开环可开开关但本拍不叠加。恒压 Kp/K 在无功选恒压时作为基值算法。</p>
          <div class="ems-param-grid">
            <label class="ems-param">额定电压 V<el-input-number v-model="drafts.droop.ratedVoltageV" size="small" :step="100" :disabled="paramsLocked" /></label>
            <label class="ems-param">死区1 %<el-input-number v-model="drafts.droop.deadband1Percent" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">死区2 %<el-input-number v-model="drafts.droop.deadband2Percent" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">k1 %<el-input-number v-model="drafts.droop.k1Percent" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">k2 %<el-input-number v-model="drafts.droop.k2Percent" size="small" :step="0.1" :disabled="paramsLocked" /></label>
            <label class="ems-param">曲线类型
              <el-select v-model="drafts.droop.voltageCurveType" size="small" :disabled="paramsLocked">
                <el-option :value="0" label="从死区边沿" />
                <el-option :value="1" label="从额定电压" />
              </el-select>
            </label>
            <label class="ems-param">段数
              <el-select v-model="drafts.droop.segmentCount" size="small" :disabled="paramsLocked">
                <el-option :value="3" label="三段" />
                <el-option :value="5" label="五段" />
              </el-select>
            </label>
            <label class="ems-param">过压投<el-switch v-model="drafts.droop.overVoltEnable" :disabled="paramsLocked" /></label>
            <label class="ems-param">欠压投<el-switch v-model="drafts.droop.underVoltEnable" :disabled="paramsLocked" /></label>
            <label class="ems-param">控制周期<el-input v-model="drafts.droop.controlCycle" size="small" :disabled="paramsLocked" placeholder="00:00:01" /></label>
            <label class="ems-param">复归时间<el-input v-model="drafts.droop.resetTime" size="small" :disabled="paramsLocked" placeholder="00:00:02" /></label>
            <label class="ems-param">最大输出 kvar<el-input-number v-model="drafts.droop.maxOutputKvar" size="small" :disabled="paramsLocked" /></label>
            <label class="ems-param">最大吸收 kvar<el-input-number v-model="drafts.droop.maxAbsorbKvar" size="small" :disabled="paramsLocked" /></label>
            <label class="ems-param">限幅系数<el-input-number v-model="drafts.droop.limitCoefficient" size="small" :step="0.1" :disabled="paramsLocked" /></label>
          </div>
          <div class="ems-param-grid" style="margin-top:8px">
            <label class="ems-param">恒压 Kp<el-input-number v-model="drafts.voltageKp" size="small" :step="0.05" :disabled="paramsLocked" /></label>
            <label class="ems-param">恒压 K<el-input-number v-model="drafts.voltageFixedK" size="small" :step="0.1" :disabled="paramsLocked" /></label>
          </div>
          <el-button size="small" type="primary" :disabled="paramsLocked || !dirtyDroop" :loading="busy" @click="applyDroop">应用</el-button>
        </el-collapse-item>
      </el-collapse>
    </div>

    <div class="card">
      <p class="card-title">运行快照</p>
      <div class="metric-grid">
        <div class="metric-item"><div class="label">电网频率</div><div class="value">{{ fmt(snap.frequencyHz, 3) }} Hz</div></div>
        <div class="metric-item"><div class="label">并网点 P/Q</div><div class="value">{{ fmt(snap.pccActivePowerKw, 1) }} / {{ fmt(snap.pccReactivePowerKvar, 1) }}</div></div>
        <div class="metric-item"><div class="label">PCC 电压</div><div class="value">{{ fmt(snap.pccLineVoltageV, 0) }} V</div></div>
        <div class="metric-item"><div class="label">35kV 母线</div><div class="value">{{ fmt(snap.stationBus35LineVoltageV, 0) }} V</div></div>
        <div class="metric-item"><div class="label">ΔP 调频</div><div class="value">{{ fmt(snap.frequencyDeltaKw, 1) }} · {{ actionLabel(snap.frequencyAction) }}</div></div>
        <div class="metric-item"><div class="label">ΔP 惯量</div><div class="value">{{ fmt(snap.inertiaDeltaKw, 1) }} · {{ actionLabel(snap.inertiaAction) }}</div></div>
        <div class="metric-item"><div class="label">ΔQ 下垂</div><div class="value">{{ fmt(snap.droopDeltaKvar, 1) }} · {{ actionLabel(snap.droopAction) }}</div></div>
        <div class="metric-item"><div class="label">站级 P 指令</div><div class="value">{{ fmt(snap.plantActiveCommandKw, 1) }} kW</div></div>
        <div class="metric-item"><div class="label">站级 Q 指令</div><div class="value">{{ fmt(snap.plantReactiveCommandKvar, 1) }} kvar</div></div>
      </div>
      <el-table :data="snap.branches || []" size="small" border stripe style="margin-top:12px">
        <el-table-column prop="index" label="支路" width="80" />
        <el-table-column label="单元" width="80">
          <template #default="{ row }">{{ row.unitIndex0 + 1 }}</template>
        </el-table-column>
        <el-table-column label="P 设定 kW">
          <template #default="{ row }">{{ fmt(row.activePowerKw, 1) }}</template>
        </el-table-column>
        <el-table-column label="Q 设定 kvar">
          <template #default="{ row }">{{ fmt(row.reactivePowerKvar, 1) }}</template>
        </el-table-column>
      </el-table>
    </div>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getEmsStrategy, postEmsStrategy, postEmsStrategyEnable } from '@/services/api.js'

const emptySlope = () => ({ enabled: false, riseKwPerSec: 100, fallKwPerSec: 100 })
const emptyPid = () => ({
  enabled: true, kp: 0.4, ki: 0.05, kb: 0.5, period: '00:00:03', deadbandKw: 5,
  outMinKw: -5000, outMaxKw: 5000, discretization: 0
})
const emptyPfr = () => ({
  enabled: true, ratedFrequencyHz: 50, deadband1Percent: 0.2, deadband2Percent: 0.5,
  droopPercent: 3, droop2Percent: 5, overDroopPercent: 0, underDroopPercent: 0,
  overDroop2Percent: 0, underDroop2Percent: 0, segmentCount: 3, overFreqEnable: true, underFreqEnable: true,
  controlCycle: '00:00:01', resetTime: '00:00:02', maxOutputKw: 5000, maxAbsorbKw: 5000, limitCoefficient: 1
})
const emptyInertia = () => ({
  enabled: false, lockPrimaryFrequency: false, ratedFrequencyHz: 50, inertiaTimeSec: 6,
  amplitudeDeadbandHz: 0.05, rateDeadbandHzPerSec: 0.1, freqMinHz: 47, freqMaxHz: 53,
  controlCycle: '00:00:00.200', resetTime: '00:00:02'
})
const emptyDroop = () => ({
  enabled: false, ratedVoltageV: 35000, deadband1Percent: 0.5, deadband2Percent: 1.5,
  k1Percent: 4, k2Percent: 6, voltageCurveType: 0, segmentCount: 3, overVoltEnable: true, underVoltEnable: true,
  controlCycle: '00:00:01', resetTime: '00:00:02', maxOutputKvar: 5000, maxAbsorbKvar: 5000, limitCoefficient: 1
})
const emptyDist = () => ({ enabled: true, socBalance: false, socMin: 0.1, socMax: 0.9 })

const busy = ref(false)
const status = ref({ enabled: false, config: {}, snapshot: {}, gateOwner: 'None' })
const enabled = ref(false)
const systemSwitch = ref(true)
const activeEnable = ref(true)
const reactiveEnable = ref(true)
const localRemote = ref(0)
const activeMode = ref(1)
const reactiveMode = ref(1)
const localP = ref(0)
const remoteP = ref(0)
const localQ = ref(0)
const remoteQ = ref(0)
const pfSet = ref(1)
const voltageSetV = ref(35000)
const pfrEnabled = ref(true)
const inertiaEnabled = ref(false)
const droopEnabled = ref(false)
const apparentLimitEnabled = ref(true)
const dampEnabled = ref(true)
const pathOpen = ref([])
const strategyOpen = ref([])
let timer = null

const drafts = reactive({
  slope: emptySlope(),
  reactiveSlope: emptySlope(),
  plant: { apparentRatedKva: 5000, plantRatedKw: 5000 },
  activePid: emptyPid(),
  reactivePid: emptyPid(),
  pfr: emptyPfr(),
  inertia: emptyInertia(),
  droop: emptyDroop(),
  voltageKp: 0.5,
  voltageFixedK: 1,
  distribution: emptyDist()
})
const applied = reactive({
  slope: emptySlope(),
  reactiveSlope: emptySlope(),
  plant: { apparentRatedKva: 5000, plantRatedKw: 5000 },
  activePid: emptyPid(),
  reactivePid: emptyPid(),
  pfr: emptyPfr(),
  inertia: emptyInertia(),
  droop: emptyDroop(),
  voltageKp: 0.5,
  voltageFixedK: 1,
  distribution: emptyDist()
})

const snap = computed(() => status.value.snapshot || {})
const blockedByThirdParty = computed(() => status.value.gateOwner === 'ThirdPartyEms')
const paramsLocked = computed(() => busy.value || blockedByThirdParty.value)
const dirtySlope = computed(() => !same(drafts.slope, applied.slope) || !same(drafts.reactiveSlope, applied.reactiveSlope))
const dirtyLimit = computed(() => !same(drafts.plant, applied.plant))
const dirtyPid = computed(() => !same(drafts.activePid, applied.activePid) || !same(drafts.reactivePid, applied.reactivePid))
const dirtyPfr = computed(() => !same(drafts.pfr, applied.pfr))
const dirtyInertia = computed(() => !same(drafts.inertia, applied.inertia))
const dirtyDroop = computed(() => !same(drafts.droop, applied.droop) || drafts.voltageKp !== applied.voltageKp || drafts.voltageFixedK !== applied.voltageFixedK)
const dirtyDist = computed(() => !same(drafts.distribution, applied.distribution))
const slopeStageOn = computed(() => !!drafts.slope.enabled || !!drafts.reactiveSlope.enabled)
const pidStageOn = computed(() => (activeMode.value !== 0 && drafts.activePid.enabled) || (reactiveMode.value !== 0 && drafts.reactivePid.enabled))
const inertiaLockingPfr = computed(() => inertiaEnabled.value && drafts.inertia.lockPrimaryFrequency && snap.value.inertiaAction === 1)

function clone(v) {
  return JSON.parse(JSON.stringify(v ?? {}))
}

function same(a, b) {
  return JSON.stringify(a) === JSON.stringify(b)
}

function fmt(v, n) {
  const x = Number(v)
  return Number.isFinite(x) ? x.toFixed(n) : '—'
}

function actionLabel(code) {
  return code === 1 ? 'ACTION' : 'RESET'
}

function openPath(name) {
  pathOpen.value = pathOpen.value.includes(name) ? pathOpen.value.filter(x => x !== name) : [...pathOpen.value, name]
}

function pathNodeClass(name, on) {
  return { 'is-off': !on, 'is-active': pathOpen.value.includes(name) }
}

function applyRunBar(s, { setpoints = false } = {}) {
  status.value = s || { enabled: false, config: {}, snapshot: {}, gateOwner: 'None' }
  enabled.value = !!status.value.enabled
  const cfg = status.value.config || {}
  systemSwitch.value = cfg.systemSwitch !== false
  activeEnable.value = cfg.activeEnable !== false
  reactiveEnable.value = cfg.reactiveEnable !== false
  localRemote.value = asEnum(cfg.localRemote, 0, { Local: 0, Remote: 1 })
  pfrEnabled.value = cfg.primaryFrequency?.enabled !== false
  inertiaEnabled.value = cfg.inertia?.enabled === true
  droopEnabled.value = cfg.voltageDroop?.enabled === true
  apparentLimitEnabled.value = cfg.apparentLimitEnabled !== false
  dampEnabled.value = cfg.dampEnabled !== false
  if (!setpoints) return
  activeMode.value = asActiveMode(cfg.activeMode)
  reactiveMode.value = asReactiveMode(cfg.reactiveMode)
  localP.value = cfg.localActiveSetKw ?? 0
  remoteP.value = cfg.remoteActiveSetKw ?? 0
  localQ.value = cfg.localReactiveSetKvar ?? 0
  remoteQ.value = cfg.remoteReactiveSetKvar ?? 0
  pfSet.value = cfg.powerFactorSet ?? 1
  voltageSetV.value = cfg.voltageSetV ?? 35000
}

function asEnum(v, fallback, names = {}) {
  if (typeof v === 'number' && Number.isFinite(v)) return v
  if (typeof v === 'string' && v in names) return names[v]
  return fallback
}

function asActiveMode(v) {
  const n = asEnum(v, 1, { OpenLoopFixed: 0, CloseLoopFixed: 1, CloseLoopCurve: 1 })
  return n === 2 ? 1 : n
}

function asReactiveMode(v) {
  const n = asEnum(v, 1, {
    OpenLoopFixed: 0, CloseLoopFixed: 1, CloseLoopCurve: 1, PowerFactor: 3, VoltageFixed: 4
  })
  return n === 2 ? 1 : n
}

function hydrateDrafts(cfg) {
  const c = cfg || {}
  Object.assign(drafts.slope, emptySlope(), clone(c.slope))
  Object.assign(drafts.reactiveSlope, emptySlope(), clone(c.reactiveSlope || c.slope))
  drafts.plant.apparentRatedKva = c.apparentRatedKva ?? 5000
  drafts.plant.plantRatedKw = c.plantRatedKw ?? 5000
  Object.assign(drafts.activePid, emptyPid(), clone(c.activePid))
  drafts.activePid.enabled = c.activePid?.enabled !== false
  drafts.activePid.discretization = asEnum(drafts.activePid.discretization, 0, { CompatiblePeriod: 0, Dt: 1 })
  Object.assign(drafts.reactivePid, emptyPid(), clone(c.reactivePid || c.activePid))
  drafts.reactivePid.enabled = (c.reactivePid || c.activePid)?.enabled !== false
  drafts.reactivePid.discretization = asEnum(drafts.reactivePid.discretization, 0, { CompatiblePeriod: 0, Dt: 1 })
  Object.assign(drafts.pfr, emptyPfr(), clone(c.primaryFrequency))
  Object.assign(drafts.inertia, emptyInertia(), clone(c.inertia))
  Object.assign(drafts.droop, emptyDroop(), clone(c.voltageDroop))
  drafts.voltageKp = c.voltageKp ?? 0.5
  drafts.voltageFixedK = c.voltageFixedK ?? 1
  Object.assign(drafts.distribution, emptyDist(), clone(c.distribution))
  drafts.distribution.enabled = c.distribution?.enabled !== false
  snapshotApplied()
}

function snapshotApplied() {
  applied.slope = clone(drafts.slope)
  applied.reactiveSlope = clone(drafts.reactiveSlope)
  applied.plant = clone(drafts.plant)
  applied.activePid = clone(drafts.activePid)
  applied.reactivePid = clone(drafts.reactivePid)
  applied.pfr = clone(drafts.pfr)
  applied.inertia = clone(drafts.inertia)
  applied.droop = clone(drafts.droop)
  applied.voltageKp = drafts.voltageKp
  applied.voltageFixedK = drafts.voltageFixedK
  applied.distribution = clone(drafts.distribution)
}

async function tick() {
  const s = await getEmsStrategy()
  status.value.snapshot = s?.snapshot || {}
  status.value.gateOwner = s?.gateOwner || 'None'
  status.value.enabled = !!s?.enabled
  enabled.value = !!s?.enabled
}

async function loadAll() {
  const s = await getEmsStrategy()
  applyRunBar(s, { setpoints: true })
  hydrateDrafts(s?.config)
}

async function onToggleEnabled(val) {
  busy.value = true
  try {
    const res = await postEmsStrategyEnable(val)
    applyRunBar(res.status)
    ElMessage.success(res.message || (val ? '已启用' : '已关闭'))
  } catch (e) {
    enabled.value = !val
    ElMessage.error(e.message || '切换失败')
  } finally {
    busy.value = false
  }
}

async function onScalar(key, val, syncApplied) {
  busy.value = true
  try {
    const res = await postEmsStrategy({ [key]: val })
    applyRunBar(res.status)
    if (syncApplied) syncApplied()
  } catch (e) {
    ElMessage.error(e.message || '更新失败')
    await loadAll()
  } finally {
    busy.value = false
  }
}

async function onPatch() {
  busy.value = true
  try {
    const res = await postEmsStrategy({
      systemSwitch: systemSwitch.value,
      activeEnable: activeEnable.value,
      reactiveEnable: reactiveEnable.value,
      localRemote: localRemote.value
    })
    applyRunBar(res.status)
  } catch (e) {
    ElMessage.error(e.message || '更新失败')
    await loadAll()
  } finally {
    busy.value = false
  }
}

async function issueSetpoint() {
  busy.value = true
  try {
    const res = await postEmsStrategy({
      activeMode: activeMode.value,
      reactiveMode: reactiveMode.value,
      localActiveSetKw: localP.value,
      remoteActiveSetKw: remoteP.value,
      localReactiveSetKvar: localQ.value,
      remoteReactiveSetKvar: remoteQ.value,
      powerFactorSet: pfSet.value,
      voltageSetV: voltageSetV.value
    })
    applyRunBar(res.status, { setpoints: true })
    ElMessage.success(res.message || '已下发')
  } catch (e) {
    ElMessage.error(e.message || '下发失败')
    await loadAll()
  } finally {
    busy.value = false
  }
}

async function onPatchStrategy() {
  busy.value = true
  try {
    const res = await postEmsStrategy({
      primaryFrequencyEnabled: pfrEnabled.value,
      inertiaEnabled: inertiaEnabled.value,
      voltageDroopEnabled: droopEnabled.value
    })
    applyRunBar(res.status)
    drafts.pfr.enabled = pfrEnabled.value
    drafts.inertia.enabled = inertiaEnabled.value
    drafts.droop.enabled = droopEnabled.value
    applied.pfr.enabled = pfrEnabled.value
    applied.inertia.enabled = inertiaEnabled.value
    applied.droop.enabled = droopEnabled.value
  } catch (e) {
    ElMessage.error(e.message || '更新失败')
    await loadAll()
  } finally {
    busy.value = false
  }
}

async function applyBody(body, keys) {
  busy.value = true
  try {
    const res = await postEmsStrategy(body)
    applyRunBar(res.status)
    for (const key of keys)
      applied[key] = clone(drafts[key])
    ElMessage.success(res.message || '已热更新')
  } catch (e) {
    ElMessage.error(e.message || '更新失败')
  } finally {
    busy.value = false
  }
}

function applySlope() {
  return applyBody({
    slope: clone(drafts.slope),
    reactiveSlope: clone(drafts.reactiveSlope)
  }, ['slope', 'reactiveSlope'])
}

function applyLimit() {
  return applyBody({
    apparentRatedKva: drafts.plant.apparentRatedKva,
    plantRatedKw: drafts.plant.plantRatedKw,
    apparentLimitEnabled: apparentLimitEnabled.value
  }, ['plant'])
}

function applyPid() {
  return applyBody({
    activePid: clone(drafts.activePid),
    reactivePid: clone(drafts.reactivePid)
  }, ['activePid', 'reactivePid'])
}

function applyPfr() {
  drafts.pfr.enabled = pfrEnabled.value
  return applyBody({ primaryFrequency: clone(drafts.pfr) }, ['pfr'])
}

function applyInertia() {
  drafts.inertia.enabled = inertiaEnabled.value
  return applyBody({ inertia: clone(drafts.inertia) }, ['inertia'])
}

function applyDroop() {
  drafts.droop.enabled = droopEnabled.value
  return applyBody({ voltageDroop: clone(drafts.droop), voltageKp: drafts.voltageKp, voltageFixedK: drafts.voltageFixedK }, ['droop', 'voltageKp', 'voltageFixedK'])
}

function applyDist() {
  return applyBody({ distribution: clone(drafts.distribution) }, ['distribution'])
}

onMounted(async () => {
  try {
    await loadAll()
  } catch (e) {
    ElMessage.error(e.message || '无法读取 EMS 策略')
  }
  timer = setInterval(() => { tick().catch(() => {}) }, 1000)
})

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>
