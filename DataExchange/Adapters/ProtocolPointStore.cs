namespace EssSimulator.DataExchange.Adapters
{
    /// <summary>
    /// 内存点影子 + 可选 Modbus 寄存器镜像。
    /// 遥测/控制写入先更新影子并通知订阅方（61850 URCB），再镜像到寄存器。
    /// </summary>
    public sealed class ProtocolPointStore : IProtocolPointStore
    {
        private readonly IModbusRegisterAdapter _modbus;
        private readonly Dictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        public ProtocolPointStore(IModbusRegisterAdapter modbus)
        {
            _modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
        }

        public event EventHandler<ProtocolPointChangedEventArgs>? PointsChanged;

        public void WriteDefaults(IReadOnlyDictionary<string, object> defaults)
        {
            if (defaults.Count == 0)
                return;

            WriteCore(defaults, slaveId: null, applyScale: true, notify: false);
        }

        public void WritePoints(IReadOnlyDictionary<string, object> values, byte? slaveId = null, bool applyScale = true)
        {
            if (values.Count == 0)
                return;

            WriteCore(values, slaveId, applyScale, notify: true);
        }

        public Dictionary<string, object> ReadAllControlRaw(IReadOnlyList<string> paramNames, byte? slaveId = null) =>
            _modbus.ReadAllControlRaw(paramNames, slaveId);

        public object? ReadParsedPoint(string paramName, byte? slaveId = null)
        {
            if (slaveId.HasValue)
                return _modbus.ReadParsedPoint(paramName, slaveId);

            lock (_gate)
            {
                if (_cache.TryGetValue(paramName, out var cached))
                    return cached;
            }

            return _modbus.ReadParsedPoint(paramName);
        }

        public object? GetCachedValue(string paramName)
        {
            lock (_gate)
            {
                if (_cache.TryGetValue(paramName, out var cached))
                    return cached;
            }

            return _modbus.ReadParsedPoint(paramName);
        }

        private void WriteCore(
            IReadOnlyDictionary<string, object> values,
            byte? slaveId,
            bool applyScale,
            bool notify)
        {
            lock (_gate)
            {
                foreach (var pair in values)
                    _cache[pair.Key] = pair.Value;
            }

            _modbus.WritePoints(values, slaveId, applyScale);

            if (notify)
                PointsChanged?.Invoke(this, new ProtocolPointChangedEventArgs(values));
        }
    }
}
