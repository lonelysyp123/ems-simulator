using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Thermal;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;

namespace EssSimulator.Tests.Thermal;

public class HvacClosedLoopTests
{
    [Fact]
    public void WithHvac_CabinetAirStaysNearSetpointUnderHeat()
    {
        var cfg = new BmsCabinetThermalConfig
        {
            HvacEnabled = true,
            HvacCoolingPowerW = 8000,
            HvacSetpointCelsius = 25,
            HvacHysteresisCelsius = 1,
            HvacProportionalGainWPerK = 2000,
            AirThermalCapacityJPerK = 20_000,
            ShellThermalCapacityJPerK = 50_000,
            BatteryThermalCapacityJPerK = 50_000,
            OutdoorToShellResistanceKPerW = 0.05,
            ShellToAirResistanceKPerW = 0.02,
            BatteryToAirResistanceKPerW = 0.01
        };

        var zone = new BmsCabinetThermalZone("bms1", cfg, 30);
        for (int i = 0; i < 200; i++)
        {
            zone.PendingBatteryHeatW = 3000;
            zone.Step(TimeSpan.FromSeconds(1), outdoorCelsius: 35);
        }

        Assert.True(zone.IsHvacCooling || zone.CabinetAirCelsius < 32);
        Assert.InRange(zone.CabinetAirCelsius, 20, 35);
    }

    [Fact]
    public void WithoutHvac_AirRisesAboveWithHvacCase()
    {
        var baseCfg = new BmsCabinetThermalConfig
        {
            HvacCoolingPowerW = 0,
            AirThermalCapacityJPerK = 20_000,
            ShellThermalCapacityJPerK = 50_000,
            BatteryThermalCapacityJPerK = 50_000,
            OutdoorToShellResistanceKPerW = 0.2,
            ShellToAirResistanceKPerW = 0.05,
            BatteryToAirResistanceKPerW = 0.02
        };

        var noHvac = new BmsCabinetThermalZone("a", baseCfg, 25);
        var withHvacCfg = new BmsCabinetThermalConfig
        {
            HvacEnabled = true,
            HvacCoolingPowerW = 10000,
            HvacSetpointCelsius = 25,
            HvacHysteresisCelsius = 0.5,
            HvacProportionalGainWPerK = 3000,
            AirThermalCapacityJPerK = 20_000,
            ShellThermalCapacityJPerK = 50_000,
            BatteryThermalCapacityJPerK = 50_000,
            OutdoorToShellResistanceKPerW = 0.2,
            ShellToAirResistanceKPerW = 0.05,
            BatteryToAirResistanceKPerW = 0.02
        };

        var withHvac = new BmsCabinetThermalZone("b", withHvacCfg, 25);

        for (int i = 0; i < 150; i++)
        {
            noHvac.PendingBatteryHeatW = 5000;
            withHvac.PendingBatteryHeatW = 5000;
            noHvac.Step(TimeSpan.FromSeconds(1), 25);
            withHvac.Step(TimeSpan.FromSeconds(1), 25);
        }

        Assert.True(noHvac.CabinetAirCelsius > withHvac.CabinetAirCelsius + 1);
    }
}

public class ThermalAgingContextTests
{
    [Fact]
    public void Arrhenius_HotterThanRef_GreaterThanOne()
    {
        ThermalAgingContext.ApplyFrom(new ThermalFeedbackConfig
        {
            TemperatureAgingEnabled = true,
            AgingReferenceCelsius = 25,
            AgingArrheniusB = 5000
        });

        Assert.True(ThermalAgingContext.ArrheniusFactor(45) > 1.5);
        Assert.InRange(ThermalAgingContext.ArrheniusFactor(25), 0.99, 1.01);
    }
}
