using EssSimulator.EmsStrategy.Domain;

namespace EssSimulator.EmsStrategy.Application;

/// <summary>
/// 策略热更新补丁。标量与嵌套对象均可空；嵌套对象整段替换对应子树。
/// </summary>
public sealed class EmsStrategyPatchRequest
{
    public bool? Enabled { get; set; }
    public bool? SystemSwitch { get; set; }
    public bool? ActiveEnable { get; set; }
    public bool? ReactiveEnable { get; set; }
    public ActiveMode? ActiveMode { get; set; }
    public ReactiveMode? ReactiveMode { get; set; }
    public LocalRemote? LocalRemote { get; set; }
    public double? LocalActiveSetKw { get; set; }
    public double? RemoteActiveSetKw { get; set; }
    public double? LocalReactiveSetKvar { get; set; }
    public double? RemoteReactiveSetKvar { get; set; }
    public double? PowerFactorSet { get; set; }
    public double? PfSign { get; set; }
    public double? VoltageSetV { get; set; }
    public double? VoltageKp { get; set; }
    public double? VoltageFixedK { get; set; }
    public double? ApparentRatedKva { get; set; }
    public double? PlantRatedKw { get; set; }
    public bool? SlopeEnabled { get; set; }
    public bool? ReactiveSlopeEnabled { get; set; }
    public bool? ApparentLimitEnabled { get; set; }
    public bool? DampEnabled { get; set; }
    public bool? ActivePidEnabled { get; set; }
    public bool? ReactivePidEnabled { get; set; }
    public bool? DistributionEnabled { get; set; }
    public bool? PrimaryFrequencyEnabled { get; set; }
    public bool? InertiaEnabled { get; set; }
    public bool? VoltageDroopEnabled { get; set; }
    public bool? SocBalance { get; set; }

    public SlopeConfig? Slope { get; set; }
    public SlopeConfig? ReactiveSlope { get; set; }
    public PidConfig? ActivePid { get; set; }
    public PidConfig? ReactivePid { get; set; }
    public PrimaryFrequencyConfig? PrimaryFrequency { get; set; }
    public InertiaConfig? Inertia { get; set; }
    public VoltageDroopConfig? VoltageDroop { get; set; }
    public DistributionConfig? Distribution { get; set; }
}
