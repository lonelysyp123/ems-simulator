using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using EssSimulator.EssDeviceSimModel.Solver;
using EssSimulator.EssDeviceSimModel.Svg;

namespace EssSimulator.Tests.Svg;

public class SvgBusContributionTests
{
    [Fact]
    public void DisabledSvg_IsNotRegistered()
    {
        using var ess = CreateEss(enable: false);
        Assert.Null(ess.Svg);
        Assert.DoesNotContain(ess.RadialGraph.Bus35.Contributors, c => c.ContributorId == "svg");
    }

    [Fact]
    public void EnabledSvg_InjectsReactivePowerAndDropsOutWhenStopped()
    {
        var svg = new SvgDevice("svg", new SvgConfig
        {
            RatedCapacityKvar = 10000,
            NominalLineVoltageV = 35000
        });
        var graph = CreateGraph(svg);
        Assert.Contains(graph.Bus35.Contributors, c => c.ContributorId == "svg");

        var baseline = Collect(graph, TimeSpan.FromSeconds(1));
        svg.RunCommand = 1;
        svg.ReactiveSetpointKvar = 5000;
        var running = Collect(graph, TimeSpan.FromSeconds(1));
        Assert.Equal(5000, running.Q - baseline.Q, 1);
        Assert.Equal(-SvgDevice.LossFraction * 10000, running.P - baseline.P, 1);

        svg.RunCommand = 0;
        var stopped = Collect(graph, TimeSpan.FromMilliseconds(100));
        Assert.Equal(baseline.Q, stopped.Q, 1);
        Assert.Equal(baseline.P, stopped.P, 1);
    }

    private static (double P, double Q) Collect(RadialNetworkGraph graph, TimeSpan step)
    {
        graph.Bus35.LineVoltageV = 35000;
        graph.Bus35.FrequencyHz = 50;
        graph.Bus35.ResetPowerAggregation();
        graph.Bus35.CollectFromContributors(new DeviceStepContext { Step = step });
        return (graph.Bus35.TotalActivePowerKw, graph.Bus35.TotalReactivePowerKvar);
    }

    private static RadialNetworkGraph CreateGraph(SvgDevice? svg)
    {
        var simCfg = new SimulatorConfig { Devices = { new EssUnitConfig() } };
        var pcsCfg = new PcsPhysicalConfig();
        var pccCfg = new PccConfig();
        var network = NetworkTopologyBuilder.Build(
            simCfg, pcsCfg, new TransformerConfig(), new UnitTransformerConfig(), new LoadConfig(), pccCfg);
        return new RadialNetworkGraph(network, pccCfg, pcsCfg, svg: svg);
    }

    private static EnergyStorageSystem CreateEss(bool enable)
    {
        var simCfg = new SimulatorConfig
        {
            EnableSvg = enable,
            Devices = { new EssUnitConfig() }
        };
        return new EnergyStorageSystem(
            simCfg,
            new PcsPhysicalConfig(),
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
    }
}
