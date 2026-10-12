using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Model;
using log4net;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    public static class TransformerDeviceFactory
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(TransformerDeviceFactory));

        public static TransformerDeviceConfig CreateConfig(TransformerConfig cfg)
        {
            double primary = NominalOr(cfg.PrimaryVoltage, 220_000);
            double secondary = NominalOr(cfg.SecondaryVoltage, 35_000);
            if (primary != cfg.PrimaryVoltage || secondary != cfg.SecondaryVoltage)
                Log.Warn($"主变额定电压无效，已改用 {primary} V / {secondary} V");
            return CreateConfig(
                cfg.RatedPower,
                primary,
                secondary,
                cfg.NoLoadLoss,
                cfg.LoadLoss,
                cfg.ImpedancePercent,
                cfg.ReactiveVoltageInfluenceCoefficient,
                cfg.NoLoadCurrentPercent,
                cfg.MagnetizingInrushEnabled,
                cfg.MagnetizingInrushDvDtThresholdPuPerSec,
                cfg.MagnetizingInrushPeakExtraMultipleOfRatedPrimary,
                cfg.MagnetizingInrushDecayTimeConstantSec,
                cfg.MagnetizingInrushMaxExtraMultipleOfRatedPrimary);
        }

        public static TransformerDeviceConfig CreateConfig(UnitTransformerConfig cfg)
        {
            double primary = NominalOr(cfg.PrimaryVoltage, 35_000);
            double secondary = NominalOr(cfg.SecondaryVoltage, 690);
            if (primary != cfg.PrimaryVoltage || secondary != cfg.SecondaryVoltage)
                Log.Warn($"单元变额定电压无效，已改用 {primary} V / {secondary} V");
            return CreateConfig(
                cfg.RatedPower,
                primary,
                secondary,
                cfg.NoLoadLoss,
                cfg.LoadLoss,
                cfg.ImpedancePercent,
                cfg.ReactiveVoltageInfluenceCoefficient,
                cfg.NoLoadCurrentPercent,
                cfg.MagnetizingInrushEnabled,
                cfg.MagnetizingInrushDvDtThresholdPuPerSec,
                cfg.MagnetizingInrushPeakExtraMultipleOfRatedPrimary,
                cfg.MagnetizingInrushDecayTimeConstantSec,
                cfg.MagnetizingInrushMaxExtraMultipleOfRatedPrimary);
        }

        /// <summary>不大于 1 V 或非有限的额定电压改用该变压器种类的默认值。</summary>
        private static double NominalOr(double voltage, double fallback) =>
            double.IsFinite(voltage) && voltage > 1 ? voltage : fallback;

        private static TransformerDeviceConfig CreateConfig(
            double ratedPowerKva,
            double primaryVoltage,
            double secondaryVoltage,
            double noLoadLossW,
            double loadLossW,
            double impedancePercent,
            double reactiveVoltageInfluenceCoefficient,
            double noLoadCurrentPercent,
            bool magnetizingInrushEnabled,
            double magnetizingInrushDvDtThresholdPuPerSec,
            double magnetizingInrushPeakExtraMultipleOfRatedPrimary,
            double magnetizingInrushDecayTimeConstantSec,
            double magnetizingInrushMaxExtraMultipleOfRatedPrimary) =>
            new()
            {
                RatedPowerKva = ratedPowerKva,
                PrimaryNominalLineVoltageV = primaryVoltage,
                SecondaryNominalLineVoltageV = secondaryVoltage,
                NoLoadLossKw = noLoadLossW / 1000.0,
                LoadLossKw = loadLossW / 1000.0,
                ImpedancePercent = impedancePercent,
                ReactiveVoltageInfluenceCoefficient = reactiveVoltageInfluenceCoefficient,
                NoLoadCurrentPercent = noLoadCurrentPercent,
                MagnetizingInrushEnabled = magnetizingInrushEnabled,
                MagnetizingInrushDvDtThresholdPuPerSec = magnetizingInrushDvDtThresholdPuPerSec,
                MagnetizingInrushPeakExtraMultipleOfRatedPrimary = magnetizingInrushPeakExtraMultipleOfRatedPrimary,
                MagnetizingInrushDecayTimeConstantSec = magnetizingInrushDecayTimeConstantSec,
                MagnetizingInrushMaxExtraMultipleOfRatedPrimary = magnetizingInrushMaxExtraMultipleOfRatedPrimary
            };

        public static TransformerDevice Create(string deviceId, TransformerDeviceConfig config) =>
            new(deviceId, config);

        public static DualEarTransformerConfig CreateDualEarConfig(SplitTransformerRuntimeConfig cfg) =>
            new()
            {
                RatedPowerKva = cfg.RatedPowerKva > 1 ? cfg.RatedPowerKva : 6300,
                PrimaryNominalLineVoltageV = cfg.PrimaryVoltage > 1 ? cfg.PrimaryVoltage : 35000,
                SecondaryNominalLineVoltageV = cfg.SecondaryVoltage > 1 ? cfg.SecondaryVoltage : 690,
                NoLoadLossKw = cfg.NoLoadLoss / 1000.0,
                LoadLossKw = cfg.LoadLoss / 1000.0,
                ImpedancePercent = cfg.ImpedancePercent > 0.1 ? cfg.ImpedancePercent : 6,
                SplitImpedancePercent = cfg.SplitImpedancePercent > 0.1 ? cfg.SplitImpedancePercent : 8,
                SplitRatio = cfg.SplitRatio is > 0.1 and < 0.9 ? cfg.SplitRatio : 0.5
            };

        public static DualEarTransformerDevice CreateDualEar(string deviceId, SplitTransformerRuntimeConfig cfg) =>
            new(deviceId, CreateDualEarConfig(cfg));
    }
}
