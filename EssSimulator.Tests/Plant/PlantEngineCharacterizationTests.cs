using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Plant;

public class PlantEngineCharacterizationTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(200);
    private static readonly DateTime SimTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static EnergyStorageSystem CreateEss(
        SimulatorConfig? simCfg = null,
        string? meterSourceBusId = null,
        PcsPhysicalConfig? pcsCfg = null)
    {
        simCfg ??= new SimulatorConfig
        {
            Devices = { new EssUnitConfig() }
        };
        pcsCfg ??= new PcsPhysicalConfig { AcVoltageNominal = 690 };
        return new EnergyStorageSystem(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig
            {
                PccMeter = new MeterInstanceConfig
                {
                    SourceBusId = meterSourceBusId ?? RuntimeBusIds.AfterMainBreaker
                }
            });
    }

    [Fact]
    public void MainBreakerClosed_PccAndStationVoltageNearNominal()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(SimTime, Step, Step);

        Assert.InRange(ess.ElectricalNetwork.PccLineVoltageV, 210_000, 230_000);
        Assert.InRange(ess.ElectricalNetwork.StationBus35LineVoltageV, 33_000, 37_000);
    }

    [Fact]
    public void MainBreakerOpen_PccVoltageZero_GridStaysLive()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(SimTime, Step, Step);

        ess.SetMainBreakerClosed(false);
        ess.PlantEngine.Step(SimTime, Step, Step);

        Assert.Equal(0, ess.ElectricalNetwork.PccLineVoltageV);
        Assert.True(ess.ElectricalNetwork.Grid.Port.Output.Ac!.Internal.LineVoltageV > 200_000);
    }

    [Fact]
    public void AfterGridThenMainOpen_NoForming_UnitTransformerSecondaryInputIsZero()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);
        ess.PlantEngine.Step(SimTime, Step, Step);

        ess.SetMainBreakerClosed(false);
        ess.PlantEngine.Step(SimTime, Step, Step);

        var xf = ess.ElectricalNetwork.UnitTransformers[0];
        double secIn = xf.Secondary.Input.Ac?.Internal.LineVoltageV ?? 0;
        Assert.True(secIn < 2,
            $"主断分闸且无构网时单元变二次入口不应垫 1% 额定电压，实际 {secIn:F1} V");
    }

    [Fact]
    public void AfterGridThenMainOpen_NoForming_MainBreakerSecondaryIsZero()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(SimTime, Step, Step);

        Assert.True(
            (ess.ElectricalNetwork.MainBreaker.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0) > 1000,
            "并网时主断二次应带电");

        ess.SetMainBreakerClosed(false);
        ess.PlantEngine.Step(SimTime, Step, Step);

        double secV = ess.ElectricalNetwork.MainBreaker.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0;
        Assert.True(secV < 2, $"主断分闸且无构网时二次侧应为 0，实际 {secV:F1} V");
        Assert.Equal(0, ess.ElectricalNetwork.MainBreaker.Secondary.Output.Ac!.Internal.LineCurrentA);
    }

    [Fact]
    public void AfterGridThenMainOpen_NoForming_MainTransformerVoltagesZero()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(SimTime, Step, Step);

        var xf = ess._mainTransformer;
        Assert.True(xf.GetCurrentState().PrimaryVoltage > 1000, "并网时主变一次应带电");
        Assert.True(xf.GetCurrentState().SecondaryVoltage > 1000, "并网时主变二次应带电");

        ess.SetMainBreakerClosed(false);
        ess.PlantEngine.Step(SimTime, Step, Step);

        var st = xf.GetCurrentState();
        Assert.True(st.PrimaryVoltage < 2, $"主断分闸且无构网时主变一次应为 0，实际 {st.PrimaryVoltage:F1} V");
        Assert.True(st.SecondaryVoltage < 2, $"主断分闸且无构网时主变二次应为 0，实际 {st.SecondaryVoltage:F1} V");
        Assert.True((xf.Primary.Output.Ac?.Internal.LineVoltageV ?? 0) < 2,
            $"主变一次端口应为 0，实际 {xf.Primary.Output.Ac?.Internal.LineVoltageV:F1} V");
        Assert.True((xf.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0) < 2,
            $"主变二次端口应为 0，实际 {xf.Secondary.Output.Ac?.Internal.LineVoltageV:F1} V");
    }

    [Fact]
    public void MeterOnAfterMainBreaker_OpenBreaker_ReportsZero_ClosedHasVoltage()
    {
        using var ess = CreateEss(meterSourceBusId: RuntimeBusIds.AfterMainBreaker);
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(SimTime, Step, Step);
        Assert.True(ess.ElectricalNetwork.PccMeter.Telemetry.Primary.LineVoltageV > 1000);

        ess.SetMainBreakerClosed(false);
        ess.PlantEngine.Step(SimTime, Step, Step);

        Assert.Equal(0, ess.ElectricalNetwork.PccMeter.Telemetry.Primary.LineVoltageV);
        Assert.Equal(0, ess.ElectricalNetwork.PccMeter.Telemetry.Primary.LineCurrentA);
    }

    [Fact]
    public void MainBreakerOpen_BlackStart_MeterOnAfterBreakerSeesBackfeedVoltage()
    {
        using var ess = CreateEss(
            meterSourceBusId: RuntimeBusIds.AfterMainBreaker,
            pcsCfg: new PcsPhysicalConfig
            {
                AcVoltageNominal = 690,
                FrequencyNominal = 50,
                MaxCurrent = 2000,
                BlackStartPrechargeDelayMs = 0,
                BlackStartVoltageRampVs = 400,
                InrushPeakMultiplier = 0.3,
                DvDtTripThresholdVPerSec = 10_000
            });
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);
        ess.SetBmsPcsLinked(0, true);
        Assert.True(ess.TrySetPcsBlackStart(0, true));

        var pcs = ess._pcsList[0];
        pcs.ApplyIslandVoltageCommand(100);
        pcs.SyncExternalRunCommand(true);
        pcs.TransitionToMode(OperationMode.Normal);
        pcs.TransitionToGMode(GridMode.Islanded);

        var t = SimTime;
        for (int i = 0; i < 20; i++)
        {
            t = t.Add(Step);
            ess.PlantEngine.Step(t, Step, Step);
        }

        double bus35 = ess.ElectricalNetwork.StationBus35LineVoltageV;
        double meterV = ess.ElectricalNetwork.PccMeter.Telemetry.Primary.LineVoltageV;
        double meterF = ess.ElectricalNetwork.PccMeter.Telemetry.Primary.FrequencyHz;
        Assert.True(bus35 > 1000, $"黑启动后 35kV 应带电，实际 {bus35:F1} V");
        Assert.True(
            meterV > 1000,
            $"主断分闸后并网点电表仍应采到主变反送电压，实际 {meterV:F1} V phase={pcs.GetBlackStartPhase()}");
        Assert.Equal(50, meterF, 1);
        double expected = 100.0 * 220_000.0 / 690.0;
        Assert.InRange(meterV, expected * 0.8, expected * 1.2);
    }

    [Fact]
    public void UnitWithFourPcs_GridStateReachesAllChannels()
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
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig()
                    }
                }
            }
        };

        using var ess = CreateEss(simCfg);
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(SimTime, Step, Step);

        Assert.Equal(4, ess._pcsList.Count);
        for (int i = 0; i < ess._pcsList.Count; i++)
            Assert.True(ess._pcsList[i].IsGridElectricallyAvailable, $"pcs{i + 1} 未收到网侧可用状态");
    }

    [Fact]
    public void RadialGraph_is_always_constructed()
    {
        using var ess = CreateEss();

        Assert.NotNull(ess.RadialGraph);
        Assert.NotNull(ess.PowerSweepEngine);
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(SimTime, Step, Step);
        Assert.InRange(ess.ElectricalNetwork.PccLineVoltageV, 210_000, 230_000);
    }

    [Fact]
    public void Step_InvokesAfterPlantStep_Once()
    {
        using var ess = CreateEss();
        int calls = 0;
        var hook = new CountingAfterPlantStep(ess, () => calls++);
        AfterPlantStep.Current = hook;
        try
        {
            ess.SetMainBreakerClosed(true);
            ess.PlantEngine.Step(SimTime, Step, Step);
            Assert.Equal(1, calls);
        }
        finally
        {
            hook.Detach();
        }
    }

    private sealed class CountingAfterPlantStep : IAfterPlantStep
    {
        private readonly EnergyStorageSystem _ess;
        private readonly Action _onCall;
        public CountingAfterPlantStep(EnergyStorageSystem ess, Action onCall)
        {
            _ess = ess;
            _onCall = onCall;
        }

        public void AfterPlantStep(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed)
        {
            if (ReferenceEquals(ess, _ess))
                _onCall();
        }

        public void Detach()
        {
            if (ReferenceEquals(EssDeviceSimModel.AfterPlantStep.Current, this))
                EssDeviceSimModel.AfterPlantStep.Reset();
        }
    }
}
