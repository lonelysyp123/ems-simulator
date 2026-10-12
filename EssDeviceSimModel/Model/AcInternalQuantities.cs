namespace EssSimulator.EssDeviceSimModel.Model
{
    /// <summary>
    /// AC 内部求解量：平衡三相下线电压、线电流及其相对电压的相位角。
    /// 有功/无功/视在/功率因数由 <see cref="AcQuantityConverter"/> 按需推导。
    /// </summary>
    public sealed class AcInternalQuantities
    {
        public ThreePhaseConnection Connection { get; init; } = ThreePhaseConnection.Star;
        public double LineVoltageV { get; init; }
        public double LineCurrentA { get; init; }
        /// <summary>
        /// 电流相对电压的相角（度）。由 atan2(Q, P) 得到，与无功同号：
        /// φ&gt;0 表示 Q&gt;0（容性升压），不是教科书「感性电流滞后」。
        /// </summary>
        public double PhaseAngleDeg { get; init; }
        public double FrequencyHz { get; init; } = 50.0;

        public double ActivePowerKw =>
            AcQuantityConverter.ComputeActivePowerKw(LineVoltageV, LineCurrentA, PhaseAngleDeg);

        public double ReactivePowerKvar =>
            AcQuantityConverter.ComputeReactivePowerKvar(LineVoltageV, LineCurrentA, PhaseAngleDeg);

        public double ApparentPowerKva =>
            AcQuantityConverter.ComputeApparentPowerKva(LineVoltageV, LineCurrentA);

        public double PowerFactor =>
            AcQuantityConverter.ComputePowerFactor(LineVoltageV, LineCurrentA, PhaseAngleDeg);

        public bool IsEnergized(double voltageThresholdV = 1.0) => LineVoltageV > voltageThresholdV;
    }
}
