using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class PcsQvDroopRampTests
{
    private static PcsDevice CreateFormingPcs(
        double? upVs = null,
        double deadbandKvar = 1e6,
        bool droopEnabled = true)
    {
        var cfg = new PcsDeviceConfig
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
            QvDroopEnabled = droopEnabled,
            QvDroopDeadbandKvar = deadbandKvar,
            QvDroopCoefficientVPerKvar = 0.02,
            DvDtTripThresholdVPerSec = 10_000,
            InrushPeakMultiplier = 0.3
        };
        if (upVs is { } u)
            cfg.VoltageRampUpVs = u;
        return PcsDeviceFactory.Create("pcs_qv", cfg);
    }

    private static void ArmIsland(PcsDevice pcs, double vCmd)
    {
        pcs.ApplyBlackStartEnabled(true);
        pcs.ApplyIslandVoltageCommand(vCmd);
        pcs.UpdateGridState(0, 50, false);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);
    }

    [Fact]
    public void DefaultRamp_ZeroTo690_ReachesInFiveSecondsWhenQInDeadband()
    {
        var pcs = CreateFormingPcs();
        ArmIsland(pcs, 690);

        var t = DateTime.UtcNow;
        double inj = 0;
        double elapsed = 0;
        const double dtMs = 200;
        while (elapsed + 1e-9 < 5.0)
        {
            t = t.AddMilliseconds(dtMs);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(dtMs));
            pcs.TryGetIslandBusVoltageInjection(out inj, out _);
            elapsed += dtMs / 1000.0;
            if (elapsed + 1e-9 < 5.0)
                Assert.True(inj < 689.5, $"t={elapsed:F1}s 注入应未到 690，实际 {inj:F1}");
        }

        Assert.InRange(inj, 689, 691);
    }

    [Fact]
    public void ApplyBlackStartDisabled_InjectionIsZero()
    {
        var pcs = CreateFormingPcs();
        ArmIsland(pcs, 690);
        pcs.Update(1200, 0, DateTime.UtcNow, TimeSpan.FromMilliseconds(400));
        Assert.True(pcs.TryGetIslandBusVoltageInjection(out var vOn, out _));
        Assert.True(vOn > 1);

        pcs.ApplyBlackStartEnabled(false);
        Assert.False(pcs.TryGetIslandBusVoltageInjection(out _, out _));
    }

    [Fact]
    public void ExportingQOutsideDeadband_InjectionBelowRamp()
    {
        var pcs = CreateFormingPcs(deadbandKvar: 10);
        ArmIsland(pcs, 690);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 30; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var inj, out _))
                pcs.RefreshBlackStartBusContext(inj);
        }

        pcs.SetTransformerMagnetizingReactiveKvar(200);
        pcs.Update(1200, 0, t.AddMilliseconds(200), TimeSpan.FromMilliseconds(200));

        Assert.True(pcs.TryGetIslandBusVoltageInjection(out var vRef, out _));
        var st = pcs.GetCurrentState();
        Assert.True(st.ReactivePower > 10, $"无功应超过死区，实际 Q={st.ReactivePower:F1}");
        Assert.True(vRef < st.IslandVoltageCommandV - 0.5,
            $"发 Q 后注入应低于设定；vRef={vRef:F1} cmd={st.IslandVoltageCommandV:F1} Q={st.ReactivePower:F1}");
    }

    [Fact]
    public void DoublingQOutsideDeadband_LowersVrefFurther()
    {
        var pcs = CreateFormingPcs(deadbandKvar: 5);
        ArmIsland(pcs, 690);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 30; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var inj, out _))
                pcs.RefreshBlackStartBusContext(inj);
        }

        pcs.SetTransformerMagnetizingReactiveKvar(80);
        t = t.AddMilliseconds(200);
        pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
        Assert.True(pcs.TryGetIslandBusVoltageInjection(out var v1, out _));

        pcs.SetTransformerMagnetizingReactiveKvar(160);
        t = t.AddMilliseconds(200);
        pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
        Assert.True(pcs.TryGetIslandBusVoltageInjection(out var v2, out _));

        Assert.True(v2 < v1 - 0.2, $"Q 加倍后 Vref 应更低；v1={v1:F2} v2={v2:F2}");
    }

    [Fact]
    public void QInsideDeadband_InjectionEqualsRamp()
    {
        var pcs = CreateFormingPcs(deadbandKvar: 500);
        ArmIsland(pcs, 690);

        var t = DateTime.UtcNow;
        for (int i = 0; i < 30; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (pcs.TryGetIslandBusVoltageInjection(out var inj, out _))
                pcs.RefreshBlackStartBusContext(inj);
        }

        pcs.SetTransformerMagnetizingReactiveKvar(20);
        pcs.Update(1200, 0, t.AddMilliseconds(200), TimeSpan.FromMilliseconds(200));
        Assert.True(pcs.TryGetIslandBusVoltageInjection(out var vRef, out _));
        var st = pcs.GetCurrentState();
        Assert.InRange(vRef, st.IslandVoltageCommandV - 0.5, st.IslandVoltageCommandV + 0.5);
    }

    [Fact]
    public void SoftStartSteps_DoNotExceedUpRate()
    {
        const double upRate = 138;
        var pcs = CreateFormingPcs(upVs: upRate, deadbandKvar: 1e6);
        ArmIsland(pcs, 690);

        var t = DateTime.UtcNow;
        double prev = 0;
        bool started = false;
        for (int i = 0; i < 30; i++)
        {
            t = t.AddMilliseconds(200);
            pcs.Update(1200, 0, t, TimeSpan.FromMilliseconds(200));
            if (!pcs.TryGetIslandBusVoltageInjection(out var inj, out _))
                continue;
            if (!started)
            {
                started = true;
                prev = inj;
                continue;
            }

            double dV = inj - prev;
            Assert.True(
                dV <= upRate * 0.2 * 1.1 + 1e-6,
                $"200ms 增幅 {dV:F2} V 超过 UpRate×dt×1.1");
            prev = inj;
        }
    }
}
