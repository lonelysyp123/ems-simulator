using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;
using EssSimulator.EmsStrategy.Domain.Algorithms;

namespace EssSimulator.Tests.EmsStrategy;

public class EmsStrategyConfigTests
{
    [Fact]
    public void CreateDefault_HasPromptPrimaryValues()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        Assert.False(cfg.Enabled);
        Assert.False(cfg.Slope.Enabled);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), cfg.PrimaryFrequency.ControlCycle);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), cfg.PrimaryFrequency.ResetTime);
        Assert.Equal(0.2, cfg.PrimaryFrequency.Deadband1Percent);
        Assert.Equal(3, cfg.PrimaryFrequency.DroopPercent);
        Assert.Equal(TimeSpan.FromMilliseconds(3000), cfg.ActivePid.Period);
        Assert.Equal(PidDiscretization.CompatiblePeriod, cfg.ActivePid.Discretization);
    }
}

public class SlopeLimiterTests
{
    [Fact]
    public void Disabled_JumpsToTarget()
    {
        var s = new SlopeLimiter();
        s.Configure(false, 10, 10);
        Assert.Equal(80, s.Step(80, 0, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Rise_FromMeasure_UsesKwPerMin()
    {
        // C: period_min = 1000/60000, Δ = 60 kW/min * 1/60 = 1 kW
        double y = SlopeLimiter.Calculate(100, 60, 60, TimeSpan.FromSeconds(1), measure: 0);
        Assert.Equal(1, y, 6);
    }

    [Fact]
    public void SameSignDown_UsesFallRate()
    {
        double y = SlopeLimiter.Calculate(10, 60, 120, TimeSpan.FromSeconds(1), measure: 100);
        Assert.Equal(98, y, 6);
    }

    [Fact]
    public void ZeroCross_DoesNotJumpSignInOneSlowStep()
    {
        double next = SlopeLimiter.Calculate(-50, 60, 60, TimeSpan.FromSeconds(1), measure: 50);
        Assert.Equal(49, next, 6);
        Assert.True(next > 0);
    }

    [Fact]
    public void Step_UsesMeasurementNotInternalState()
    {
        var s = new SlopeLimiter();
        s.Configure(true, 60, 60);
        Assert.Equal(1, s.Step(100, measure: 0, TimeSpan.FromSeconds(1)), 6);
        Assert.Equal(2, s.Step(100, measure: 1, TimeSpan.FromSeconds(1)), 6);
    }
}

public class PidControllerTests
{
    [Fact]
    public void Deadband_HoldsOutput()
    {
        var pid = new PidController();
        pid.Configure(new PidConfig
        {
            Kp = 1,
            Ki = 1,
            Period = TimeSpan.FromSeconds(1),
            DeadbandKw = 2,
            OutMinKw = -100,
            OutMaxKw = 100,
            Discretization = PidDiscretization.Dt
        });
        Assert.Equal(0, pid.Step(0.5, TimeSpan.FromSeconds(1)));
        Assert.Equal(0, pid.Integral);
    }

    [Fact]
    public void Output_Clamped()
    {
        var pid = new PidController();
        pid.Configure(new PidConfig
        {
            Kp = 10,
            Ki = 0,
            Period = TimeSpan.FromSeconds(1),
            OutMinKw = -5,
            OutMaxKw = 5,
            Discretization = PidDiscretization.Dt
        });
        Assert.Equal(5, pid.Step(10, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void CompatiblePeriod_WaitsUnlessForced()
    {
        var pid = new PidController();
        pid.Configure(new PidConfig
        {
            Kp = 1,
            Ki = 0,
            Period = TimeSpan.FromSeconds(3),
            OutMinKw = -100,
            OutMaxKw = 100,
            Discretization = PidDiscretization.CompatiblePeriod
        });
        Assert.Equal(0, pid.Step(10, TimeSpan.FromSeconds(1)));
        Assert.Equal(10, pid.Step(10, TimeSpan.FromSeconds(1), force: true));
    }

    [Fact]
    public void CompatiblePeriod_FirstTick55ThenDeadbandHolds()
    {
        var pid = new PidController();
        pid.Configure(new PidConfig
        {
            Kp = 0.4,
            Ki = 0.05,
            Kb = 0.5,
            Period = TimeSpan.FromMilliseconds(3000),
            DeadbandKw = 5,
            OutMinKw = -5000,
            OutMaxKw = 5000,
            Discretization = PidDiscretization.CompatiblePeriod
        });

        double u0 = pid.Compute(100, measure: 0, realValue: 0, TimeSpan.FromSeconds(3));
        Assert.Equal(55, u0, 6);
        Assert.Equal(15, pid.Integral, 6);

        double uHold = pid.Compute(100, measure: 95, realValue: 95, TimeSpan.FromSeconds(3));
        Assert.Equal(55, uHold, 6);
        Assert.Equal(15, pid.Integral, 6);
    }

    [Fact]
    public void CompatiblePeriod_NextTickAddsPAndDeltaIToLastOutput()
    {
        var pid = new PidController();
        pid.Configure(new PidConfig
        {
            Kp = 0.4,
            Ki = 0.05,
            Kb = 0.5,
            Period = TimeSpan.FromMilliseconds(3000),
            DeadbandKw = 0,
            OutMinKw = -5000,
            OutMaxKw = 5000,
            Discretization = PidDiscretization.CompatiblePeriod
        });

        double u0 = pid.Compute(100, measure: 0, realValue: 0, TimeSpan.FromSeconds(3));
        Assert.Equal(55, u0, 6);

        // e=50, P=20, ΔI = 0.05*3*50 − 0.5*(55−50) = 5 → u = 55+20+5 = 80
        double u1 = pid.Compute(100, measure: 50, realValue: 50, TimeSpan.FromSeconds(3));
        Assert.Equal(80, u1, 6);
        Assert.Equal(20, pid.Integral, 6);
    }

    [Fact]
    public void CompatiblePeriod_Kp08_DeadbandHoldsLastCommand()
    {
        var pid = new PidController();
        pid.Configure(new PidConfig
        {
            Kp = 0.8,
            Ki = 0.05,
            Kb = 0.5,
            Period = TimeSpan.FromMilliseconds(3000),
            DeadbandKw = 5,
            OutMinKw = -5000,
            OutMaxKw = 5000,
            Discretization = PidDiscretization.CompatiblePeriod
        });

        double u0 = pid.Compute(100, measure: 0, realValue: 0, TimeSpan.FromSeconds(3));
        Assert.Equal(95, u0, 6);

        double uHold = pid.Compute(100, measure: 95, realValue: 95, TimeSpan.FromSeconds(3));
        Assert.Equal(95, uHold, 6);
        Assert.Equal(15, pid.Integral, 6);
    }

    [Fact]
    public void PCompute_MatchesC()
    {
        var pid = new PidController();
        pid.Configure(new PidConfig
        {
            Kp = 0.5,
            OutMinKw = 0,
            OutMaxKw = 70000
        });
        double u = pid.PCompute(35000, 34000);
        Assert.Equal(34500, u, 6);
    }

    [Fact]
    public void Configure_DoesNotResetIntegral()
    {
        var pid = new PidController();
        var cfg = new PidConfig
        {
            Kp = 0,
            Ki = 1,
            Period = TimeSpan.FromSeconds(1),
            OutMinKw = -100,
            OutMaxKw = 100,
            Discretization = PidDiscretization.Dt
        };
        pid.Configure(cfg);
        pid.Step(10, TimeSpan.FromSeconds(1));
        Assert.Equal(10, pid.Integral, 3);
        cfg.Kp = 2;
        pid.Configure(cfg);
        Assert.Equal(10, pid.Integral, 3);
        Assert.Equal(2, pid.Kp);
    }
}

public class ApparentPowerLimiterTests
{
    [Fact]
    public void LimitsActiveByOtherAxis()
    {
        double p = ApparentPowerLimiter.LimitGrid(100, 60, 100);
        Assert.Equal(80, p, 5);
    }

    [Fact]
    public void OtherAxisAtRated_ForcesZero()
    {
        Assert.Equal(0, ApparentPowerLimiter.LimitGrid(50, 100, 100));
        Assert.Equal(0, ApparentPowerLimiter.LimitReactive(50, 100, 100));
    }

    [Fact]
    public void Damp_HalvesStepWhenCircleExceeded()
    {
        // measured 80, limited 100, slope target 100, other 0, S=90 → 100²>90², damp
        double y = ApparentPowerLimiter.Damp(80, 100, 100, 0, 90);
        Assert.Equal(90, y, 5);
    }

    [Fact]
    public void Damp_SkipsWhenAlreadyInsideCircle()
    {
        double y = ApparentPowerLimiter.Damp(0, 50, 50, 0, 100);
        Assert.Equal(50, y, 5);
    }

    [Fact]
    public void OpenLimit_PredictsGridFromStorageDelta()
    {
        // grid 80, other 0, target storage 50, storage meas 0, S=100 → predict 130, clip storage
        double y = ApparentPowerLimiter.LimitOpen(80, 0, 50, 0, 100);
        Assert.Equal(20, y, 5);
    }
}
