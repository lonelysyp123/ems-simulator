using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Data;

namespace EssSimulator.Tests.Protocol;

public class ModbusTcpSessionTests
{
    [Fact]
    public async Task MasterRead_ReturnsWrittenRegister_AndUnknownSlaveKeepsConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var factory = new ModbusFactory();
        var network = factory.CreateSlaveNetwork(listener);
        var store = new SlaveDataStore();
        store.HoldingRegisters.WritePoints(100, new ushort[] { 123 });
        network.AddSlave(factory.CreateSlave(1, store));

        using var cts = new CancellationTokenSource();
        var accept = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _ = ServeAsync(client, network, factory, cts.Token);
            }
        });

        try
        {
            using var tcp = new TcpClient("127.0.0.1", port) { ReceiveTimeout = 2000, SendTimeout = 2000 };
            var master = factory.CreateMaster(tcp);

            Assert.Equal((ushort)123, master.ReadHoldingRegisters(1, 100, 1)[0]);

            var missing = Assert.Throws<SlaveException>(() => master.ReadHoldingRegisters(9, 100, 1));
            Assert.Equal((byte)SlaveExceptionCodes.SlaveDeviceFailure, missing.SlaveExceptionCode);

            Assert.Equal((ushort)123, master.ReadHoldingRegisters(1, 100, 1)[0]);
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await accept.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task ServeAsync(
        TcpClient client,
        IModbusSlaveNetwork network,
        IModbusFactory factory,
        CancellationToken cancellationToken)
    {
        try
        {
            client.NoDelay = true;
            await EssSimulator.Protocol.Modbus.ModbusTcpSession.RunAsync(
                client.GetStream(), network, factory, cancellationToken);
        }
        finally
        {
            try { client.Close(); } catch { /* 会话结束 */ }
        }
    }
}
