using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class PcsBlackStartTests
{
    private static PcsDevice CreateDevice() =>
        PcsDeviceFactory.Create("pcs_test", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1000,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 200,
            BlackStartFrequencyStartHz = 47,
            BlackStartCurrentLimitFraction = 0.3,
            InrushPeakMultiplier = 0.3,
            DvDtTripThresholdVPerSec = 10_000
        });

    [Fact]
    public void ApplyIslandFrequencyCommand_StoresClampedHz()
    {
        var pcs = CreateDevice();
        pcs.ApplyIslandFrequencyCommand(50.5);
        Assert.Equal(50.5, pcs.IslandFrequencyCommandHz, 3);

        pcs.ApplyIslandFrequencyCommand(80);
        Assert.InRange(pcs.IslandFrequencyCommandHz, 45, 55);

        pcs.ApplyIslandFrequencyCommand(0);
        Assert.Equal(0, pcs.IslandFrequencyCommandHz);
    }

    [Fact]
    public void ApplyBlackStartEnabled_StartsInPreparingPhase()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        Assert.Equal(BlackStartPhase.Preparing, pcs.GetBlackStartPhase());
        Assert.False(pcs.IsBlackStartActive);
    }

    [Fact]
    public void AdvancePhase_TransitionsToSoftStartingAfterPrecharge()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));

        Assert.Equal(BlackStartPhase.SoftStarting, pcs.GetBlackStartPhase());
        Assert.True(pcs.IsBlackStartActive);
    }

    [Fact]
    public void ClosedLoopBuild_OutputsPowerForStationDemand()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(400));
        pcs.SetTransformerMagnetizingReactiveKvar(80);
        pcs.SetBlackStartSharedLossActivePowerKw(20);
        pcs.RefreshBlackStartBusContext(25);

        pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));

        var st = pcs.GetCurrentState();
        Assert.True(st.ActivePower > 0, "建压期应对电压差输出有功");
        Assert.True(st.ReactivePower > 0, "建压期应输出无功支撑");
    }

    [Fact]
    public void CurrentLimit_CapsPowerDuringRegulating()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.SetTransformerMagnetizingReactiveKvar(800);
        pcs.SetBlackStartInrushDemand(50, 400);

        for (int i = 0; i < 3; i++)
            pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));

        var st = pcs.GetCurrentState();
        double sKva = Math.Sqrt(st.ActivePower * st.ActivePower + st.ReactivePower * st.ReactivePower);
        double u = Math.Max(st.AcVoltage, st.IslandVoltageEffectiveV);
        double iEst = sKva * 1000 / (Math.Max(u, 10) * Math.Sqrt(3));
        Assert.True(iEst <= 1000 * 0.31);
        Assert.True(Math.Abs(st.AcCurrent) <= 1000 * 0.31 + st.InrushCurrentA + 1);
    }

    [Fact]
    public void TryGetIslandBusVoltageInjection_ReturnsRampVoltageDuringSoftStart()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));
        pcs.Update(1200, 0, DateTime.UtcNow.AddMilliseconds(200), TimeSpan.FromMilliseconds(200));

        Assert.Equal(BlackStartPhase.SoftStarting, pcs.GetBlackStartPhase());
        Assert.True(pcs.TryGetIslandBusVoltageInjection(out var v, out var f));
        Assert.True(v > 1.0);
        Assert.Equal(50, f, 3);
    }

    [Fact]
    public void BlackStart_FrequencyIsNominal_FromSoftStartOnward()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(100);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 8; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out _, out var f))
                Assert.Equal(50, f, 3);
            Assert.Equal(50, pcs.GetCurrentState().Frequency, 3);
        }
    }

    [Fact]
    public void IslandVoltageCommand_100V_InjectionRampsToCommand()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(100);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        double inj = 0;
        var t = DateTime.UtcNow;
        for (int i = 0; i < 12; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var stepInj, out _))
                inj = stepInj;
            pcs.RefreshBlackStartBusContext(inj);
        }

        var st = pcs.GetCurrentState();
        Assert.InRange(inj, 95, 105);
        Assert.InRange(st.IslandVoltageEffectiveV, 95, 105);
        Assert.Equal(50, st.Frequency, 3);
        Assert.True(
            inj > 90,
            $"100V 设定应按目标建压，不能被 f/50 或 8% 母线锁死；inj={inj:F1} eff={st.IslandVoltageEffectiveV:F1}");
    }

    [Fact]
    public void DefaultInrushScale_StillReaches100VCommand()
    {
        var (inj, st) = RampIsland(CreateDefaultInrushPcs(), vCmd: 100, steps: 12);
        Assert.Equal(0, st.FaultType);
        Assert.InRange(inj, 95, 105);
        Assert.Equal(50, st.Frequency, 3);
    }

    [Fact]
    public void DefaultInrush_SlowRampReaches690VWithoutTrip()
    {
        var (inj, st) = RampIsland(CreateDefaultInrushPcs(), vCmd: 690, steps: 40);
        Assert.Equal(0, st.FaultType);
        Assert.True(inj > 650, $"软起 690V 不应被设计内涌流打断；inj={inj:F1} type={st.FaultType} msg={st.FaultMessage}");
        Assert.Equal(OperationMode.Normal, st.Mode);
        Assert.True(st.InrushCurrentA < 1000, $"慢爬涌流应远小于额定；Iinrush={st.InrushCurrentA:F1}");
    }

    [Fact]
    public void ApplyBlackStartEnabled_TrueAgain_DoesNotResetPhase()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));

        Assert.Equal(BlackStartPhase.SoftStarting, pcs.GetBlackStartPhase());

        pcs.ApplyBlackStartEnabled(true);

        Assert.Equal(BlackStartPhase.SoftStarting, pcs.GetBlackStartPhase());

        pcs.Update(1200, 0, DateTime.UtcNow.AddMilliseconds(200), TimeSpan.FromMilliseconds(200));
        Assert.True(pcs.TryGetIslandBusVoltageInjection(out var v, out _));
        Assert.True(v > 1.0);
    }

    [Fact]
    public void IslandVoltageInjection_DoesNotLockToEightPercentBus()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        double bus = 0;
        var t = DateTime.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var stepInj, out _))
                bus = stepInj;
            pcs.RefreshBlackStartBusContext(bus);
        }

        Assert.True(bus > 100, $"斜坡阶段注入过低: bus={bus:F1} phase={pcs.GetBlackStartPhase()}");

        // 690V × 8% = 55.2V：旧逻辑一旦母线刚过此阈值就把注入锁成母线电压
        pcs.RefreshBlackStartBusContext(56);
        var st = pcs.GetCurrentState();
        Assert.True(pcs.TryGetIslandBusVoltageInjection(out var inj, out _));
        Assert.True(
            inj > 100,
            $"注入应跟随软起斜坡，不能锁在 8% 母线；inj={inj:F1} phase={pcs.GetBlackStartPhase()} cmd={st.IslandVoltageCommandV:F1} eff={st.IslandVoltageEffectiveV:F1}");
    }

    [Fact]
    public void IslandVoltageAfterBlackStartEnabled_RampsTowardCommand()
    {
        var pcs = CreateDevice();
        pcs.ApplyBlackStartEnabled(true);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));

        pcs.ApplyIslandVoltageCommand(690);

        double bus = 0;
        var t = DateTime.UtcNow;
        for (int i = 0; i < 40; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var inj, out _))
                bus = inj;
            pcs.RefreshBlackStartBusContext(bus);
        }

        Assert.True(bus > 200, $"先开黑启动再写孤岛电压应爬向 690V，不能停在 ~55.9V；实际 {bus:F1} V");
    }

    [Fact]
    public void AfterSynchronized_NewIslandVoltageCommand_RampsTowardNewTarget()
    {
        var pcs = CreateDefaultInrushPcs();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(50);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        double bus = 0;
        var t = DateTime.UtcNow;
        for (int i = 0; i < 8; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var inj, out _))
                bus = inj;
            pcs.RefreshBlackStartBusContext(bus);
        }

        Assert.Equal(BlackStartPhase.Synchronized, pcs.GetBlackStartPhase());
        Assert.InRange(bus, 45, 55);

        pcs.ApplyIslandVoltageCommand(690);
        for (int i = 0; i < 20; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var inj, out _))
                bus = inj;
            pcs.RefreshBlackStartBusContext(bus);
        }

        Assert.True(bus > 200, $"同步后改 yt3 应继续爬升，不能锁在 50V；bus={bus:F1} phase={pcs.GetBlackStartPhase()}");
    }

    [Fact]
    public void AfterSynchronized_SameIslandVoltageCommand_StaysSynchronized()
    {
        var pcs = CreateDefaultInrushPcs();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        double bus = 0;
        var t = DateTime.UtcNow;
        for (int i = 0; i < 40; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var inj, out _))
                bus = inj;
            pcs.RefreshBlackStartBusContext(bus);
        }

        Assert.Equal(BlackStartPhase.Synchronized, pcs.GetBlackStartPhase());
        Assert.True(bus > 650);

        pcs.ApplyIslandVoltageCommand(690);
        Assert.Equal(BlackStartPhase.Synchronized, pcs.GetBlackStartPhase());
    }

    [Fact]
    public void FastVoltageRise_ExceedingDesignInrush_TripsInrushFault()
    {
        var pcs = PcsDeviceFactory.Create("pcs_inrush_step", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1000,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 8000,
            InrushPeakMultiplier = 6.0,
            DvDtTripThresholdVPerSec = 1_000_000,
            DvDtRideThroughMs = 10_000
        });

        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        PcsState st = pcs.GetCurrentState();
        for (int i = 0; i < 8; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            st = pcs.GetCurrentState();
            if (st.FaultType != 0)
                break;
        }

        Assert.Equal(5, st.FaultType);
        Assert.Contains("Inrush", st.FaultMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OperationMode.Off, st.Mode);
        Assert.True((st.ProtectionFlags & 0x08) != 0, "超出设计峰值的涌流应置过流告警位");
    }

    private static PcsDevice CreateDefaultInrushPcs() =>
        PcsDeviceFactory.Create("pcs_inrush_scale", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1000,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 200,
            BlackStartCurrentLimitFraction = 0.3,
            InrushPeakMultiplier = 6.0,
            DvDtTripThresholdVPerSec = 10_000
        });

    private static (double InjectionV, PcsState State) RampIsland(PcsDevice pcs, double vCmd, int steps)
    {
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(vCmd);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        double inj = 0;
        var t = DateTime.UtcNow;
        PcsState st = pcs.GetCurrentState();
        for (int i = 0; i < steps; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var stepInj, out _))
                inj = stepInj;
            pcs.RefreshBlackStartBusContext(inj);
            st = pcs.GetCurrentState();
        }

        return (inj, st);
    }
}
