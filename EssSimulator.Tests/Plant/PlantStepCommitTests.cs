using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Plant;

[Collection("PlantEngineStaticHooks")]
public class PlantStepCommitTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(200);
    private static readonly DateTime SimTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static EnergyStorageSystem CreateEss()
    {
        var simCfg = new SimulatorConfig { Devices = { new EssUnitConfig() } };
        var pcsCfg = new PcsPhysicalConfig { AcVoltageNominal = 690 };
        return new EnergyStorageSystem(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
    }

    [Fact]
    public void FailedStep_RestoresElectricalAndDynamic_DoesNotProject()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));
        Assert.InRange(ess.ElectricalNetwork.PccLineVoltageV, 210_000, 230_000);

        ess.SetMainBreakerClosed(false);
        Assert.True(ess.PlantEngine.Step(SimTime.Add(Step), Step, Step));
        Assert.Equal(0, ess.ElectricalNetwork.PccLineVoltageV);

        double socBefore = ess._bmsRackDevices[0].Soc;
        double innerBefore = ess._pcsList[0].CurrentInnerOutput;
        int projections = 0;
        var hook = new CountingAfterPlantStep(ess, () => projections++);
        AfterPlantStep.Current = hook;
        try
        {
            ess.SetMainBreakerClosed(true);
            ess.PlantEngine.FailNextStep = true;
            DateTime failTime = SimTime.Add(Step).Add(Step);
            DateTime committedBefore = ess.PlantEngine.LastCommittedMeasurements.SimTime;
            Assert.False(ess.PlantEngine.Step(failTime, Step, Step));

            Assert.True(ess.SolverFault);
            Assert.False(string.IsNullOrEmpty(ess.SolverFaultMessage));
            Assert.Equal(0, ess.ElectricalNetwork.PccLineVoltageV);
            Assert.Equal(socBefore, ess._bmsRackDevices[0].Soc);
            Assert.Equal(innerBefore, ess._pcsList[0].CurrentInnerOutput);
            Assert.Equal(0, projections);
            Assert.Equal(committedBefore, ess.PlantEngine.LastCommittedMeasurements.SimTime);
            Assert.NotEqual(failTime, ess.PlantEngine.LastCommittedMeasurements.SimTime);
        }
        finally
        {
            AfterPlantStep.Reset();
        }
    }

    [Fact]
    public void SuccessfulStep_ClearsSolverFault_AndPublishesCommittedMeasurements()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.FailNextStep = true;
        Assert.False(ess.PlantEngine.Step(SimTime, Step, Step));
        Assert.True(ess.SolverFault);

        Assert.True(ess.PlantEngine.Step(SimTime.Add(Step), Step, Step));
        Assert.False(ess.SolverFault);
        Assert.Null(ess.SolverFaultMessage);
        Assert.True(ess.PlantEngine.LastCommittedMeasurements.Pcs.Count > 0);
        Assert.Equal(SimTime.Add(Step), ess.PlantEngine.LastCommittedMeasurements.SimTime);
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
    }
}
