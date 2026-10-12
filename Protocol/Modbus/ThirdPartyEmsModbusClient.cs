using System.Net.Sockets;
using NModbus;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// 第三方 EMS 用 Modbus TCP 主站：按点表条目 FC6 写 / FC4 读，编码与从站一致。
    /// </summary>
    public sealed class ThirdPartyEmsModbusClient : IDisposable
    {
        private const int TimeoutMs = 2000;
        private TcpClient? _tcp;
        private IModbusMaster? _master;
        private byte _slaveId = 1;

        public bool IsConnected => _tcp is { Connected: true } && _master != null;

        public string? LastError { get; private set; }

        public string Host { get; private set; } = "127.0.0.1";

        public int Port { get; private set; }

        public byte SlaveId => _slaveId;

        /// <summary>连接失败返回明确中文原因，不向调用方抛异常。</summary>
        public bool TryConnect(string host, int port, byte slaveId, out string error)
        {
            Disconnect();
            error = ConnectOnce(host, port, slaveId) ?? "";
            if (error.Length == 0)
                return true;

            Thread.Sleep(80);
            error = ConnectOnce(host, port, slaveId) ?? "";
            if (error.Length == 0)
                return true;

            LastError = error;
            return false;
        }

        public void Disconnect()
        {
            try { _master?.Dispose(); }
            catch { /* ignore */ }
            _master = null;
            try { _tcp?.Close(); }
            catch { /* ignore */ }
            try { _tcp?.Dispose(); }
            catch { /* ignore */ }
            _tcp = null;
        }

        public bool TryWrite(MapEntry entry, double engineeringValue, out string error)
        {
            if (!EnsureConnected(out error))
                return false;
            try
            {
                byte[] bytes = ModbusPointCodec.Encode(engineeringValue, entry, applyScale: true);
                ushort[] regs = Common.ConvertBytesToUShorts(bytes);
                ushort addr = (ushort)entry.Address;
                if (regs.Length == 1)
                    _master!.WriteSingleRegister(_slaveId, addr, regs[0]);
                else
                    _master!.WriteMultipleRegisters(_slaveId, addr, regs);
                error = "";
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"写 {entry.ParamName}@{entry.Address} 失败：{Unwrap(ex).Message}";
                LastError = error;
                return false;
            }
        }

        public bool TryRead(MapEntry entry, out double engineeringValue, out string error)
        {
            engineeringValue = 0;
            if (!EnsureConnected(out error))
                return false;
            try
            {
                ushort count = (ushort)Math.Max(1, entry.Size / 16);
                ushort addr = (ushort)entry.Address;
                ushort[] regs = entry.FunctionCode == 4
                    ? _master!.ReadInputRegisters(_slaveId, addr, count)
                    : _master!.ReadHoldingRegisters(_slaveId, addr, count);
                byte[] bytes = UshortsToBytes(regs);
                engineeringValue = Convert.ToDouble(ModbusPointCodec.Decode(bytes, entry));
                error = "";
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"读 {entry.ParamName}@{entry.Address} 失败：{Unwrap(ex).Message}";
                LastError = error;
                return false;
            }
        }

        public void Dispose() => Disconnect();

        private string? ConnectOnce(string host, int port, byte slaveId)
        {
            TcpClient? tcp = null;
            try
            {
                tcp = new TcpClient { ReceiveTimeout = TimeoutMs, SendTimeout = TimeoutMs };
                if (!tcp.ConnectAsync(host, port).Wait(TimeSpan.FromMilliseconds(TimeoutMs)) || !tcp.Connected)
                {
                    tcp.Dispose();
                    return $"连接 {host}:{port} 超时";
                }

                var master = new ModbusFactory().CreateMaster(tcp);
                master.Transport.ReadTimeout = TimeoutMs;
                master.Transport.WriteTimeout = TimeoutMs;
                master.Transport.Retries = 1;
                _tcp = tcp;
                _master = master;
                _slaveId = slaveId;
                Host = host;
                Port = port;
                LastError = null;
                return null;
            }
            catch (Exception ex)
            {
                tcp?.Dispose();
                return $"连接 {host}:{port} 失败：{Unwrap(ex).Message}";
            }
        }

        private bool EnsureConnected(out string error)
        {
            if (IsConnected)
            {
                error = "";
                return true;
            }

            error = LastError ?? "尚未连接到 Modbus 从站";
            return false;
        }

        private static byte[] UshortsToBytes(ushort[] regs)
        {
            var bytes = new byte[regs.Length * 2];
            Buffer.BlockCopy(regs, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static Exception Unwrap(Exception ex) =>
            ex is AggregateException ag ? ag.InnerException ?? ex : ex;
    }
}
