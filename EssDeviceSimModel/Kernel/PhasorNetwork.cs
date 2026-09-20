using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    public sealed class NetworkSolveException : InvalidOperationException
    {
        public NetworkSolveException(string message) : base(message) { }
    }

    /// <summary>
    /// 三相平衡基波相量网络：Y V = I。构网源必须以有限阻抗贴入（Norton）。
    /// 独立于径向潮流，供测试宿主使用。
    /// </summary>
    public sealed class PhasorNetwork
    {
        public const double MinSeriesImpedanceOhm = 1e-6;

        private readonly List<string> _nodes = new();
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
        private string? _groundId;
        private readonly List<Action<Complex[,], Complex[]>> _stamps = new();

        public IReadOnlyList<string> NodeIds => _nodes;

        public int AddNode(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Node id required.", nameof(id));
            if (_index.TryGetValue(id, out int existing))
                return existing;
            int i = _nodes.Count;
            _nodes.Add(id);
            _index[id] = i;
            return i;
        }

        public void SetGround(string id)
        {
            AddNode(id);
            _groundId = id;
        }

        /// <summary>阻抗支路。电流从 from 流向 to 时，I = (Vfrom − Vto) / Z。</summary>
        public void AddImpedance(string fromId, string toId, Complex z)
        {
            EnsureFiniteZ(z, "impedance branch");
            int a = AddNode(fromId);
            int b = AddNode(toId);
            Complex y = Complex.One / z;
            _stamps.Add((Y, _) => StampAdmittance(Y, a, b, y));
        }

        /// <summary>
        /// 对地电压源串阻抗（构网 PCS / 戴维南）：Norton I = E/Z 注入 terminal，Y += 1/Z。
        /// </summary>
        public void AddVoltageBehindImpedance(string terminalId, Complex e, Complex z)
        {
            EnsureFiniteZ(z, "voltage source");
            int t = AddNode(terminalId);
            Complex y = Complex.One / z;
            _stamps.Add((Y, I) =>
            {
                Y[t, t] += y;
                I[t] += e * y;
            });
        }

        /// <summary>注入 terminal 的电流源（流入节点为正，与 YV=I 一致）。</summary>
        public void AddInjectedCurrent(string terminalId, Complex i)
        {
            int t = AddNode(terminalId);
            _stamps.Add((_, I) => I[t] += i);
        }

        /// <summary>闭合开关按小电阻贴入；断开则不装配。</summary>
        public void AddSwitch(string fromId, string toId, bool closed, double closedResistanceOhm = 1e-4)
        {
            if (!closed)
            {
                AddNode(fromId);
                AddNode(toId);
                return;
            }

            AddImpedance(fromId, toId, closedResistanceOhm);
        }

        public PhasorSolution Solve()
        {
            if (_nodes.Count == 0)
                throw new NetworkSolveException("Network has no nodes.");

            int n = _nodes.Count;
            var Y = new Complex[n, n];
            var I = new Complex[n];
            foreach (var stamp in _stamps)
                stamp(Y, I);

            int ground = _groundId != null ? _index[_groundId] : -1;
            var unknown = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                if (i == ground)
                    continue;
                unknown.Add(i);
            }

            if (unknown.Count == 0)
            {
                var onlyGround = new Complex[n];
                return new PhasorSolution(_nodes, onlyGround, ground);
            }

            int m = unknown.Count;
            var A = new Complex[m, m];
            var b = new Complex[m];
            for (int r = 0; r < m; r++)
            {
                int ir = unknown[r];
                b[r] = I[ir];
                if (ground >= 0)
                    b[r] -= Y[ir, ground] * Complex.Zero;
                for (int c = 0; c < m; c++)
                    A[r, c] = Y[ir, unknown[c]];
            }

            var x = SolveLinear(A, b);
            var v = new Complex[n];
            for (int r = 0; r < m; r++)
                v[unknown[r]] = x[r];
            return new PhasorSolution(_nodes, v, ground);
        }

        public Complex BranchCurrent(PhasorSolution solution, string fromId, string toId, Complex z)
        {
            EnsureFiniteZ(z, "branch current");
            return (solution.Voltage(fromId) - solution.Voltage(toId)) / z;
        }

        public Complex VoltageBehindZCurrent(PhasorSolution solution, string terminalId, Complex e, Complex z)
        {
            EnsureFiniteZ(z, "source current");
            return (e - solution.Voltage(terminalId)) / z;
        }

        private static void StampAdmittance(Complex[,] Y, int a, int b, Complex y)
        {
            Y[a, a] += y;
            Y[b, b] += y;
            Y[a, b] -= y;
            Y[b, a] -= y;
        }

        private static void EnsureFiniteZ(Complex z, string what)
        {
            if (z.Magnitude < MinSeriesImpedanceOhm)
                throw new NetworkSolveException(
                    $"Refusing {what} with |Z|={z.Magnitude:G6} Ω; forming sources must have finite output impedance.");
        }

        private static Complex[] SolveLinear(Complex[,] A, Complex[] b)
        {
            int n = b.Length;
            var M = new Complex[n, n + 1];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                    M[r, c] = A[r, c];
                M[r, n] = b[r];
            }

            for (int k = 0; k < n; k++)
            {
                int pivot = k;
                double best = M[k, k].Magnitude;
                for (int r = k + 1; r < n; r++)
                {
                    double mag = M[r, k].Magnitude;
                    if (mag > best)
                    {
                        best = mag;
                        pivot = r;
                    }
                }

                if (best < 1e-14)
                    throw new NetworkSolveException("Singular admittance matrix.");

                if (pivot != k)
                {
                    for (int c = k; c <= n; c++)
                        (M[k, c], M[pivot, c]) = (M[pivot, c], M[k, c]);
                }

                Complex akk = M[k, k];
                for (int r = k + 1; r < n; r++)
                {
                    Complex f = M[r, k] / akk;
                    for (int c = k; c <= n; c++)
                        M[r, c] -= f * M[k, c];
                }
            }

            var x = new Complex[n];
            for (int i = n - 1; i >= 0; i--)
            {
                Complex s = M[i, n];
                for (int c = i + 1; c < n; c++)
                    s -= M[i, c] * x[c];
                x[i] = s / M[i, i];
            }

            return x;
        }
    }

    public sealed class PhasorSolution
    {
        private readonly IReadOnlyList<string> _ids;
        private readonly Complex[] _v;
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

        internal PhasorSolution(IReadOnlyList<string> ids, Complex[] voltages, int groundIndex)
        {
            _ids = ids;
            _v = voltages;
            GroundIndex = groundIndex;
            for (int i = 0; i < ids.Count; i++)
                _index[ids[i]] = i;
        }

        public int GroundIndex { get; }

        public Complex Voltage(string nodeId) => _v[_index[nodeId]];

        public IReadOnlyList<string> NodeIds => _ids;
    }
}
