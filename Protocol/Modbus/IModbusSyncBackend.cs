namespace EssSimulator.Protocol.Modbus
{
    /// <summary>Modbus 与仿真之间的数据同步后端（DataExchangeSession 或 RegisterOnlyBackend）。</summary>
    public interface IModbusSyncBackend
    {
        void Start();
        void Stop();
        void SetDataObjectByMesurePointName(string name, object value);
        /// <summary>写簇级控制点（门限等）；非 DataExchange / 无 rack 时返回 false。</summary>
        bool TrySetRackControl(int rackIndex, string name, object value, out string message);
        void PublishControlToSlave(string name, object value);
        void InvalidateDataShadow(string name);
        object? GetDataObjectByMesurePointName(string name, IModbusSlave slave, ModbusParser parser);
    }
}
