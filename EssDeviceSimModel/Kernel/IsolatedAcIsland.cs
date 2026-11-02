using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    /// <summary>独立测试宿主：一个交流母线 + 若干最小 PCS 端口 + 可选负荷。不接入 PlantEngine。</summary>
    public sealed class IsolatedAcIsland
    {
        private readonly List<MinPcsPort> _pcs = new();
        private readonly List<MinGflPort> _gfl = new();
        private Complex? _loadZ;
        private Complex? _shuntZ;
        private Complex _injectedIntoBus;
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
        public IReadOnlyList<MinGflPort> GflPorts => _gfl;

        public void AddPcs(MinPcsPort port)
        {
            if (port.TerminalNodeId != BusNodeId)
                throw new ArgumentException("PCS terminal must attach to the island bus.", nameof(port));
            _pcs.Add(port);
        }

        public void AddGfl(MinGflPort port)
        {
            if (port.TerminalNodeId != BusNodeId)
                throw new ArgumentException("GFL terminal must attach to the island bus.", nameof(port));
            _gfl.Add(port);
        }

        public void SetLoadImpedance(Complex zLoadOhm) => _loadZ = zLoadOhm;

        /// <summary>
        /// 母线对地励磁支路（铁损电导 + 励磁电纳）。恒定导纳，功率随电压平方。
        /// 并网岛不贴：励磁由上域电网吸收。
        /// </summary>
        public void SetBusShunt(Complex zShuntOhm)
        {
            if (zShuntOhm.Magnitude < PhasorNetwork.MinSeriesImpedanceOhm)
                throw new NetworkSolveException("Bus shunt impedance must be finite.");
            _shuntZ = zShuntOhm;
        }

        public void ClearBusShunt() => _shuntZ = null;

        /// <summary>流入母线为正。共享励磁吸收电流时传入负值。</summary>
        public void SetBusInjectedCurrent(Complex iIntoBus) => _injectedIntoBus = iIntoBus;

        public void ClearBusInjectedCurrent() => _injectedIntoBus = Complex.Zero;

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

            foreach (var gfl in _gfl)
            {
                copy.AddGfl(new MinGflPort(gfl.Id, gfl.TerminalNodeId)
                {
                    ActivePowerW = gfl.ActivePowerW,
                    ReactivePowerVAr = gfl.ReactivePowerVAr,
                    CurrentLimitA = gfl.CurrentLimitA
                });
            }

            if (_loadZ.HasValue)
                copy.SetLoadImpedance(_loadZ.Value);
            if (_shuntZ.HasValue)
                copy.SetBusShunt(_shuntZ.Value);
            copy.SetBusInjectedCurrent(_injectedIntoBus);
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
            Complex busEstimate = InitialBusEstimate();

            for (int iter = 0; iter < MaxCurrentLimitIterations; iter++)
            {
                var (net, sol) = BuildAndSolve(limited, clamped, busEstimate);
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
                busEstimate = bus;
                havePrev = true;

                if (settled)
                    return BuildResult(net, sol, limited, clamped);
            }

            throw new NetworkSolveException(
                $"Current-limit reassembly did not settle within {MaxCurrentLimitIterations} iterations on island '{BusNodeId}'.");
        }

        /// <summary>
        /// 跟网口注入电流依赖母线电压，需定点迭代；首拍用已知相角的源估计母线：
        /// 并网岛取戴维南，孤岛取构网内电势，全无则 0（纯跟网无定压源时本就不收敛，按求解失败处理）。
        /// </summary>
        private Complex InitialBusEstimate()
        {
            if (_vth.HasValue)
                return _vth.Value;
            if (_pcs.Count > 0)
                return _pcs[0].InternalVoltageV;
            return Complex.Zero;
        }

        private (PhasorNetwork net, PhasorSolution sol) BuildAndSolve(bool[] limited, Complex[] clamped, Complex busEstimate)
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

            foreach (var gfl in _gfl)
                gfl.Stamp(net, busEstimate);

            if (_loadZ.HasValue)
                net.AddImpedance(BusNodeId, GroundNodeId, _loadZ.Value);
            if (_shuntZ.HasValue)
                net.AddImpedance(BusNodeId, GroundNodeId, _shuntZ.Value);
            if (_injectedIntoBus != Complex.Zero)
                net.AddInjectedCurrent(BusNodeId, _injectedIntoBus);

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

            var gflCurrents = new Complex[_gfl.Count];
            Complex gflSum = Complex.Zero;
            for (int i = 0; i < _gfl.Count; i++)
            {
                gflCurrents[i] = _gfl[i].InjectedCurrentA;
                gflSum += gflCurrents[i];
            }

            Complex loadI = Complex.Zero;
            if (_loadZ.HasValue)
                loadI = net.BranchCurrent(sol, BusNodeId, GroundNodeId, _loadZ.Value);

            Complex shuntI = Complex.Zero;
            if (_shuntZ.HasValue)
                shuntI = net.BranchCurrent(sol, BusNodeId, GroundNodeId, _shuntZ.Value);

            Complex theveninIntoBus = Complex.Zero;
            if (_vth.HasValue && _zth.HasValue)
                theveninIntoBus = net.VoltageBehindZCurrent(sol, BusNodeId, _vth.Value, _zth.Value);

            return new IslandSolveResult(
                sol, BusNodeId, currents, sumI, gflCurrents, gflSum, loadI, theveninIntoBus, limited, shuntI);
        }
    }

    public sealed class IslandSolveResult
    {
        internal IslandSolveResult(
            PhasorSolution solution,
            string busNodeId,
            Complex[] pcsCurrents,
            Complex pcsCurrentSum,
            Complex[] gflCurrents,
            Complex gflCurrentSum,
            Complex loadCurrent,
            Complex theveninCurrentIntoBus,
            bool[] pcsCurrentLimited,
            Complex shuntCurrent)
        {
            Solution = solution;
            BusNodeId = busNodeId;
            PcsCurrents = pcsCurrents;
            PcsCurrentSum = pcsCurrentSum;
            GflCurrents = gflCurrents;
            GflCurrentSum = gflCurrentSum;
            LoadCurrent = loadCurrent;
            TheveninCurrentIntoBus = theveninCurrentIntoBus;
            PcsCurrentLimited = pcsCurrentLimited;
            ShuntCurrent = shuntCurrent;
        }

        public PhasorSolution Solution { get; }
        public string BusNodeId { get; }
        public IReadOnlyList<Complex> PcsCurrents { get; }
        public Complex PcsCurrentSum { get; }

        /// <summary>各跟网口本拍注入母线的电流（流向母线为正，与 <see cref="IsolatedAcIsland.GflPorts"/> 同序）。</summary>
        public IReadOnlyList<Complex> GflCurrents { get; }
        public Complex GflCurrentSum { get; }

        public Complex LoadCurrent { get; }

        /// <summary>励磁支路离开母线的电流。未贴支路时为 0。</summary>
        public Complex ShuntCurrent { get; }

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
