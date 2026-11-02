namespace EssSimulator
{
    /// <summary>
    /// 统一 Modbus 从站对外接口（普通与BMS）
    /// </summary>
    public interface IModbusSlave
    {
        void DeviceConnect();
        void DeviceDisconnect();
        bool GetCommunicatorState();

        // 读取当前从站的所有数据（参数名->原始字节或值）
        Dictionary<string, object>? Read(byte slaveId = 1);

        // 读取当前从站的数据（参数名->原始字节或值）
        byte[]? Read(string paramName);

        // 写入数据到从站（applyScale=false 时按原始寄存器值写入，与 EMS/mbpoll 一致）
        bool Write(System.Collections.Generic.Dictionary<string, object> data, byte slaveId = 1, bool applyScale = true);
    }
}