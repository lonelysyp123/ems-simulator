using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    /// <summary>
    /// 影子 690 V 岛：用快照装配内核并求解。提交状态只活在本对象上，不写生产母线。
    /// 并网时自备 a–d；同步参考电压时只更新 PCS 内电势，不把生产 P/Q 写入影子边界。
    /// </summary>
    public sealed class IslandShadowRunner
    {
        public IslandShadowRunner(IslandShadowSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            Island = new IsolatedAcIsland(snapshot.BusId);
            foreach (var src in snapshot.Sources)
            {
                Island.AddPcs(new MinPcsPort(src.Id, snapshot.BusId, src.OutputImpedanceOhm)
                {
                    InternalVoltageV = src.InternalVoltageV
                });
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

        /// <summary>只同步构网参考；不复制生产已提交的 P/Q 或 Voc。</summary>
        public void SyncFormingReferences(IslandShadowSnapshot snapshot)
        {
            if (snapshot.Sources.Count != Island.PcsPorts.Count)
                throw new ArgumentException("Shadow topology changed; rebuild the runner.", nameof(snapshot));

            for (int i = 0; i < Island.PcsPorts.Count; i++)
            {
                if (!string.Equals(Island.PcsPorts[i].Id, snapshot.Sources[i].Id, StringComparison.Ordinal))
                    throw new ArgumentException("Shadow source order/id mismatch.", nameof(snapshot));
                Island.PcsPorts[i].InternalVoltageV = snapshot.Sources[i].InternalVoltageV;
            }
        }

        public IslandShadowTickResult Tick(DomainEventKind kind = DomainEventKind.None)
        {
            if (Handshake != null)
            {
                if (!Handshake.TryTick(kind, out var hs) || hs.Lower is null)
                    throw new NetworkSolveException("Shadow domain handshake failed.");
                return FromIsland(hs.Lower, Handshake.CommittedVoc);
            }

            Island.ClearThevenin();
            return FromIsland(Island.Solve(), voc: null);
        }

        private static IslandShadowTickResult FromIsland(IslandSolveResult island, Complex? voc)
        {
            Complex vbus = island.Solution.Voltage(island.BusNodeId);
            return new IslandShadowTickResult(vbus, island, voc);
        }
    }

    public sealed class IslandShadowTickResult
    {
        internal IslandShadowTickResult(Complex busVoltage, IslandSolveResult island, Complex? voc)
        {
            BusVoltage = busVoltage;
            Island = island;
            Voc = voc;
        }

        public Complex BusVoltage { get; }
        public IslandSolveResult Island { get; }
        public Complex? Voc { get; }
    }
}
