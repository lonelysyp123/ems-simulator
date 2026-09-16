using System;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Control;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    public sealed partial class PcsDevice
    {
        public double FormingPhaseRad => _formingPhase.ThetaRad;
        public bool IsLiveBusFollower => _liveBusFollower;
        public bool IsPreSyncReadyToCutIn { get; private set; }

        /// <summary>
        /// 离网/黑启动 V/f 模式下，PCS 作为 690V 母线电压源的输出（供 <see cref="Propagation.PcsBusVoltageSource"/> 使用）。
        /// </summary>
        public bool TryGetIslandBusVoltageInjection(out double lineVoltageV, out double frequencyHz)
        {
            lineVoltageV = 0;
            frequencyHz = _blackStartIslandFreqHz;

            if (_liveBusFollower)
                return false;

            if (!EssIslandBusLogic.IsPcsIslandVoltageBuilding(_currentState))
                return false;

            if (_blackStartEnabled &&
                _blackStartPhase is BlackStartPhase.SoftStarting
                    or BlackStartPhase.VoltageRegulating
                    or BlackStartPhase.Synchronized)
            {
                lineVoltageV = FormingVoltageRef();
                frequencyHz = _blackStartIslandFreqHz;
            }
            else if (_currentState.IslandVoltageEffectiveV > 1.0)
            {
                lineVoltageV = _currentState.IslandVoltageEffectiveV;
            }

            if (lineVoltageV <= 1.0)
                return false;

            frequencyHz = _blackStartIslandFreqHz;
            return true;
        }

        /// <summary>EMS/Modbus 写入孤岛电压设定（V）。黑启动中按斜坡跟踪新目标。</summary>
        public void ApplyIslandVoltageCommand(double voltageV)
        {
            if (_ignoreZeroIslandCommandAfterJoin && voltageV < 1.0)
                return;
            if (voltageV >= 1.0)
                _ignoreZeroIslandCommandAfterJoin = false;

            double maxV = _config.AcVoltageNominal;
            voltageV = Math.Clamp(voltageV, 0, maxV);
            bool commandChanged;
            lock (_islandVfLock)
            {
                commandChanged = Math.Abs(_islandVfCommandV - voltageV) > 0.5;
                _islandVfCommandV = voltageV;
                _currentState.IslandVoltageCommandV = voltageV;
            }

            // 仅设定变化时退出同步去调压；重复下发同一 yt3 不得把「已同步」打回「调压」
            if (commandChanged && _blackStartEnabled && _blackStartPhase == BlackStartPhase.Synchronized)
            {
                _blackStartPhase = BlackStartPhase.VoltageRegulating;
                _currentState.BlackStartPhase = _blackStartPhase;
            }
        }

        /// <summary>EMS/Modbus 写入孤岛频率设定（Hz）。0 表示沿用额定频率。</summary>
        public double IslandFrequencyCommandHz => _islandFrequencyCommandHz;

        public void ApplyIslandFrequencyCommand(double frequencyHz)
        {
            if (frequencyHz < 1.0)
            {
                _islandFrequencyCommandHz = 0;
                return;
            }

            double nom = _config.FrequencyNominal;
            _islandFrequencyCommandHz = Math.Clamp(frequencyHz, nom - 5.0, nom + 5.0);
        }

        private double IslandFrequencyRefHz() =>
            _islandFrequencyCommandHz > 1.0 ? _islandFrequencyCommandHz : _config.FrequencyNominal;

        /// <summary>EMS 写入黑启动开启。</summary>
        public void ApplyBlackStartEnabled(bool enabled)
        {
            if (enabled == _blackStartEnabled)
                return;

            _blackStartEnabled = enabled;
            _currentState.BlackStartEnabled = enabled;
            if (!enabled)
            {
                ResetBlackStartRuntime();
                return;
            }

            lock (_setpointLock)
            {
                _rampStopRequested = true;
                _pendingActiveSetpoint = 0;
                _pendingReactiveSetpoint = 0;
            }

            _loadActivePowerKw = 0;
            _loadReactivePowerKvar = 0;
            _liveBusFollower = false;
            IsPreSyncReadyToCutIn = false;
            _ignoreZeroIslandCommandAfterJoin = false;
            _joinedFromLiveBus = false;
            _blackStartJoinShare = 1.0;
            _blackStartPhase = BlackStartPhase.Preparing;
            _currentState.BlackStartPhase = BlackStartPhase.Preparing;
            _blackStartPrepareRemainingSec = _blackStartPrechargeDelaySec;
            _blackStartSoftCapV = 0;
            _blackStartIslandFreqHz = IslandFrequencyRefHz();
            _blackStartInrushActiveKw = 0;
            _blackStartInrushReactiveKvar = 0;
            _voltageOuter.Reset();
            _currentInner.Reset();
            _formingPhase.Reset();
            _pll.Reset();
            _voltageRamp.Reset(0);
        }

        /// <summary>主循环刷新同单元 690V 母线电压，判定同步/跌落。不得用邻机母线把本机打成已同步。</summary>
        public void RefreshBlackStartBusContext(
            double unitBusVoltageV,
            double busFrequencyHz = 0,
            double busPhaseRad = 0)
        {
            _unitBusVoltageV = Math.Max(0, unitBusVoltageV);
            _busFrequencyHz = busFrequencyHz;
            _busPhaseRad = busPhaseRad;
            if (!_blackStartEnabled)
            {
                ResetBlackStartRuntime();
                return;
            }

            TryLatchLiveBusFollower();

            double vCmd;
            lock (_islandVfLock)
                vCmd = _islandVfCommandV;
            double target = Math.Max(vCmd, 1.0);
            double energizedV = target * _blackStartBusEnergizedFraction;

            if (_blackStartPhase is BlackStartPhase.VoltageRegulating or BlackStartPhase.Synchronized
                && vCmd > 1.0 && _blackStartSoftCapV >= energizedV)
            {
                _blackStartPhase = BlackStartPhase.Synchronized;
                PublishFormingFrequency();
            }
            else if (_blackStartPhase == BlackStartPhase.Synchronized && _blackStartSoftCapV < energizedV * 0.92)
            {
                _blackStartPhase = BlackStartPhase.VoltageRegulating;
            }

            _currentState.BlackStartPhase = _blackStartPhase;
            PublishBlackStartEffectiveVoltage();
        }

        public BlackStartPhase GetBlackStartPhase() => _blackStartPhase;

        public bool IsBlackStartSynchronized =>
            _blackStartEnabled && _blackStartPhase == BlackStartPhase.Synchronized;

        public bool IsBlackStartActive =>
            _blackStartEnabled &&
            !_liveBusFollower &&
            _currentState.Mode == OperationMode.Normal &&
            _currentState.GMode == GridMode.Islanded &&
            _blackStartPhase is BlackStartPhase.SoftStarting
                or BlackStartPhase.VoltageRegulating
                or BlackStartPhase.Synchronized;

        /// <summary>预同步窗口满足后切入为构网并机：对齐母线 V/θ 再注入，不再跟网挂 0 指令。</summary>
        public bool TryCutInAsFormingParallel()
        {
            if (!_liveBusFollower || !IsPreSyncReadyToCutIn)
                return false;

            double vJoin = Math.Max(_unitBusVoltageV, 1.0);
            double vCmd;
            lock (_islandVfLock)
                vCmd = _islandVfCommandV;
            if (vCmd < 1.0)
                vCmd = vJoin;

            lock (_islandVfLock)
            {
                _islandVfCommandV = vCmd;
                _currentState.IslandVoltageCommandV = vCmd;
            }

            _voltageRamp.Reset(vJoin);
            _voltageRamp.Target = vCmd;
            _blackStartSoftCapV = vJoin;
            _formingPhase.Reset(_busPhaseRad);
            _blackStartIslandFreqHz = _busFrequencyHz > 1
                ? _busFrequencyHz
                : IslandFrequencyRefHz();
            _transientAcVoltageV = vJoin;
            _prevSubStepAcVoltageV = vJoin;
            _liveBusFollower = false;
            IsPreSyncReadyToCutIn = false;
            _joinedFromLiveBus = true;
            _blackStartJoinShare = 0;
            _ignoreZeroIslandCommandAfterJoin = true;
            _blackStartPhase = BlackStartPhase.Synchronized;
            _currentState.BlackStartPhase = _blackStartPhase;
            PublishFormingFrequency();
            PublishBlackStartEffectiveVoltage();
            return true;
        }

        public bool TryCutInAsGridFollower() => TryCutInAsFormingParallel();

        /// <summary>
        /// 启停已合且预同步窗口满足时，在设备步进里切入构网并机。
        /// 不依赖 mapper 命令指纹变化，避免后机停在「准备」。
        /// </summary>
        private void TryAutoCutInAsFormingParallel()
        {
            if (!_liveBusFollower || !IsPreSyncReadyToCutIn || !_externalRunCommand || _faultTripLatched)
                return;
            if (!TryCutInAsFormingParallel())
                return;
            if (_currentState.GMode != GridMode.Islanded)
                TransitionToGMode(GridMode.Islanded, "预同步构网并机");
            TransitionToMode(OperationMode.Normal, "预同步构网并机");
        }

        public void SetTransformerMagnetizingReactiveKvar(double reactiveKvar) =>
            _transformerMagnetizingReactiveKvar = Math.Max(0, reactiveKvar);

        /// <summary>已构网（含死母线软起主机）才承接站用电/励磁；活母线跟网锁相中不承接。</summary>
        public bool TakesIslandStationLoad =>
            _blackStartEnabled
            && !_liveBusFollower
            && _currentState.GMode == GridMode.Islanded
            && _blackStartPhase is BlackStartPhase.SoftStarting
                or BlackStartPhase.VoltageRegulating
                or BlackStartPhase.Synchronized;

        /// <summary>站用电分摊权重：死母线主机为 1；活母线并机后从 0 斜坡到 1。</summary>
        public double BlackStartStationLoadShare =>
            !TakesIslandStationLoad
                ? 0
                : _joinedFromLiveBus
                    ? Math.Clamp(_blackStartJoinShare, 0, 1)
                    : 1.0;

        public void SetBlackStartSharedLossActivePowerKw(double activeKw) =>
            _blackStartSharedLossActivePowerKw = Math.Max(0, activeKw);

        public void SetBlackStartInrushDemand(double activeKw, double reactiveKvar)
        {
            _blackStartInrushActiveKw = Math.Max(0, activeKw);
            _blackStartInrushReactiveKvar = Math.Max(0, reactiveKvar);
        }

        private void TryLatchLiveBusFollower()
        {
            if (_liveBusFollower)
                return;
            // 本机已在建压/调压/同步：不得把自己或同伴抬起的母线再判成「邻机活母线」。
            if (_blackStartPhase is BlackStartPhase.SoftStarting
                or BlackStartPhase.VoltageRegulating
                or BlackStartPhase.Synchronized)
                return;

            double enableV = _pllEnableVoltagePu * _config.AcVoltageNominal;
            double sensed = SensedLiveBusVoltageV();
            if (sensed < enableV)
                return;

            if (_blackStartSoftCapV >= enableV)
                return;

            EnterLiveBusFollower();
        }

        private double SensedLiveBusVoltageV()
        {
            double v = _unitBusVoltageV;
            var input = AcPortHelper.ReadAcInput(Ac);
            if (input.LineVoltageV > v)
                v = input.LineVoltageV;
            return v;
        }

        private void EnterLiveBusFollower()
        {
            _liveBusFollower = true;
            _joinedFromLiveBus = false;
            _blackStartJoinShare = 0;
            _blackStartSoftCapV = 0;
            _voltageRamp.Reset(0);
            _blackStartPhase = BlackStartPhase.Following;
            _currentState.BlackStartPhase = _blackStartPhase;
            IsPreSyncReadyToCutIn = false;
        }

        private void AdvanceBlackStartPhase(TimeSpan timeStep)
        {
            if (!_blackStartEnabled)
                return;

            double dt = Math.Max(timeStep.TotalSeconds, 0);
            TryLatchLiveBusFollower();

            if (_liveBusFollower)
            {
                AdvanceFollowerPll(dt);
                return;
            }

            if (_joinedFromLiveBus && _blackStartJoinShare < 1.0)
            {
                if (_blackStartJoinShareRampSec <= 0 || dt <= 0)
                    _blackStartJoinShare = 1.0;
                else
                    _blackStartJoinShare = Math.Min(1.0, _blackStartJoinShare + dt / _blackStartJoinShareRampSec);
            }

            double vCmd;
            lock (_islandVfLock)
                vCmd = _islandVfCommandV;
            PublishFormingFrequency();

            if (_blackStartPhase == BlackStartPhase.Preparing)
            {
                _blackStartPrepareRemainingSec -= dt;
                if (_blackStartPrepareRemainingSec <= 0 && IsDcBusReadyForBlackStart() && vCmd > 1.0)
                    _blackStartPhase = BlackStartPhase.SoftStarting;
            }

            if (            _blackStartPhase is BlackStartPhase.SoftStarting
                or BlackStartPhase.VoltageRegulating
                or BlackStartPhase.Synchronized)
            {
                RampSoftCapTowardCommand(vCmd, dt);
                _formingPhase.Step(_blackStartIslandFreqHz, dt);
                _currentState.FormingPhaseRad = _formingPhase.ThetaRad;
                if (_blackStartPhase == BlackStartPhase.SoftStarting
                    && vCmd > 1.0
                    && _blackStartSoftCapV >= vCmd - 1.0)
                    _blackStartPhase = BlackStartPhase.VoltageRegulating;
            }

            _currentState.BlackStartPhase = _blackStartPhase;
        }

        private void AdvanceFollowerPll(double dt)
        {
            double enableV = _pllEnableVoltagePu * _config.AcVoltageNominal;
            double busF = _busFrequencyHz > 1 ? _busFrequencyHz : _config.FrequencyNominal;
            _pll.Step(_unitBusVoltageV, busF, _busPhaseRad, enableV, dt);
            IsPreSyncReadyToCutIn = _pll.Enabled && _preSync.IsReadyToCutIn(
                _pll.VoltageV,
                _pll.FrequencyHz,
                _pll.ThetaRad,
                _unitBusVoltageV,
                busF,
                _busPhaseRad,
                _config.AcVoltageNominal);
            if (_blackStartPhase != BlackStartPhase.Following)
            {
                _blackStartPhase = BlackStartPhase.Following;
                _currentState.BlackStartPhase = BlackStartPhase.Following;
            }
        }

        private void RampSoftCapTowardCommand(double vCmd, double dt)
        {
            _voltageRamp.Target = vCmd;
            _blackStartSoftCapV = _voltageRamp.Step(dt);
        }

        /// <summary>构网注入电压：先斜坡跟踪 V0，再按无功下垂得到 Vref。</summary>
        private double FormingVoltageRef()
        {
            double vRamp = _voltageRamp.Output;
            bool applyDroop = _qvDroopEnabled && _qvDroop.Nq > 0;
            if (_qvDroopAfterSoftStartOnly && _blackStartPhase == BlackStartPhase.SoftStarting)
                applyDroop = false;
            if (_joinedFromLiveBus && _blackStartJoinShare < 1.0)
                applyDroop = false;
            if (!applyDroop)
                return vRamp;
            return _qvDroop.Compute(vRamp, _currentState.ReactivePower);
        }

        private void PublishFormingFrequency()
        {
            _blackStartIslandFreqHz = _pfDroop.Compute(
                IslandFrequencyRefHz(),
                _currentState.ActivePower);
        }

        private bool IsDcBusReadyForBlackStart() =>
            _currentState.DcVoltage >= _config.DcVoltageRangeMin * 0.95;

        private double FormingCurrentLimitA()
        {
            if (_blackStartPhase == BlackStartPhase.Synchronized)
                return _config.MaxCurrent;
            return _config.MaxCurrent * _blackStartCurrentLimitFraction;
        }

        private void ApplyBlackStartPowerControl(TimeSpan timeStep)
        {
            if (_liveBusFollower)
            {
                _currentState.ActivePower = _loadActivePowerKw;
                _currentState.ReactivePower = _loadReactivePowerKvar;
                return;
            }

            if (!IsBlackStartActive)
            {
                if (_blackStartEnabled && _blackStartPhase == BlackStartPhase.Preparing)
                {
                    _currentState.ActivePower = 0;
                    _currentState.ReactivePower = 0;
                }
                return;
            }

            double vRef = FormingVoltageRef();
            if (vRef < 1.0)
            {
                _voltageOuter.Reset();
                _currentInner.Reset();
                _currentState.ActivePower = 0;
                _currentState.ReactivePower = 0;
                return;
            }

            double dt = timeStep.TotalSeconds;
            double vMeas = Math.Max(_transientAcVoltageV, _currentState.AcVoltage);
            double iMax = FormingCurrentLimitA();
            double iRefV;
            if (_blackStartPhase != BlackStartPhase.SoftStarting && Math.Abs(vRef - vMeas) < 5.0)
            {
                _voltageOuter.Reset();
                iRefV = 0;
            }
            else
                iRefV = _voltageOuter.Step(vRef, vMeas, dt, iMax);

            double pDem = _blackStartSharedLossActivePowerKw + _loadActivePowerKw + _blackStartInrushActiveKw;
            double qDem = _transformerMagnetizingReactiveKvar + _loadReactivePowerKvar + _blackStartInrushReactiveKvar;

            double vPow = Math.Max(vRef, 10.0);
            double iP = pDem * 1000.0 / (vPow * Math.Sqrt(3.0));
            double iQ = qDem * 1000.0 / (vPow * Math.Sqrt(3.0)) + iRefV;
            double iMagRef = Math.Sqrt(iP * iP + iQ * iQ);
            double iOut = _currentInner.Step(iMagRef, _currentInner.Output, dt, iMax);
            double scale = iMagRef > 1e-9 ? iOut / iMagRef : 0;
            double targetP = iP * scale * vPow * Math.Sqrt(3.0) / 1000.0;
            double targetQ = iQ * scale * vPow * Math.Sqrt(3.0) / 1000.0;

            if (_blackStartPhase != BlackStartPhase.Synchronized && dt > 0)
            {
                double maxStep = Math.Max(_blackStartMaxActivePowerKw * dt, 0.5);
                double currentP = _currentState.ActivePower;
                if (targetP > currentP + maxStep)
                    targetP = currentP + maxStep;
                else if (targetP < currentP - maxStep)
                    targetP = Math.Max(0, targetP);
            }

            targetP = Math.Clamp(targetP, -_config.MaxPower, _config.MaxPower);
            targetQ = Math.Clamp(targetQ, -_config.MaxPower, _config.MaxPower);
            ClampApparentPower(ref targetP, ref targetQ, _config.RatedPower);
            ApplyBlackStartCurrentLimit(ref targetP, ref targetQ, vPow);

            _currentState.ActivePower = targetP;
            _currentState.ReactivePower = targetQ;
            PublishFormingFrequency();
        }

        private void ApplyBlackStartCurrentLimit(ref double activeKw, ref double reactiveKvar, double voltageV)
        {
            double iLimit = FormingCurrentLimitA();
            double denomV = Math.Max(voltageV, 10.0);
            double apparentKva = Math.Sqrt(activeKw * activeKw + reactiveKvar * reactiveKvar);
            double currentMag = apparentKva * 1000.0 / (denomV * Math.Sqrt(3.0));
            if (currentMag <= iLimit || currentMag < 1e-6)
                return;

            double scale = iLimit / currentMag;
            activeKw *= scale;
            reactiveKvar *= scale;
        }

        private void UpdateIslandVoltageEffectiveTowardCommand(TimeSpan timeStep)
        {
            if (_blackStartEnabled)
            {
                PublishBlackStartEffectiveVoltage();
                lock (_islandVfLock)
                    _currentState.IslandVoltageCommandV = _islandVfCommandV;
                return;
            }

            lock (_islandVfLock)
            {
                if (_currentState.Mode is OperationMode.Off or OperationMode.Standby)
                    _islandVfEffectiveV = 0;
                else
                {
                    double target = _islandVfCommandV;
                    double eff = _islandVfEffectiveV;
                    double gap = target - eff;
                    if (Math.Abs(gap) < 1e-6)
                        _islandVfEffectiveV = target;
                    else
                    {
                        double dt = Math.Max(timeStep.TotalSeconds, 1e-6);
                        double rampFrac = Math.Min(1.0, dt / _islandVoltageRampDurationSec);
                        _islandVfEffectiveV = eff + gap * rampFrac;
                    }
                }

                _currentState.IslandVoltageCommandV = _islandVfCommandV;
                _currentState.IslandVoltageEffectiveV = _islandVfEffectiveV;
            }
        }

        private void PublishBlackStartEffectiveVoltage()
        {
            double vDisplay = 0;
            if (!_liveBusFollower
                && _blackStartPhase != BlackStartPhase.Preparing
                && _blackStartPhase != BlackStartPhase.Inactive
                && _blackStartPhase != BlackStartPhase.Following)
                vDisplay = FormingVoltageRef();

            lock (_islandVfLock)
            {
                _islandVfEffectiveV = vDisplay;
                _currentState.IslandVoltageEffectiveV = vDisplay;
            }
        }

        private void ResetBlackStartRuntime()
        {
            _blackStartPhase = BlackStartPhase.Inactive;
            _currentState.BlackStartPhase = BlackStartPhase.Inactive;
            _blackStartPrepareRemainingSec = 0;
            _voltageRamp.Reset(0);
            _blackStartSoftCapV = 0;
            _blackStartIslandFreqHz = IslandFrequencyRefHz();
            _blackStartInrushActiveKw = 0;
            _blackStartInrushReactiveKvar = 0;
            _liveBusFollower = false;
            IsPreSyncReadyToCutIn = false;
            _joinedFromLiveBus = false;
            _blackStartJoinShare = 1.0;
            _ignoreZeroIslandCommandAfterJoin = false;
            _voltageOuter.Reset();
            _currentInner.Reset();
            _formingPhase.Reset();
            _pll.Reset();
            _currentState.FormingPhaseRad = 0;
            lock (_islandVfLock)
            {
                _islandVfEffectiveV = 0;
                _currentState.IslandVoltageEffectiveV = 0;
            }
            // 暂态状态重置
            _transientAcVoltageV = 0;
            _prevSubStepAcVoltageV = 0;
            _dvDt = 0;
            _dvDtRideThroughMs = 0;
            _inrushTriggered = false;
            _inrushExceededDesignPeak = false;
            _inrushCurrentA = 0;
        }
    }
}
