using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class FormingDualLoopTests
{
    private static PcsDevice CreatePcs() =>
        PcsDeviceFactory.Create("pcs_dual", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1000,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 138,
            BlackStartCurrentLimitFraction = 0.45,
            QvDroopDeadbandKvar = 1e6,
            DvDtTripThresholdVPerSec = 10_000,
            InrushPeakMultiplier = 0.3
        });

    [Fact]
    public void NeighborBusAt690_OwnRampZero_IsNotSynchronized()
    {
        var pcs = CreatePcs();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.RefreshBlackStartBusContext(690, 50, 0);

        Assert.True(pcs.IsLiveBusFollower);
        Assert.NotEqual(BlackStartPhase.Synchronized, pcs.GetBlackStartPhase());
        Assert.False(pcs.TryGetIslandBusVoltageInjection(out _, out _));
    }

    [Fact]
    public void FirstTickAfterPrecharge_CurrentWithinImax()
    {
        var pcs = CreatePcs();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.SetTransformerMagnetizingReactiveKvar(400);
        pcs.SetBlackStartInrushDemand(80, 200);

        pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));

        var st = pcs.GetCurrentState();
        Assert.Equal(BlackStartPhase.SoftStarting, st.BlackStartPhase);
        Assert.True(Math.Abs(st.AcCurrent) <= 1000 + 1e-6, $"I={st.AcCurrent:F1} A");
        Assert.Equal(0, st.FaultType);
    }

    [Fact]
    public void ZeroIslandVoltageCommand_AfterStart_AcCurrentStaysNearZero()
    {
        var pcs = CreatePcs();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(0);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
        pcs.SetTransformerMagnetizingReactiveKvar(400);

        var t = DateTime.UtcNow;
        PcsState st = pcs.GetCurrentState();
        for (int i = 0; i < 20; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            st = pcs.GetCurrentState();
        }

        Assert.True(Math.Abs(st.AcCurrent) < 5, $"建压前电流应接近 0，I={st.AcCurrent:F1} A");
        Assert.True(st.AcVoltage < 2, $"建压前电压应接近 0，U={st.AcVoltage:F1} V");
        Assert.Equal(0, st.FaultType);
        Assert.False(pcs.TryGetIslandBusVoltageInjection(out _, out _));

        pcs.ApplyIslandVoltageCommand(690);
        for (int i = 0; i < 8; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
        }

        Assert.True(pcs.TryGetIslandBusVoltageInjection(out var inj, out _));
        Assert.True(inj > 50, $"写 690 后应开始建压；inj={inj:F1}");
    }

    [Fact]
    public void DefaultRamp_Reaches690InFiveSeconds()
    {
        var pcs = CreatePcs();
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        double inj = 0;
        for (int i = 0; i < 25; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            pcs.TryGetIslandBusVoltageInjection(out inj, out _);
            pcs.RefreshBlackStartBusContext(inj);
        }

        Assert.InRange(inj, 689, 691);
        Assert.Equal(BlackStartPhase.Synchronized, pcs.GetBlackStartPhase());
    }
}

public class FormingPhaseTests
{
    [Fact]
    public void FormingPcs_IntegratesPhaseAtInjectedFrequency()
    {
        var pcs = PcsDeviceFactory.Create("pcs_phase", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1000,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 138,
            QvDroopDeadbandKvar = 1e6,
            DvDtTripThresholdVPerSec = 10_000,
            InrushPeakMultiplier = 0.3
        });
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        double unwrapped = 0;
        double prev = 0;
        var t = DateTime.UtcNow;
        const int steps = 125;
        const double dtMs = 8;
        for (int i = 0; i < steps; i++)
        {
            t = t.AddMilliseconds(dtMs);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(dtMs));
            double th = pcs.FormingPhaseRad;
            unwrapped += EssSimulator.EssDeviceSimModel.Control.PhaseIntegrator.AngleErrorRad(th, prev);
            prev = th;
        }

        double expected = 2 * Math.PI * 50 * steps * dtMs / 1000.0;
        Assert.InRange(unwrapped, expected * 0.95, expected * 1.05);
    }
}

