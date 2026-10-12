namespace EssSimulator.DataExchange.Adapters
{
    public interface IModbusRegisterAdapter
    {
        // null 跟随当前设备从站号，显式值始终是绝对从站号。
        void WriteDefaults(IReadOnlyDictionary<string, object> defaults);
        void WritePoints(IReadOnlyDictionary<string, object> values, byte? slaveId = null, bool applyScale = true);
        Dictionary<string, object> ReadAllControlRaw(IReadOnlyList<string> paramNames, byte? slaveId = null);
        object? ReadParsedPoint(string paramName, byte? slaveId = null);
    }
}
