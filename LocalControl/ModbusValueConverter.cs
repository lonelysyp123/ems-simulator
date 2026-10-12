namespace EssSimulator.LocalControl
{
    internal static class ModbusValueConverter
    {
        public static double ToDouble(object raw) =>
            raw switch
            {
                bool b => b ? 1 : 0,
                byte v => v,
                sbyte v => v,
                short v => v,
                ushort v => v,
                int v => v,
                uint v => v,
                long v => v,
                ulong v => v,
                float v => v,
                double v => v,
                decimal v => (double)v,
                _ => double.TryParse(raw.ToString(), out var parsed) ? parsed : 0
            };

        public static string FormatControlValue(double value) =>
            double.IsNaN(value) ? "<init>" : value.ToString("G");

        public static bool TryNormalizeHvBreakerCommand(double rawValue, out double normalizedValue, out bool closed)
        {
            normalizedValue = 0;
            closed = false;
            int cmd = (int)Math.Round(rawValue);
            if (cmd == 0xAA)
            {
                normalizedValue = 0xAA;
                closed = true;
                return true;
            }

            if (cmd == 0xEE)
            {
                normalizedValue = 0xEE;
                return true;
            }

            return false;
        }
    }
}
