using System.Numerics;
using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using EssSimulator.EssDeviceSimModel.Shadow;
using EssSimulator.EssDeviceSimModel.Solver;

namespace EssSimulator.Tests.Shadow;

public class RadialIslandShadowTests
{
    private sealed class FixedFormingSource : IBusVoltageSource
    {
        public FixedFormingSource(string id, double lineV)
        {
            SourceId = id;
            _lineV = lineV;
        }

        public string SourceId { get; }
        private readonly double _lineV;
        public bool IsInjecting(DeviceStepContext context) => true;
        public (double LineVoltageV, double FrequencyHz) GetInjection(DeviceStepContext context) => (_lineV, 50);
    }

    private sealed class ImpedanceFormingSource : IBusVoltageSource
    {
        public ImpedanceFormingSource(string id, double lineV, Complex z)
        {
            SourceId = id;
            _lineV = lineV;
            OutputImpedanceOhm = z;
        }

        public string SourceId { get; }
        private readonly double _lineV;
        public Complex OutputImpedanceOhm { get; }
        public bool IsInjecting(DeviceStepContext context) => true;
        public (double LineVoltageV, double FrequencyHz) GetInjection(DeviceStepContext context) => (_lineV, 50);
    }

    [Fact]
    public void Capture_UsesPerSourceOutputImpedance_NotSingleDefault()
    {
        var graph = CreateTwoPcsUnitGraph();
        var bus = graph.UnitBuses690[0];
        var zA = new Complex(0.05, 0.20);
        bus.RegisterVoltageSource(new ImpedanceFormingSource("a", 690, zA));
        bus.RegisterVoltageSource(new FixedFormingSource("b", 650));
        var sweep = Sweep(graph);

        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);

