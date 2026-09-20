using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;

namespace EssSimulator.Tests.Plant;

[Collection("PlantEngineStaticHooks")]
public class Unit690KernelTakeoverTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(200);
    private static readonly DateTime SimTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FixedFormingSource : IBusVoltageSource
    {
        public FixedFormingSource(string id, double lineV) => (SourceId, _lineV) = (id, lineV);
        public string SourceId { get; }
        private readonly double _lineV;
        public bool IsInjecting(DeviceStepContext context) => true;
        public (double LineVoltageV, double FrequencyHz) GetInjection(DeviceStepContext context) => (_lineV, 50);
    }

    private static EnergyStorageSystem CreateEss() =>
        new(
            new SimulatorConfig { Devices = { new EssUnitConfig() } },
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());

    [Fact]
    public void DefaultSimpleUnit_MarksUnit0KernelOwned()
    {
        using var ess = CreateEss();
        Assert.True(ess.RadialGraph.UnitBuses690[0].KernelOwned);
    }

    [Fact]
    public void GridTied_OwnedBusVoltageComesFromKernelNotCouplerZero()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));

        double v690 = ess.RadialGraph.UnitBuses690[0].LineVoltageV;
        Assert.InRange(v690, 600, 750);
    }

    [Fact]
    public void IslandedTwoFormingSources_OwnedBusIsNotLeftAtZero()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);
        var bus = ess.RadialGraph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FixedFormingSource("a", 690));
        bus.RegisterVoltageSource(new FixedFormingSource("b", 650));

        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));
        Assert.InRange(bus.LineVoltageV, 665, 675);
    }

    [Fact]
    public void SolverFailure_RestoresOwnedBusVoltage()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));
        double v0 = ess.RadialGraph.UnitBuses690[0].LineVoltageV;

        ess.PlantEngine.FailNextStep = true;
        Assert.False(ess.PlantEngine.Step(SimTime + Step, Step, Step));
        Assert.Equal(v0, ess.RadialGraph.UnitBuses690[0].LineVoltageV, 3);
    }

    [Fact]
    public void GridTied_PcsAcVoltageMatchesKernelBus_NotTransformerOrLossFormula()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));

        var bus = ess.RadialGraph.UnitBuses690[0];
        var pcs = ess._pcsList[0];
        Assert.True(pcs.AcTerminalLockedToNetwork);
        Assert.Equal(bus.LineVoltageV, pcs.GetCurrentState().AcVoltage, 1);
        Assert.Equal(bus.LineVoltageV, ess.GetUnitAcBusVoltage(0), 1);
    }

    [Fact]
    public void IslandedTwoForming_PcsTelemetryFollowsBusNotInternalE()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);
        var bus = ess.RadialGraph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FixedFormingSource("a", 690));
        bus.RegisterVoltageSource(new FixedFormingSource("b", 650));

        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));
        Assert.Equal(bus.LineVoltageV, ess._pcsList[0].GetCurrentState().AcVoltage, 1);
        Assert.Equal(bus.LineVoltageV, ess.GetUnitAcBusVoltage(0), 1);
    }
}
