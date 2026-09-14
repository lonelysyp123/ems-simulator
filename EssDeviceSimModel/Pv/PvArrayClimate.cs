namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>光伏方阵联调气候：环境温度 + 光照入射角，替代按时刻正弦的辐照。</summary>
    public sealed class PvArrayClimate
    {
        public const double DefaultAmbientC = 25;
        public const double DefaultIncidenceDeg = 90;
        public const double DefaultAlbedo = 0;

        public double AmbientTemperatureC { get; private set; } = DefaultAmbientC;
        public double IncidenceAngleDeg { get; private set; } = DefaultIncidenceDeg;
        public double Albedo { get; private set; } = DefaultAlbedo;
        public double CellTemperatureC { get; internal set; } = DefaultAmbientC;
        public double PlaneOfArrayWm2 => PvIrradianceModel.EvaluatePlaneOfArrayWm2(IncidenceAngleDeg);
        public double RearWm2 { get; internal set; }
        public double AvailableAcPowerKw { get; internal set; }
        public double ActivePowerKw { get; internal set; }
        public double DcVoltageV { get; internal set; }
        public double DcCurrentA { get; internal set; }
        public string LimitReason { get; internal set; } = "停机";

        public void SetAmbientTemperatureC(double ambientC) =>
            AmbientTemperatureC = Math.Clamp(ambientC, -40, 90);

        public void SetIncidenceAngleDeg(double incidenceDeg) =>
            IncidenceAngleDeg = Math.Clamp(incidenceDeg, 0, 180);

        public void SetAlbedo(double albedo) =>
            Albedo = Math.Clamp(albedo, 0, 1);
    }
}