public class PfDroopParallelTests
{
    private static PcsDevice Create(string id) =>
        PcsDeviceFactory.Create(id, new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1000,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 138,
            QvDroopDeadbandKvar = 1e6,
            PfDroopDeadbandKw = 1,
            DvDtTripThresholdVPerSec = 10_000,
            InrushPeakMultiplier = 0.3
        });

    [Fact]
    public void SingleMachineIdle_FrequencyNearNominal()
    {
        var pcs = Create("pcs1");
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(690);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 30; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
        }

        Assert.True(pcs.TryGetIslandBusVoltageInjection(out _, out var f));
        Assert.InRange(f, 49.9, 50.1);
    }

    [Fact]
    public void TwoFormingMachines_SameStationLoad_PowerShareWithin15Percent()
    {
        var a = Create("pcs_a");
        var b = Create("pcs_b");
        foreach (var pcs in new[] { a, b })
        {
            pcs.ApplyBlackStartEnabled(true);
            pcs.ApplyIslandVoltageCommand(690);
            pcs.UpdateGridState(0, 50, false);
            pcs.TransitionToMode(OperationMode.Normal);
            pcs.TransitionToGMode(GridMode.Islanded);
            pcs.SetBlackStartSharedLossActivePowerKw(80);
            pcs.SetTransformerMagnetizingReactiveKvar(40);
        }

        var t = DateTime.UtcNow;
        for (int i = 0; i < 30; i++)
        {
            t = t.AddMilliseconds(200);
            a.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            b.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            a.TryGetIslandBusVoltageInjection(out var va, out _);
            b.TryGetIslandBusVoltageInjection(out var vb, out _);
            a.RefreshBlackStartBusContext(Math.Max(va, vb));
            b.RefreshBlackStartBusContext(Math.Max(va, vb));
        }

        double pA = a.GetCurrentState().ActivePower;
        double pB = b.GetCurrentState().ActivePower;
        double rel = Math.Abs(pA - pB) / 1725.0;
        Assert.True(rel < 0.15, $"P share {pA:F1} vs {pB:F1} rel={rel:P1}");
    }
}

