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

    private sealed class DcFormingSource : IBusVoltageSource
    {
        public DcFormingSource(string id, double lineV, double dcV, double eff)
        {
            SourceId = id;
            _lineV = lineV;
            DcVoltageV = dcV;
            Efficiency = eff;
        }

        public string SourceId { get; }
        private readonly double _lineV;
        public double DcVoltageV { get; }
        public double Efficiency { get; }
        public bool IsInjecting(DeviceStepContext context) => true;
        public (double LineVoltageV, double FrequencyHz) GetInjection(DeviceStepContext context) => (_lineV, 50);
    }

    private sealed class FollowingSource : IBusVoltageSource
    {
        public FollowingSource(
            string id,
            double activePowerW,
            double reactivePowerVAr = 0,
            double dcV = 0,
            double eff = AverageConverterPort.DefaultEfficiency,
            double? limitA = null)
        {
            SourceId = id;
            _pW = activePowerW;
            _qVAr = reactivePowerVAr;
            DcVoltageV = dcV;
            Efficiency = eff;
            FollowingCurrentLimitA = limitA;
        }

        public string SourceId { get; }
        private readonly double _pW;
        private readonly double _qVAr;
        public double DcVoltageV { get; }
        public double Efficiency { get; }
        public double? FollowingCurrentLimitA { get; }
        public bool IsInjecting(DeviceStepContext context) => false;
        public (double LineVoltageV, double FrequencyHz) GetInjection(DeviceStepContext context) => (0, 50);
        public bool IsGridFollowing(DeviceStepContext context) => true;
        public (double ActivePowerW, double ReactivePowerVAr) GetFollowingInjection(DeviceStepContext context) =>
            (_pW, _qVAr);
    }

    [Fact]
    public void Capture_PopulatesDcVoltageAndEfficiency_FromSource()
    {
        var graph = CreateTwoPcsUnitGraph();
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new DcFormingSource("a", 690, 1200, 0.98));
        bus.RegisterVoltageSource(new FixedFormingSource("b", 650));
        var sweep = Sweep(graph);

        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);

        Assert.Equal(1200, snap.Sources[0].DcVoltageV);
        Assert.Equal(0.98, snap.Sources[0].Efficiency);
        Assert.Equal(0, snap.Sources[1].DcVoltageV, 6);
        Assert.Equal(AverageConverterPort.DefaultEfficiency, snap.Sources[1].Efficiency);
    }

    [Fact]
    public void Host_SettlesPerSourceDcCurrent_WithoutWritingBusAgain()
    {
        var graph = CreateTwoPcsUnitGraph();
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new DcFormingSource("a", 690, 1200, 0.98));
        bus.RegisterVoltageSource(new DcFormingSource("b", 650, 1100, 0.98));
        var host = new ProductionUnit690Host(bus);
        var sweep = Sweep(graph);

        host.ApplyAfterUpper(graph, sweep);
        double busAfterSolve = bus.LineVoltageV;

        Assert.True(busAfterSolve > 1.0, "内核应把 690 母线解出非零电压");
        Assert.True(host.TryGetSettledDcCurrent("a", out double idcA));
        Assert.True(host.TryGetSettledDcCurrent("b", out double idcB));
        Assert.True(idcA > 0, "高电势源放电，直流电流流入设备");
        Assert.True(idcB < 0, "低电势源充电，直流电流流出设备");
        Assert.False(host.TryGetSettledDcCurrent("missing", out _));
        Assert.Equal(busAfterSolve, bus.LineVoltageV);
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

    [Fact]
    public void Capture_EmitsGridFollowingSources_WhenGridTied()
    {
        var graph = CreateTwoPcsUnitGraph();
        graph.Bus35.LineVoltageV = 35000;
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FollowingSource("gfl", 100_000, 20_000, dcV: 1200, eff: 0.98, limitA: 1588));
        var sweep = Sweep(graph, lastBus35: 35000, mainBreakerClosed: true, openCircuitBus35: 35000);

        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);

        Assert.Empty(snap.Sources);
        Assert.Single(snap.Following);
        var f = snap.Following[0];
        Assert.Equal("gfl", f.Id);
        Assert.Equal(100_000, f.ActivePowerW, 6);
        Assert.Equal(20_000, f.ReactivePowerVAr, 6);
        Assert.Equal(1200, f.DcVoltageV, 6);
        Assert.Equal(0.98, f.Efficiency, 6);
        Assert.Equal(1588, f.CurrentLimitA!.Value, 6);
    }

    [Fact]
    public void Capture_DropsFollowing_WhenNoVoltageReference()
    {
        var graph = CreateTwoPcsUnitGraph();
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FollowingSource("gfl", 100_000));
        var sweep = Sweep(graph, mainBreakerClosed: false, openCircuitBus35: 0);

        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);

        Assert.Empty(snap.Sources);
        Assert.Empty(snap.Following);
        Assert.Null(snap.ReferredGridEmf);
    }

    [Fact]
    public void Capture_KeepsFollowing_WhenIslandedWithFormingSource()
    {
        var graph = CreateTwoPcsUnitGraph();
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FixedFormingSource("gfm", 690));
        bus.RegisterVoltageSource(new FollowingSource("gfl", 50_000));
        var sweep = Sweep(graph, mainBreakerClosed: false, openCircuitBus35: 0);

        var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, 0, sweep);

        Assert.Single(snap.Sources);
        Assert.Single(snap.Following);
        Assert.Null(snap.ReferredGridEmf);
    }

    [Fact]
    public void Host_CommitsGridFollowingPower_AndSettlesDc_WhenGridTied()
    {
        var graph = CreateTwoPcsUnitGraph();
        graph.Bus35.LineVoltageV = 35000;
        var bus = graph.UnitBuses690[0];
        bus.RegisterVoltageSource(new FollowingSource("gfl", 100_000, dcV: 1200, eff: 0.98));
        var host = new ProductionUnit690Host(bus);
        var sweep = Sweep(graph, lastBus35: 35000, mainBreakerClosed: true, openCircuitBus35: 35000);

        host.ApplyAfterUpper(graph, sweep);

        Assert.True(bus.LineVoltageV > 1.0, "并网跟网岛应解出非零母线电压");
        Assert.True(host.TryGetCommittedInjectionKw(out double pKw, out _));
        Assert.True(pKw > 90 && pKw < 105,
            $"跟网 PCS 的有功应进入上域提交注入（修复此前丢失），实际 {pKw} kW");
        Assert.True(host.TryGetSettledDcCurrent("gfl", out double idc));
        Assert.True(idc > 0, "放电（向母线送电）时直流电流流入设备（为正）");
    }
}
