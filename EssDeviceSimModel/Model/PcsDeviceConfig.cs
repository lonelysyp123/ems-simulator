namespace EssSimulator.EssDeviceSimModel.Model
{
    /// <summary>新电气网络 PCS 设备完整配置（含爬坡、黑启动、保护限值）。</summary>
    public sealed class PcsDeviceConfig
    {
        public double RatedPowerKw { get; set; } = 2508;
        public double MaxPowerKw { get; set; } = 2508;
        public double Efficiency { get; set; } = 0.99;
        public double DcVoltageRangeMinV { get; set; } = 1000;
        public double DcVoltageRangeMaxV { get; set; } = 1500;
        public double AcNominalLineVoltageV { get; set; } = 690;
        public double FrequencyHz { get; set; } = 50;
        public double MaxCurrentA { get; set; } = 2200;
        public ThreePhaseConnection AcConnection { get; set; } = ThreePhaseConnection.Star;

        public double GridLossCoefficient { get; set; } = 0.01;
        public double RampSlope { get; set; } = 1;
        public int RampIntervalMs { get; set; } = 100;
        public int RampDelayMs { get; set; } = 0;

        public double IslandVoltageRampDurationMs { get; set; } = 100;
        public double BlackStartActivePowerGainKwPerVolt { get; set; } = 2.174;
        public double BlackStartMaxActivePowerKw { get; set; } = 200;
        public double BlackStartMagnetizingPowerFraction { get; set; } = 0.02;
        public double BlackStartBusEnergizedFraction { get; set; } = 0.85;
        public double BlackStartPrechargeDelayMs { get; set; } = 300;
        public double BlackStartVoltageRampVs { get; set; } = 138;
        public double VoltageRampUpVs { get; set; }
        public double VoltageRampDownVs { get; set; }
        public bool QvDroopEnabled { get; set; } = true;
        public double QvDroopCoefficientVPerKvar { get; set; }
        public double QvDroopDeadbandKvar { get; set; }
        public double QvDroopQ0Kvar { get; set; }
        public bool QvDroopEnableAfterSoftStartOnly { get; set; }
        public double QvDroopVmaxPu { get; set; } = 1.10;
        public bool PfDroopEnabled { get; set; } = true;
        public double PfDroopCoefficientHzPerKw { get; set; }
        public double PfDroopDeadbandKw { get; set; }
        public double PfDroopP0Kw { get; set; }
        public double PllEnableVoltagePu { get; set; } = 0.20;
        public double PllTauSec { get; set; } = 0.10;
        public double PreSyncEnableVoltagePu { get; set; } = 0.70;
        public double PreSyncVoltageWindowPu { get; set; } = 0.05;
        public double PreSyncFrequencyWindowHz { get; set; } = 0.2;
        public double PreSyncPhaseWindowDeg { get; set; } = 10;
        public double BlackStartFrequencyStartHz { get; set; } = 47;
        public double BlackStartFrequencyRampHzPerSec { get; set; } = 12;
        public double BlackStartReactiveVoltageGainKvarPerV { get; set; } = 4.0;
        public double BlackStartCurrentLimitFraction { get; set; } = 0.45;
        public string BlackStartSteadyLossShareMode { get; set; } = "AllOnBus";
        public double BlackStartJoinShareRampSec { get; set; } = 5.0;

        // 暂态建模参数
        public double TransientSubStepMs { get; set; } = 10;
        public double VoltageControllerTauMs { get; set; } = 15;
        public double InrushPeakMultiplier { get; set; } = 6.0;
        public double InrushDecayTauMs { get; set; } = 300;
        public double InrushTriggerVoltageFrac { get; set; } = 0.15;
        public double DvDtTripThresholdVPerSec { get; set; } = 500;
        public double DvDtRideThroughMs { get; set; } = 100;
    }
}
