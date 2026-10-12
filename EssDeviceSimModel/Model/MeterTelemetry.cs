namespace EssSimulator.EssDeviceSimModel.Model
{
    public sealed class MeterTelemetry
    {
        public AcInternalQuantities Primary { get; init; } = new();
        public AcInternalQuantities Secondary { get; init; } = new();
        public AcTerminalQuantities ReportedTerminal { get; init; } = new();
        public double ForwardActiveEnergyKwh { get; init; }
        public double ReverseActiveEnergyKwh { get; init; }
        public double ForwardReactiveEnergyKvarh { get; init; }
        public double ReverseReactiveEnergyKvarh { get; init; }
    }

    public static class MeterQuantityConverter
    {
        public static AcInternalQuantities ToSecondary(AcInternalQuantities primary, PtConfig pt, CtConfig ct)
        {
            double ptRatio = pt.Ratio;
            double ctRatio = ct.Ratio;
            if (ptRatio <= 0 || ctRatio <= 0)
                return new AcInternalQuantities { Connection = primary.Connection };

            return new AcInternalQuantities
            {
                Connection = pt.Connection,
                LineVoltageV = primary.LineVoltageV / ptRatio,
                LineCurrentA = primary.LineCurrentA / ctRatio,
                PhaseAngleDeg = primary.PhaseAngleDeg,
                FrequencyHz = primary.FrequencyHz
            };
        }

        public static AcInternalQuantities ToReportedPrimary(
            AcInternalQuantities secondary,
            PtConfig pt,
            CtConfig ct,
            MeterReportedQuantity reportedQuantity)
        {
            if (reportedQuantity == MeterReportedQuantity.Secondary)
                return secondary;

            double ptRatio = pt.Ratio;
            double ctRatio = ct.Ratio;
            return new AcInternalQuantities
            {
                Connection = pt.Connection,
                LineVoltageV = secondary.LineVoltageV * ptRatio,
                LineCurrentA = secondary.LineCurrentA * ctRatio,
                PhaseAngleDeg = secondary.PhaseAngleDeg,
                FrequencyHz = secondary.FrequencyHz
            };
        }

        public static MeterTelemetry CreateTelemetry(
            AcInternalQuantities primary,
            PtConfig pt,
            CtConfig ct,
            MeterReportedQuantity reportedQuantity)
        {
            var secondary = ToSecondary(primary, pt, ct);
            var reported = ToReportedPrimary(secondary, pt, ct, reportedQuantity);

            return new MeterTelemetry
            {
                Primary = primary,
                Secondary = secondary,
                ReportedTerminal = AcQuantityConverter.ToTerminal(reported)
            };
        }
    }
}
