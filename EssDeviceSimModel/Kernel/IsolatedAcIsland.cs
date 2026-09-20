using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    /// <summary>独立测试宿主：一个交流母线 + 若干最小 PCS 端口 + 可选负荷。不接入 PlantEngine。</summary>
    public sealed class IsolatedAcIsland
    {
        private readonly List<MinPcsPort> _pcs = new();
        private Complex? _loadZ;
        private Complex? _vth;
        private Complex? _zth;

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

        /// <summary>并网岛：上域戴维南贴在母线。角度锁在 Vth，不再另取自由 datum。</summary>
        public void SetThevenin(Complex vth, Complex zth)
        {
            if (zth.Magnitude < PhasorNetwork.MinSeriesImpedanceOhm)
                throw new NetworkSolveException("Thevenin Zth must be finite.");
            _vth = vth;
            _zth = zth;
        }

        public void ClearThevenin()
        {
            _vth = null;
            _zth = null;
        }

        public IsolatedAcIsland CloneTopology()
        {
            var copy = new IsolatedAcIsland(BusNodeId, GroundNodeId);
            foreach (var pcs in _pcs)
            {
                copy.AddPcs(new MinPcsPort(pcs.Id, pcs.TerminalNodeId, pcs.OutputImpedanceOhm)
                {
                    InternalVoltageV = pcs.InternalVoltageV
                });
            }

            if (_loadZ.HasValue)
                copy.SetLoadImpedance(_loadZ.Value);
            return copy;
        }

        public IslandSolveResult Solve()
        {
            var net = new PhasorNetwork();
            net.SetGround(GroundNodeId);
            net.AddNode(BusNodeId);

            foreach (var pcs in _pcs)
                pcs.Stamp(net);

            if (_loadZ.HasValue)
                net.AddImpedance(BusNodeId, GroundNodeId, _loadZ.Value);

            if (_vth.HasValue && _zth.HasValue)
                net.AddVoltageBehindImpedance(BusNodeId, _vth.Value, _zth.Value);

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

            Complex theveninIntoBus = Complex.Zero;
            if (_vth.HasValue && _zth.HasValue)
                theveninIntoBus = net.VoltageBehindZCurrent(sol, BusNodeId, _vth.Value, _zth.Value);

            return new IslandSolveResult(sol, BusNodeId, currents, sumI, loadI, theveninIntoBus);
        }
    }

    public sealed class IslandSolveResult
    {
        internal IslandSolveResult(
            PhasorSolution solution,
            string busNodeId,
            Complex[] pcsCurrents,
            Complex pcsCurrentSum,
            Complex loadCurrent,
            Complex theveninCurrentIntoBus)
        {
            Solution = solution;
            BusNodeId = busNodeId;
            PcsCurrents = pcsCurrents;
            PcsCurrentSum = pcsCurrentSum;
            LoadCurrent = loadCurrent;
            TheveninCurrentIntoBus = theveninCurrentIntoBus;
        }

        public PhasorSolution Solution { get; }
        public string BusNodeId { get; }
        public IReadOnlyList<Complex> PcsCurrents { get; }
        public Complex PcsCurrentSum { get; }
        public Complex LoadCurrent { get; }

        /// <summary>戴维南支路流入母线的电流。(Vth − Vbus) / Zth。</summary>
        public Complex TheveninCurrentIntoBus { get; }

        /// <summary>下域交给上域的注入：母线侧送入变压器的复功率 S = −Vbus Ith*。</summary>
        public Complex InjectionIntoUpperVa
        {
            get
            {
                Complex vbus = Solution.Voltage(BusNodeId);
                return -vbus * Complex.Conjugate(TheveninCurrentIntoBus);
            }
        }
    }
}
