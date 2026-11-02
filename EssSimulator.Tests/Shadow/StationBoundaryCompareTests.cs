using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using EssSimulator.EssDeviceSimModel.Shadow;

namespace EssSimulator.Tests.Shadow;

[Collection("PlantEngineStaticHooks")]
public class StationBoundaryCompareTests
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

    private static EnergyStorageSystem CreateEss(params EssUnitConfig[] units)
    {
        var cfg = new SimulatorConfig();
        if (units.Length == 0)
            cfg.Devices.Add(new EssUnitConfig());
        else
            cfg.Devices.AddRange(units);
        return new EnergyStorageSystem(
            cfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
    }

    [Fact]
    public void Attach_MarksBus35KernelOwned()
    {
        using var ess = CreateEss();
        Assert.True(ess.RadialGraph.Bus35.KernelOwned);
    }

    [Fact]
    public void GridTied_FirstTick_Bus35FollowsKernelVoc()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));

        var host = ess.PowerSweepEngine.OwnedIslands[0];
        var sample = StationBoundaryCompare.Capture(ess.RadialGraph, host);

        Assert.True(sample.VocResidualV < 1.0, $"生产 35 kV 应等于核 Voc 折算，残差 {sample.VocResidualV}");
        Assert.True((sample.KernelZth - sample.RadialZth).Magnitude < 1e-12);
        Assert.InRange(sample.KernelBus690, 600, 750);
        Assert.InRange(sample.RadialV35, 25_000, 40_000);
    }

    [Fact]
    public void SecondTick_Bus35DropsWithDelayedGridZ()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));

        var host = ess.PowerSweepEngine.OwnedIslands[0];
        var afterFirst = StationBoundaryCompare.Capture(ess.RadialGraph, host);
        double oc35 = RadialTheveninPort.ReferLvToHv(
            host.Handshake!.GridEmf.Magnitude, host.Bus, ess.RadialGraph.Bus35);

        Assert.True(ess.PlantEngine.Step(SimTime + Step, Step, Step));
        var afterSecond = StationBoundaryCompare.Capture(ess.RadialGraph, host);

        Assert.True(afterSecond.VocResidualV < 1.0, "接替后 35 kV 必须跟核 Voc，不得再留径向开路值");
        if (afterFirst.KernelTheveninCurrent.Magnitude > 1)
        {
            Assert.True(
                Math.Abs(afterSecond.RadialV35 - oc35) > 5,
                "有注入时 35 kV 应相对开路出现 GridZ 压降");
        }
    }

    [Fact]
    public void ShadowHandshake_DoesNotWriteProductionBus35()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));

        var host = ess.PowerSweepEngine.OwnedIslands[0];
        Assert.NotNull(host.Handshake);
        double v35 = ess.RadialGraph.Bus35.LineVoltageV;
        double v690 = host.Bus.LineVoltageV;

        var shadow = host.Handshake!.CreateShadow();
        Assert.True(shadow.TryTick(DomainEventKind.None, out _));

        Assert.Equal(v35, ess.RadialGraph.Bus35.LineVoltageV);
        Assert.Equal(v690, host.Bus.LineVoltageV);
    }

    [Fact]
    public void IslandedTwoUnits_Bus35IsAdmittanceWeighted_NotMaxOfForming()
    {
        using var ess = CreateEss(new EssUnitConfig(), new EssUnitConfig());
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);
        ess.SetUnitBreakerClosed(1, true);
        ess.RadialGraph.UnitBuses690[0].RegisterVoltageSource(new FixedFormingSource("a", 690));
        ess.RadialGraph.UnitBuses690[1].RegisterVoltageSource(new FixedFormingSource("b", 650));
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));

        double v0 = ess.RadialGraph.UnitBuses690[0].LineVoltageV;
        double v1 = ess.RadialGraph.UnitBuses690[1].LineVoltageV;
        Assert.InRange(v0, 660, 689);
        Assert.InRange(v1, 620, 649);

        double weightedLv = (v0 + v1) / 2.0;
        double expected35 = RadialTheveninPort.ReferLvToHv(weightedLv, ess.RadialGraph.UnitBuses690[0], ess.RadialGraph.Bus35);
        double max35 = RadialTheveninPort.ReferLvToHv(690, ess.RadialGraph.UnitBuses690[0], ess.RadialGraph.Bus35);
        Assert.InRange(ess.RadialGraph.Bus35.LineVoltageV, expected35 - 250, expected35 + 250);
        Assert.True(
            ess.RadialGraph.Bus35.LineVoltageV < max35 - 400,
            "离网 35 kV 不得再按构网电压取最大反送");
        Assert.Equal(ess.RadialGraph.Bus35.LineVoltageV, ess.ElectricalNetwork.StationBus35LineVoltageV, 1);
    }
}
