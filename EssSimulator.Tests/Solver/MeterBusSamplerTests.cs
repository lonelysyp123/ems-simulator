using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Solver;
using EssSimulator.EssSimModelApi.ElectricMeter;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.Tests.Solver;

public class MeterBusSamplerTests
{
    private static EnergyStorageSystem CreateEss(string? sourceBusId)
    {
        var cfg = new SimulatorConfig
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
        return new EnergyStorageSystem(
            cfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig
            {
                PccMeter = new MeterInstanceConfig { SourceBusId = sourceBusId }
            });
    }

    [Fact]
    public void MainBreakerOpen_MeterOnAfterBreakerBus_ReportsZeroVoltage_GridStaysLive()
    {
        using var ess = CreateEss(RuntimeBusIds.AfterMainBreaker);
        var step = TimeSpan.FromMilliseconds(200);
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(DateTime.UtcNow, step, step);
        Assert.True(ess.ElectricalNetwork.PccMeter.Telemetry.Primary.LineVoltageV > 1000);

        ess.SetMainBreakerClosed(false);
        ess.PlantEngine.Step(DateTime.UtcNow, step, step);

        Assert.Equal(0, ess.ElectricalNetwork.PccMeter.Telemetry.Primary.LineVoltageV);
        Assert.Equal(0, ess.ElectricalNetwork.PccMeter.Telemetry.Primary.LineCurrentA);
        Assert.True(ess.ElectricalNetwork.Grid.Port.Output.Ac!.Internal.LineVoltageV > 200_000);

        var em = new EmData();
        double fwd = 0, rev = 0;
        EmMapper.MapEssToEmData(ess, em, step, ref fwd, ref rev);
        Assert.Equal(0, em.LineVoltageAB);
    }

    [Fact]
    public void MainBreakerOpen_MeterOnGridBus_KeepsGridVoltage()
    {
        using var ess = CreateEss(RuntimeBusIds.Grid);
        var step = TimeSpan.FromMilliseconds(200);
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(DateTime.UtcNow, step, step);

        ess.SetMainBreakerClosed(false);
        ess.PlantEngine.Step(DateTime.UtcNow, step, step);

        Assert.InRange(ess.ElectricalNetwork.PccMeter.Telemetry.Primary.LineVoltageV, 210_000, 230_000);
        Assert.Equal(0, ess.ElectricalNetwork.PccLineVoltageV);
    }

    [Fact]
    public void Sample_AfterMainBreaker_hits_graph_not_port_fallback()
    {
        using var ess = CreateEss(RuntimeBusIds.AfterMainBreaker);
        var step = TimeSpan.FromMilliseconds(200);
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(DateTime.UtcNow, step, step);

        Assert.NotNull(ess.RadialGraph);
        var sampled = MeterBusSampler.Sample(
            ess.ElectricalNetwork, ess.RadialGraph, RuntimeBusIds.AfterMainBreaker, 50);

        Assert.Equal(ess.RadialGraph.BusAfterMainBreaker.LineVoltageV, sampled.LineVoltageV, 1);
        Assert.True(sampled.LineVoltageV > 1000);
    }

    [Fact]
    public void Sample_unknown_bus_with_graph_returns_zero_not_after_breaker()
    {
        using var ess = CreateEss(RuntimeBusIds.AfterMainBreaker);
        var step = TimeSpan.FromMilliseconds(200);
        ess.SetMainBreakerClosed(true);
        ess.PlantEngine.Step(DateTime.UtcNow, step, step);

        var after = MeterBusSampler.Sample(
            ess.ElectricalNetwork, ess.RadialGraph, RuntimeBusIds.AfterMainBreaker, 50);
        Assert.True(after.LineVoltageV > 1000);

        var unknown = MeterBusSampler.Sample(
            ess.ElectricalNetwork, ess.RadialGraph, "BUS_DOES_NOT_EXIST", 50);
        Assert.Equal(0, unknown.LineVoltageV);
        Assert.Equal(0, unknown.LineCurrentA);
        Assert.NotEqual(after.LineVoltageV, unknown.LineVoltageV);
    }
}
