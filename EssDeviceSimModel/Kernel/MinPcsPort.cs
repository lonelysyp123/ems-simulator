using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    /// <summary>
    /// 阶段 2 最小 PCS 交流贴片：受控电压源串有限输出阻抗。参考值由外部控制提供，本类不均分邻机。
    /// 阶段 3 直流结算见 <see cref="AverageConverterPort"/>。
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

        /// <summary>
        /// 硬限流幅值（A，流入母线电流的模）。null = 不限流。属控制/设备属性，由外部写入，本类不自行整定。
        /// 超过此幅值时本端口在本拍由电压源模式切到限流模式（离散事件，重装配后再解）。
        /// </summary>
        public double? CurrentLimitA { get; set; }

        /// <summary>阶段 2 占位输出阻抗，直到阶段 3 从 PCS 端口方程给出真值。</summary>
        public static readonly Complex DefaultOutputImpedanceOhm = new(0.04, 0.18);

        /// <summary>电压源模式贴片：E 串有限输出阻抗。</summary>
        public void Stamp(PhasorNetwork network) =>
            network.AddVoltageBehindImpedance(TerminalNodeId, InternalVoltageV, OutputImpedanceOhm);

        /// <summary>
        /// 限流模式贴片：把已钳位的电流注入母线。电流被钳定后串联阻抗从母线侧网络方程中消失
        /// （理想电流源串阻抗对外等效为该电流源），只决定内电势需求，由控制提供。
        /// </summary>
        public void StampCurrentSource(PhasorNetwork network, Complex currentIntoBus) =>
            network.AddInjectedCurrent(TerminalNodeId, currentIntoBus);

        /// <summary>电压源模式下的支路电流（流入母线为正）。限流模式下不可用，应取钳位电流。</summary>
        public Complex TerminalCurrent(PhasorSolution solution) =>
            (InternalVoltageV - solution.Voltage(TerminalNodeId)) / OutputImpedanceOhm;

        /// <summary>给定母线下电压源模式想推的电流（流入母线为正）。与当前模式无关，恒可计算，用于限流判定。</summary>
        public Complex DesiredCurrentAt(Complex busVoltage) =>
            (InternalVoltageV - busVoltage) / OutputImpedanceOhm;
    }
}
