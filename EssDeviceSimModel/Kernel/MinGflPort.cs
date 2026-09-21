using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    /// <summary>
    /// 阶段 3 跟网（GFL）变流器平均模型：诺顿电流源。交流侧按 P/Q 设定与母线电压注入
    /// I = conj(P+jQ)/conj(Vbus)（交付到母线为正），不建内电势、不定义角频率（由所在岛的戴维南/构网机锁定）。
    /// 控制参考（P/Q 设定）一拍滞后由外部写入；本类只把设定翻译成本拍注入电流，并施加幅值硬限流。
    /// 直流结算复用 <see cref="AverageConverterPort"/>：本端口注入母线的电流即「流向母线的端口电流」。
    /// </summary>
    public sealed class MinGflPort
    {
        /// <summary>母线电压低于此幅值（V）时不注入，避免除零/病态。</summary>
        public const double MinBusVoltageForInjection = 1.0;

        public MinGflPort(string id, string terminalNodeId)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Id required.", nameof(id));
            if (string.IsNullOrWhiteSpace(terminalNodeId))
                throw new ArgumentException("Terminal node required.", nameof(terminalNodeId));

            Id = id;
            TerminalNodeId = terminalNodeId;
        }

        public string Id { get; }
        public string TerminalNodeId { get; }

        /// <summary>本拍交付到交流母线的有功设定（W，流出设备为正）。控制参考，一拍滞后。</summary>
        public double ActivePowerW { get; set; }

        /// <summary>本拍交付到交流母线的无功设定（VAr，流出设备为正）。控制参考，一拍滞后。</summary>
        public double ReactivePowerVAr { get; set; }

        /// <summary>硬限流幅值（A，注入母线电流的模）。null = 不限流。属控制/设备属性，由外部写入。</summary>
        public double? CurrentLimitA { get; set; }

        /// <summary>上次 <see cref="Stamp"/> 实际注入母线的电流（已限流）。供岛结果与直流结算取用。</summary>
        public Complex InjectedCurrentA { get; private set; }

        /// <summary>
        /// 给定母线电压下想注入的电流（流向母线为正）：S = P+jQ = Vbus·conj(I) ⇒ I = conj(S)/conj(Vbus)。
        /// 超过 <see cref="CurrentLimitA"/> 时钳幅保角（交付功率随之下降，不假装仍达设定）。
        /// </summary>
        public Complex InjectionCurrentAt(Complex busVoltage)
        {
            if (busVoltage.Magnitude < MinBusVoltageForInjection)
                return Complex.Zero;

            Complex s = new(ActivePowerW, ReactivePowerVAr);
            Complex i = Complex.Conjugate(s) / Complex.Conjugate(busVoltage);
            if (CurrentLimitA is { } limit && i.Magnitude > limit)
                i = limit * i / i.Magnitude;
            return i;
        }

        /// <summary>把本拍注入电流贴入网络（流入节点为正），并记录到 <see cref="InjectedCurrentA"/>。</summary>
        public void Stamp(PhasorNetwork network, Complex busVoltage)
        {
            ArgumentNullException.ThrowIfNull(network);
            InjectedCurrentA = InjectionCurrentAt(busVoltage);
            network.AddInjectedCurrent(TerminalNodeId, InjectedCurrentA);
        }
    }
}
