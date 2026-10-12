namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>气象站 PC4 pro（转发 ID 27）遥测。辐照/环境温度为单元 Update 的输入镜像。</summary>
    public sealed class PvWeatherStation
    {
        private DateTime _day = DateTime.MinValue;

        public double AmbientTemperatureC { get; private set; }
        public double ModuleTemperatureC { get; private set; }
        public double HumidityRh { get; set; } = 50;
        public double PressureHpa { get; set; } = 1013.25;
        public double SlopeIrradianceWm2 { get; private set; }
        public double HorizontalIrradianceWm2 { get; private set; }
        public double WindAngleDeg { get; set; }
        public double WindSpeedMs { get; set; }
        public double TotalHorizontalIrradiationWhm2 { get; private set; }
        public double TotalSlopeIrradiationWhm2 { get; private set; }
        public double DailyHorizontalIrradiationWhm2 { get; private set; }
        public double DailySlopeIrradiationWhm2 { get; private set; }

        public void Update(
            double ambientC,
            double moduleTempC,
            double slopeWm2,
            double horizontalWm2,
            DateTime timeStamp,
            TimeSpan step)
        {
            if (_day != DateTime.MinValue && _day.Date != timeStamp.Date)
            {
                DailyHorizontalIrradiationWhm2 = 0;
                DailySlopeIrradiationWhm2 = 0;
            }

            _day = timeStamp;
            AmbientTemperatureC = ambientC;
            ModuleTemperatureC = moduleTempC;
            SlopeIrradianceWm2 = Math.Max(0, slopeWm2);
            HorizontalIrradianceWm2 = Math.Max(0, horizontalWm2);
            double hours = Math.Max(0, step.TotalHours);
            double dSlope = SlopeIrradianceWm2 * hours;
            double dHoriz = HorizontalIrradianceWm2 * hours;
            DailySlopeIrradiationWhm2 += dSlope;
            DailyHorizontalIrradiationWhm2 += dHoriz;
            TotalSlopeIrradiationWhm2 += dSlope;
            TotalHorizontalIrradiationWhm2 += dHoriz;
        }
    }
}
