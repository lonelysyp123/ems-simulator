using System.Text.Json;
using System.Text.Json.Serialization;
using EssSimulator.Core;
using EssSimulator.EmsStrategy.Adapter;
using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;

namespace EssSimulator.Tests.EmsStrategy;

public class EmsStrategyRuntimePersistTests : SimulatorHostTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void DefaultConfigPath_MatchesLoadPath()
    {
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "configs", "ems-strategy.json"),
            EmsStrategyRuntime.DefaultConfigPath);
    }

    [Fact]
    public void TryReplace_WritesSlopeAndPid()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ems-persist-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "ems-strategy.json");
        try
        {
            var runtime = new EmsStrategyRuntime(new EmsStrategyEngine(), EmsStrategyConfig.CreateDefault(), path);
            Assert.False(File.Exists(path));

            var next = runtime.Config.Clone();
            next.Slope.Enabled = true;
            next.Slope.RiseKwPerSec = 33;
            next.ActivePid.Kp = 1.25;
            Assert.True(runtime.TryReplace(next, out var error), error);

            Assert.True(File.Exists(path));
            var loaded = JsonSerializer.Deserialize<EmsStrategyConfig>(File.ReadAllText(path), JsonOptions);
            Assert.NotNull(loaded);
            Assert.True(loaded!.Slope.Enabled);
            Assert.Equal(33, loaded.Slope.RiseKwPerSec);
            Assert.Equal(1.25, loaded.ActivePid.Kp);
            Assert.Equal(3, loaded.PrimaryFrequency.DroopPercent);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TrySetEnabled_DoesNotWrite_WhenThirdPartyBlocked()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ems-persist-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "ems-strategy.json");
        Directory.CreateDirectory(dir);
        const string original = "{\"Enabled\":false}";
        File.WriteAllText(path, original);
        try
        {
            ExternalControlGate.SetBlocked(true);
            var runtime = new EmsStrategyRuntime(new EmsStrategyEngine(), EmsStrategyConfig.CreateDefault(), path);
            Assert.False(runtime.TrySetEnabled(true, out var error));
            Assert.Equal(ExternalControlGate.ThirdPartyBlockedMessage, error);
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class EmsStrategyConfigPatcherTests
{
    [Fact]
    public void Apply_PidOnly_LeavesPrimaryFrequency()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.PrimaryFrequency.DroopPercent = 7;
        cfg.ActivePid.Kp = 0.4;
        EmsStrategyConfigPatcher.Apply(cfg, new EmsStrategyPatchRequest
        {
            ActivePid = new PidConfig { Kp = 1.1, Ki = 0.02, Period = TimeSpan.FromSeconds(2) }
        });
        Assert.Equal(1.1, cfg.ActivePid.Kp);
        Assert.Equal(0.02, cfg.ActivePid.Ki);
        Assert.Equal(7, cfg.PrimaryFrequency.DroopPercent);
    }

    [Fact]
    public void Apply_PrimaryFrequency_ReplacesSubtree()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        EmsStrategyConfigPatcher.Apply(cfg, new EmsStrategyPatchRequest
        {
            PrimaryFrequency = new PrimaryFrequencyConfig
            {
                Enabled = true,
                Deadband1Percent = 0.05,
                DroopPercent = 4.5,
                ControlCycle = TimeSpan.FromMilliseconds(500)
            }
        });
        Assert.Equal(0.05, cfg.PrimaryFrequency.Deadband1Percent);
        Assert.Equal(4.5, cfg.PrimaryFrequency.DroopPercent);
        Assert.Equal(TimeSpan.FromMilliseconds(500), cfg.PrimaryFrequency.ControlCycle);
        Assert.Equal(0.4, cfg.ActivePid.Kp);
    }

    [Fact]
    public void Apply_LegacyScalars_StillWork()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        EmsStrategyConfigPatcher.Apply(cfg, new EmsStrategyPatchRequest
        {
            LocalActiveSetKw = 120,
            PrimaryFrequencyEnabled = false,
            SlopeEnabled = true,
            SocBalance = true
        });
        Assert.Equal(120, cfg.LocalActiveSetKw);
        Assert.False(cfg.PrimaryFrequency.Enabled);
        Assert.True(cfg.Slope.Enabled);
        Assert.True(cfg.Distribution.SocBalance);
        Assert.Equal(3, cfg.PrimaryFrequency.DroopPercent);
    }

    [Fact]
    public void Apply_ThenStep_UsesNewDeadband()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        cfg.Enabled = true;
        cfg.Slope.Enabled = false;
        cfg.ActivePid.DeadbandKw = 0;
        cfg.ActivePid.Discretization = PidDiscretization.Dt;
        cfg.PrimaryFrequency.Deadband1Percent = 0.2;
        var engine = new EmsStrategyEngine();
        engine.Initialize(cfg);
        engine.UpdateMeasurements(Meas(49.95));
        engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.Equal(ActionState.Reset, engine.GetSnapshot().FrequencyAction);

        var pfr = cfg.PrimaryFrequency.Clone();
        pfr.Deadband1Percent = 0.01;
        EmsStrategyConfigPatcher.Apply(cfg, new EmsStrategyPatchRequest { PrimaryFrequency = pfr });
        engine.UpdateConfig(cfg);
        engine.UpdateMeasurements(Meas(49.95));
        engine.Step(TimeSpan.FromMilliseconds(200));
        Assert.Equal(ActionState.Action, engine.GetSnapshot().FrequencyAction);
    }

    [Fact]
    public void Apply_CloseLoopCurve_CoercesToFixed()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        EmsStrategyConfigPatcher.Apply(cfg, new EmsStrategyPatchRequest
        {
            ActiveMode = ActiveMode.CloseLoopCurve,
            ReactiveMode = ReactiveMode.CloseLoopCurve
        });
        Assert.Equal(ActiveMode.CloseLoopFixed, cfg.ActiveMode);
        Assert.Equal(ReactiveMode.CloseLoopFixed, cfg.ReactiveMode);
    }

    [Fact]
    public void Apply_PathEnableScalars_AfterNested()
    {
        var cfg = EmsStrategyConfig.CreateDefault();
        EmsStrategyConfigPatcher.Apply(cfg, new EmsStrategyPatchRequest
        {
            ActivePid = new PidConfig { Enabled = true, Kp = 0.9 },
            ActivePidEnabled = false,
            ApparentLimitEnabled = false,
            DampEnabled = false,
            DistributionEnabled = false,
            ReactiveSlopeEnabled = true
        });
        Assert.False(cfg.ActivePid.Enabled);
        Assert.Equal(0.9, cfg.ActivePid.Kp);
        Assert.False(cfg.ApparentLimitEnabled);
        Assert.False(cfg.DampEnabled);
        Assert.False(cfg.Distribution.Enabled);
        Assert.True(cfg.ReactiveSlope.Enabled);
    }

    private static PlantMeasurements Meas(double frequencyHz) => new()
    {
        FrequencyHz = frequencyHz,
        PccActivePowerKw = 0,
        Branches = new[]
        {
            new PcsBranchState
            {
                Index = 0, UnitIndex0 = 0, PcsIndexInUnit = 0,
                CommOk = true, Running = true, Soc = 0.5,
                RatedKw = 2500, MaxChargeKw = 2500, MaxDischargeKw = 2500
            }
        }
    };
}
