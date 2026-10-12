using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using EssSimulator.EssDeviceSimModel.Solver;

namespace EssSimulator.Tests.Propagation;

public class RadialNetworkGraphTests
{
    private static RadialNetworkGraph CreateGraph()
    {
        var simCfg = new SimulatorConfig
        {
            Devices = { new EssUnitConfig() }
        };
        var pcsCfg = new PcsPhysicalConfig();
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

    [Fact]
    public void BusIds_match_RuntimeBusIds()
    {
        var graph = CreateGraph();

        Assert.Equal(RuntimeBusIds.Grid, graph.BusGrid.BusId);
        Assert.Equal(RuntimeBusIds.AfterMainBreaker, graph.BusAfterMainBreaker.BusId);
        Assert.Equal(RuntimeBusIds.Station35, graph.Bus35.BusId);
        Assert.Equal(RuntimeBusIds.Unit690(0), graph.UnitBuses690[0].BusId);
    }

    [Fact]
    public void FindBus_AfterMainBreaker_returns_same_node()
    {
        var graph = CreateGraph();

        Assert.Same(graph.BusAfterMainBreaker, graph.FindBus(RuntimeBusIds.AfterMainBreaker));
    }

    [Fact]
    public void FindBus_legacy_BUS_MAIN_SEC_maps_to_AfterMainBreaker()
    {
        var graph = CreateGraph();

        Assert.Same(graph.BusAfterMainBreaker, graph.FindBus(RuntimeBusIds.LegacyAfterMainBreaker));
    }

    [Fact]
    public void Split_transformer_creates_left_and_right_690_buses()
    {
        var simCfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs = { new EssSimulator.Configuration.PcsDeviceConfig(), new EssSimulator.Configuration.PcsDeviceConfig() },
                    Bms = { new BmsDeviceConfig(), new BmsDeviceConfig() },
                    SplitTransformer = new SplitTransformerRuntimeConfig
                    {
                        Present = true,
                        LeftEarPcsIndices = { 0 },
                        RightEarPcsIndices = { 1 }
                    }
                }
            }
        };
        var pcsCfg = new PcsPhysicalConfig();
        var pccCfg = new PccConfig();
        var network = NetworkTopologyBuilder.Build(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            pccCfg,
            pcsPerUnit: new[] { 2 });
        var graph = new RadialNetworkGraph(network, pccCfg, pcsCfg);

        Assert.Single(network.DualEarTransformers[0]);
        Assert.Equal(RuntimeBusIds.Unit690Left(0), graph.UnitBuses690[0].BusId);
        Assert.Equal(RuntimeBusIds.Unit690Right(0), graph.ExtraUnitBuses690[0].BusId);
        Assert.Same(graph.UnitBuses690[0], graph.Bus690ForPcsChannel(0));
        Assert.Same(graph.ExtraUnitBuses690[0], graph.Bus690ForPcsChannel(1));
        Assert.Same(graph.ExtraUnitBuses690[0], graph.FindBus(RuntimeBusIds.Unit690Right(0)));
    }

    [Fact]
    public void Two_split_transformers_create_four_ear_buses()
    {
        var simCfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs =
                    {
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig()
                    },
                    Bms =
                    {
                        new BmsDeviceConfig(), new BmsDeviceConfig(),
                        new BmsDeviceConfig(), new BmsDeviceConfig()
                    },
                    SplitTransformers =
                    {
                        new SplitTransformerRuntimeConfig
                        {
                            Present = true,
                            LeftEarPcsIndices = { 0 },
                            RightEarPcsIndices = { 1 }
                        },
                        new SplitTransformerRuntimeConfig
                        {
                            Present = true,
                            LeftEarPcsIndices = { 2 },
                            RightEarPcsIndices = { 3 }
                        }
                    }
                }
            }
        };
        var pcsCfg = new PcsPhysicalConfig();
        var pccCfg = new PccConfig();
        var network = NetworkTopologyBuilder.Build(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            pccCfg,
            pcsPerUnit: new[] { 4 });
        var graph = new RadialNetworkGraph(network, pccCfg, pcsCfg);

        Assert.Equal(2, network.DualEarTransformers[0].Count);
        Assert.Equal(2, network.SplitEarAssignments[0]!.Transformers.Count);
        Assert.Equal(RuntimeBusIds.Unit690Ear(0, 0, false), graph.Bus690ForPcsChannel(0).BusId);
        Assert.Equal(RuntimeBusIds.Unit690Ear(0, 0, true), graph.Bus690ForPcsChannel(1).BusId);
        Assert.Equal(RuntimeBusIds.Unit690Ear(0, 1, false), graph.Bus690ForPcsChannel(2).BusId);
        Assert.Equal(RuntimeBusIds.Unit690Ear(0, 1, true), graph.Bus690ForPcsChannel(3).BusId);
        Assert.NotNull(graph.FindBus(RuntimeBusIds.Unit690Ear(0, 1, false)));
        Assert.NotNull(graph.FindBus(RuntimeBusIds.Unit690Ear(0, 1, true)));
    }
}
