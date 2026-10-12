using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Control;
using ModelPcsDeviceConfig = EssSimulator.EssDeviceSimModel.Model.PcsDeviceConfig;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    public static class PcsDeviceFactory
    {
        public static ModelPcsDeviceConfig CreateConfig(
            PcsPhysicalConfig pcsCfg,
            PcsRampConfig rampCfg)
        {
            var deviceCfg = new ModelPcsDeviceConfig
            {
                RatedPowerKw = pcsCfg.RatedPower,
                MaxPowerKw = pcsCfg.MaxPower,
                Efficiency = pcsCfg.Efficiency,
                DcVoltageRangeMinV = pcsCfg.DcVoltageRangeMin,
                DcVoltageRangeMaxV = pcsCfg.DcVoltageRangeMax,
                AcNominalLineVoltageV = pcsCfg.AcVoltageNominal,
                FrequencyHz = pcsCfg.FrequencyNominal,
                MaxCurrentA = pcsCfg.MaxCurrent,
                GridLossCoefficient = pcsCfg.GridLossCoefficient,
                RampSlope = rampCfg.Slope,
                RampIntervalMs = rampCfg.IntervalMs,
                RampDelayMs = rampCfg.DelayMs,
                IslandVoltageRampDurationMs = pcsCfg.IslandVoltageRampDurationMs,
                BlackStartActivePowerGainKwPerVolt = pcsCfg.BlackStartActivePowerGainKwPerVolt,
                BlackStartMaxActivePowerKw = pcsCfg.BlackStartMaxActivePowerKw,
                BlackStartMagnetizingPowerFraction = pcsCfg.BlackStartMagnetizingPowerFraction,
                BlackStartBusEnergizedFraction = pcsCfg.BlackStartBusEnergizedFraction,
                BlackStartPrechargeDelayMs = pcsCfg.BlackStartPrechargeDelayMs,
                BlackStartVoltageRampVs = pcsCfg.BlackStartVoltageRampVs,
                BlackStartFrequencyStartHz = pcsCfg.BlackStartFrequencyStartHz,
                BlackStartFrequencyRampHzPerSec = pcsCfg.BlackStartFrequencyRampHzPerSec,
                BlackStartReactiveVoltageGainKvarPerV = pcsCfg.BlackStartReactiveVoltageGainKvarPerV,
                BlackStartCurrentLimitFraction = pcsCfg.BlackStartCurrentLimitFraction,
                TransientSubStepMs = pcsCfg.TransientSubStepMs,
                VoltageControllerTauMs = pcsCfg.VoltageControllerTauMs,
                InrushPeakMultiplier = pcsCfg.InrushPeakMultiplier,
                InrushDecayTauMs = pcsCfg.InrushDecayTauMs,
                InrushTriggerVoltageFrac = pcsCfg.InrushTriggerVoltageFrac,
                DvDtTripThresholdVPerSec = pcsCfg.DvDtTripThresholdVPerSec,
                DvDtRideThroughMs = pcsCfg.DvDtRideThroughMs
            };
            PcsFormingVoltageSettings.ApplyResolved(pcsCfg, deviceCfg);
            return deviceCfg;
        }

        public static PcsDevice Create(string deviceId, ModelPcsDeviceConfig config) =>
            new(deviceId, config);
    }
}
