using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    /// <summary>
    /// 阶段 2 最小 PCS 端口：受控电压源串有限输出阻抗。参考值由外部控制提供，本类不均分邻机。
    /// </summary>
    public sealed class MinPcsPort
    {
        public MinPcsPort(string id, string terminalNodeId, Complex outputImpedanceOhm)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Id required.", nameof(id));
            if (outputImpedanceOhm.Magnitude < PhasorNetwork.MinSeriesImpedanceOhm)
                throw new NetworkSolveException($"PCS '{id}' output impedance must be finite.");

            Id = id;
            TerminalNodeId = terminalNodeId;
            OutputImpedanceOhm = outputImpedanceOhm;
        }

        public string Id { get; }
        public string TerminalNodeId { get; }
        public Complex OutputImpedanceOhm { get; }
        public Complex InternalVoltageV { get; set; }

        /// <summary>阶段 2 占位输出阻抗，直到阶段 3 从 PCS 端口方程给出真值。</summary>
        public static readonly Complex DefaultOutputImpedanceOhm = new(0.04, 0.18);

        public void Stamp(PhasorNetwork network) =>
            network.AddVoltageBehindImpedance(TerminalNodeId, InternalVoltageV, OutputImpedanceOhm);

        public Complex TerminalCurrent(PhasorSolution solution) =>
            (InternalVoltageV - solution.Voltage(TerminalNodeId)) / OutputImpedanceOhm;
    }
}
