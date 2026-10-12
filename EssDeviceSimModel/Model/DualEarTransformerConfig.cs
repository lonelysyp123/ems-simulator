namespace EssSimulator.EssDeviceSimModel.Model
{
    /// <summary>双耳（分裂绕组）箱变参数：穿越路径复用两绕组模型，两耳功率独立。</summary>
    public sealed class DualEarTransformerConfig
    {
        public double RatedPowerKva { get; set; } = 6300;
        public double PrimaryNominalLineVoltageV { get; set; } = 35000;
        public double SecondaryNominalLineVoltageV { get; set; } = 690;
        public ThreePhaseConnection PrimaryConnection { get; set; } = ThreePhaseConnection.Star;
        public ThreePhaseConnection SecondaryConnection { get; set; } = ThreePhaseConnection.Star;
        public double NoLoadLossKw { get; set; } = 0.08;
        public double LoadLossKw { get; set; } = 0.4;
        public double ImpedancePercent { get; set; } = 6;
        public double SplitImpedancePercent { get; set; } = 8;
        public double SplitRatio { get; set; } = 0.5;
        public double ReactiveVoltageInfluenceCoefficient { get; set; } = 1.0;
        public double NoLoadCurrentPercent { get; set; } = 2;
        public bool MagnetizingInrushEnabled { get; set; } = true;
        public double MagnetizingInrushDvDtThresholdPuPerSec { get; set; } = 0.8;
        public double MagnetizingInrushPeakExtraMultipleOfRatedPrimary { get; set; } = 4.0;
        public double MagnetizingInrushDecayTimeConstantSec { get; set; } = 0.45;
        public double MagnetizingInrushMaxExtraMultipleOfRatedPrimary { get; set; } = 12.0;

        public TransformerDeviceConfig ToThroughConfig() => new()
        {
            RatedPowerKva = RatedPowerKva,
            PrimaryNominalLineVoltageV = PrimaryNominalLineVoltageV,
            SecondaryNominalLineVoltageV = SecondaryNominalLineVoltageV,
            PrimaryConnection = PrimaryConnection,
            SecondaryConnection = SecondaryConnection,
            NoLoadLossKw = NoLoadLossKw,
            LoadLossKw = LoadLossKw,
            ImpedancePercent = ImpedancePercent,
            ReactiveVoltageInfluenceCoefficient = ReactiveVoltageInfluenceCoefficient,
            NoLoadCurrentPercent = NoLoadCurrentPercent,
            MagnetizingInrushEnabled = MagnetizingInrushEnabled,
            MagnetizingInrushDvDtThresholdPuPerSec = MagnetizingInrushDvDtThresholdPuPerSec,
            MagnetizingInrushPeakExtraMultipleOfRatedPrimary = MagnetizingInrushPeakExtraMultipleOfRatedPrimary,
            MagnetizingInrushDecayTimeConstantSec = MagnetizingInrushDecayTimeConstantSec,
            MagnetizingInrushMaxExtraMultipleOfRatedPrimary = MagnetizingInrushMaxExtraMultipleOfRatedPrimary
        };
    }
}
