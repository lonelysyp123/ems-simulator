using EssSimulator.Protocol.Modbus;

namespace EssSimulator
{
    /// <summary>
    /// 协议层可编排设备接口：ModbusSimServer 与 LocalControlModbusServer 共用，
    /// 供 <see cref="EssSimulator.Protocol.Modbus.ProtocolLayerManager"/> 做端口分配、启停与热重建。
    /// </summary>
    public interface IProtocolLayerServer
    {
        string ServerName { get; }
        bool IsOnline { get; }
        /// <summary>当前生效端口。</summary>
        int Port { get; }
        /// <summary>当前生效从站号。</summary>
        byte SlaveId { get; }
        /// <summary>BMS 簇级从站数量（非 BMS 设备为 0）。</summary>
        int RackCount { get; }
        /// <summary>已加载的点表（bank + 可选 rack），供协议层地址查重使用。</summary>
        ModbusPointMap PointMap { get; }

        bool Start(int maxRetries = 30);
        void Stop();
        /// <summary>调整端口/从站号（要求先离线；由协议层管理器在重建时调用）。</summary>
        void Reconfigure(int port, byte slaveId);
    }
}
