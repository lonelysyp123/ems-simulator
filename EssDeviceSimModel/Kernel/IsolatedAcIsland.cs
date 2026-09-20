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
                    InternalVoltageV = pcs.InternalVoltageV,
                    CurrentLimitA = pcs.CurrentLimitA
                });
            }

            if (_loadZ.HasValue)
                copy.SetLoadImpedance(_loadZ.Value);
            return copy;
        }

        private const int MaxCurrentLimitIterations = 200;
        private const double BusVoltageSettleTolerance = 1e-9;
        private const double LimitRelativeTolerance = 1e-9;

        /// <summary>
        /// 求解本拍岛状态。限流是<b>本拍离散模式事件</b>：某 PCS 的 |I_want|=(E−Vbus)/Z 超过其
        /// <see cref="MinPcsPort.CurrentLimitA"/> 时，把它从「E 串 Z」切成「注入钳位电流」并重装配再解
        /// （第五节步骤 4 语义），不塞进同一次线性求解、也不当作失败冻结。
        /// 单调锁存（本拍一旦限流不再退出）避免模式抖动；只有重装配始终不收敛才抛 <see cref="NetworkSolveException"/>。
        /// 无任何 PCS 设限流时，退化为单次线性求解，与阶段 2 行为一致。
        /// </summary>
        public IslandSolveResult Solve()
        {
            int n = _pcs.Count;
            var limited = new bool[n];
            var clamped = new Complex[n];
            Complex prevBus = Complex.Zero;
            bool havePrev = false;

            for (int iter = 0; iter < MaxCurrentLimitIterations; iter++)
            {
                var (net, sol) = BuildAndSolve(limited, clamped);
                Complex bus = sol.Voltage(BusNodeId);

                bool setChanged = false;
                for (int i = 0; i < n; i++)
                {
                    var pcs = _pcs[i];
                    Complex iWant = pcs.DesiredCurrentAt(bus);
                    if (pcs.CurrentLimitA is { } limit &&
                        iWant.Magnitude > limit * (1.0 + LimitRelativeTolerance) &&
                        !limited[i])
                    {
                        limited[i] = true;
                        setChanged = true;
                    }

                    if (limited[i])
                        clamped[i] = pcs.CurrentLimitA!.Value * iWant / iWant.Magnitude;
                }

                bool settled = !setChanged && havePrev &&
                    (bus - prevBus).Magnitude <= BusVoltageSettleTolerance * Math.Max(1.0, bus.Magnitude);
                prevBus = bus;
                havePrev = true;

                if (settled)
                    return BuildResult(net, sol, limited, clamped);
            }

            throw new NetworkSolveException(
                $"Current-limit reassembly did not settle within {MaxCurrentLimitIterations} iterations on island '{BusNodeId}'.");
        }

        private (PhasorNetwork net, PhasorSolution sol) BuildAndSolve(bool[] limited, Complex[] clamped)
        {
            var net = new PhasorNetwork();
            net.SetGround(GroundNodeId);
            net.AddNode(BusNodeId);

            for (int i = 0; i < _pcs.Count; i++)
            {
                if (limited[i])
                    _pcs[i].StampCurrentSource(net, clamped[i]);
                else
                    _pcs[i].Stamp(net);
            }

            if (_loadZ.HasValue)
                net.AddImpedance(BusNodeId, GroundNodeId, _loadZ.Value);

            if (_vth.HasValue && _zth.HasValue)
                net.AddVoltageBehindImpedance(BusNodeId, _vth.Value, _zth.Value);

            return (net, net.Solve());
        }

        private IslandSolveResult BuildResult(PhasorNetwork net, PhasorSolution sol, bool[] limited, Complex[] clamped)
        {
            int n = _pcs.Count;
            var currents = new Complex[n];
            Complex sumI = Complex.Zero;
            for (int i = 0; i < n; i++)
            {
                currents[i] = limited[i] ? clamped[i] : _pcs[i].TerminalCurrent(sol);
                sumI += currents[i];
            }

            Complex loadI = Complex.Zero;
            if (_loadZ.HasValue)
                loadI = net.BranchCurrent(sol, BusNodeId, GroundNodeId, _loadZ.Value);

            Complex theveninIntoBus = Complex.Zero;
            if (_vth.HasValue && _zth.HasValue)
                theveninIntoBus = net.VoltageBehindZCurrent(sol, BusNodeId, _vth.Value, _zth.Value);

            return new IslandSolveResult(sol, BusNodeId, currents, sumI, loadI, theveninIntoBus, limited);
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
            Complex theveninCurrentIntoBus,
            bool[] pcsCurrentLimited)
        {
            Solution = solution;
            BusNodeId = busNodeId;
            PcsCurrents = pcsCurrents;
            PcsCurrentSum = pcsCurrentSum;
            LoadCurrent = loadCurrent;
            TheveninCurrentIntoBus = theveninCurrentIntoBus;
            PcsCurrentLimited = pcsCurrentLimited;
        }

        public PhasorSolution Solution { get; }
        public string BusNodeId { get; }
        public IReadOnlyList<Complex> PcsCurrents { get; }
        public Complex PcsCurrentSum { get; }
        public Complex LoadCurrent { get; }

        /// <summary>各 PCS 本拍是否处于限流模式（与 <see cref="PcsCurrents"/> 同序）。</summary>
        public IReadOnlyList<bool> PcsCurrentLimited { get; }

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