public class LiveBusFollowerTests
{
    private static PcsDevice Create(string id) =>
        PcsDeviceFactory.Create(id, new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1100,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 300,
            BlackStartVoltageRampVs = 138,
            QvDroopDeadbandKvar = 1e6,
            DvDtTripThresholdVPerSec = 10_000,
            InrushPeakMultiplier = 0.3
        });

    [Fact]
    public void LiveBus_BlackStartDoesNotInjectVoltageSource()
    {
        var host = Create("pcs1");
        host.ApplyBlackStartEnabled(true);
        host.ApplyIslandVoltageCommand(690);
        host.UpdateGridState(0, 50, false);
        host.TransitionToMode(OperationMode.Normal);
        host.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        double hostV = 0;
        for (int i = 0; i < 30; i++)
        {
            t = t.AddMilliseconds(200);
            host.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            host.TryGetIslandBusVoltageInjection(out hostV, out _);
            host.RefreshBlackStartBusContext(hostV, 50, host.FormingPhaseRad);
        }

        Assert.True(hostV > 600);

        var follower = Create("pcs2");
        follower.ApplyBlackStartEnabled(true);
        follower.ApplyIslandVoltageCommand(690);
        follower.UpdateGridState(0, 50, false);
        follower.RefreshBlackStartBusContext(hostV, 50, host.FormingPhaseRad);
        follower.TransitionToMode(OperationMode.Normal);
        follower.TransitionToGMode(GridMode.Islanded);

        t = t.AddMilliseconds(300);
        follower.Update(1200, 0, t, TimeSpan.FromMilliseconds(300));
        follower.RefreshBlackStartBusContext(hostV, 50, host.FormingPhaseRad);

        Assert.True(follower.IsLiveBusFollower);
        Assert.False(follower.TryGetIslandBusVoltageInjection(out _, out _));
        Assert.NotEqual(BlackStartPhase.Synchronized, follower.GetBlackStartPhase());
        Assert.Equal(0, follower.GetCurrentState().FaultType);
    }

    [Fact]
    public void PreSyncWindow_CutInAsFormingParallelInjectsAtBusVoltage()
    {
        var follower = Create("pcs2");
        follower.ApplyBlackStartEnabled(true);
        follower.ApplyIslandVoltageCommand(690);
        follower.UpdateGridState(0, 50, false);
        follower.RefreshBlackStartBusContext(690, 50, 0.1);
        follower.TransitionToMode(OperationMode.Standby);
        follower.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            t = t.AddMilliseconds(50);
            follower.Update(1200, 0, t, TimeSpan.FromMilliseconds(50));
            follower.RefreshBlackStartBusContext(690, 50, 0.1);
        }

        Assert.True(follower.IsPreSyncReadyToCutIn);
        Assert.True(follower.TryCutInAsFormingParallel());
        follower.TransitionToMode(OperationMode.Normal);
        follower.Update(1200, 0, t.AddMilliseconds(50), TimeSpan.FromMilliseconds(50));

        Assert.False(follower.IsLiveBusFollower);
        Assert.Equal(BlackStartPhase.Synchronized, follower.GetBlackStartPhase());
        Assert.True(follower.TryGetIslandBusVoltageInjection(out var inj, out var f));
        Assert.InRange(inj, 680, 700);
        Assert.InRange(f, 49.5, 50.5);
        Assert.Equal(0, follower.GetCurrentState().FaultType);
    }

    [Fact]
    public void PreSyncReady_AtTwentyPercentBus_AllowsCutInWhenBlackStartOn()
    {
        const double busV = 138; // 0.2 × 690：旧 0.7 pu 门槛会卡住后机
        var follower = Create("pcs2");
        follower.ApplyBlackStartEnabled(true);
        follower.ApplyIslandVoltageCommand(busV);
        follower.UpdateGridState(0, 50, false);
        follower.SyncExternalRunCommand(true);
        follower.RefreshBlackStartBusContext(busV, 50, 0.1);
        follower.TransitionToMode(OperationMode.Standby);
        follower.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            t = t.AddMilliseconds(50);
            follower.RefreshBlackStartBusContext(busV, 50, 0.1);
            follower.Update(1200, 0, t, TimeSpan.FromMilliseconds(50));
        }

        Assert.False(follower.IsLiveBusFollower);
        Assert.Equal(BlackStartPhase.Synchronized, follower.GetBlackStartPhase());
        Assert.Equal(OperationMode.Normal, follower.GetCurrentState().Mode);
        Assert.True(follower.TryGetIslandBusVoltageInjection(out var inj, out _));
        Assert.InRange(inj, 120, 160);
    }

    [Fact]
    public void PreSyncReady_WithRunCommand_CutsInOnUpdateWithoutMapper()
    {
        var follower = Create("pcs2");
        follower.ApplyBlackStartEnabled(true);
        follower.ApplyIslandVoltageCommand(690);
        follower.UpdateGridState(0, 50, false);
        follower.SyncExternalRunCommand(true);
        follower.RefreshBlackStartBusContext(690, 50, 0.1);
        follower.TransitionToMode(OperationMode.Standby);
        follower.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 8; i++)
        {
            t = t.AddMilliseconds(50);
            follower.RefreshBlackStartBusContext(690, 50, 0.1);
            follower.Update(1200, 0, t, TimeSpan.FromMilliseconds(50));
        }

        Assert.False(follower.IsLiveBusFollower);
        Assert.Equal(BlackStartPhase.Synchronized, follower.GetBlackStartPhase());
        Assert.Equal(OperationMode.Normal, follower.GetCurrentState().Mode);
        Assert.True(follower.TryGetIslandBusVoltageInjection(out var inj, out _));
        Assert.InRange(inj, 680, 700);
    }

    [Fact]
    public void PreSyncReady_WhenBusFrequencyNotYetPublished()
    {
        var follower = Create("pcs2");
        follower.ApplyBlackStartEnabled(true);
        follower.ApplyIslandVoltageCommand(690);
        follower.UpdateGridState(0, 50, false);
        follower.RefreshBlackStartBusContext(690, 0, 0);
        follower.TransitionToMode(OperationMode.Standby);
        follower.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 8; i++)
        {
            t = t.AddMilliseconds(50);
            follower.Update(1200, 0, t, TimeSpan.FromMilliseconds(50));
            follower.RefreshBlackStartBusContext(690, 0, 0);
        }

        Assert.True(follower.IsPreSyncReadyToCutIn);
    }

    [Fact]
    public void FormingParallelCutIn_ZeroCommand_KeepsBusVoltage()
    {
        var follower = Create("pcs2");
        follower.ApplyBlackStartEnabled(true);
        follower.ApplyIslandVoltageCommand(0);
        follower.UpdateGridState(0, 50, false);
        follower.RefreshBlackStartBusContext(686.5, 50, 0.2);
        follower.TransitionToMode(OperationMode.Standby);
        follower.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 8; i++)
        {
            t = t.AddMilliseconds(50);
            follower.Update(1200, 0, t, TimeSpan.FromMilliseconds(50));
            follower.RefreshBlackStartBusContext(686.5, 50, 0.2);
        }

        Assert.True(follower.TryCutInAsFormingParallel());
        follower.TransitionToMode(OperationMode.Normal);
        follower.ApplyIslandVoltageCommand(0);
        follower.Update(1200, 0, t.AddMilliseconds(50), TimeSpan.FromMilliseconds(50));

        Assert.True(follower.TryGetIslandBusVoltageInjection(out var inj, out _));
        Assert.True(inj > 600, $"yt3=0 并机后不得把母线拉到 0；inj={inj:F1}");
    }
}

