using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;
using EssSimulator.EmsStrategy.Domain.Algorithms;
using EssSimulator.EmsStrategy.Domain.Services;

namespace EssSimulator.Tests.EmsStrategy;

public class PfToReactiveConverterTests
{
    [Fact]
    public void UnityPf_ZeroQ()
    {
        Assert.Equal(0, PfToReactiveConverter.FromPf(1000, 1), 6);
    }

    [Fact]
    public void Pf090_MatchesFormula()
    {
        double q = PfToReactiveConverter.FromPf(1000, 0.9);
        double expected = 1000 * Math.Sqrt(1 - 0.81) / 0.9;
        Assert.Equal(expected, q, 6);
    }
}

public class InertiaCalculatorTests
{
    [Fact]
    public void FallingFrequency_ProducesPositiveP()
    {
        var calc = new InertiaCalculator();
        var cfg = new InertiaConfig
        {
            Enabled = true,
            RatedFrequencyHz = 50,
            InertiaTimeSec = 6,
            AmplitudeDeadbandHz = 0.05,
            RateDeadbandHzPerSec = 0.05,
            FreqMinHz = 47,
            FreqMaxHz = 53,
            ControlCycle = TimeSpan.FromSeconds(1)
        };
        calc.Evaluate(50.0, TimeSpan.FromSeconds(1), 5000, cfg);
        var s = calc.Evaluate(49.7, TimeSpan.FromSeconds(1), 5000, cfg);
        Assert.True(s.ConditionMet);
        Assert.True(s.DfDtHzPerSec < 0);
        Assert.True(s.PowerKw > 0);
    }

    [Fact]
    public void InsideAmplitudeDeadband_NotActing()
    {
        var calc = new InertiaCalculator();
        var cfg = new InertiaConfig
        {
            AmplitudeDeadbandHz = 0.5,
            RateDeadbandHzPerSec = 0.01,
            ControlCycle = TimeSpan.FromSeconds(1),
            InertiaTimeSec = 6
        };
        calc.Evaluate(50.0, TimeSpan.FromSeconds(1), 5000, cfg);
        var s = calc.Evaluate(49.9, TimeSpan.FromSeconds(1), 5000, cfg);
        Assert.False(s.ConditionMet);
        Assert.Equal(0, s.PowerKw);
    }
}

public class VoltageDroopTests
{
    private static VoltageDroopConfig DefaultCfg() => new()
    {
        RatedVoltageV = 35000,
        Deadband1Percent = 0.5,
        Deadband2Percent = 1.5,
        K1Percent = 4,
        K2Percent = 6,
        SegmentCount = 3,
        VoltageCurveType = 0,
        UnderVoltEnable = true,
        OverVoltEnable = true,
        MaxOutputKvar = 5000,
        MaxAbsorbKvar = 5000,
        LimitCoefficient = 1
    };

    private static double Segment(double uSpan, double u, double u0, double qRated, double k) =>
        (uSpan - u) / u0 * qRated / k * 100.0;

    [Fact]
    public void UnderVoltage_MatchesPercentDroopFromDeadband()
    {
        var cfg = DefaultCfg();
        // db1=175 V, Uspan=34825, ΔQ=(34825-34000)/35000*5000/4*100
        Assert.Equal(Segment(34825, 34000, 35000, 5000, 4), VoltageDroopCalculator.ComputeDeltaKvar(34000, 5000, cfg), 6);
    }

    [Fact]
    public void FromRatedCurve_UsesU0AsSpan()
    {
        var cfg = DefaultCfg();
        cfg.VoltageCurveType = 1;
        Assert.Equal(Segment(35000, 34000, 35000, 5000, 4), VoltageDroopCalculator.ComputeDeltaKvar(34000, 5000, cfg), 6);
    }

    [Fact]
    public void InsideDeadband_Zero()
    {
        var cfg = new VoltageDroopConfig { RatedVoltageV = 35000, Deadband1Percent = 1 };
        Assert.False(VoltageDroopCalculator.IsOutsideDeadband(35000, cfg));
        Assert.Equal(0, VoltageDroopCalculator.ComputeDeltaKvar(35000, 5000, cfg));
    }

    [Fact]
    public void OverVoltage_NegativeDelta()
    {
        var cfg = DefaultCfg();
        double dq = VoltageDroopCalculator.ComputeDeltaKvar(36000, 5000, cfg);
        Assert.True(dq < 0);
        Assert.Equal(Segment(35175, 36000, 35000, 5000, 4), dq, 6);
    }

