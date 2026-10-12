namespace EssSimulator.DataExchange.Pipeline
{
    /// <summary>遥测 / 控制 shadow，仅在值变化时触发 I/O。</summary>
    public sealed class ShadowStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, object?> _telemetry = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object?> _control = new(StringComparer.OrdinalIgnoreCase);

        public void SeedTelemetry(string paramName, object? value)
        {
            lock (_gate)
                _telemetry[paramName] = value;
        }

        public void SeedControl(string paramName, object? value)
        {
            lock (_gate)
                _control[paramName] = value;
        }

        public bool TelemetryChanged(string paramName, object? newValue)
        {
            lock (_gate)
            {
                if (_telemetry.TryGetValue(paramName, out var prev) && ValuesEqual(prev, newValue))
                    return false;

                _telemetry[paramName] = newValue;
                return true;
            }
        }

        public bool TryDetectControlChange(string paramName, object? incoming, out object? previous)
        {
            lock (_gate)
            {
                _control.TryGetValue(paramName, out previous);
                return !ValuesEqual(previous, incoming);
            }
        }

        public void CommitControl(string paramName, object? value)
        {
            lock (_gate)
                _control[paramName] = value;
        }

        public void InvalidateTelemetry(string paramName)
        {
            lock (_gate)
                _telemetry.Remove(paramName);
        }

        /// <summary>清空遥测 shadow（Modbus 重连后 SlaveDataStore 重建，需强制全量回写）。</summary>
        public void ClearTelemetry()
        {
            lock (_gate)
                _telemetry.Clear();
        }

        public static bool ValuesEqual(object? a, object? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            if (a is bool || b is bool)
            {
                bool ba = a is bool bb ? bb : Convert.ToDouble(a) != 0;
                bool bb2 = b is bool bc ? bc : Convert.ToDouble(b) != 0;
                return ba == bb2;
            }

            if (double.TryParse(a.ToString(), out var da) &&
                double.TryParse(b.ToString(), out var db))
                return Math.Abs(da - db) < 1e-9;

            return Equals(a, b);
        }
    }
}
