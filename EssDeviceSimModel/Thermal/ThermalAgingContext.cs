using EssSimulator.Configuration;

namespace EssSimulator.EssDeviceSimModel.Thermal
{
    /// <summary>电芯日历老化上下文（由 <see cref="PlantThermalSystem"/> 每步刷新）。</summary>
    public static class ThermalAgingContext
    {
        public static bool Enabled { get; set; } = true;
        public static double ReferenceCelsius { get; set; } = 25;
        public static double ArrheniusB { get; set; } = 5000;
        public static double CalendarAgingPerYearAtRef { get; set; } = 0.02;

        public static void ApplyFrom(ThermalFeedbackConfig cfg)
        {
            if (cfg == null)
            {
                Enabled = false;
                return;
            }

            Enabled = cfg.TemperatureAgingEnabled;
            ReferenceCelsius = cfg.AgingReferenceCelsius;
            ArrheniusB = cfg.AgingArrheniusB;
            CalendarAgingPerYearAtRef = Math.Max(0, cfg.CalendarAgingPerYearAtRef);
        }

        /// <summary>相对参考温度的 Arrhenius 倍率。</summary>
        public static double ArrheniusFactor(double temperatureCelsius)
        {
            double tK = temperatureCelsius + 273.15;
            double tRef = ReferenceCelsius + 273.15;
            if (tK < 200 || tRef < 200)
                return 1;
            return Math.Exp(ArrheniusB * (1.0 / tRef - 1.0 / tK));
        }
    }
}
