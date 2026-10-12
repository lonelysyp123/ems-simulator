namespace EssSimulator.DataExchange.Adapters
{
    /// <summary>协议无关点值变化（遥测回写或控制写入）。</summary>
    public sealed class ProtocolPointChangedEventArgs : EventArgs
    {
        public ProtocolPointChangedEventArgs(IReadOnlyDictionary<string, object> values)
        {
            Values = values;
        }

        public IReadOnlyDictionary<string, object> Values { get; }
    }

    /// <summary>
    /// 协议门面共用的点影子：ParamName → 工程值。
    /// Modbus 寄存器与 IEC 61850 IED 都读写同一份影子。
    /// </summary>
    public interface IProtocolPointStore : IModbusRegisterAdapter
    {
        event EventHandler<ProtocolPointChangedEventArgs>? PointsChanged;

        /// <summary>最近一次写入的工程值；尚未写入时回退到解析后的寄存器值。</summary>
        object? GetCachedValue(string paramName);
    }
}
