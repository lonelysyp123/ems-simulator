using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class UnitHvLiveCloseTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(200);
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CloseUnit2HvOntoLiveIsland_DoesNotTripUnit1()
    {
        Assert.Equal(3500, new BreakerConfig().Unit.FaultThresholdA);
        Assert.True(new UnitTransformerConfig().MagnetizingInrushEnabled);

        using var ess = CreateTwoUnitPlant();
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);
        ess.SetUnitBreakerClosed(1, false);

        for (int i = 0; i < ess._pcsList.Count; i++)
            ess.SetBmsPcsLinked(i, true);

        ArmForming(ess, startInclusive: 0, endExclusive: 2);

        var t = T0;
        for (int i = 0; i < 40; i++)
        {
            t = t.Add(Step);
            ess.PlantEngine.Step(t, Step, Step);
        }

        Assert.True(ess.IsUnitBreakerClosed(0));
        Assert.False(ess.IsUnitBreakerTripped(0));
        Assert.False(ess.IsUnitBreakerClosed(1));
        double v690 = ess._pcsList[0].GetCurrentState().AcVoltage;
        Assert.True(v690 > 650, $"单元 1 应已构网到约 690 V，实际 {v690:F1} V");

        ess.SetUnitBreakerClosed(1, true);

        double iMax = 2000;
        for (int i = 0; i < 25; i++)
        {
            t = t.Add(Step);
            ess.PlantEngine.Step(t, Step, Step);

            Assert.False(ess.IsUnitBreakerTripped(0), $"合闸后第 {i + 1} 拍单元 1 高压跳闸");
            Assert.True(ess.IsUnitBreakerClosed(0), $"合闸后第 {i + 1} 拍单元 1 高压应保持合");
            Assert.True(ess.IsUnitBreakerClosed(1), $"合闸后第 {i + 1} 拍单元 2 高压应合");
            Assert.False(ess.IsUnitBreakerTripped(1), $"合闸后第 {i + 1} 拍单元 2 高压跳闸");

            for (int ch = 0; ch < 2; ch++)
            {
                var st = ess._pcsList[ch].GetCurrentState();
                Assert.True(
                    Math.Abs(st.AcCurrent) <= iMax + 1e-3,
                    $"PCS{ch + 1} |I|={st.AcCurrent:F1} A 超过 Imax={iMax}");
                Assert.Equal(0, st.FaultType);
            }
        }

        Assert.True(ess.IsUnitBreakerClosed(0));
        Assert.False(ess.IsUnitBreakerTripped(0));
        Assert.True(ess.IsUnitBreakerClosed(1));
        Assert.False(ess.IsUnitBreakerTripped(1));

        var host = ess._pcsList[0].GetCurrentState();
        Assert.True(
            host.BlackStartEnabled &&
            host.BlackStartPhase is BlackStartPhase.VoltageRegulating or BlackStartPhase.Synchronized,
            $"单元 1 应仍离网同步，实际 phase={host.BlackStartPhase}");
        Assert.InRange(host.AcVoltage, 650, 710);
        Assert.Equal(3500, new BreakerConfig().Unit.FaultThresholdA);
        Assert.True(ess.ElectricalNetwork.UnitTransformers[1].GetCurrentState().SecondaryVoltage > 1);
    }

    private static EnergyStorageSystem CreateTwoUnitPlant()
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
                },
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
        var unitXf = new UnitTransformerConfig
        {
            RatedPower = 6300,
            MagnetizingInrushEnabled = true
        };
        return new EnergyStorageSystem(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            unitXf,
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
    }

    private static void ArmForming(EnergyStorageSystem ess, int startInclusive, int endExclusive)
    {
        for (int i = startInclusive; i < endExclusive; i++)
        {
            Assert.True(ess.TrySetPcsBlackStart(i, true));
            var pcs = ess._pcsList[i];
            pcs.ApplyIslandVoltageCommand(690);
            pcs.SyncExternalRunCommand(true);
            pcs.TransitionToMode(OperationMode.Normal);
            pcs.TransitionToGMode(GridMode.Islanded);
        }
    }
}
