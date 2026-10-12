using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class IslandJoinReactiveShareTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CloseUnit2Hv_HostTakesMagnetizing_JoinerDoesNotInject()
    {
        using var ess = CreatePlant();
        var t = BuildUnit1(ess);

        double qHost0 = UnitQ(ess, 0, 2);
        ess.SetUnitBreakerClosed(1, true);
        for (int i = 0; i < 50; i++)
            t = StepPlant(ess, t);

        Assert.True(ess.GetUnitAcBusVoltage(1) > 400, $"合闸后单元2母线应带电，实际 {ess.GetUnitAcBusVoltage(1):F1} V");
        Assert.True(UnitQ(ess, 0, 2) > qHost0 + 40, $"合闸后单元1应承接励磁无功，合闸前 {qHost0:F1} 后 {UnitQ(ess, 0, 2):F1}");
        Assert.InRange(UnitQ(ess, 2, 4), -1, 5);
        Assert.False(ess._pcsList[2].TryGetIslandBusVoltageInjection(out _, out _));
        Assert.False(ess._pcsList[2].TakesIslandStationLoad);
    }

    [Fact]
    public void StartUnit2OnLiveBus_FollowsThenRampsReactiveAfterCutIn()
    {
        using var ess = CreatePlant();
        var t = BuildUnit1(ess);
        ess.SetUnitBreakerClosed(1, true);
        for (int i = 0; i < 15; i++)
            t = StepPlant(ess, t);

        double qHostBefore = UnitQ(ess, 0, 2);
        ArmForming(ess, 2, 4, run: false);
        Assert.True(ess._pcsList[2].GetCurrentState().BlackStartEnabled, "单元2黑启动应开启");
        Assert.Equal(BlackStartPhase.Preparing, ess._pcsList[2].GetBlackStartPhase());
        Assert.True(
            ess.GetUnitAcBusVoltage(1) > 400,
            $"开机前单元2母线应已带电，实际 {ess.GetUnitAcBusVoltage(1):F1} V");

        ess._pcsList[2].RefreshBlackStartBusContext(
            ess.GetUnitAcBusVoltage(1), 50, 0);
        Assert.True(
            ess._pcsList[2].IsLiveBusFollower,
            $"刷新后应进入跟网锁相 phase={ess._pcsList[2].GetBlackStartPhase()} " +
            $"bus={ess.GetUnitAcBusVoltage(1):F1}");

        for (int i = 0; i < 8; i++)
            t = StepPlant(ess, t);

        Assert.True(ess._pcsList[2].IsLiveBusFollower, "活母线上后机应保持锁相，不构网");
        Assert.False(ess._pcsList[2].TryGetIslandBusVoltageInjection(out _, out _));
        Assert.InRange(UnitQ(ess, 2, 4), -1, 15);
        Assert.True(UnitQ(ess, 0, 2) > qHostBefore * 0.8, "锁相期间励磁仍主要由单元1承担");

        ArmForming(ess, 2, 4, run: true);

        for (int i = 0; i < 40; i++)
        {
            t = StepPlant(ess, t);
            if (!ess._pcsList[2].IsLiveBusFollower
                && ess._pcsList[2].GetBlackStartPhase() == BlackStartPhase.Synchronized)
                break;
        }

        Assert.False(ess._pcsList[2].IsLiveBusFollower);
        Assert.Equal(BlackStartPhase.Synchronized, ess._pcsList[2].GetBlackStartPhase());
        double qJoinEarly = UnitQ(ess, 2, 4);
        double qHostEarly = UnitQ(ess, 0, 2);
        Assert.True(
            qJoinEarly < qHostEarly * 0.45,
            $"切入后无功应斜坡接手，实际 u2={qJoinEarly:F1} u1={qHostEarly:F1}");

        for (int i = 0; i < 60; i++)
            t = StepPlant(ess, t);

        double qHost = UnitQ(ess, 0, 2);
        double qJoin = UnitQ(ess, 2, 4);
        Assert.True(qJoin > 40, $"斜坡结束后单元2应承接无功，实际 {qJoin:F1}");
        Assert.True(
            Math.Abs(qHost - qJoin) < 0.35 * Math.Max(qHost, 1),
            $"斜坡结束后应接近均分，u1={qHost:F1} u2={qJoin:F1}");
    }

    private static DateTime BuildUnit1(EnergyStorageSystem ess)
    {
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);
        ess.SetUnitBreakerClosed(1, false);
        for (int i = 0; i < ess._pcsList.Count; i++)
            ess.SetBmsPcsLinked(i, true);
        ArmForming(ess, 0, 2, run: true);

        var t = T0;
        for (int i = 0; i < 50; i++)
            t = StepPlant(ess, t);

        var st = ess._pcsList[0].GetCurrentState();
        Assert.True(st.AcVoltage > 650, $"单元1应已建压 U={st.AcVoltage:F1}");
        return t;
    }

    private static DateTime StepPlant(EnergyStorageSystem ess, DateTime t)
    {
        t = t.Add(Step);
        ess.PlantEngine.Step(t, Step, Step);
        return t;
    }

    private static double UnitQ(EnergyStorageSystem ess, int start, int end)
    {
        double q = 0;
        for (int i = start; i < end; i++)
            q += ess._pcsList[i].GetCurrentState().ReactivePower;
        return q;
    }

    private static EnergyStorageSystem CreatePlant()
    {
        var simCfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs = { new Configuration.PcsDeviceConfig(), new Configuration.PcsDeviceConfig() }
                },
                new EssUnitConfig
                {
                    Pcs = { new Configuration.PcsDeviceConfig(), new Configuration.PcsDeviceConfig() }
                }
            }
        };
        var pcsCfg = new PcsPhysicalConfig
        {
            AcVoltageNominal = 690,
            FrequencyNominal = 50,
            MaxCurrent = 2000,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 400,
            BlackStartJoinShareRampSec = 5,
            InrushPeakMultiplier = 0.3,
            DvDtTripThresholdVPerSec = 10_000
        };
        return new EnergyStorageSystem(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            new UnitTransformerConfig { RatedPower = 6300, MagnetizingInrushEnabled = true },
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
    }

    private static void ArmForming(EnergyStorageSystem ess, int startInclusive, int endExclusive, bool run)
    {
        for (int i = startInclusive; i < endExclusive; i++)
        {
            var pcs = ess._pcsList[i];
            // SyncExternalRunCommand(false) 会清黑启动，预同步待命只能撤启停位。
            if (run)
                pcs.SyncExternalRunCommand(true);
            else
                pcs.WithdrawExternalRunCommand();

            Assert.True(ess.TrySetPcsBlackStart(i, true));
            pcs.ApplyIslandVoltageCommand(690);
            pcs.TransitionToMode(run ? OperationMode.Normal : OperationMode.Standby);
            pcs.TransitionToGMode(GridMode.Islanded);
        }
    }
}
