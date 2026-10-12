namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>光伏逆变器配置。直流侧 16 簇 × 30 块；交流侧对齐储能 PCS（690 V 线电压）。</summary>
    public sealed class PvInverterConfig
    {
        public int ModulesPerString { get; init; } = PvStringSimulator.DefaultModuleCount;
        public int StringCount { get; init; } = 16;
        public double RatedPowerKw { get; init; } = 320;
        // 未配置时沿用额定有功数值作为仿真缺省，不代表厂家铭牌值。
        public double? RatedReactivePowerKvar { get; init; }
        public double MaxPowerKw { get; init; } = 352;
        public double Efficiency { get; init; } = 0.99;
        public double DcVoltageRangeMinV { get; init; } = 500;
        public double DcVoltageRangeMaxV { get; init; } = 1500;
        /// <summary>低于此温度开始线性降额，到 InhibitMinTempC 可发为 0。</summary>
        public double FullPowerMinTempC { get; init; } = -10;
        public double InhibitMinTempC { get; init; } = -40;
        public double AcNominalLineVoltageV { get; init; } = 690;
        public double FrequencyHz { get; init; } = 50;
        public double GridLossCoefficient { get; init; } = 0.01;
        public double RampSlope { get; init; } = 1;
        public int RampIntervalMs { get; init; } = 100;
    }
}
