namespace EssSimulator.EssDeviceSimModel.Model
{
    public sealed class PtConfig
    {
        public double PrimaryLineVoltageV { get; set; } = 220000;
        public double SecondaryLineVoltageV { get; set; } = 100;
        public ThreePhaseConnection Connection { get; set; } = ThreePhaseConnection.Star;

        public double Ratio => SecondaryLineVoltageV > 1e-9
            ? PrimaryLineVoltageV / SecondaryLineVoltageV
            : 0;
    }

    public sealed class CtConfig
    {
        public double PrimaryCurrentA { get; set; } = 2000;
        public double SecondaryCurrentA { get; set; } = 5;

        public double Ratio => SecondaryCurrentA > 1e-9
            ? PrimaryCurrentA / SecondaryCurrentA
            : 0;
    }

    public sealed class MeterInstanceConfig
    {
        public string? MountDescription { get; set; }
        public PtConfig Pt { get; set; } = new();
        public CtConfig Ct { get; set; } = new();
        public MeterReportedQuantity ReportedQuantity { get; set; } = MeterReportedQuantity.Primary;
        public double BurdenVa { get; set; } = 5;
        public string AccuracyClass { get; set; } = "0.2S";
        /// <summary>
        /// 组态连线解析出的运行时母线 Id（如 BUS_AFTER_MAIN_BRK）。
        /// 空则按默认并网点抽头（主断下游）。
        /// </summary>
        public string? SourceBusId { get; set; }
    }

    public sealed class MeterConfig
    {
        public const string Section = "Meter";

        public MeterInstanceConfig PccMeter { get; set; } = new();
    }
}
