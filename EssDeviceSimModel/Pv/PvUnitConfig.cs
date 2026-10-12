namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>
    /// 光伏单元：一台 35 kV/690 V 箱变下挂 16 台组串逆变器；
    /// 单台逆变器仍为 16 簇 × 30 块、320 kW / 690 V。
    /// </summary>
    public sealed class PvUnitConfig
    {
        public const int DefaultInverterCount = 16;

        public int InverterCount { get; init; } = DefaultInverterCount;
        public PvInverterConfig Inverter { get; init; } = new();
        public double UnitXfPrimaryV { get; init; } = 35000;
        public double UnitXfSecondaryV { get; init; } = 690;
        public double UnitXfRatedKva { get; init; } = DefaultInverterCount * 320;
        /// <summary>空载损耗（W），对齐储能单元变默认 100 W。</summary>
        public double UnitXfNoLoadLossW { get; init; } = 100;
        /// <summary>负载损耗（W），对齐储能单元变默认 200 W。</summary>
        public double UnitXfLoadLossW { get; init; } = 200;
        /// <summary>短路阻抗（%）。本期只保存，不参与压降。</summary>
        public double UnitXfImpedancePercent { get; init; } = 5;
        /// <summary>空载电流（%）。本期只保存，并网时不注入励磁无功。</summary>
        public double UnitXfNoLoadCurrentPercent { get; init; } = 2;
    }
}
