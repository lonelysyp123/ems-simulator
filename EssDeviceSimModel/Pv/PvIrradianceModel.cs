namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>
    /// 平面辐照：联调入射角按太阳高度角。90° 正对 1000 W/㎡，0°/180° 掠射为 0。
    /// G = Gpeak · sin(θ)，θ∈(0, 180)。
    /// </summary>
    public static class PvIrradianceModel
    {
        public const double PeakWm2 = 1000;

        public static double EvaluatePlaneOfArrayWm2(double incidenceAngleDeg, double beamWm2 = PeakWm2)
        {
            double beam = Math.Max(0, beamWm2);
            if (beam <= 0)
                return 0;

            double thetaDeg = incidenceAngleDeg;
            if (thetaDeg <= 0 || thetaDeg >= 180)
                return 0;

            return beam * Math.Sin(thetaDeg * Math.PI / 180.0);
        }
    }
}
