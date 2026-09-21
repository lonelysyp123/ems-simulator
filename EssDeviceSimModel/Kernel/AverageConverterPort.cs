using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    /// <summary>
    /// 阶段 3 平均变流器：交流仍是受控源串有限阻抗；直流只按已解端口功率结算。
    /// 控制参考（内电势）由外部写入，本类不均分邻机、不从直流反写交流。
    /// </summary>
    public sealed class AverageConverterPort
    {
        public const double DefaultEfficiency = 0.99;

        public AverageConverterPort(
            string id,
            string terminalNodeId,
            Complex outputImpedanceOhm,
            double efficiency = DefaultEfficiency)
        {
            if (efficiency <= 0 || efficiency > 1.0)
                throw new NetworkSolveException($"Converter '{id}' efficiency must be in (0, 1].");

            Ac = new MinPcsPort(id, terminalNodeId, outputImpedanceOhm);
            Efficiency = efficiency;
        }

        public MinPcsPort Ac { get; }
        public double Efficiency { get; }
        public double DcVoltageV { get; set; }

        public Complex InternalVoltageV
        {
            get => Ac.InternalVoltageV;
            set => Ac.InternalVoltageV = value;
        }

        public void Stamp(PhasorNetwork network) => Ac.Stamp(network);

        /// <summary>
        /// 用本拍岛的<b>已提交</b>交流解结算直流：电流取 <see cref="IslandSolveResult.PcsCurrents"/>（限流时即钳位电流），
        /// 而不是按 (E−Vbus)/Z 重算——后者在限流拍会高估功率、破坏能量守恒。<paramref name="portIndex"/> 必须对应本端口在岛中的装配序。
        /// 不修改 <see cref="InternalVoltageV"/>。
        /// </summary>
        public ConverterEnergyResult SettleDc(IslandSolveResult result, int portIndex)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (portIndex < 0 || portIndex >= result.PcsCurrents.Count)
                throw new ArgumentOutOfRangeException(nameof(portIndex));

            Complex v = result.Solution.Voltage(Ac.TerminalNodeId);
            Complex iTowardBus = result.PcsCurrents[portIndex];
            Complex sIntoDevice = v * Complex.Conjugate(-iTowardBus);
            return DcEnergySettlement.Settle(sIntoDevice.Real, DcVoltageV, Efficiency);
        }
    }

    /// <summary>交流口功率（流入设备为正）→ 直流电流。Vdc 必须是已提交的电池电压。</summary>
    public static class DcEnergySettlement
    {
        public static ConverterEnergyResult Settle(double pacIntoDeviceW, double dcVoltageV, double efficiency)
        {
            if (dcVoltageV < PhasorNetwork.MinSeriesImpedanceOhm)
                throw new NetworkSolveException("DC voltage must be finite for energy settlement.");
            if (efficiency <= 0 || efficiency > 1.0)
                throw new NetworkSolveException("Efficiency must be in (0, 1].");

            double pdcIntoDeviceW;
            if (pacIntoDeviceW <= 0)
            {
                double pacOut = -pacIntoDeviceW;
                pdcIntoDeviceW = pacOut / efficiency;
            }
            else
            {
                pdcIntoDeviceW = -pacIntoDeviceW * efficiency;
            }

            double lossW = pdcIntoDeviceW + pacIntoDeviceW;
            double idcIntoDeviceA = pdcIntoDeviceW / dcVoltageV;
            return new ConverterEnergyResult(pacIntoDeviceW, pdcIntoDeviceW, idcIntoDeviceA, lossW);
        }
    }

    public readonly struct ConverterEnergyResult
    {
        public ConverterEnergyResult(
            double pacIntoDeviceW,
            double pdcIntoDeviceW,
            double idcIntoDeviceA,
            double lossW)
        {
            PacIntoDeviceW = pacIntoDeviceW;
            PdcIntoDeviceW = pdcIntoDeviceW;
            IdcIntoDeviceA = idcIntoDeviceA;
            LossW = lossW;
        }

        public double PacIntoDeviceW { get; }
        public double PdcIntoDeviceW { get; }
        public double IdcIntoDeviceA { get; }
        public double LossW { get; }
    }
}
