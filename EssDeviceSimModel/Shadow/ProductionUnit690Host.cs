using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Propagation;

namespace EssSimulator.EssDeviceSimModel.Shadow
{
    /// <summary>
    /// 生产接替的一条 690 V 母线：本拍用上层 PCC 开路 Voc，下层提交只留在本对象。
    /// 求解失败抛给 PlantEngine 整步冻结。
    /// </summary>
    public sealed class ProductionUnit690Host
    {
        private IslandShadowRunner? _runner;
        private string _key = "";
        private bool _hasCommitted;
        private readonly Dictionary<string, ConverterEnergyResult> _settledDc = new();
        private readonly Dictionary<string, (double ActivePowerW, double ReactivePowerVAr)> _solvedAc = new();

        public ProductionUnit690Host(ElectricalBusNode bus) =>
            Bus = bus ?? throw new ArgumentNullException(nameof(bus));

        public ElectricalBusNode Bus { get; }
        public SplitDomainHandshake? Handshake => _runner?.Handshake;

        /// <summary>本拍离开母线、经漏抗送往 35 kV 的共享励磁电流。</summary>
        public Complex SharedExcitationCurrent { get; private set; }

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
            bool empty = snap.Sources.Count == 0 && snap.Following.Count == 0 && snap.ReferredGridEmf is null;
            SharedExcitationCurrent = -snap.BusInjectedCurrent;
            if (empty)
            {
                SharedExcitationCurrent = Complex.Zero;
                _settledDc.Clear();
                _solvedAc.Clear();
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
                _runner.SetBusShunt(snap.BusShuntOhm);
                _runner.SetBusInjectedCurrent(snap.BusInjectedCurrent);
                if (_runner.Handshake != null && snap.ReferredGridEmf is { } emf)
                    _runner.Handshake.GridEmf = emf;
            }

            var tick = _runner.Tick();
            RecordSettledDc(snap, tick);
            RecordSolvedAc(snap, tick);
            double mag = tick.BusVoltage.Magnitude;
            Bus.SetVoltage(mag, mag > 1.0 ? sweep.SystemFrequencyHz : 0, sweep, notifyCouplers: false);
            if (_runner.Handshake != null)
                _hasCommitted = true;
        }

        private void RecordSettledDc(IslandShadowSnapshot snap, IslandShadowTickResult tick)
        {
            _settledDc.Clear();
            for (int i = 0; i < snap.Sources.Count && i < tick.DcSettlements.Count; i++)
            {
                if (tick.DcSettlements[i] is { } dc)
                    _settledDc[snap.Sources[i].Id] = dc;
            }

            for (int j = 0; j < snap.Following.Count && j < tick.FollowingDcSettlements.Count; j++)
            {
                if (tick.FollowingDcSettlements[j] is { } dc)
                    _settledDc[snap.Following[j].Id] = dc;
            }
        }

        /// <summary>
        /// 记录本拍内核解出的每台机交付母线的交流复功率 S = Vbus·conj(I_intoBus)。
        /// 构网取 <see cref="IslandSolveResult.PcsCurrents"/>（限流时即钳位电流），跟网取
        /// <see cref="IslandSolveResult.GflCurrents"/>，二者都以流向母线为正。不依赖直流侧是否存在。
        /// </summary>
        private void RecordSolvedAc(IslandShadowSnapshot snap, IslandShadowTickResult tick)
        {
            _solvedAc.Clear();
            Complex vbus = tick.BusVoltage;
            var island = tick.Island;
            for (int i = 0; i < snap.Sources.Count && i < island.PcsCurrents.Count; i++)
            {
                Complex s = vbus * Complex.Conjugate(island.PcsCurrents[i]);
                _solvedAc[snap.Sources[i].Id] = (s.Real, s.Imaginary);
            }

            for (int j = 0; j < snap.Following.Count && j < island.GflCurrents.Count; j++)
            {
                Complex s = vbus * Complex.Conjugate(island.GflCurrents[j]);
                _solvedAc[snap.Following[j].Id] = (s.Real, s.Imaginary);
            }
        }

        /// <summary>
        /// 只读：本拍按网络解出的交流功率结算的某源直流电流（流入设备为正，A）。
        /// 无直流侧或本拍未结算时返回 false。不得据此回写生产戴维南或注入。
        /// </summary>
        public bool TryGetSettledDcCurrent(string sourceId, out double idcIntoDeviceA)
        {
            if (sourceId != null && _settledDc.TryGetValue(sourceId, out var dc))
            {
                idcIntoDeviceA = dc.IdcIntoDeviceA;
                return true;
            }

            idcIntoDeviceA = 0;
            return false;
        }

        /// <summary>
        /// 只读：本拍内核解出的某源交付母线的交流功率（流出设备为正，W/VAr）。
        /// 构网源为限流钳位后的实际值，跟网源为限流后的实际注入，均可能不同于控制设定。
        /// 本源不在本岛或本拍未求解时返回 false。不得据此回写生产戴维南或注入。
        /// </summary>
        public bool TryGetSolvedAcPower(string sourceId, out double activePowerW, out double reactivePowerVAr)
        {
            if (sourceId != null && _solvedAc.TryGetValue(sourceId, out var ac))
            {
                activePowerW = ac.ActivePowerW;
                reactivePowerVAr = ac.ReactivePowerVAr;
                return true;
            }

            activePowerW = 0;
            reactivePowerVAr = 0;
            return false;
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
            var followingIds = new string[snap.Following.Count];
            for (int j = 0; j < snap.Following.Count; j++)
                followingIds[j] = snap.Following[j].Id;
            return string.Join(",", ids)
                + "#" + string.Join(",", followingIds)
                + "|" + (snap.ReferredGridEmf.HasValue ? "g" : "i");
        }
    }
}
