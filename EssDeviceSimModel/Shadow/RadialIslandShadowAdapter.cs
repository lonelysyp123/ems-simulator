using System.Numerics;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using EssSimulator.EssDeviceSimModel.Solver;

namespace EssSimulator.EssDeviceSimModel.Shadow
{
    /// <summary>
    /// 从径向图只读采集 690 V 岛快照。不调用 SetVoltage，不把影子 P/Q 写回生产 Coupler。
    /// </summary>
    public static class RadialIslandShadowAdapter
    {
        public static IslandShadowSnapshot CaptureUnit690(
            RadialNetworkGraph graph,
            int unitIndex,
            PropagationSweepContext sweep,
            Complex? pcsOutputZ = null)
        {
            ArgumentNullException.ThrowIfNull(graph);
            if (unitIndex < 0 || unitIndex >= graph.UnitBuses690.Count)
                throw new ArgumentOutOfRangeException(nameof(unitIndex));
            return CaptureBus(graph, graph.UnitBuses690[unitIndex], sweep, pcsOutputZ);
        }

        public static IslandShadowSnapshot CaptureBus(
            RadialNetworkGraph graph,
            ElectricalBusNode bus,
            PropagationSweepContext sweep,
            Complex? pcsOutputZ = null)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(bus);
            Complex zout = pcsOutputZ ?? MinPcsPort.DefaultOutputImpedanceOhm;
            var sources = new List<FormingSourceSample>();
            foreach (var src in bus.VoltageSources)
            {
                if (!src.IsInjecting(sweep.DeviceContext))
                    continue;
                var (v, _) = src.GetInjection(sweep.DeviceContext);
                if (v <= 1.0)
                    continue;
                sources.Add(new FormingSourceSample(src.SourceId, v, zout));
            }

            Complex zth = LeakageOhmLv(LeakageConfigFor(graph, bus));
            Complex? gridEmf = null;
            Complex? gridZ = null;
            bool gridTied = sweep.MainBreakerClosed && sweep.OpenCircuitBus35LineVoltageV > 1.0;
            bool islandPassive = !sweep.MainBreakerClosed
                && graph.Bus35.LineVoltageV > 1.0
                && sources.Count == 0;
            if (gridTied || islandPassive)
            {
                double v35oc = gridTied ? sweep.OpenCircuitBus35LineVoltageV : graph.Bus35.LineVoltageV;
                double ratio = bus.NominalLineVoltageV / Math.Max(graph.Bus35.NominalLineVoltageV, 1.0);
                gridEmf = v35oc * ratio;
                gridZ = zth * RadialTheveninPort.StandInGridZMultiple;
            }

            return new IslandShadowSnapshot(bus.BusId, sources, gridEmf, gridZ, zth);
        }

        public static TransformerDeviceConfig? LeakageConfigFor(RadialNetworkGraph graph, ElectricalBusNode bus)
        {
            for (int u = 0; u < graph.UnitBuses690.Count; u++)
            {
                var duals = graph.Network.DualEarsOfUnit(u);
                if (duals.Count > 0)
                {
                    for (int t = 0; t < duals.Count; t++)
                    {
                        var left = graph.FindBus(RuntimeBusIds.Unit690Ear(u, t, right: false));
                        var right = graph.FindBus(RuntimeBusIds.Unit690Ear(u, t, right: true));
                        if (left == bus || right == bus)
                            return EarLeakageConfig(duals[t].Config);
                    }

                    continue;
                }

                if (graph.UnitBuses690[u] == bus && u < graph.Network.UnitTransformers.Count)
                    return graph.Network.UnitTransformers[u].Config;
            }

            return null;
        }

        /// <summary>单元变漏抗归上域戴维南：Z = zpu · V² / S，按纯电抗。</summary>
        public static Complex LeakageOhmLv(TransformerDeviceConfig? cfg)
        {
            double v = cfg?.SecondaryNominalLineVoltageV > 1 ? cfg.SecondaryNominalLineVoltageV : 690;
            double s = (cfg?.RatedPowerKva > 1 ? cfg.RatedPowerKva : 2500) * 1000.0;
            double zPu = (cfg?.ImpedancePercent > 0.1 ? cfg.ImpedancePercent : 4) / 100.0;
            double x = zPu * v * v / s;
            return new Complex(0, Math.Max(x, PhasorNetwork.MinSeriesImpedanceOhm));
        }

        private static TransformerDeviceConfig EarLeakageConfig(DualEarTransformerConfig cfg)
        {
            var through = cfg.ToThroughConfig();
            through.ImpedancePercent = cfg.SplitImpedancePercent > 0.1 ? cfg.SplitImpedancePercent : 8;
            through.RatedPowerKva = Math.Max(cfg.RatedPowerKva * Math.Clamp(cfg.SplitRatio, 0.1, 0.9), 1);
            return through;
        }
    }
}
