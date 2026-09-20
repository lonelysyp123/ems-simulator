using System.Numerics;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;

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

            ElectricalBusNode bus = graph.UnitBuses690[unitIndex];
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

            TransformerDeviceConfig? xfCfg = unitIndex < graph.Network.UnitTransformers.Count
                ? graph.Network.UnitTransformers[unitIndex].Config
                : null;
            Complex zth = LeakageOhmLv(xfCfg);
            Complex? gridEmf = null;
            Complex? gridZ = null;
            if (graph.Bus35.LineVoltageV > 1.0)
            {
                double ratio = bus.NominalLineVoltageV / Math.Max(graph.Bus35.NominalLineVoltageV, 1.0);
                gridEmf = graph.Bus35.LineVoltageV * ratio;
                gridZ = zth * 10.0;
            }

            return new IslandShadowSnapshot(bus.BusId, sources, gridEmf, gridZ, zth);
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
    }
}
