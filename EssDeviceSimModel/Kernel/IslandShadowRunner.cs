using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    /// <summary>
    /// 影子 690 V 岛：用快照装配内核并求解。提交状态只活在本对象上，不写生产母线。
    /// 并网时自备 a–d；同步参考电压时只更新 PCS 内电势，不把生产 P/Q 写入影子边界。
    /// </summary>
    public sealed class IslandShadowRunner
    {
        private readonly List<AverageConverterPort> _converters = new();

        public IslandShadowRunner(IslandShadowSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            Island = new IsolatedAcIsland(snapshot.BusId);
            foreach (var src in snapshot.Sources)
            {
                var conv = new AverageConverterPort(src.Id, snapshot.BusId, src.OutputImpedanceOhm, src.Efficiency)
                {
                    InternalVoltageV = src.InternalVoltageV,
                    DcVoltageV = src.DcVoltageV
                };
                _converters.Add(conv);
                Island.AddPcs(conv.Ac);
            }

            if (snapshot.ReferredGridEmf is { } emf &&
                snapshot.GridZ is { } gridZ &&
                snapshot.TheveninZ is { } zth)
            {
                Handshake = new SplitDomainHandshake(Island, emf, gridZ, zth);
            }
        }

        public IsolatedAcIsland Island { get; }
        public SplitDomainHandshake? Handshake { get; }

        /// <summary>本岛各构网源的平均变流器口（与快照源同序），交流侧即 <see cref="Island"/> 的 PCS 口。</summary>
        public IReadOnlyList<AverageConverterPort> Converters => _converters;

        /// <summary>只同步构网参考与已提交电池电压；不复制生产已提交的 P/Q 或 Voc。</summary>
        public void SyncFormingReferences(IslandShadowSnapshot snapshot)
        {
            if (snapshot.Sources.Count != Island.PcsPorts.Count)
                throw new ArgumentException("Shadow topology changed; rebuild the runner.", nameof(snapshot));

            for (int i = 0; i < Island.PcsPorts.Count; i++)
            {
                if (!string.Equals(Island.PcsPorts[i].Id, snapshot.Sources[i].Id, StringComparison.Ordinal))
                    throw new ArgumentException("Shadow source order/id mismatch.", nameof(snapshot));
                _converters[i].InternalVoltageV = snapshot.Sources[i].InternalVoltageV;
                _converters[i].DcVoltageV = snapshot.Sources[i].DcVoltageV;
            }
        }

        public IslandShadowTickResult Tick(DomainEventKind kind = DomainEventKind.None)
        {
            IslandSolveResult island;
            Complex? voc;
            if (Handshake != null)
            {
                if (!Handshake.TryTick(kind, out var hs) || hs.Lower is null)
                    throw new NetworkSolveException("Shadow domain handshake failed.");
                island = hs.Lower;
                voc = Handshake.CommittedVoc;
            }
            else
            {
                Island.ClearThevenin();
                island = Island.Solve();
                voc = null;
            }

            Complex vbus = island.Solution.Voltage(island.BusNodeId);
            return new IslandShadowTickResult(vbus, island, voc, SettleAllDc(island));
        }

        /// <summary>
        /// 按本拍<b>已提交</b>交流解结算各源直流。无直流侧（Vdc&lt;=0）的位置留 null。
        /// portIndex 即装配序，与 <see cref="IslandSolveResult.PcsCurrents"/> 同序。
        /// </summary>
        private ConverterEnergyResult?[] SettleAllDc(IslandSolveResult island)
        {
            var results = new ConverterEnergyResult?[_converters.Count];
            for (int i = 0; i < _converters.Count; i++)
            {
                if (_converters[i].DcVoltageV <= PhasorNetwork.MinSeriesImpedanceOhm)
                    continue;
                results[i] = _converters[i].SettleDc(island, i);
            }

            return results;
        }
    }

    public sealed class IslandShadowTickResult
    {
        internal IslandShadowTickResult(
            Complex busVoltage,
            IslandSolveResult island,
            Complex? voc,
            IReadOnlyList<ConverterEnergyResult?> dcSettlements)
        {
            BusVoltage = busVoltage;
            Island = island;
            Voc = voc;
            DcSettlements = dcSettlements;
        }

        public Complex BusVoltage { get; }
        public IslandSolveResult Island { get; }
        public Complex? Voc { get; }

        /// <summary>各构网源本拍直流结算，与快照源同序；无直流侧（Vdc&lt;=0）的位置为 null。</summary>
        public IReadOnlyList<ConverterEnergyResult?> DcSettlements { get; }
    }
}
