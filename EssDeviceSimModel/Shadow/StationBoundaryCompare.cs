using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;

namespace EssSimulator.EssDeviceSimModel.Shadow
{
    /// <summary>
    /// 上域开路电压从 PCC 折算；漏抗归上域。GridZ 目前用 10·Zth 作为网架压降占位。
    /// </summary>
    public static class RadialTheveninPort
    {
        public const double StandInGridZMultiple = 10.0;

        public static double LvPerHv(ElectricalBusNode lvBus, ElectricalBusNode hvBus) =>
            lvBus.NominalLineVoltageV / Math.Max(hvBus.NominalLineVoltageV, 1.0);

        public static double ReferLvToHv(double lvVoltageV, ElectricalBusNode lvBus, ElectricalBusNode hvBus)
        {
            double ratio = LvPerHv(lvBus, hvBus);
            return ratio > 1e-12 ? lvVoltageV / ratio : 0;
        }

        public static RadialThevenin FromLvBus(RadialNetworkGraph graph, ElectricalBusNode lvBus)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(lvBus);

            Complex zth = RadialIslandShadowAdapter.LeakageOhmLv(
                RadialIslandShadowAdapter.LeakageConfigFor(graph, lvBus));
            double v35 = graph.Bus35.LineVoltageV;
            Complex voc = Complex.Zero;
            if (v35 > 1.0)
            {
                double ratio = LvPerHv(lvBus, graph.Bus35);
                voc = v35 * ratio;
            }

