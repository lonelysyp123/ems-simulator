namespace EssSimulator.LocalControl
{
    /// <summary>黑启动期间 PCS「准备就绪」判据：交流侧实测线电压已跟上孤岛电压设定值。</summary>
    internal static class LcPcsReady
    {
        /// <summary>允许偏差为设定值的 ±5%；设定值为 0 时不判就绪，否则停机态会被误判为已建压。</summary>
        public const double Tolerance = 0.05;

        public static bool IsReady(bool blackStartEnabled, double settingVolts, double vab, double vbc, double vca)
        {
            if (!blackStartEnabled || settingVolts <= 0)
                return false;

            double band = settingVolts * Tolerance;
            return Within(vab, settingVolts, band)
                && Within(vbc, settingVolts, band)
                && Within(vca, settingVolts, band);
        }

        private static bool Within(double measured, double target, double band) =>
            Math.Abs(measured - target) <= band;
    }
}
