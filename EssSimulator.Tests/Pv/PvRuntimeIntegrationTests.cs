using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using EssSimulator.EssDeviceSimModel.Pv;
using EssSimulator.EssDeviceSimModel.Solver;

namespace EssSimulator.Tests.Pv;

public class PvRuntimeIntegrationTests
{
    [Fact]
    public void BusContributor_AfterStcStart_ReportsGenerationPower()
    {
        var unit = PvUnitDevice.CreateDefault("pv1");
        unit.Logger.SubarrayOnOff = 1;
        unit.UpdateGridState(690, 50, isUtilityGridAvailable: true);
        unit.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(5));

        var bus = new ElectricalBusNode("BUS_35", 35000);
        bus.RegisterContributor(new PvUnitBusContributor(unit));
        bus.CollectFromContributors(new DeviceStepContext());

        Assert.InRange(bus.TotalActivePowerKw, 16 * 318, 16 * 322);
        Assert.True(bus.TotalActivePowerKw > 0);
    }

    [Fact]
    public void TopologyBuilder_PvOnly_HasNoEssUnitBranches()
    {
        var simCfg = new SimulatorConfig
        {
            PvUnits = { new PvUnitRuntimeConfig { Name = "光伏单元-1", InverterCount = 1 } }
        };

        var network = NetworkTopologyBuilder.Build(
            simCfg,
            new PcsPhysicalConfig(),
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig());

        Assert.Empty(network.UnitTransformers);
        Assert.Empty(network.UnitBreakers);
        Assert.Empty(network.PcsDevices);
        Assert.Empty(network.BmsDevices);
        Assert.NotNull(network.MainTransformer);
        Assert.NotNull(network.Load);
    }

    [Fact]
    public void EnergyStorageSystem_PvOnly_StepsPowerIndependentOfClock()
    {
        var simCfg = new SimulatorConfig
        {
            PvUnits =
            {
                new PvUnitRuntimeConfig
                {
                    Name = "光伏单元-1",
                    InverterCount = 1,
                    StringCount = 2,
                    ModulesPerString = 30,
                    InverterRatedPowerKw = 320,
                    InverterMaxPowerKw = 352
                }
            }
        };

        using var ess = new EnergyStorageSystem(
            simCfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());

        Assert.Empty(ess._pcsList);
        Assert.Single(ess.PvUnits);
        Assert.Equal(1, ess.PvUnits[0].InverterCount);

        var midnight = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);
        ess.PlantEngine.Step(midnight, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        Assert.True(ess.PvUnits[0].ActivePowerKw > 20);
        Assert.Equal(1, ess.PvUnits[0].Logger.SubarrayOnOff);
    }

    [Fact]
    public void EnergyStorageSystem_PvOnly_IncidenceAngleCutsPower()
    {
        var simCfg = new SimulatorConfig
        {
            PvUnits =
            {
                new PvUnitRuntimeConfig
                {
                    Name = "光伏单元-1",
                    InverterCount = 1,
                    StringCount = 2,
                    ModulesPerString = 30,
                    InverterRatedPowerKw = 320,
                    InverterMaxPowerKw = 352
                }
            }
        };

        using var ess = new EnergyStorageSystem(
            simCfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());

        var t = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);
        ess.PlantEngine.Step(t, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        double full = ess.PvUnits[0].ActivePowerKw;
        Assert.True(full > 20);

        ess.TrySetPvArrayClimate(1, "A", "angle", 20, out _);
        ess.TrySetPvArrayClimate(1, "B", "angle", 20, out _);
        ess.PlantEngine.Step(t, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        Assert.True(ess.PvUnits[0].ActivePowerKw < full * 0.5);
    }

    [Fact]
    public void EnergyStorageSystem_PvOnly_AlbedoRaisesAvailablePower()
    {
        var simCfg = new SimulatorConfig
        {
            PvUnits =
            {
                new PvUnitRuntimeConfig
                {
                    Name = "光伏单元-1",
                    InverterCount = 1,
                    StringCount = 2,
                    ModulesPerString = 30,
                    InverterRatedPowerKw = 320,
                    InverterMaxPowerKw = 352
                }
            }
        };

        using var ess = new EnergyStorageSystem(
            simCfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());

        var t = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);
        ess.PlantEngine.Step(t, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        double baseAvail = ess.PvUnits[0].MaximumDischargePowerKw;

        Assert.True(ess.TrySetPvArrayClimate(1, "A", "albedo", 0.2, out _));
        Assert.True(ess.TrySetPvArrayClimate(1, "B", "albedo", 0.2, out _));
        ess.PlantEngine.Step(t, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        Assert.True(ess.PvUnits[0].MaximumDischargePowerKw > baseAvail);
        Assert.InRange(ess.PvUnits[0].ArrayA.Albedo, 0.199, 0.201);
    }
}
