using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Propagation;

namespace EssSimulator.EssDeviceSimModel.Shadow
{
    /// <summary>
    /// 生产接替的一个 690 V 岛：本拍用上层径向已解出的 Voc，下层提交只留在本对象。
    /// 求解失败抛给 PlantEngine 整步冻结。
    /// </summary>
    public sealed class ProductionUnit690Host
    {
        private IslandShadowRunner? _runner;
        private string _key = "";
        private bool _hasCommitted;

        public ProductionUnit690Host(int unitIndex) => UnitIndex = unitIndex;

        public int UnitIndex { get; }

        public static ProductionUnit690Host? TryAttachUnit0(RadialNetworkGraph graph)
        {
            if (graph.UnitBuses690.Count == 0)
                return null;
            if (graph.Network.DualEarsOfUnit(0).Count > 0)
                return null;

            graph.UnitBuses690[0].KernelOwned = true;
            return new ProductionUnit690Host(0);
        }

        public void ApplyAfterUpper(RadialNetworkGraph graph, PropagationSweepContext sweep)
        {
            var bus = graph.UnitBuses690[UnitIndex];
            var snap = RadialIslandShadowAdapter.CaptureUnit690(graph, UnitIndex, sweep);
            bool empty = snap.Sources.Count == 0 && snap.ReferredGridEmf is null;
            if (empty)
            {
                bus.SetVoltage(0, 0, sweep, notifyCouplers: false);
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
            bus.SetVoltage(mag, mag > 1.0 ? sweep.SystemFrequencyHz : 0, sweep, notifyCouplers: false);
            if (_runner.Handshake != null)
                _hasCommitted = true;
        }

        public bool TryGetCommittedInjectionKw(out double pKw, out double qKvar)
        {
            pKw = 0;
            qKvar = 0;
            if (!_hasCommitted || _runner?.Handshake == null)
                return false;

            Complex s = _runner.Handshake.CommittedInjectionVa;
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
