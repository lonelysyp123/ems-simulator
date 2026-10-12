using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;
using EssSimulator.EmsStrategy.Domain.Algorithms;

namespace EssSimulator.Tests.EmsStrategy;

public class FrequencyDroopTests
{
    private static PrimaryFrequencyConfig DefaultCfg() => new()
    {
        Enabled = true,
        RatedFrequencyHz = 50,
        Deadband1Percent = 0.2,
        Deadband2Percent = 0.5,
        DroopPercent = 3,
        Droop2Percent = 5,
        SegmentCount = 3,
        OverFreqEnable = true,
        UnderFreqEnable = true,
        ControlCycle = TimeSpan.FromSeconds(1),
        ResetTime = TimeSpan.FromSeconds(2),
        MaxOutputKw = 5000,
        MaxAbsorbKw = 5000,
        LimitCoefficient = 1
    };

    [Fact]
    public void InsideDeadband_Zero()
    {
        var cfg = DefaultCfg();
        Assert.False(FrequencyDroopCalculator.IsOutsideDeadband(50.05, cfg));
        Assert.Equal(0, FrequencyDroopCalculator.ComputeDeltaKw(50.05, 5000, cfg));
    }

    [Fact]
    public void UnderFreq_PositiveDelta()
    {
        var cfg = DefaultCfg();
        double dp = FrequencyDroopCalculator.ComputeDeltaKw(49.8, 5000, cfg);
        Assert.True(dp > 0);
        // db1=0.1Hz, f_span=49.9, ΔP=(49.9-49.8)/50*5000/3*100 = 333.333
        Assert.Equal(1000.0 / 3.0, dp, 5);
    }

    [Fact]
    public void OverDroopOverride_ChangesMagnitude()
    {
        var cfg = DefaultCfg();
        cfg.OverDroopPercent = 6;
        double with3 = FrequencyDroopCalculator.ComputeDeltaKw(50.2, 5000, DefaultCfg());
        double with6 = FrequencyDroopCalculator.ComputeDeltaKw(50.2, 5000, cfg);
        Assert.Equal(with3 / 2.0, with6, 5);
    }

    [Fact]
    public void OverFreq_NegativeDelta()
    {
        var cfg = DefaultCfg();
        double dp = FrequencyDroopCalculator.ComputeDeltaKw(50.2, 5000, cfg);
        Assert.True(dp < 0);
        Assert.Equal(-1000.0 / 3.0, dp, 5);
    }

    [Fact]
    public void UnderFreqDisabled_StaysZero()
    {
        var cfg = DefaultCfg();
        cfg.UnderFreqEnable = false;
        Assert.False(FrequencyDroopCalculator.IsOutsideDeadband(49.7, cfg));
        Assert.Equal(0, FrequencyDroopCalculator.ComputeDeltaKw(49.7, 5000, cfg));
    }

    [Fact]
    public void Limit_ClampsOutput()
    {
        var cfg = DefaultCfg();
        cfg.MaxOutputKw = 10;
        double dp = FrequencyDroopCalculator.ComputeDeltaKw(49.0, 5000, cfg);
        Assert.Equal(10, dp);
    }

    [Fact]
    public void FiveSegment_OuterUsesSecondDroop()
    {
        var cfg = DefaultCfg();
        cfg.SegmentCount = 5;
        double inner = FrequencyDroopCalculator.ComputeDeltaKw(49.8, 5000, cfg);
        double outer = FrequencyDroopCalculator.ComputeDeltaKw(49.6, 5000, cfg);
        Assert.True(outer > inner);
        double atBreak = FrequencyDroopCalculator.ComputeDeltaKw(49.75, 5000, cfg);
        Assert.True(outer > atBreak);
    }

    [Fact]
    public void ActionMachine_FirstTickComputesImmediately()
    {
        var machine = new AuxActionMachine();
        var cfg = DefaultCfg();
        double y = machine.Step(
            outsideDeadband: true,
            TimeSpan.FromMilliseconds(200),
            cfg.ControlCycle,
            cfg.ResetTime,
            () => 42);
        Assert.Equal(ActionState.Action, machine.State);
        Assert.Equal(42, y);
    }

    [Fact]
    public void ActionMachine_ResetRequiresHoldTime()
    {
        var machine = new AuxActionMachine();
        var cfg = DefaultCfg();
        machine.Step(true, TimeSpan.FromMilliseconds(200), cfg.ControlCycle, cfg.ResetTime, () => 42);
        double still = machine.Step(false, TimeSpan.FromSeconds(1), cfg.ControlCycle, cfg.ResetTime, () => 99);
        Assert.Equal(ActionState.Action, machine.State);
        Assert.Equal(42, still);
        machine.Step(false, TimeSpan.FromSeconds(1), cfg.ControlCycle, cfg.ResetTime, () => 99);
        Assert.Equal(ActionState.Reset, machine.State);
        Assert.Equal(0, machine.Output);
    }
}
