using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;

namespace EssSimulator.Tests.EmsStrategy;

public class EmsStrategyEngineTests
{
    private static PlantMeasurements Meas(double frequencyHz, bool running = true) => new()
    {
        FrequencyHz = frequencyHz,
        PccActivePowerKw = 0,
        PccReactivePowerKvar = 0,
        Branches = new[]
        {
            new PcsBranchState
            {
                Index = 0, UnitIndex0 = 0, PcsIndexInUnit = 0,
                CommOk = true, Running = running, Soc = 0.5,
                RatedKw = 2500, MaxChargeKw = 2500, MaxDischargeKw = 2500
            },
            new PcsBranchState
            {
                Index = 1, UnitIndex0 = 0, PcsIndexInUnit = 1,
                CommOk = true, Running = running, Soc = 0.5,
                RatedKw = 2500, MaxChargeKw = 2500, MaxDischargeKw = 2500
            }
        }
    };

    [Fact]
    public void Initialize_Step_ReturnsZeroWhenDisabled()
    {
        var engine = new EmsStrategyEngine();
        engine.Initialize(EmsStrategyConfig.CreateDefault());
        engine.UpdateMeasurements(Meas(49.5));
        var output = engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.False(output.OutputEnabled);
        Assert.Equal(0, output.PlantActiveCommandKw);
    }

    [Fact]
    public void CloseLoop_FrequencyStep_FirstTickNonZeroWhenSlopeOff()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.Slope.Enabled = false;
        cfg.LocalActiveSetKw = 0;
        cfg.ActivePid.DeadbandKw = 0;
        cfg.ActivePid.Kp = 1;
        cfg.ActivePid.Ki = 0;
        cfg.ActivePid.Discretization = PidDiscretization.Dt;
        var engine = new EmsStrategyEngine();
        engine.Initialize(cfg);
        engine.UpdateMeasurements(Meas(49.7));
        var output = engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.True(output.OutputEnabled);
        Assert.Equal(ActionState.Action, output.FrequencyAction);
        Assert.True(output.FrequencyDeltaKw > 0);
        Assert.True(output.PlantActiveCommandKw > 0);
        Assert.Equal(2, output.Branches.Count);
        Assert.True(output.Branches.Sum(b => b.ActivePowerKw) > 0);
    }

    [Fact]
    public void SystemSwitchOff_ForcesZero()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.SystemSwitch = false;
        var engine = new EmsStrategyEngine();
        engine.Initialize(cfg);
        engine.UpdateMeasurements(Meas(49.5));
        var output = engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.False(output.OutputEnabled);
        Assert.Equal(0, output.PlantActiveCommandKw);
    }

    [Fact]
    public void ModeSwitch_ResetsAction()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.Slope.Enabled = false;
        cfg.ActivePid.DeadbandKw = 0;
        cfg.ActivePid.Discretization = PidDiscretization.Dt;
        var engine = new EmsStrategyEngine();
        engine.Initialize(cfg);
        engine.UpdateMeasurements(Meas(49.7));
        engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.Equal(ActionState.Action, engine.GetSnapshot().FrequencyAction);

        cfg.ActiveMode = ActiveMode.OpenLoopFixed;
        cfg.LocalActiveSetKw = 0;
        engine.UpdateConfig(cfg);
        engine.UpdateMeasurements(Meas(49.7));
        engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.Equal(ActionState.Reset, engine.GetSnapshot().FrequencyAction);
    }

    [Fact]
    public void UpdateConfig_KpChange_DoesNotResetFrequencyAction()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.Slope.Enabled = false;
        cfg.ActivePid.DeadbandKw = 0;
        cfg.ActivePid.Discretization = PidDiscretization.Dt;
        var engine = new EmsStrategyEngine();
        engine.Initialize(cfg);
        engine.UpdateMeasurements(Meas(49.7));
        engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.Equal(ActionState.Action, engine.GetSnapshot().FrequencyAction);

        cfg.ActivePid.Kp = 2;
        engine.UpdateConfig(cfg);
        engine.UpdateMeasurements(Meas(49.7));
        engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.Equal(ActionState.Action, engine.GetSnapshot().FrequencyAction);
    }
}
