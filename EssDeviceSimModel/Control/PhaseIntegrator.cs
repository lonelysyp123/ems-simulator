namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>构网相位积分：θ̇ = 2πf，包到 [−π, π)。</summary>
    public sealed class PhaseIntegrator
    {
        public double ThetaRad { get; private set; }

        public void Reset(double thetaRad = 0) => ThetaRad = WrapToPi(thetaRad);

        public void Step(double freqHz, double dt)
        {
            if (dt <= 0)
                return;
            ThetaRad = WrapToPi(ThetaRad + 2.0 * Math.PI * freqHz * dt);
        }

        public static double WrapToPi(double rad)
        {
            const double twoPi = 2.0 * Math.PI;
            rad %= twoPi;
            if (rad >= Math.PI)
                rad -= twoPi;
            else if (rad < -Math.PI)
                rad += twoPi;
            return rad;
        }

        public static double AngleErrorRad(double a, double b) => WrapToPi(a - b);
    }
}
