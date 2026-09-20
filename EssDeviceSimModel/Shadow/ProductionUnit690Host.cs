using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Propagation;

namespace EssSimulator.EssDeviceSimModel.Shadow
{
    /// <summary>
    /// 生产接替的一条 690 V 母线：本拍用上层径向已解出的 Voc，下层提交只留在本对象。
    /// 求解失败抛给 PlantEngine 整步冻结。
    /// </summary>
    public sealed class ProductionUnit690Host
    {
        private IslandShadowRunner? _runner;
        private string _key = "";
        private bool _hasCommitted;

        public ProductionUnit690Host(ElectricalBusNode bus) =>
            Bus = bus ?? throw new ArgumentNullException(nameof(bus));

        public ElectricalBusNode Bus { get; }

        public static IReadOnlyList<ProductionUnit690Host> AttachAll(RadialNetworkGraph graph)
        {
            var hosts = new List<ProductionUnit690Host>();
            foreach (var bus in graph.AllUnit690Buses)
            {
                bus.KernelOwned = true;
                hosts.Add(new ProductionUnit690Host(bus));
            }

            return hosts;
        }

        public void ApplyAfterUpper(RadialNetworkGraph graph, PropagationSweepContext sweep)
        {
            var snap = RadialIslandShadowAdapter.CaptureBus(graph, Bus, sweep);
            bool empty = snap.Sources.Count == 0 && snap.ReferredGridEmf is null;
            if (empty)
            {
                Bus.SetVoltage(0, 0, sweep, notifyCouplers: false);
                return;
            }

            string key = SourceKey(snap);
            if (_runner == null || _key != key)
            {
                _runner = new IslandShadowRunner(snap);
                _key = key;
                _hasCommitted = false;
            }
            else
            {
                _runner.SyncFormingReferences(snap);
                if (_runner.Handshake != null && snap.ReferredGridEmf is { } emf)
                    _runner.Handshake.GridEmf = emf;
            }

            var tick = _runner.Tick();
            double mag = tick.BusVoltage.Magnitude;
            Bus.SetVoltage(mag, mag > 1.0 ? sweep.SystemFrequencyHz : 0, sweep, notifyCouplers: false);
            if (_runner.Handshake != null)
                _hasCommitted = true;
        }

        public bool TryGetCommittedInjectionKw(out double pKw, out double qKvar)
        {
            pKw = 0;
            qKvar = 0;
            if (!_hasCommitted || _runner?.Handshake == null)
                return false;

            var s = _runner.Handshake.CommittedInjectionVa;
            pKw = s.Real / 1000.0;
            qKvar = s.Imaginary / 1000.0;
            return true;
        }

        private static string SourceKey(IslandShadowSnapshot snap)
        {
            var ids = new string[snap.Sources.Count];
            for (int i = 0; i < snap.Sources.Count; i++)
                ids[i] = snap.Sources[i].Id;
            return string.Join(",", ids) + "|" + (snap.ReferredGridEmf.HasValue ? "g" : "i");
        }
    }
}