public class PcsFollowerCutInTests
{
    [Fact]
    public void Pcs2StartsAfterPcs1Energized_NoOvercurrent()
    {
        var pcs1 = PcsDeviceFactory.Create("pcs1", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1100,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 138,
            QvDroopDeadbandKvar = 1e6,
            DvDtTripThresholdVPerSec = 10_000,
            InrushPeakMultiplier = 0.3
        });
        var pcs2 = PcsDeviceFactory.Create("pcs2", new PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1100,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5,
            BlackStartPrechargeDelayMs = 300,
            BlackStartVoltageRampVs = 138,
            BlackStartCurrentLimitFraction = 0.45,
            QvDroopDeadbandKvar = 1e6,
            DvDtTripThresholdVPerSec = 10_000,
            InrushPeakMultiplier = 0.3
        });

        pcs1.ApplyBlackStartEnabled(true);
        pcs1.ApplyIslandVoltageCommand(690);
        pcs1.UpdateGridState(0, 50, false);
        pcs1.TransitionToMode(OperationMode.Normal);
        pcs1.TransitionToGMode(GridMode.Islanded);

        var t = DateTime.UtcNow;
        double bus = 0;
        for (int i = 0; i < 30; i++)
        {
            t = t.AddMilliseconds(200);
            pcs1.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            pcs1.TryGetIslandBusVoltageInjection(out bus, out _);
            pcs1.RefreshBlackStartBusContext(bus);
        }

        Assert.True(bus >= 0.85 * 690);

        pcs2.ApplyBlackStartEnabled(true);
        pcs2.ApplyIslandVoltageCommand(690);
        pcs2.UpdateGridState(0, 50, false);
        pcs2.RefreshBlackStartBusContext(bus, 50, pcs1.FormingPhaseRad);
        pcs2.TransitionToMode(OperationMode.Normal);
        pcs2.TransitionToGMode(GridMode.Islanded);

        t = t.AddMilliseconds(300);
        pcs2.Update(1200, 0, t, TimeSpan.FromMilliseconds(300));
        var st = pcs2.GetCurrentState();

        Assert.True(pcs2.IsLiveBusFollower);
        Assert.NotEqual(BlackStartPhase.Synchronized, pcs2.GetBlackStartPhase());
        Assert.Equal(0, st.FaultType);
        Assert.DoesNotContain("Over current", st.FaultMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.True(Math.Abs(st.AcCurrent) <= 1100, $"pcs2 I={st.AcCurrent:F1} A msg={st.FaultMessage}");
        Assert.Equal(0, pcs1.GetCurrentState().FaultType);

        Assert.True(pcs2.IsPreSyncReadyToCutIn);
        Assert.True(pcs2.TryCutInAsFormingParallel());
        pcs2.TransitionToMode(OperationMode.Normal);
        pcs1.SetTransformerMagnetizingReactiveKvar(200);
        pcs2.SetTransformerMagnetizingReactiveKvar(200);

        for (int i = 0; i < 10; i++)
        {
            t = t.AddMilliseconds(50);
            pcs1.Update(1200, 0, t, TimeSpan.FromMilliseconds(50));
            pcs2.Update(1200, 0, t, TimeSpan.FromMilliseconds(50));
            pcs1.TryGetIslandBusVoltageInjection(out var v1, out _);
            pcs2.TryGetIslandBusVoltageInjection(out var v2, out _);
            double busNow = Math.Max(v1, v2);
            pcs1.RefreshBlackStartBusContext(busNow, 50, pcs1.FormingPhaseRad);
            pcs2.RefreshBlackStartBusContext(busNow, 50, pcs1.FormingPhaseRad);
        }

        Assert.True(pcs2.TryGetIslandBusVoltageInjection(out var inj2, out _));
        Assert.InRange(inj2, 600, 760);
        var st2 = pcs2.GetCurrentState();
        Assert.Equal(0, st2.FaultType);
        Assert.True(Math.Abs(st2.AcCurrent) <= 1100, $"pcs2 after join I={st2.AcCurrent:F1}");
        Assert.True(st2.ReactivePower > 50, $"pcs2 should share Q; Q={st2.ReactivePower:F1}");
        Assert.True(pcs1.GetCurrentState().ReactivePower > 50);
    }
}
