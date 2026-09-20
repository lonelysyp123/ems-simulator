using System.Numerics;
using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Shadow;

namespace EssSimulator.Tests.Shadow;

[Collection("PlantEngineStaticHooks")]
public class StationBoundaryCompareTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(200);
    private static readonly DateTime SimTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
    public void GridTied_FirstTick_KernelVocMatchesRadialReferral()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));

        var host = ess.PowerSweepEngine.OwnedIslands[0];
        var sample = StationBoundaryCompare.Capture(ess.RadialGraph, host);

        Assert.InRange(sample.RadialV35, 33_000, 37_000);
        Assert.True(sample.VocResidualV < 1e-6, $"首拍 I=0，核 Voc 应等于径向折算，残差 {sample.VocResidualV}");
        Assert.True((sample.KernelZth - sample.RadialZth).Magnitude < 1e-12);
        Assert.InRange(sample.KernelBus690, 600, 750);
    }

    [Fact]
    public void SecondTick_KernelVocAppliesDelayedDrop_RadialKeeps35kV()
    {
        using var ess = CreateEss();
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);
        Assert.True(ess.PlantEngine.Step(SimTime, Step, Step));

        var host = ess.PowerSweepEngine.OwnedIslands[0];
        var afterFirst = StationBoundaryCompare.Capture(ess.RadialGraph, host);
        Complex iPrev = afterFirst.KernelTheveninCurrent;
        double v35First = afterFirst.RadialV35;

        Assert.True(ess.PlantEngine.Step(SimTime + Step, Step, Step));
        var afterSecond = StationBoundaryCompare.Capture(ess.RadialGraph, host);
        Complex vocExpected = afterSecond.RadialReferredVoc - iPrev * host.Handshake!.GridZ;

        Assert.True((afterSecond.KernelVoc - vocExpected).Magnitude < 1e-4);
        Assert.InRange(afterSecond.RadialV35, 33_000, 37_000);
        Assert.True(Math.Abs(afterSecond.RadialV35 - v35First) < 500,
            "35 kV 仍由径向拥有，不被核 Voc 改写成 690 折算值");
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
}
