namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>光伏组件铭牌与 STC 电参数。</summary>
    public sealed class PvModuleSpec
    {
        public required string Model { get; init; }
        public required string Technology { get; init; }
        public int CellCount { get; init; }
        /// <summary>串联电池片数（决定 Voc 量级）。264 片四分片常见为 66 串 × 4 并。</summary>
        public int SeriesCells { get; init; }

        public double PmaxStcW { get; init; }
        public double VmpStcV { get; init; }
        public double ImpStcA { get; init; }
        public double VocStcV { get; init; }
        public double IscStcA { get; init; }
        public double Efficiency { get; init; }

        /// <summary>Pmax 温度系数 (1/K)，如 -0.0026 表示 -0.26%/℃。</summary>
        public double GammaPmaxPerK { get; init; }
        /// <summary>Voc 相对温度系数 (1/K)。</summary>
        public double BetaVocPerK { get; init; }
        /// <summary>Isc 相对温度系数 (1/K)。</summary>
        public double AlphaIscPerK { get; init; }
        public double NoctC { get; init; }
        public double Bifaciality { get; init; }

        public double LengthMm { get; init; }
        public double WidthMm { get; init; }
        public double ThicknessMm { get; init; }
        public double WeightKg { get; init; }
        public double MaxSystemVoltageV { get; init; }
        public double SeriesFuseA { get; init; }
        public double FirstYearDegradation { get; init; }
        public double AnnualDegradation { get; init; }

        public const double StcIrradianceWm2 = 1000.0;
        public const double StcCellTempC = 25.0;
    }

    /// <summary>给定辐照与电池温度下的工作点。</summary>
    public readonly record struct PvModuleOperatingPoint(
        double PmpW,
        double VmpV,
        double ImpA,
        double VocV,
        double IscA,
        double GeffWm2,
        double CellTempC);
}
