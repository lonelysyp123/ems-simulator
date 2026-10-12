namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>
    /// 构网 Q-V 下垂：Vref = Vramp − nq × (Q − Q0)；无功死区内不改电压参考。不含 P-f。
    /// </summary>
    public sealed class QvDroopRegulator
    {
        public double Nq { get; }
        public double Q0Kvar { get; }
        public double DeadbandKvar { get; }
        public double VMin { get; }
        public double VMax { get; }

        public QvDroopRegulator(
            double nq,
            double q0Kvar,
            double deadbandKvar,
            double vMin,
            double vMax)
        {
            Nq = Math.Max(0, nq);
            Q0Kvar = q0Kvar;
            DeadbandKvar = Math.Max(0, deadbandKvar);
            VMin = vMin;
            VMax = vMax < vMin ? vMin : vMax;
        }

        public double Compute(double vRamp, double qKvar)
        {
            if (Nq <= 0)
                return Clamp(vRamp);

            double dq = qKvar - Q0Kvar;
            if (Math.Abs(dq) <= DeadbandKvar)
                return Clamp(vRamp);

            return Clamp(vRamp - Nq * dq);
        }

        private double Clamp(double v) => Math.Clamp(v, VMin, VMax);
    }
}
