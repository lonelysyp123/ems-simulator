namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>
    /// 预同步窗口：黑启动使能后比较待发 V/f/θ 与母线。
    /// 母线最低电压由 PLL（<c>PllEnableVoltagePu</c>）启锁承担，不再另设预同步电压门槛。
    /// </summary>
    public sealed class PreSyncSupervisor
    {
        /// <summary>历史配置项，已不参与切入判定；保留以免旧配置报错。</summary>
        public double EnableVoltagePu { get; }
        public double VoltageWindowPu { get; }
        public double FrequencyWindowHz { get; }
        public double PhaseWindowRad { get; }

        public PreSyncSupervisor(
            double enableVoltagePu = 0.70,
            double voltageWindowPu = 0.05,
            double frequencyWindowHz = 0.2,
            double phaseWindowDeg = 10)
        {
            EnableVoltagePu = Math.Clamp(enableVoltagePu, 0, 1.5);
            VoltageWindowPu = Math.Max(0, voltageWindowPu);
            FrequencyWindowHz = Math.Max(0, frequencyWindowHz);
            PhaseWindowRad = Math.Abs(phaseWindowDeg) * Math.PI / 180.0;
        }

        public bool IsPreSyncActive(bool blackStartEnabled) => blackStartEnabled;

        public bool IsReadyToCutIn(
            double vOwn,
            double fOwn,
            double thetaOwn,
            double vBus,
            double fBus,
            double thetaBus,
            double vNom,
            bool blackStartEnabled)
        {
            if (!IsPreSyncActive(blackStartEnabled))
                return false;

            double vn = Math.Max(vNom, 1.0);
            if (Math.Abs(vOwn - vBus) / vn > VoltageWindowPu)
                return false;
            if (Math.Abs(fOwn - fBus) > FrequencyWindowHz)
                return false;
            return Math.Abs(PhaseIntegrator.AngleErrorRad(thetaOwn, thetaBus)) <= PhaseWindowRad;
        }
    }
}
