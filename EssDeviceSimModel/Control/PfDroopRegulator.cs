namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>
    /// 构网 P-f 下垂：f = f0 − mp × (P − P0)；有功死区内不调频。不含 Q、V。
    /// </summary>
    public sealed class PfDroopRegulator
    {
        public const double DefaultRatedPowerDropHz = 0.5;

        public double Mp { get; }
        public double P0Kw { get; }
        public double DeadbandKw { get; }
        public double FMinHz { get; }
        public double FMaxHz { get; }

        public PfDroopRegulator(
            double mp,
            double p0Kw,
            double deadbandKw,
            double fMinHz,
            double fMaxHz)
        {
            Mp = Math.Max(0, mp);
            P0Kw = p0Kw;
            DeadbandKw = Math.Max(0, deadbandKw);
            FMinHz = fMinHz;
            FMaxHz = fMaxHz < fMinHz ? fMinHz : fMaxHz;
        }

        public static double ResolveMp(double mp, double ratedKw, double dropHzAtRated = DefaultRatedPowerDropHz) =>
            mp > 0 ? mp : Math.Max(0, dropHzAtRated) / Math.Max(ratedKw, 1.0);

        public static double ResolveDeadband(double deadbandKw, double ratedKw) =>
            deadbandKw > 0 ? deadbandKw : 0.02 * Math.Max(ratedKw, 1.0);

        public double Compute(double f0Hz, double pKw)
        {
            if (Mp <= 0)
                return Clamp(f0Hz);

            double dp = pKw - P0Kw;
            if (Math.Abs(dp) <= DeadbandKw)
                return Clamp(f0Hz);

            return Clamp(f0Hz - Mp * dp);
        }

        private double Clamp(double f) => Math.Clamp(f, FMinHz, FMaxHz);
    }
}
