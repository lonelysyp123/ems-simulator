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
            var sources = new List<FormingSourceSample>();
            var following = new List<FollowingSourceSample>();
            foreach (var src in bus.VoltageSources)
            {
                if (src.IsInjecting(sweep.DeviceContext))
                {
                    var (v, _) = src.GetInjection(sweep.DeviceContext);
                    if (v <= 1.0)
                        continue;
                    Complex zout = pcsOutputZ ?? src.OutputImpedanceOhm;
                    sources.Add(new FormingSourceSample(src.SourceId, v, zout, src.DcVoltageV, src.Efficiency));
                }
                else if (src.IsGridFollowing(sweep.DeviceContext))
                {
                    var (pW, qVAr) = src.GetFollowingInjection(sweep.DeviceContext);
                    following.Add(new FollowingSourceSample(
                        src.SourceId, pW, qVAr, src.DcVoltageV, src.Efficiency, src.FollowingCurrentLimitA));
                }
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

            // 跟网源只在母线本拍有定压参考（上域戴维南或构网源）时贴入，否则纯 PQ 岛无解（Y 奇异）。
            IReadOnlyList<FollowingSourceSample> followingOut =
                gridEmf.HasValue || sources.Count > 0
                    ? following
                    : Array.Empty<FollowingSourceSample>();

            Complex? shunt = null;
            Complex injected = Complex.Zero;
            if (gridEmf is null && sources.Count > 0)
            {
                shunt = ShuntOhmFromAdmittance(MagnetizingAdmittance(LeakageConfigFor(graph, bus)));
                injected = -SharedExcitationCurrentLeavingBus(graph, bus, sweep);
            }

            return new IslandShadowSnapshot(
                bus.BusId, sources, gridEmf, gridZ, zth, followingOut, shunt, injected);
        }

        /// <summary>
        /// 离开本岛、经单元变漏抗送往 35 kV 的共享励磁电流。
        /// 含主变和尚未构网的邻居变，按漏抗导纳分配。本单元变自己的空载不在这里，它留在对地导纳上。
        /// </summary>
        private static Complex SharedExcitationCurrentLeavingBus(
            RadialNetworkGraph graph,
            ElectricalBusNode bus,
            PropagationSweepContext sweep)
        {
            if (sweep.MainBreakerClosed || graph.Bus35.LineVoltageV <= 1.0)
                return Complex.Zero;
            if (!StationBoundaryApply.IsUnitHvClosed(graph, bus) || !HasFormingSource(bus, sweep))
                return Complex.Zero;

            double vLv = graph.Bus35.LineVoltageV
                * bus.NominalLineVoltageV
                / Math.Max(graph.Bus35.NominalLineVoltageV, 1.0);
            Complex iTotal = Complex.Zero;
            if (graph.Network.HasMainTransformer)
            {
                iTotal += MagnetizingAdmittanceAt(
                    graph.Network.MainTransformer.Config,
                    bus.NominalLineVoltageV) * vLv;
            }

            Complex wSum = Complex.Zero;
            Complex wSelf = Complex.Zero;
            foreach (var other in graph.AllUnit690Buses)
            {
                if (!StationBoundaryApply.IsUnitHvClosed(graph, other))
                    continue;
                if (HasFormingSource(other, sweep))
                {
                    Complex w = LeakageAdmittance(graph, other);
                    wSum += w;
                    if (ReferenceEquals(other, bus))
                        wSelf = w;
                    continue;
                }

                double vK = other.LineVoltageV > 1.0 ? other.LineVoltageV : vLv;
                iTotal += MagnetizingAdmittance(LeakageConfigFor(graph, other)) * vK;
            }

            if (wSum.Magnitude < 1e-18)
                return Complex.Zero;
            return iTotal * (wSelf / wSum);
        }

        private static Complex LeakageAdmittance(RadialNetworkGraph graph, ElectricalBusNode bus) =>
            Complex.One / LeakageOhmLv(LeakageConfigFor(graph, bus));

        private static bool HasFormingSource(ElectricalBusNode bus, PropagationSweepContext sweep)
        {
            foreach (var src in bus.VoltageSources)
            {
                if (!src.IsInjecting(sweep.DeviceContext))
                    continue;
                var (v, _) = src.GetInjection(sweep.DeviceContext);
                if (v > 1.0)
                    return true;
            }

            return false;
        }

        private static Complex? ShuntOhmFromAdmittance(Complex y) =>
            y.Magnitude < 1e-12 ? null : Complex.One / y;

        /// <summary>
        /// 单元变空载支路折到低压侧：Q = (I0%/100)·S，P = 铁损，均在额定电压下标定。
        /// 导纳恒定，运行电压偏离额定时功率按 V² 变化。无空载数据时返回 null。
        /// </summary>
        public static Complex? MagnetizingShuntOhm(TransformerDeviceConfig? cfg)
        {
            if (cfg == null)
                return null;

            double v = cfg.SecondaryNominalLineVoltageV > 1 ? cfg.SecondaryNominalLineVoltageV : 690;
            double sVa = Math.Max(cfg.RatedPowerKva, 1) * 1000.0;
            double qVar = Math.Max(0, cfg.NoLoadCurrentPercent) / 100.0 * sVa;
            double pW = Math.Max(0, cfg.NoLoadLossKw) * 1000.0;
            if (qVar < 1 && pW < 1)
                return null;

            double g = pW / (v * v);
            double b = qVar / (v * v);
            Complex y = new Complex(g, -b);
            if (y.Magnitude < 1e-12)
                return null;
            return Complex.One / y;
        }

        /// <summary>空载支路导纳，标定在该变二次额定电压上。无空载数据时为 0。</summary>
        public static Complex MagnetizingAdmittance(TransformerDeviceConfig? cfg) =>
            MagnetizingAdmittanceAt(cfg, cfg?.SecondaryNominalLineVoltageV ?? 0);

        /// <summary>
        /// 把空载功率折到 <paramref name="voltageBaseV"/> 上的对地导纳。
        /// 主变二次是 35 kV，贴到 690 V 岛时用岛的额定电压，使母线在额定值时吸收铭牌励磁。
        /// </summary>
        public static Complex MagnetizingAdmittanceAt(TransformerDeviceConfig? cfg, double voltageBaseV)
        {
            if (cfg == null || voltageBaseV <= 1)
                return Complex.Zero;

            double sVa = Math.Max(cfg.RatedPowerKva, 1) * 1000.0;
            double qVar = Math.Max(0, cfg.NoLoadCurrentPercent) / 100.0 * sVa;
            double pW = Math.Max(0, cfg.NoLoadLossKw) * 1000.0;
            if (qVar < 1 && pW < 1)
                return Complex.Zero;

            double v2 = voltageBaseV * voltageBaseV;
            return new Complex(pW / v2, -(qVar / v2));
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
            double split = Math.Clamp(cfg.SplitRatio, 0.1, 0.9);
            through.ImpedancePercent = cfg.SplitImpedancePercent > 0.1 ? cfg.SplitImpedancePercent : 8;
            through.RatedPowerKva = Math.Max(cfg.RatedPowerKva * split, 1);
            through.NoLoadLossKw = Math.Max(0, through.NoLoadLossKw) * split;
            return through;
        }
    }
}
