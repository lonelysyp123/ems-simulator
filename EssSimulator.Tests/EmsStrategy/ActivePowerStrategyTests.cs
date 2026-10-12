using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;
using EssSimulator.EmsStrategy.Domain.Services;

namespace EssSimulator.Tests.EmsStrategy;

public class ActivePowerStrategyTests
{
    private static PlantMeasurements Meas(double f, double pPcc = 0) => new()
    {
        FrequencyHz = f,
        PccActivePowerKw = pPcc,
        PccReactivePowerKvar = 0,
        Branches = new[]
        {
            new PcsBranchState
            {
                Index = 0, Running = true, CommOk = true, Soc = 0.5, RatedKw = 2500,
                MaxChargeKw = 2500, MaxDischargeKw = 2500
            }
        }
    };

    [Fact]
    public void OpenLoop_IgnoresFrequencyAndHasNoPiLag()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.ActiveMode = ActiveMode.OpenLoopFixed;
        cfg.LocalActiveSetKw = 200;
        cfg.Slope.Enabled = false;
        var s = new ActivePowerStrategy();
        double y = s.Step(cfg, Meas(49.5), TimeSpan.FromMilliseconds(200));
        Assert.Equal(200, y, 5);
        Assert.Equal(ActionState.Reset, s.FrequencyAction);
        Assert.Equal(0, s.FrequencyDeltaKw);
    }

    [Fact]
    public void CloseLoop_TargetIsBasePlusDelta()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.ActiveMode = ActiveMode.CloseLoopFixed;
        cfg.LocalActiveSetKw = 100;
        cfg.Slope.Enabled = false;
        cfg.ActivePid.DeadbandKw = 0;
        cfg.ActivePid.Discretization = PidDiscretization.Dt;
        var s = new ActivePowerStrategy();
        s.Step(cfg, Meas(49.8), TimeSpan.FromMilliseconds(200));
        Assert.Equal(ActionState.Action, s.FrequencyAction);
        Assert.True(s.FrequencyDeltaKw > 0);
        Assert.Equal(100 + s.FrequencyDeltaKw, s.LastTarget, 5);
    }

    [Fact]
    public void ReverseLock_HoldsPBaseWhileFrequencyActing()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.LocalActiveSetKw = 0;
        cfg.Slope.Enabled = false;
        cfg.ActivePid.DeadbandKw = 0;
        cfg.ActivePid.Discretization = PidDiscretization.Dt;
        var s = new ActivePowerStrategy();
        s.Step(cfg, Meas(49.8), TimeSpan.FromMilliseconds(200));
        Assert.True(s.FrequencyDeltaKw > 0);
        cfg.LocalActiveSetKw = -400;
        s.Step(cfg, Meas(49.8), TimeSpan.FromMilliseconds(200));
        Assert.Equal(0, s.LastPBase, 5);
    }

    [Fact]
    public void PidDisabled_PassesDampedTargetThrough()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.LocalActiveSetKw = 250;
        cfg.Slope.Enabled = false;
        cfg.ApparentLimitEnabled = false;
        cfg.DampEnabled = false;
        cfg.PrimaryFrequency.Enabled = false;
        cfg.Inertia.Enabled = false;
        cfg.ActivePid.Enabled = false;
        var s = new ActivePowerStrategy();
        double y = s.Step(cfg, Meas(50), TimeSpan.FromMilliseconds(200));
        Assert.Equal(250, y, 5);
        Assert.Equal(250, s.LastAfterLimit, 5);
    }

    [Fact]
    public void ApparentLimitDisabled_DoesNotClipToCircle()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.LocalActiveSetKw = 1000;
        cfg.ApparentRatedKva = 100;
        cfg.Slope.Enabled = false;
        cfg.ApparentLimitEnabled = false;
        cfg.DampEnabled = false;
        cfg.PrimaryFrequency.Enabled = false;
        cfg.Inertia.Enabled = false;
        cfg.ActivePid.Enabled = false;
        var s = new ActivePowerStrategy();
        Assert.Equal(1000, s.Step(cfg, Meas(50), TimeSpan.FromMilliseconds(200)), 5);

        cfg.ApparentLimitEnabled = true;
        s.Reset();
        Assert.Equal(100, s.Step(cfg, Meas(50), TimeSpan.FromMilliseconds(200)), 5);
    }

    [Fact]
    public void DampDisabled_SkipsHalfFactor()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.LocalActiveSetKw = 1000;
        cfg.ApparentRatedKva = 5000;
        cfg.Slope.Enabled = false;
        cfg.ApparentLimitEnabled = true;
        cfg.DampEnabled = false;
        cfg.PrimaryFrequency.Enabled = false;
        cfg.Inertia.Enabled = false;
        cfg.ActivePid.Enabled = false;
        var s = new ActivePowerStrategy();
        var meas = Meas(50);
        Assert.Equal(1000, s.Step(cfg, meas, TimeSpan.FromMilliseconds(200), otherAxisTarget: 5000), 5);

        cfg.DampEnabled = true;
        s.Reset();
        Assert.Equal(500, s.Step(cfg, meas, TimeSpan.FromMilliseconds(200), otherAxisTarget: 5000), 5);
    }
}
