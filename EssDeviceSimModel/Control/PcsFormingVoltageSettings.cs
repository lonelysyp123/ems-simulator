using EssSimulator.Configuration;
using DevicePcsConfig = EssSimulator.EssDeviceSimModel.Model.PcsDeviceConfig;

namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>构网电压斜坡、Q-V 与 P-f 下垂的配置折算。</summary>
    public static class PcsFormingVoltageSettings
    {
        public const double NominalRampSeconds = 5.0;
        public const double DefaultDroopPercent = 0.04;
        public const double DefaultDeadbandFraction = 0.02;
        public const double DefaultVmaxPu = 1.10;

        public static double FiveSecondFloorVs(double vNom) =>
            Math.Max(vNom, 1.0) / NominalRampSeconds;

        public static (double UpRate, double DownRate) ResolveRampRates(
            double vNom,
            double blackStartVoltageRampVs,
            double voltageRampUpVs,
            double voltageRampDownVs)
        {
            double floor = FiveSecondFloorVs(vNom);
            double up = voltageRampUpVs > 0
                ? voltageRampUpVs
                : Math.Max(Math.Max(0, blackStartVoltageRampVs), floor);
            double down = voltageRampDownVs > 0 ? voltageRampDownVs : up;
            return (up, down);
        }

        public static double ResolveNq(bool enabled, double nq, double vNom, double ratedKw)
        {
            if (!enabled)
                return 0;
            if (nq > 0)
                return nq;
            return DefaultDroopPercent * Math.Max(vNom, 1.0) / Math.Max(ratedKw, 1.0);
        }

        public static double ResolveDeadband(double deadbandKvar, double ratedKw) =>
            deadbandKvar > 0 ? deadbandKvar : DefaultDeadbandFraction * Math.Max(ratedKw, 1.0);

        public static double ResolveVmaxPu(double vmaxPu) =>
            vmaxPu > 0 ? vmaxPu : DefaultVmaxPu;

        public static void ApplyResolved(PcsPhysicalConfig pcsCfg, DevicePcsConfig deviceCfg)
        {
            var (up, down) = ResolveRampRates(
                pcsCfg.AcVoltageNominal,
                pcsCfg.BlackStartVoltageRampVs,
                pcsCfg.VoltageRampUpVs,
                pcsCfg.VoltageRampDownVs);
            deviceCfg.VoltageRampUpVs = up;
            deviceCfg.VoltageRampDownVs = down;
            deviceCfg.QvDroopEnabled = pcsCfg.QvDroopEnabled;
            deviceCfg.QvDroopCoefficientVPerKvar = ResolveNq(
                pcsCfg.QvDroopEnabled,
                pcsCfg.QvDroopCoefficientVPerKvar,
                pcsCfg.AcVoltageNominal,
                pcsCfg.RatedPower);
            deviceCfg.QvDroopDeadbandKvar = ResolveDeadband(pcsCfg.QvDroopDeadbandKvar, pcsCfg.RatedPower);
            deviceCfg.QvDroopQ0Kvar = pcsCfg.QvDroopQ0Kvar;
            deviceCfg.QvDroopEnableAfterSoftStartOnly = pcsCfg.QvDroopEnableAfterSoftStartOnly;
            deviceCfg.QvDroopVmaxPu = ResolveVmaxPu(pcsCfg.QvDroopVmaxPu);
            deviceCfg.PfDroopEnabled = pcsCfg.PfDroopEnabled;
            deviceCfg.PfDroopCoefficientHzPerKw = pcsCfg.PfDroopEnabled
                ? PfDroopRegulator.ResolveMp(pcsCfg.PfDroopCoefficientHzPerKw, pcsCfg.RatedPower)
                : 0;
            deviceCfg.PfDroopDeadbandKw = PfDroopRegulator.ResolveDeadband(
                pcsCfg.PfDroopDeadbandKw, pcsCfg.RatedPower);
            deviceCfg.PfDroopP0Kw = pcsCfg.PfDroopP0Kw;
            deviceCfg.PllEnableVoltagePu = pcsCfg.PllEnableVoltagePu > 0 ? pcsCfg.PllEnableVoltagePu : 0.20;
            deviceCfg.PllTauSec = pcsCfg.PllTauSec > 0 ? pcsCfg.PllTauSec : 0.10;
            deviceCfg.PreSyncEnableVoltagePu = pcsCfg.PreSyncEnableVoltagePu > 0
                ? pcsCfg.PreSyncEnableVoltagePu
                : 0.70;
            deviceCfg.PreSyncVoltageWindowPu = pcsCfg.PreSyncVoltageWindowPu > 0
                ? pcsCfg.PreSyncVoltageWindowPu
                : 0.05;
            deviceCfg.PreSyncFrequencyWindowHz = pcsCfg.PreSyncFrequencyWindowHz > 0
                ? pcsCfg.PreSyncFrequencyWindowHz
                : 0.2;
            deviceCfg.PreSyncPhaseWindowDeg = pcsCfg.PreSyncPhaseWindowDeg > 0
                ? pcsCfg.PreSyncPhaseWindowDeg
                : 10;
            deviceCfg.BlackStartSteadyLossShareMode = pcsCfg.BlackStartSteadyLossShareMode;
            // ≤0 表示立即均分，不得改写成默认 5 s。
            deviceCfg.BlackStartJoinShareRampSec = pcsCfg.BlackStartJoinShareRampSec;
        }
    }
}