        Assert.Equal(2, snap.Sources.Count);
        Assert.True((snap.Sources[0].OutputImpedanceOhm - zA).Magnitude < 1e-12);
        Assert.True((snap.Sources[1].OutputImpedanceOhm - MinPcsPort.DefaultOutputImpedanceOhm).Magnitude < 1e-12,
            "未覆盖的源走接口默认阻抗");
    }

    [Fact]
    public void Capture_ExplicitOverrideWins_OverSourceImpedance()
    {
        var graph = CreateTwoPcsUnitGraph();
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new ImpedanceFormingSource("a", 690, new Complex(0.05, 0.20)));
        var sweep = Sweep(graph);
        var forced = new Complex(0.1, 0.3);

        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep, pcsOutputZ: forced);

        Assert.True((snap.Sources[0].OutputImpedanceOhm - forced).Magnitude < 1e-12);
    }

    private static RadialNetworkGraph CreateTwoPcsUnitGraph()
    {
        var simCfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs = { new Configuration.PcsDeviceConfig(), new Configuration.PcsDeviceConfig() },
                    Bms = { new BmsDeviceConfig(), new BmsDeviceConfig() }
                }
            }
        };
        var pcsCfg = new PcsPhysicalConfig { AcVoltageNominal = 690 };
        var pccCfg = new PccConfig();
        var network = NetworkTopologyBuilder.Build(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            pccCfg);
        return new RadialNetworkGraph(network, pccCfg, pcsCfg);
    }

    private static PropagationSweepContext Sweep(
        RadialNetworkGraph graph,
        double lastBus35 = 0,
        bool mainBreakerClosed = false,
        double openCircuitBus35 = 0) => new()
    {
        DeviceContext = new DeviceStepContext(),
        Step = TimeSpan.FromMilliseconds(100),
        Bus35 = graph.Bus35,
        PcsCfg = graph.PcsCfg,
        SystemFrequencyHz = 50,
        LastBus35LineVoltageV = lastBus35,
        StationBusNominalLineVoltageV = graph.Bus35.NominalLineVoltageV,
        MainBreakerClosed = mainBreakerClosed,
        OpenCircuitBus35LineVoltageV = openCircuitBus35
    };

    [Fact]
    public void ShadowSolve_DoesNotWriteProductionBusVoltage()
    {
        var graph = CreateTwoPcsUnitGraph();
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FixedFormingSource("a", 690));
        bus.RegisterVoltageSource(new FixedFormingSource("b", 650));
        var sweep = Sweep(graph);

        Assert.True(bus.ApplyLocalVoltageSources(sweep));
        double productionV = bus.LineVoltageV;
        Assert.Equal(670, productionV, 0);

        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);
        var runner = new IslandShadowRunner(snap);
        IslandShadowTickResult shadow = runner.Tick();

        Assert.Equal(productionV, bus.LineVoltageV);
        Assert.Equal(50, bus.FrequencyHz);
        Assert.True(shadow.Island.PcsCurrents[0].Magnitude > 20);
        Assert.True((shadow.Island.PcsCurrentSum).Magnitude < 1e-6);
        Assert.True(Math.Abs(shadow.BusVoltage.Magnitude - productionV) < 5,
            "Equal-Z island voltage stays near the arithmetic mean; circulating current is the distinguisher");
    }

    [Fact]
    public void SyncFormingReferences_DoesNotCopyProductionBoundaryIntoShadow()
    {
        var graph = CreateTwoPcsUnitGraph();
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FixedFormingSource("a", 690));
        bus.RegisterVoltageSource(new FixedFormingSource("b", 650));
        var sweep = Sweep(graph);

        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);
        var runner = new IslandShadowRunner(snap);
        runner.Tick();

        bus.LineVoltageV = 123;
        bus.FrequencyHz = 47;
        runner.SyncFormingReferences(snap);
        runner.Tick();

        Assert.Equal(123, bus.LineVoltageV);
        Assert.Equal(47, bus.FrequencyHz);
    }

    [Fact]
    public void Capture_UsesUnitTransformerLeakageForTheveninZ()
    {
        var graph = CreateTwoPcsUnitGraph();
        var sweep = Sweep(graph);
        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);
        Complex expected = RadialIslandShadowAdapter.LeakageOhmLv(graph.Network.UnitTransformers[0].Config);
        Assert.NotNull(snap.TheveninZ);
        Assert.True((snap.TheveninZ!.Value - expected).Magnitude < 1e-12);
        Assert.True(snap.TheveninZ.Value.Magnitude >= PhasorNetwork.MinSeriesImpedanceOhm);
    }

    [Fact]
    public void UnequalFormingE_ShadowHasCirculatingCurrent_ProductionOnlyAverages()
    {
        var graph = CreateTwoPcsUnitGraph();
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FixedFormingSource("a", 690));
        bus.RegisterVoltageSource(new FixedFormingSource("b", 650));
        var sweep = Sweep(graph);

        bus.ApplyLocalVoltageSources(sweep);
        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);
        var shadow = new IslandShadowRunner(snap).Tick();

        Complex iCirc = (690 - 650) / (2.0 * MinPcsPort.DefaultOutputImpedanceOhm);
        Assert.True((shadow.Island.PcsCurrents[0] - iCirc).Magnitude < 1e-6);
        Assert.Equal(670, bus.LineVoltageV, 0);
    }

    [Fact]
    public void GridTiedCapture_ShadowHandshakeDoesNotWriteStationBus()
    {
        var graph = CreateTwoPcsUnitGraph();
        graph.Bus35.LineVoltageV = 35000;
        graph.Bus35.FrequencyHz = 50;
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FixedFormingSource("a", 690));
        var sweep = Sweep(graph, lastBus35: 35000, mainBreakerClosed: true, openCircuitBus35: 35000);

        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);
        Assert.NotNull(snap.ReferredGridEmf);
        var runner = new IslandShadowRunner(snap);
        Assert.NotNull(runner.Handshake);
        runner.Tick();

        Assert.Equal(35000, graph.Bus35.LineVoltageV);
        Assert.Equal(0, bus.LineVoltageV);
    }
}
