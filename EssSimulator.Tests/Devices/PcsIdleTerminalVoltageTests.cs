using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;

namespace EssSimulator.Tests.Devices;

public class PcsIdleTerminalVoltageTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(200);
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static PcsDevice CreateIdlePcs() =>
        PcsDeviceFactory.Create("pcs_idle", new EssSimulator.EssDeviceSimModel.Model.PcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1000,
            RatedPowerKw = 1725,
            MaxPowerKw = 1897.5
        });

    [Fact]
    public void OffPcs_OnLiveBus_ReportsBusVoltageWithZeroCurrent()
    {
        var pcs = CreateIdlePcs();
        Assert.Equal(OperationMode.Off, pcs.GetCurrentState().Mode);

        PropagationPortBinding.SetAcVoltageInput(pcs.Ac, 689.1, ThreePhaseConnection.Star, 50);
        pcs.Update(1200, 0, T0, Step);

        var st = pcs.GetCurrentState();
        Assert.Equal(OperationMode.Off, st.Mode);
        Assert.InRange(st.AcVoltage, 688, 691);
        Assert.InRange(st.Frequency, 49.5, 50.5);
        Assert.Equal(0, st.AcCurrent, 3);
        Assert.Equal(0, st.ActivePower, 3);
        Assert.Equal(0, st.ReactivePower, 3);

        var acOut = pcs.Ac.Output.Ac!.Internal;
        Assert.InRange(acOut.LineVoltageV, 688, 691);
        Assert.Equal(0, acOut.LineCurrentA, 3);
    }

    [Fact]
    public void OffPcs_DeadBus_ReportsZeroVoltage()
    {
        var pcs = CreateIdlePcs();
        pcs.UpdateGridState(0, 0, false);
        pcs.Update(0, 0, T0, Step);

        var st = pcs.GetCurrentState();
        Assert.True(st.AcVoltage < 2, $"空母线停机电压应为 0，实际 {st.AcVoltage:F1} V");
        Assert.Equal(0, st.Frequency, 3);
        Assert.Equal(0, pcs.Ac.Output.Ac!.Internal.LineVoltageV, 3);
    }

    [Fact]
    public void OffPcs_GridAvailable_FollowsUtilityVoltage()
    {
        var pcs = CreateIdlePcs();
        pcs.UpdateGridState(690, 50, isUtilityGridAvailable: true);
        pcs.Update(1200, 0, T0, Step);

        var st = pcs.GetCurrentState();
        Assert.Equal(OperationMode.Off, st.Mode);
        Assert.InRange(st.AcVoltage, 680, 700);
        Assert.Equal(0, st.AcCurrent, 3);
    }

    [Fact]
    public void SameBusNeighborForming_IdlePcsFollowsBusVoltage()
    {
        using var ess = CreateOneUnitTwoPcsPlant();
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);
        ess.SetBmsPcsLinked(0, true);
        ess.SetBmsPcsLinked(1, true);

        Assert.True(ess.TrySetPcsBlackStart(0, true));
        var host = ess._pcsList[0];
        host.ApplyIslandVoltageCommand(690);
        host.SyncExternalRunCommand(true);
        host.TransitionToMode(OperationMode.Normal);
        host.TransitionToGMode(GridMode.Islanded);

        var t = T0;
        for (int i = 0; i < 40; i++)
        {
            t = t.Add(Step);
            ess.PlantEngine.Step(t, Step, Step);
        }

        var hostSt = ess._pcsList[0].GetCurrentState();
        Assert.True(hostSt.AcVoltage > 650, $"构网机应已抬压，实际 {hostSt.AcVoltage:F1} V");

        var idle = ess._pcsList[1];
        var idleSt = idle.GetCurrentState();
        Assert.Equal(OperationMode.Off, idleSt.Mode);
        Assert.InRange(idleSt.AcVoltage, hostSt.AcVoltage - 15, hostSt.AcVoltage + 15);
        Assert.True(Math.Abs(idleSt.AcCurrent) < 5, $"未开机 PCS 电流应接近 0，实际 {idleSt.AcCurrent:F1} A");
        Assert.Equal(0, idleSt.ActivePower, 1);
        Assert.InRange(idle.Ac.Output.Ac!.Internal.LineVoltageV, 650, 710);
    }

    [Fact]
    public void MainOpen_NoFormingPcs_AcVoltageIsZero()
    {
        using var ess = CreateOneUnitTwoPcsPlant();
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);

        var t = T0;
        ess.PlantEngine.Step(t, Step, Step);

        AssertDeadAc(ess._pcsList[0], "pcs0");
        AssertDeadAc(ess._pcsList[1], "pcs1");
    }

    [Fact]
    public void AfterGridThenMainOpen_NoForming_AcVoltageDropsToZero()
    {
        using var ess = CreateOneUnitTwoPcsPlant();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);

        var t = T0;
        for (int i = 0; i < 5; i++)
        {
            t = t.Add(Step);
            ess.PlantEngine.Step(t, Step, Step);
        }

        Assert.True(ess._pcsList[0].GetCurrentState().AcVoltage > 100,
            "并网时应先看到交流电压");

        ess.SetMainBreakerClosed(false);
        for (int i = 0; i < 5; i++)
        {
            t = t.Add(Step);
            ess.PlantEngine.Step(t, Step, Step);
        }

        AssertDeadAc(ess._pcsList[0], "pcs0");
        AssertDeadAc(ess._pcsList[1], "pcs1");
    }

    private static void AssertDeadAc(PcsDevice pcs, string name)
    {
        var st = pcs.GetCurrentState();
        Assert.True(st.AcVoltage < 2, $"{name} 空母线 AcVoltage 应为 0，实际 {st.AcVoltage:F1} V");
        Assert.Equal(0, st.Frequency, 3);
        Assert.True((pcs.Ac.Input.Ac?.Internal.LineVoltageV ?? 0) < 2,
            $"{name} Ac.Input 应为 0，实际 {pcs.Ac.Input.Ac?.Internal.LineVoltageV:F1} V");
        Assert.True((pcs.Ac.Output.Ac?.Internal.LineVoltageV ?? 0) < 2,
            $"{name} Ac.Output 应为 0，实际 {pcs.Ac.Output.Ac?.Internal.LineVoltageV:F1} V");
    }

    private static EnergyStorageSystem CreateOneUnitTwoPcsPlant()
    {
        var simCfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs =
                    {
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig()
                    }
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
            InrushPeakMultiplier = 0.3,
            DvDtTripThresholdVPerSec = 10_000
        };
        return new EnergyStorageSystem(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            new UnitTransformerConfig { RatedPower = 6300 },
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
    }
}
