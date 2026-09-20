using System;
using EssSimulator.EssDeviceSimModel.Control;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    public sealed partial class PcsDevice
    {
        internal PcsStepMemento CaptureStepMemento()
        {
            return new PcsStepMemento
            {
                State = CloneState(_currentState),
                GridVoltage = _gridState.Voltage,
                GridFrequency = _gridState.Frequency,
                GridAvailable = _gridState.IsAvailable,
                PendingActiveSetpoint = _pendingActiveSetpoint,
                PendingReactiveSetpoint = _pendingReactiveSetpoint,
                LoadActivePowerKw = _loadActivePowerKw,
                LoadReactivePowerKvar = _loadReactivePowerKvar,
                IslandVfCommandV = _islandVfCommandV,
                IslandVfEffectiveV = _islandVfEffectiveV,
                BlackStartEnabled = _blackStartEnabled,
                BlackStartPhase = _blackStartPhase,
                BlackStartPrepareRemainingSec = _blackStartPrepareRemainingSec,
                BlackStartSoftCapV = _blackStartSoftCapV,
                BlackStartIslandFreqHz = _blackStartIslandFreqHz,
                IslandFrequencyCommandHz = _islandFrequencyCommandHz,
                UnitBusVoltageV = _unitBusVoltageV,
                BusFrequencyHz = _busFrequencyHz,
                BusPhaseRad = _busPhaseRad,
                LiveBusFollower = _liveBusFollower,
                JoinedFromLiveBus = _joinedFromLiveBus,
                BlackStartJoinShare = _blackStartJoinShare,
                TransientAcVoltageV = _transientAcVoltageV,
                PrevSubStepAcVoltageV = _prevSubStepAcVoltageV,
                DvDt = _dvDt,
                DvDtRideThroughMs = _dvDtRideThroughMs,
                InrushTriggered = _inrushTriggered,
                InrushCurrentA = _inrushCurrentA,
                InrushPeakThisTick = _inrushPeakThisTick,
                VoltageRampOutput = _voltageRamp.Output,
                VoltageRampTarget = _voltageRamp.Target,
                VoltageOuterIntegral = _voltageOuter.Integral,
                VoltageOuterOutput = _voltageOuter.Output,
                CurrentInnerOutput = _currentInner.Output,
                CurrentInnerSaturated = _currentInner.Saturated,
                FormingThetaRad = _formingPhase.ThetaRad,
                PllEnabled = _pll.Enabled,
                PllVoltageV = _pll.VoltageV,
                PllFrequencyHz = _pll.FrequencyHz,
                PllThetaRad = _pll.ThetaRad,
                IsPreSyncReadyToCutIn = IsPreSyncReadyToCutIn,
                ExternalRunCommand = _externalRunCommand,
                LatchedFaultType = _latchedFaultType,
                LatchedFaultMessage = _latchedFaultMessage,
                FaultTripLatched = _faultTripLatched
            };
        }

        internal void RestoreStepMemento(PcsStepMemento m)
        {
            _currentState = CloneState(m.State);
            _gridState.Voltage = m.GridVoltage;
            _gridState.Frequency = m.GridFrequency;
            _gridState.IsAvailable = m.GridAvailable;
            _pendingActiveSetpoint = m.PendingActiveSetpoint;
            _pendingReactiveSetpoint = m.PendingReactiveSetpoint;
            _loadActivePowerKw = m.LoadActivePowerKw;
            _loadReactivePowerKvar = m.LoadReactivePowerKvar;
            _islandVfCommandV = m.IslandVfCommandV;
            _islandVfEffectiveV = m.IslandVfEffectiveV;
            _blackStartEnabled = m.BlackStartEnabled;
            _blackStartPhase = m.BlackStartPhase;
            _blackStartPrepareRemainingSec = m.BlackStartPrepareRemainingSec;
            _blackStartSoftCapV = m.BlackStartSoftCapV;
            _blackStartIslandFreqHz = m.BlackStartIslandFreqHz;
            _islandFrequencyCommandHz = m.IslandFrequencyCommandHz;
            _unitBusVoltageV = m.UnitBusVoltageV;
            _busFrequencyHz = m.BusFrequencyHz;
            _busPhaseRad = m.BusPhaseRad;
            _liveBusFollower = m.LiveBusFollower;
            _joinedFromLiveBus = m.JoinedFromLiveBus;
            _blackStartJoinShare = m.BlackStartJoinShare;
            _transientAcVoltageV = m.TransientAcVoltageV;
            _prevSubStepAcVoltageV = m.PrevSubStepAcVoltageV;
            _dvDt = m.DvDt;
            _dvDtRideThroughMs = m.DvDtRideThroughMs;
            _inrushTriggered = m.InrushTriggered;
            _inrushCurrentA = m.InrushCurrentA;
            _inrushPeakThisTick = m.InrushPeakThisTick;
            _voltageRamp.Restore(m.VoltageRampOutput, m.VoltageRampTarget);
            _voltageOuter.Restore(m.VoltageOuterIntegral, m.VoltageOuterOutput);
            _currentInner.Restore(m.CurrentInnerOutput, m.CurrentInnerSaturated);
            _formingPhase.Restore(m.FormingThetaRad);
            _pll.Restore(m.PllEnabled, m.PllVoltageV, m.PllFrequencyHz, m.PllThetaRad);
            IsPreSyncReadyToCutIn = m.IsPreSyncReadyToCutIn;
            _externalRunCommand = m.ExternalRunCommand;
            _latchedFaultType = m.LatchedFaultType;
            _latchedFaultMessage = m.LatchedFaultMessage;
            _faultTripLatched = m.FaultTripLatched;
            PublishPortsFromState();
        }

        internal double CurrentInnerOutput => _currentInner.Output;

        private static PcsState CloneState(PcsState s) => new()
        {
            Mode = s.Mode,
            GMode = s.GMode,
            DcVoltage = s.DcVoltage,
            DcCurrent = s.DcCurrent,
            AcVoltage = s.AcVoltage,
            AcCurrent = s.AcCurrent,
            ActivePower = s.ActivePower,
            ReactivePower = s.ReactivePower,
            Frequency = s.Frequency,
            Temperature = s.Temperature,
            ActivePowerSettingVal = s.ActivePowerSettingVal,
            ReactivePowerSettingVal = s.ReactivePowerSettingVal,
            PowerFatorSettingVal = s.PowerFatorSettingVal,
            DcCCSettingVal = s.DcCCSettingVal,
            DcCVSettingVal = s.DcCVSettingVal,
            DcCPSettingVal = s.DcCPSettingVal,
            DcProtectChgCurrent = s.DcProtectChgCurrent,
            DcProtectChgVoltage = s.DcProtectChgVoltage,
            DcProtectDsgCurrent = s.DcProtectDsgCurrent,
            DcProtectDsgVoltage = s.DcProtectDsgVoltage,
            DcLimitChgCurrent = s.DcLimitChgCurrent,
            DcLimitChgVoltage = s.DcLimitChgVoltage,
            DcLimitDsgCurrent = s.DcLimitDsgCurrent,
            DcLimitDsgVoltage = s.DcLimitDsgVoltage,
            DcLimitChgPower = s.DcLimitChgPower,
            DcLimitDsgPower = s.DcLimitDsgPower,
            FaultType = s.FaultType,
            FaultMessage = s.FaultMessage,
            DailyChargeEnergy = s.DailyChargeEnergy,
            TotalChargeEnergy = s.TotalChargeEnergy,
            DailyDischargeEnergy = s.DailyDischargeEnergy,
            TotalDischargeEnergy = s.TotalDischargeEnergy,
            Timestamp = s.Timestamp,
            IslandVoltageCommandV = s.IslandVoltageCommandV,
            IslandVoltageEffectiveV = s.IslandVoltageEffectiveV,
            BlackStartEnabled = s.BlackStartEnabled,
            BlackStartPhase = s.BlackStartPhase,
            FormingPhaseRad = s.FormingPhaseRad,
            DvDt = s.DvDt,
            InrushCurrentA = s.InrushCurrentA,
            InrushPeakA = s.InrushPeakA,
            ProtectionFlags = s.ProtectionFlags
        };
    }

    internal sealed class PcsStepMemento
    {
        public PcsState State { get; init; } = new();
        public double GridVoltage { get; init; }
        public double GridFrequency { get; init; }
        public bool GridAvailable { get; init; }
        public double PendingActiveSetpoint { get; init; }
        public double PendingReactiveSetpoint { get; init; }
        public double LoadActivePowerKw { get; init; }
        public double LoadReactivePowerKvar { get; init; }
        public double IslandVfCommandV { get; init; }
        public double IslandVfEffectiveV { get; init; }
        public bool BlackStartEnabled { get; init; }
        public BlackStartPhase BlackStartPhase { get; init; }
        public double BlackStartPrepareRemainingSec { get; init; }
        public double BlackStartSoftCapV { get; init; }
        public double BlackStartIslandFreqHz { get; init; }
        public double IslandFrequencyCommandHz { get; init; }
        public double UnitBusVoltageV { get; init; }
        public double BusFrequencyHz { get; init; }
        public double BusPhaseRad { get; init; }
        public bool LiveBusFollower { get; init; }
        public bool JoinedFromLiveBus { get; init; }
        public double BlackStartJoinShare { get; init; }
        public double TransientAcVoltageV { get; init; }
        public double PrevSubStepAcVoltageV { get; init; }
        public double DvDt { get; init; }
        public double DvDtRideThroughMs { get; init; }
        public bool InrushTriggered { get; init; }
        public double InrushCurrentA { get; init; }
        public double InrushPeakThisTick { get; init; }
        public double VoltageRampOutput { get; init; }
        public double VoltageRampTarget { get; init; }
        public double VoltageOuterIntegral { get; init; }
        public double VoltageOuterOutput { get; init; }
        public double CurrentInnerOutput { get; init; }
        public bool CurrentInnerSaturated { get; init; }
        public double FormingThetaRad { get; init; }
        public bool PllEnabled { get; init; }
        public double PllVoltageV { get; init; }
        public double PllFrequencyHz { get; init; }
        public double PllThetaRad { get; init; }
        public bool IsPreSyncReadyToCutIn { get; init; }
        public bool ExternalRunCommand { get; init; }
        public ushort LatchedFaultType { get; init; }
        public string? LatchedFaultMessage { get; init; }
        public bool FaultTripLatched { get; init; }
    }
}
