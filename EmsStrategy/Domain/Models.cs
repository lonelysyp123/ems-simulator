namespace EssSimulator.EmsStrategy.Domain;

/// <summary>并网点与站级测点。功率符号：放电为正、充电为负（kW）。</summary>
public sealed class PlantMeasurements
{
    public DateTime SimTime { get; init; }
    public double FrequencyHz { get; init; }
    public double PccActivePowerKw { get; init; }
    public double PccReactivePowerKvar { get; init; }
    public double PccLineVoltageV { get; init; }
    public double StationBus35LineVoltageV { get; init; }
    public IReadOnlyList<PcsBranchState> Branches { get; init; } = Array.Empty<PcsBranchState>();
}

public sealed class PcsBranchState
{
    public int Index { get; init; }
    public int UnitIndex0 { get; init; }
    public int PcsIndexInUnit { get; init; }
    public bool CommOk { get; init; } = true;
    public bool Fault { get; init; }
    public bool Running { get; init; }
    public bool ChargeProhibited { get; init; }
    public bool DischargeProhibited { get; init; }
    /// <summary>SOC，0~1。</summary>
    public double Soc { get; init; } = 0.5;
    public double RatedKw { get; init; }
    public double MaxChargeKw { get; init; }
    public double MaxDischargeKw { get; init; }
    public double MeasuredActiveKw { get; init; }
    public double MeasuredReactiveKvar { get; init; }
}

public sealed class BranchCommand
{
    public int Index { get; init; }
    public int UnitIndex0 { get; init; }
    public int PcsIndexInUnit { get; init; }
    public double ActivePowerKw { get; init; }
    public double ReactivePowerKvar { get; init; }
}

public sealed class EmsControlOutput
{
    public double PlantActiveTargetKw { get; init; }
    public double PlantActiveCommandKw { get; init; }
    public double PlantReactiveCommandKvar { get; init; }
    public double FrequencyDeltaKw { get; init; }
    public ActionState FrequencyAction { get; init; }
    public double InertiaDeltaKw { get; init; }
    public ActionState InertiaAction { get; init; }
    public double DroopDeltaKvar { get; init; }
    public ActionState DroopAction { get; init; }
    public double QBaseKvar { get; init; }
    public double PlantReactiveTargetKvar { get; init; }
    public double PBaseKw { get; init; }
    public double BeforeLimitKw { get; init; }
    public double AfterLimitKw { get; init; }
    public double PiOutputKw { get; init; }
    public bool OutputEnabled { get; init; }
    public IReadOnlyList<BranchCommand> Branches { get; init; } = Array.Empty<BranchCommand>();
}

public sealed class EmsStrategySnapshot
{
    public bool Enabled { get; init; }
    public bool OutputEnabled { get; init; }
    public ActiveMode ActiveMode { get; init; }
    public ReactiveMode ReactiveMode { get; init; }
    public LocalRemote LocalRemote { get; init; }
    public double FrequencyHz { get; init; }
    public double PccActivePowerKw { get; init; }
    public double PccReactivePowerKvar { get; init; }
    public double PccLineVoltageV { get; init; }
    public double StationBus35LineVoltageV { get; init; }
    public double PBaseKw { get; init; }
    public double FrequencyDeltaKw { get; init; }
    public ActionState FrequencyAction { get; init; }
    public double InertiaDeltaKw { get; init; }
    public ActionState InertiaAction { get; init; }
    public double DroopDeltaKvar { get; init; }
    public ActionState DroopAction { get; init; }
    public double QBaseKvar { get; init; }
    public double PlantReactiveTargetKvar { get; init; }
    public double PlantReactiveCommandKvar { get; init; }
    public double PlantActiveTargetKw { get; init; }
    public double PlantActiveCommandKw { get; init; }
    public double PiOutputKw { get; init; }
    public double BeforeLimitKw { get; init; }
    public double AfterLimitKw { get; init; }
    public IReadOnlyList<BranchCommand> Branches { get; init; } = Array.Empty<BranchCommand>();
}
