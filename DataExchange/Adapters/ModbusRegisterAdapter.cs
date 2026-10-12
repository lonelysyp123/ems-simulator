using EssSimulator;

namespace EssSimulator.DataExchange.Adapters
{
    public sealed class ModbusRegisterAdapter : IModbusRegisterAdapter
    {
        private readonly IModbusSlave _slave;
        private readonly ModbusParser _parser;
        private readonly DeviceInfoDto _deviceInfo;

        public ModbusRegisterAdapter(IModbusSlave slave, ModbusParser parser, DeviceInfoDto deviceInfo)
        {
            _slave = slave;
            _parser = parser;
            _deviceInfo = deviceInfo;
        }

        public void WriteDefaults(IReadOnlyDictionary<string, object> defaults)
        {
            if (defaults.Count == 0)
                return;

            WriteWithSuppressedNotifications(new Dictionary<string, object>(defaults), applyScale: true);
        }

        public void WritePoints(IReadOnlyDictionary<string, object> values, byte? slaveId = null, bool applyScale = true)
        {
            if (values.Count == 0)
                return;

            WriteWithSuppressedNotifications(new Dictionary<string, object>(values), slaveId, applyScale);
        }

        private void WriteWithSuppressedNotifications(
            Dictionary<string, object> values,
            byte? slaveId = null,
            bool applyScale = true)
        {
            byte targetSlaveId = slaveId ?? _deviceInfo.slaveId;
            if (_slave is ModbusSlave modbusSlave)
            {
                using (modbusSlave.SuppressWriteNotifications())
                    modbusSlave.Write(values, targetSlaveId, applyScale);
                return;
            }

            _slave.Write(values, targetSlaveId, applyScale);
        }

        public Dictionary<string, object> ReadAllControlRaw(IReadOnlyList<string> paramNames, byte? slaveId = null)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var allRaw = _slave.Read(slaveId ?? _deviceInfo.slaveId);
            if (allRaw == null || allRaw.Count == 0)
                return result;

            foreach (var name in paramNames)
            {
                if (allRaw.TryGetValue(name, out var raw))
                    result[name] = raw;
            }

            return result;
        }

        public object? ReadParsedPoint(string paramName, byte? slaveId = null)
        {
            var raw = !slaveId.HasValue || slaveId.Value == _deviceInfo.slaveId
                ? _slave.Read(paramName)
                : _slave.Read(slaveId.Value)?.GetValueOrDefault(paramName);
            if (raw == null)
                return null;

            var parsed = _parser.DataParse(new Dictionary<string, object> { { paramName, raw } });
            return parsed.TryGetValue(paramName, out var val) ? val : null;
        }
    }
}