    [Fact]
    public void PlantRating_ScalesOutput()
    {
        var cfg = DefaultCfg();
        cfg.MaxOutputKvar = 20000;
        cfg.MaxAbsorbKvar = 20000;
        double at5 = VoltageDroopCalculator.ComputeDeltaKvar(34000, 5000, cfg);
        double at10 = VoltageDroopCalculator.ComputeDeltaKvar(34000, 10000, cfg);
        Assert.Equal(at5 * 2, at10, 6);
    }

    [Fact]
    public void FiveSegment_OuterUsesSecondDroop()
    {
        var cfg = DefaultCfg();
        cfg.SegmentCount = 5;
        double inner = VoltageDroopCalculator.ComputeDeltaKvar(34700, 5000, cfg);
        double outer = VoltageDroopCalculator.ComputeDeltaKvar(34000, 5000, cfg);
        Assert.True(inner > 0);
        Assert.True(outer > inner);
        double db2 = 35000 * 1.5 / 100.0;
        double atBreak = VoltageDroopCalculator.ComputeDeltaKvar(35000 - db2, 5000, cfg);
        Assert.True(outer > atBreak);
    }

    [Fact]
    public void SelectMeasuredVoltage_Prefers35kVWhenRatedIs35kV()
    {
        Assert.Equal(34000, VoltageDroopCalculator.SelectMeasuredVoltage(220000, 34000, 35000), 6);
        Assert.Equal(220000, VoltageDroopCalculator.SelectMeasuredVoltage(220000, 34000, 220000), 6);
        Assert.Equal(220000, VoltageDroopCalculator.SelectMeasuredVoltage(220000, 0, 35000), 6);
    }
}

public class ReactivePowerStrategyTests
{
    [Fact]
    public void OpenLoop_IgnoresVoltageDroop()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.ReactiveMode = ReactiveMode.OpenLoopFixed;
        cfg.LocalReactiveSetKvar = 300;
        cfg.VoltageDroop.Enabled = true;
        cfg.ReactiveSlope.Enabled = false;
        var s = new ReactivePowerStrategy();
        double y = s.Step(cfg, new PlantMeasurements { PccLineVoltageV = 30000, PccActivePowerKw = 0 }, TimeSpan.FromMilliseconds(200));
        Assert.Equal(300, y, 5);
        Assert.Equal(ActionState.Reset, s.DroopAction);
    }

    [Fact]
    public void CloseLoopDroop_UsesStationBusWhenRatedIs35kV()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.ReactiveMode = ReactiveMode.CloseLoopFixed;
        cfg.VoltageDroop.Enabled = true;
        cfg.ReactiveSlope.Enabled = false;
        cfg.ReactivePid.Enabled = false;
        var s = new ReactivePowerStrategy();
        s.Step(cfg, new PlantMeasurements
        {
            PccLineVoltageV = 220000,
            StationBus35LineVoltageV = 34000,
            PccReactivePowerKvar = 0
        }, TimeSpan.FromMilliseconds(200));
        Assert.Equal(ActionState.Action, s.DroopAction);
        Assert.True(s.DroopDeltaKvar > 0);
        Assert.Equal(
            VoltageDroopCalculator.ComputeDeltaKvar(34000, cfg.PlantRatedKw, cfg.VoltageDroop),
            s.DroopDeltaKvar,
            5);
    }

    [Fact]
    public void VoltageFixed_MapsPControlToQ()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.ReactiveMode = ReactiveMode.VoltageFixed;
        cfg.VoltageSetV = 35000;
        cfg.VoltageKp = 0.5;
        cfg.VoltageFixedK = 1;
        cfg.ReactiveSlope.Enabled = false;
        cfg.ReactivePid.DeadbandKw = 0;
        cfg.ReactivePid.Kp = 1;
        cfg.ReactivePid.Ki = 0;
        cfg.ReactivePid.Discretization = PidDiscretization.Dt;
        var s = new ReactivePowerStrategy();
        var meas = new PlantMeasurements { PccLineVoltageV = 34000, PccReactivePowerKvar = 100 };
        s.Step(cfg, meas, TimeSpan.FromMilliseconds(200));
        // u_out = 34000 + 0.5*1000 = 34500; Q = (34500-34000)*1 + 100 = 600
        Assert.Equal(600, s.LastQBase, 5);
    }

    [Fact]
    public void PowerFactor_SetsQFromP()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.ReactiveMode = ReactiveMode.PowerFactor;
        cfg.PowerFactorSet = 0.9;
        cfg.ReactiveSlope.Enabled = false;
        cfg.ReactivePid.DeadbandKw = 0;
        cfg.ReactivePid.Kp = 1;
        cfg.ReactivePid.Ki = 0;
        cfg.ReactivePid.Discretization = PidDiscretization.Dt;
        var s = new ReactivePowerStrategy();
        var meas = new PlantMeasurements { PccActivePowerKw = 1000, PccReactivePowerKvar = 0 };
        s.Step(cfg, meas, TimeSpan.FromMilliseconds(200));
        Assert.True(s.LastQBase > 0);
        Assert.Equal(PfToReactiveConverter.FromPf(1000, 0.9), s.LastQBase, 5);
    }
}

