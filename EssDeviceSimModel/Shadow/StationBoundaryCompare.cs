using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Propagation;

namespace EssSimulator.EssDeviceSimModel.Shadow
{
    /// <summary>
    /// 从径向 35 kV 只读折低压戴维南。漏抗归上域；不写 Bus35。
    /// GridZ 目前用 10·Zth 作为网架压降占位，对照时记为已知偏差。
    /// </summary>
    public static class RadialTheveninPort
    {
        public const double StandInGridZMultiple = 10.0;

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
                double ratio = lvBus.NominalLineVoltageV / Math.Max(graph.Bus35.NominalLineVoltageV, 1.0);
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

    /// <summary>生产径向 35 kV 与核边界的只读对照，禁止写回 Bus35。</summary>
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

        /// <summary>核 Voc 相对径向折算开路电压的残差。首拍应为 0；之后是一拍 GridZ 压降，不是生产 35 kV 被核改写。</summary>
        public double VocResidualV => (KernelVoc - RadialReferredVoc).Magnitude;
    }
}
