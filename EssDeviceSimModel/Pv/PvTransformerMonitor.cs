namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>箱变油温/绕组温度（转发 ID 1/2），并给出 Logger DIN 用的告警/跳闸。</summary>
    public sealed class PvTransformerMonitor
    {
        public const double OilAlarmC = 80;
        public const double OilTripC = 95;
        public const double WindingAlarmC = 105;
        public const double WindingTripC = 120;

        public double OilTemperatureC { get; private set; }
        public double WindingTemperatureC { get; private set; }
        public bool OilAlarm => OilTemperatureC >= OilAlarmC;
        public bool OilTrip => OilTemperatureC >= OilTripC;
        public bool WindingAlarm => WindingTemperatureC >= WindingAlarmC;
        public bool WindingTrip => WindingTemperatureC >= WindingTripC;

        public void Update(double ambientC, double loadPu)
        {
            loadPu = Math.Clamp(loadPu, 0, 1.2);
            OilTemperatureC = ambientC + 18.0 * loadPu;
            WindingTemperatureC = OilTemperatureC + 12.0 * loadPu;
        }
    }
}