            return new RadialThevenin(v35, voc, zth, zth * StandInGridZMultiple);
        }
    }

    public readonly struct RadialThevenin
    {
        public RadialThevenin(double v35, Complex referredVoc, Complex zth, Complex gridZ)
        {
            V35 = v35;
            ReferredVoc = referredVoc;
            Zth = zth;
            GridZ = gridZ;
        }

        public double V35 { get; }
        public Complex ReferredVoc { get; }
        public Complex Zth { get; }
        public Complex GridZ { get; }
    }

    /// <summary>把核边界写回生产 35 kV：并网用 Voc，离网用导纳加权的 690 反送。影子交接不得调用。</summary>
    public static class StationBoundaryApply
    {
        public static void Attach(RadialNetworkGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            graph.Bus35.KernelOwned = true;
        }

        public static void CommitBus35(
            RadialNetworkGraph graph,
            IReadOnlyList<ProductionUnit690Host> hosts,
            PropagationSweepContext sweep)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(hosts);
            ArgumentNullException.ThrowIfNull(sweep);

            ElectricalBusNode lvRef = hosts.Count > 0 ? hosts[0].Bus : graph.Bus35;
            double ratio = RadialTheveninPort.LvPerHv(lvRef, graph.Bus35);
            double v35 = sweep.MainBreakerClosed
                ? GridTiedBus35(graph, hosts, sweep, ratio)
                : IslandedBus35(graph, hosts, ratio);

            double f = v35 > 1.0 ? sweep.SystemFrequencyHz : 0;
            graph.Bus35.SetVoltage(v35, f, sweep);
            graph.Network.StationBus35LineVoltageV = v35;
        }

        public static bool IsUnitHvClosed(RadialNetworkGraph graph, ElectricalBusNode lvBus)
        {
            var net = graph.Network;
            for (int u = 0; u < net.UnitBreakers.Count; u++)
            {
                if (!BusBelongsToUnit(graph, lvBus, u))
                    continue;
                var brk = net.UnitBreakers[u];
                return brk.SwitchState.IsClosed && !brk.SwitchState.IsTripped;
            }

            return false;
        }

        private static double GridTiedBus35(
            RadialNetworkGraph graph,
            IReadOnlyList<ProductionUnit690Host> hosts,
            PropagationSweepContext sweep,
            double ratio)
        {
            SplitDomainHandshake? hs0 = null;
            Complex iSum = Complex.Zero;
            foreach (var host in hosts)
            {
                if (!IsUnitHvClosed(graph, host.Bus) || host.Handshake == null)
                    continue;
                hs0 ??= host.Handshake;
                iSum += host.Handshake.CommittedTheveninCurrent;
            }

            if (hs0 == null || ratio <= 1e-12)
                return sweep.OpenCircuitBus35LineVoltageV;

            return (hs0.GridEmf - iSum * hs0.GridZ).Magnitude / ratio;
        }

        private static double IslandedBus35(
            RadialNetworkGraph graph,
            IReadOnlyList<ProductionUnit690Host> hosts,
            double ratio)
        {
            Complex ySum = Complex.Zero;
            Complex iN = Complex.Zero;
            foreach (var host in hosts)
            {
                if (!IsUnitHvClosed(graph, host.Bus) || host.Bus.LineVoltageV <= 1.0)
                    continue;
                Complex zth = RadialIslandShadowAdapter.LeakageOhmLv(
                    RadialIslandShadowAdapter.LeakageConfigFor(graph, host.Bus));
                Complex y = 1.0 / zth;
                ySum += y;
                iN += host.Bus.LineVoltageV * y;
            }

            if (ySum.Magnitude < 1e-18 || ratio <= 1e-12)
                return 0;

            return (iN / ySum).Magnitude / ratio;
        }

        private static bool BusBelongsToUnit(RadialNetworkGraph graph, ElectricalBusNode lvBus, int unit)
        {
            if (unit < graph.UnitBuses690.Count && ReferenceEquals(graph.UnitBuses690[unit], lvBus))
                return true;

            var duals = graph.Network.DualEarsOfUnit(unit);
            for (int t = 0; t < duals.Count; t++)
            {
                var left = graph.FindBus(RuntimeBusIds.Unit690Ear(unit, t, right: false));
                var right = graph.FindBus(RuntimeBusIds.Unit690Ear(unit, t, right: true));
                if (ReferenceEquals(left, lvBus) || ReferenceEquals(right, lvBus))
                    return true;
            }

            return false;
        }
    }

    /// <summary>生产 35 kV 与核边界对照。</summary>
    public static class StationBoundaryCompare
    {
        public static StationBoundarySample Capture(RadialNetworkGraph graph, ProductionUnit690Host host)
        {
            var th = RadialTheveninPort.FromLvBus(graph, host.Bus);
            host.TryGetCommittedInjectionKw(out double pKw, out double qKvar);
            var hs = host.Handshake;
            return new StationBoundarySample(
                host.Bus.BusId,
                th.V35,
                host.Bus.LineVoltageV,
                th.ReferredVoc,
                hs?.CommittedVoc ?? Complex.Zero,
                th.Zth,
                hs?.TheveninZ ?? th.Zth,
                pKw,
                qKvar,
                hs?.CommittedTheveninCurrent ?? Complex.Zero);
        }
    }

    public sealed class StationBoundarySample
    {
        public StationBoundarySample(
            string busId,
            double radialV35,
            double kernelBus690,
            Complex radialReferredVoc,
            Complex kernelVoc,
            Complex radialZth,
            Complex kernelZth,
            double kernelInjectionKw,
            double kernelInjectionKvar,
            Complex kernelTheveninCurrent)
        {
            BusId = busId;
            RadialV35 = radialV35;
            KernelBus690 = kernelBus690;
            RadialReferredVoc = radialReferredVoc;
            KernelVoc = kernelVoc;
            RadialZth = radialZth;
            KernelZth = kernelZth;
            KernelInjectionKw = kernelInjectionKw;
            KernelInjectionKvar = kernelInjectionKvar;
            KernelTheveninCurrent = kernelTheveninCurrent;
        }

        public string BusId { get; }
        public double RadialV35 { get; }
        public double KernelBus690 { get; }
        public Complex RadialReferredVoc { get; }
        public Complex KernelVoc { get; }
        public Complex RadialZth { get; }
        public Complex KernelZth { get; }
        public double KernelInjectionKw { get; }
        public double KernelInjectionKvar { get; }
        public Complex KernelTheveninCurrent { get; }

        /// <summary>生产 35 kV 折回低压后相对核 Voc 的残差；接替后应接近 0。</summary>
        public double VocResidualV => (KernelVoc - RadialReferredVoc).Magnitude;
    }
}
