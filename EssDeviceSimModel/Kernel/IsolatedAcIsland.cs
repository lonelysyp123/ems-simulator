using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    /// <summary>独立测试宿主：一个交流母线 + 若干最小 PCS 端口 + 可选负荷。不接入 PlantEngine。</summary>
    public sealed class IsolatedAcIsland
    {
        private readonly List<MinPcsPort> _pcs = new();
        private Complex? _loadZ;

        public IsolatedAcIsland(string busNodeId, string groundNodeId = "gnd")
        {
            BusNodeId = busNodeId;
            GroundNodeId = groundNodeId;
        }

        public string BusNodeId { get; }
        public string GroundNodeId { get; }
        public IReadOnlyList<MinPcsPort> PcsPorts => _pcs;

        public void AddPcs(MinPcsPort port)
        {
            if (port.TerminalNodeId != BusNodeId)
                throw new ArgumentException("PCS terminal must attach to the island bus.", nameof(port));
            _pcs.Add(port);
        }

        public void SetLoadImpedance(Complex zLoadOhm) => _loadZ = zLoadOhm;

        public IslandSolveResult Solve()
        {
            var net = new PhasorNetwork();
            net.SetGround(GroundNodeId);
            net.AddNode(BusNodeId);

            foreach (var pcs in _pcs)
                pcs.Stamp(net);

            if (_loadZ.HasValue)
                net.AddImpedance(BusNodeId, GroundNodeId, _loadZ.Value);

            var sol = net.Solve();
            var currents = new Complex[_pcs.Count];
            Complex sumI = Complex.Zero;
            for (int i = 0; i < _pcs.Count; i++)
            {
                currents[i] = _pcs[i].TerminalCurrent(sol);
                sumI += currents[i];
            }

            Complex loadI = Complex.Zero;
            if (_loadZ.HasValue)
                loadI = net.BranchCurrent(sol, BusNodeId, GroundNodeId, _loadZ.Value);

            return new IslandSolveResult(sol, currents, sumI, loadI);
        }
    }

    public sealed class IslandSolveResult
    {
        internal IslandSolveResult(
            PhasorSolution solution,
            Complex[] pcsCurrents,
            Complex pcsCurrentSum,
            Complex loadCurrent)
        {
            Solution = solution;
            PcsCurrents = pcsCurrents;
            PcsCurrentSum = pcsCurrentSum;
            LoadCurrent = loadCurrent;
        }

        public PhasorSolution Solution { get; }
        public IReadOnlyList<Complex> PcsCurrents { get; }
        public Complex PcsCurrentSum { get; }
        public Complex LoadCurrent { get; }
    }
}