public class ActivePowerPhase2Tests
{
    [Fact]
    public void InertiaLock_SkipsPrimaryFrequency()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.Slope.Enabled = false;
        cfg.ActivePid.DeadbandKw = 0;
        cfg.ActivePid.Kp = 1;
        cfg.ActivePid.Ki = 0;
        cfg.ActivePid.Discretization = PidDiscretization.Dt;
        cfg.Inertia.Enabled = true;
        cfg.Inertia.LockPrimaryFrequency = true;
        cfg.Inertia.AmplitudeDeadbandHz = 0.05;
        cfg.Inertia.RateDeadbandHzPerSec = 0.05;
        cfg.Inertia.ControlCycle = TimeSpan.FromSeconds(1);
        cfg.Inertia.InertiaTimeSec = 6;
        var s = new ActivePowerStrategy();
        var b = new[]
        {
            new PcsBranchState { Index = 0, Running = true, CommOk = true, Soc = 0.5, RatedKw = 2500, MaxChargeKw = 2500, MaxDischargeKw = 2500 }
        };
        s.Step(cfg, new PlantMeasurements { FrequencyHz = 50, Branches = b }, TimeSpan.FromSeconds(1));
        s.Step(cfg, new PlantMeasurements { FrequencyHz = 49.6, Branches = b }, TimeSpan.FromSeconds(1));
        Assert.Equal(ActionState.Action, s.InertiaAction);
        Assert.Equal(ActionState.Reset, s.FrequencyAction);
        Assert.True(s.InertiaDeltaKw > 0);
        Assert.Equal(0, s.FrequencyDeltaKw);
    }
}

public class EmsStrategyEnginePhase2Tests
{
    [Fact]
    public void CloseLoop_TargetIncludesInertiaWhenEnabled()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.Slope.Enabled = false;
        cfg.PrimaryFrequency.Enabled = false;
        cfg.Inertia.Enabled = true;
        cfg.Inertia.AmplitudeDeadbandHz = 0.05;
        cfg.Inertia.RateDeadbandHzPerSec = 0.05;
        cfg.Inertia.ControlCycle = TimeSpan.FromSeconds(1);
        cfg.ActivePid.DeadbandKw = 0;
        cfg.ActivePid.Kp = 1;
        cfg.ActivePid.Ki = 0;
        cfg.ActivePid.Discretization = PidDiscretization.Dt;
        var engine = new EmsStrategyEngine();
        engine.Initialize(cfg);
        var branches = new[]
        {
            new PcsBranchState { Index = 0, Running = true, CommOk = true, Soc = 0.5, RatedKw = 2500, MaxChargeKw = 2500, MaxDischargeKw = 2500 }
        };
        engine.UpdateMeasurements(new PlantMeasurements { FrequencyHz = 50, Branches = branches });
        engine.Step(TimeSpan.FromSeconds(1));
        engine.UpdateMeasurements(new PlantMeasurements { FrequencyHz = 49.6, Branches = branches });
        var output = engine.Step(TimeSpan.FromSeconds(1));
        Assert.Equal(ActionState.Action, output.InertiaAction);
        Assert.Equal(output.PBaseKw + output.InertiaDeltaKw, output.PlantActiveTargetKw, 5);
    }

    [Fact]
    public void OpenLoopReactive_DistributesQ()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.ActiveEnable = false;
        cfg.ReactiveMode = ReactiveMode.OpenLoopFixed;
        cfg.LocalReactiveSetKvar = 200;
        cfg.ReactiveSlope.Enabled = false;
        var engine = new EmsStrategyEngine();
        engine.Initialize(cfg);
        engine.UpdateMeasurements(new PlantMeasurements
        {
            Branches = new[]
            {
                new PcsBranchState { Index = 0, Running = true, CommOk = true, Soc = 0.5, RatedKw = 2500, MaxChargeKw = 2500, MaxDischargeKw = 2500 },
                new PcsBranchState { Index = 1, Running = true, CommOk = true, Soc = 0.5, RatedKw = 2500, MaxChargeKw = 2500, MaxDischargeKw = 2500 }
            }
        });
        var output = engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.Equal(200, output.PlantReactiveCommandKvar, 5);
        Assert.Equal(200, output.Branches.Sum(b => b.ReactivePowerKvar), 5);
    }
}
