using System.Net.Sockets;
using log4net;
using NModbus;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// 一条 Modbus TCP 连接：异步读满一帧，交给已挂载从站，再写回响应。
    /// 等待下一帧时不占工作线程。同一条连接按顺序处理，不流水线。
    /// </summary>
    internal static class ModbusTcpSession
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ModbusTcpSession));

        /// <summary>
        /// 同时处理的请求数。寄存器读写会占住线程池线程并和遥测写抢锁，
        /// 不设上限时两套 EMS 轮询会把 Kestrel / SignalR 饿死，一次图协商超时。
        /// </summary>
        private static readonly SemaphoreSlim DispatchSlots = new(SlotCount, SlotCount);

        private static int SlotCount => Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

        public static async Task RunAsync(
            NetworkStream stream,
            IModbusSlaveNetwork network,
            IModbusFactory factory,
            CancellationToken cancellationToken)
        {
            var header = new byte[ModbusTcpFrame.HeaderLength];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false))
                        return;

                    if (header[2] != 0 || header[3] != 0 || !ModbusTcpFrame.TryGetBodyLength(header, out ushort length))
                        return;

                    var body = new byte[length];
                    if (!await ReadExactAsync(stream, body, cancellationToken).ConfigureAwait(false))
                        return;

                    ushort transactionId = ModbusTcpFrame.ReadTransactionId(header);
                    await DispatchSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                    byte[] response;
                    try
                    {
                        response = Dispatch(factory, network, body);
                    }
                    finally
                    {
                        DispatchSlots.Release();
                    }

                    byte[] frame = ModbusTcpFrame.PrefixHeader(transactionId, response);
                    await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 监听关闭
            }
            catch (IOException)
            {
                // 对端断开
            }
            catch (ObjectDisposedException)
            {
                // 套接字已释放
            }
            catch (Exception ex)
            {
                Log.Warn("Modbus TCP 会话异常，关闭该连接", ex);
            }
        }

        internal static byte[] Dispatch(IModbusFactory factory, IModbusSlaveNetwork network, byte[] body)
        {
            if (body.Length < 2)
                return ExceptionBody(body.Length > 0 ? body[0] : (byte)0, 0, SlaveExceptionCodes.IllegalDataValue);

            byte unitId = body[0];
            byte function = body[1];
            if (!TryGetSlave(network, unitId, out NModbus.IModbusSlave? slave) || slave == null)
                return ExceptionBody(unitId, function, SlaveExceptionCodes.SlaveDeviceFailure);

            IModbusFunctionService service;
            try
            {
                service = factory.GetFunctionService(function);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or InvalidOperationException)
            {
                return ExceptionBody(unitId, function, SlaveExceptionCodes.IllegalFunction);
            }

            IModbusMessage request;
            try
            {
                request = service.CreateRequest(body);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
            {
                return ExceptionBody(unitId, function, SlaveExceptionCodes.IllegalDataValue);
            }

            try
            {
                IModbusMessage response = slave.ApplyRequest(request);
                return response.MessageFrame;
            }
            catch (SlaveException ex)
            {
                return ExceptionBody(unitId, function, ex.SlaveExceptionCode);
            }
        }

        private static bool TryGetSlave(IModbusSlaveNetwork network, byte unitId, out NModbus.IModbusSlave? slave)
        {
            try
            {
                slave = network.GetSlave(unitId);
                return slave != null;
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or ArgumentException)
            {
                slave = null;
                return false;
            }
        }

        private static byte[] ExceptionBody(byte unitId, byte function, byte code) =>
            new[] { unitId, (byte)(function | 0x80), code };

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    return false;
                offset += read;
            }

            return true;
        }
    }
}
