using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using log4net;

namespace EssSimulator.EssDeviceSimModel.Solver
{
    /// <summary>
    /// 按组态解析的运行时母线 Id 采集电表一次侧电气量。
    /// 生产路径必须传入径向图；<paramref name="graph"/> 为 null 仅供 <c>NetworkSolver</c> 单测夹具走端口采样。
    /// </summary>
    public static class MeterBusSampler
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(MeterBusSampler));

        public static AcInternalQuantities Sample(
            ElectricalNetwork network,
            RadialNetworkGraph? graph,
            string? sourceBusId,
            double systemFrequencyHz)
        {
            string requested = sourceBusId;
            string busId = RuntimeBusIds.Canonicalize(
                string.IsNullOrWhiteSpace(sourceBusId)
                    ? RuntimeBusIds.AfterMainBreaker
                    : sourceBusId);
            bool mainClosed = NetworkControlBridge.IsBreakerClosed(network.MainBreaker);

            if (graph != null)
            {
                var node = graph.FindBus(busId);
                if (node != null)
                    return FromBusNode(network, busId, node, mainClosed, systemFrequencyHz);

                _log.Error($"[MeterBusSampler] 未知母线 Id '{requested}'，电表采样返回零（不回退到其它母线）");
                return Quantities(0, 0, 0, 0);
            }

            return FromNetworkPorts(network, busId, mainClosed, systemFrequencyHz);
        }

        private static AcInternalQuantities FromBusNode(
            ElectricalNetwork network,
            string busId,
            ElectricalBusNode node,
            bool mainClosed,
            double systemFrequencyHz)
        {
            double v = node.LineVoltageV;
            double i = node.TotalLineCurrentA;
            double phi = node.TotalPhaseAngleDeg;
            if (i < 1e-9)
            {
                var series = SeriesCurrent(network, busId, mainClosed);
                i = series.LineCurrentA;
                phi = series.PhaseAngleDeg;
            }

            return Quantities(v, i, phi, v > 1.0 ? systemFrequencyHz : 0);
        }

        private static AcInternalQuantities FromNetworkPorts(
            ElectricalNetwork network,
            string busId,
            bool mainClosed,
            double systemFrequencyHz)
        {
            if (busId == RuntimeBusIds.Grid)
            {
                var grid = network.Grid.Port.Output.Ac?.Internal ?? new AcInternalQuantities();
                var cur = SeriesCurrent(network, busId, mainClosed);
                return Quantities(grid.LineVoltageV, cur.LineCurrentA, cur.PhaseAngleDeg,
                    grid.LineVoltageV > 1.0 ? systemFrequencyHz : 0);
            }

            if (busId == RuntimeBusIds.AfterMainBreaker)
            {
                if (!mainClosed)
                    return Quantities(0, 0, 0, 0);
                var raw = network.HasMainTransformer
                    ? network.MainTransformer.Primary.Output.Ac?.Internal
                    : network.MainBreaker.Secondary.Output.Ac?.Internal;
                raw ??= new AcInternalQuantities();
                return Quantities(raw.LineVoltageV, raw.LineCurrentA, raw.PhaseAngleDeg,
                    raw.LineVoltageV > 1.0 ? systemFrequencyHz : 0);
            }

            if (busId == RuntimeBusIds.Station35)
            {
                double v = network.StationBus35LineVoltageV;
                var cur = network.MainTransformer.Secondary.Output.Ac?.Internal
                    ?? network.Load.Port.Output.Ac?.Internal
                    ?? new AcInternalQuantities();
                return Quantities(v, cur.LineCurrentA, cur.PhaseAngleDeg, v > 1.0 ? systemFrequencyHz : 0);
            }

            var unitQty = Unit690Port(network, busId);
            if (unitQty != null)
            {
                var raw = unitQty;
                return Quantities(raw.LineVoltageV, raw.LineCurrentA, raw.PhaseAngleDeg,
                    raw.LineVoltageV > 1.0 ? systemFrequencyHz : 0);
            }

            _log.Error($"[MeterBusSampler] 未知母线 Id '{busId}'（无径向图），电表采样返回零");
            return Quantities(0, 0, 0, 0);
        }

        private static AcInternalQuantities SeriesCurrent(
            ElectricalNetwork network,
            string busId,
            bool mainClosed)
        {
            if (busId == RuntimeBusIds.Grid)
            {
                var pri = network.MainBreaker.Primary.Output.Ac?.Internal;
                return pri ?? new AcInternalQuantities();
            }

            if (busId == RuntimeBusIds.AfterMainBreaker)
            {
                if (!mainClosed)
                    return new AcInternalQuantities();
                return network.HasMainTransformer
                    ? network.MainTransformer.Primary.Output.Ac?.Internal ?? new AcInternalQuantities()
                    : network.MainBreaker.Secondary.Output.Ac?.Internal ?? new AcInternalQuantities();
            }

            if (busId == RuntimeBusIds.Station35)
                return network.MainTransformer.Secondary.Output.Ac?.Internal
                    ?? network.Load.Port.Output.Ac?.Internal
                    ?? new AcInternalQuantities();

            return Unit690Port(network, busId) ?? new AcInternalQuantities();
        }

        private static AcInternalQuantities? Unit690Port(ElectricalNetwork network, string busId)
        {
            if (RuntimeBusIds.TryParseUnit690Ear(busId, out int earUnit, out int xfmrIndex, out bool rightEar))
            {
                var duals = network.DualEarsOfUnit(earUnit);
                if (xfmrIndex >= 0 && xfmrIndex < duals.Count)
                {
                    var dual = duals[xfmrIndex];
                    return (rightEar ? dual.SecondaryRight : dual.SecondaryLeft).Output.Ac?.Internal
                        ?? new AcInternalQuantities();
                }
            }

            if (RuntimeBusIds.TryParseUnit690(busId, out int unit)
                && unit >= 0 && unit < network.UnitTransformers.Count)
                return network.UnitTransformers[unit].Secondary.Output.Ac?.Internal
                    ?? new AcInternalQuantities();

            return null;
        }

        private static AcInternalQuantities Quantities(
            double lineVoltageV,
            double lineCurrentA,
            double phaseAngleDeg,
            double frequencyHz) =>
            new()
            {
                Connection = ThreePhaseConnection.Star,
                LineVoltageV = lineVoltageV,
                LineCurrentA = lineCurrentA,
                PhaseAngleDeg = phaseAngleDeg,
                FrequencyHz = frequencyHz
            };
    }
}
