using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Plant;

public class PlantEngineReplayTests
{
    private static EnergyStorageSystem CreateEss()
    {
        var simCfg = new SimulatorConfig
        {
            Runtime = { PropagationIntervalMs = 100 },
            Devices = { new EssUnitConfig() }
        };
        return new EnergyStorageSystem(
            simCfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
    }

    [Fact]
    public void TwoPlants_SameLogicalTicksAndCommands_MatchKeyQuantities()
    {
        using var a = CreateEss();
        using var b = CreateEss();

        for (int i = 0; i < 8; i++)
        {
            var (t, dt, integ) = a.Clock.Tick();
            b.Clock.Tick();
            a.SetMainBreakerClosed(true);
            b.SetMainBreakerClosed(true);
            a.PlantEngine.Step(t, dt, integ);
            b.PlantEngine.Step(t, dt, integ);
        }

        Assert.Equal(a.ElectricalNetwork.PccLineVoltageV, b.ElectricalNetwork.PccLineVoltageV);
        Assert.Equal(a._bmsRackDevices[0].Soc, b._bmsRackDevices[0].Soc);
        Assert.Equal(a._pcsList[0].GetCurrentState().ActivePower, b._pcsList[0].GetCurrentState().ActivePower);
        Assert.Equal(a.Clock.SimTime, b.Clock.SimTime);
    }
}
