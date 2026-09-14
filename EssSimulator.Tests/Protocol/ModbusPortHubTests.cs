using System.Net.Sockets;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests;

public class ModbusPortHubTests
{
    private static int NextPort() => Random.Shared.Next(42000, 58000);

    private static MapEntry Entry(string name, int addr, int fc = 6, int size = 16) =>
        new() { ParamName = name, Address = addr, FunctionCode = fc, Size = size };

    [Fact]
    public void SamePort_DifferentSlaveIds_BothAttach_IsolatedDataStores()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        try
        {
            var a = hub.AttachDevice(port, 1, "devA", new[] { Entry("A", 100) });
            var b = hub.AttachDevice(port, 2, "devB", new[] { Entry("B", 100) }); // 同地址但不同从站号，无需查重

            Assert.True(a.Ok);
            Assert.True(b.Ok);
            Assert.True(hub.IsPortListening(port));
            Assert.NotSame(a.DataStore, b.DataStore);

            var attached = hub.GetAttachedDevices(port);
            Assert.Equal(2, attached.Count);
            Assert.Contains("devA", attached[1]);
            Assert.Contains("devB", attached[2]);
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void SamePort_SameSlaveId_NonOverlapping_SharesDataStore()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        try
        {
            var a = hub.AttachDevice(port, 1, "emu", new[] { Entry("E", 0) });
            var b = hub.AttachDevice(port, 1, "lc", new[] { Entry("L", 100) });

            Assert.True(a.Ok);
            Assert.True(b.Ok);
            Assert.Same(a.DataStore, b.DataStore);
            Assert.Same(a.DataStore, hub.GetDataStore(port, 1));

            var attached = hub.GetAttachedDevices(port);
            Assert.Single(attached);
            Assert.Equal(2, attached[1].Count);
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void SamePort_SameSlaveId_OverlappingAddresses_AttachFails()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        try
        {
            var a = hub.AttachDevice(port, 1, "emu", new[] { Entry("E", 100) });
            var b = hub.AttachDevice(port, 1, "lc", new[] { Entry("L", 100) });

            Assert.True(a.Ok);
            Assert.False(b.Ok);
            Assert.NotEmpty(b.Errors);
            Assert.Contains(b.Errors, e => e.Contains("emu") && e.Contains("lc"));

            // 冲突设备不应进入槽位设备清单
            var attached = hub.GetAttachedDevices(port);
            Assert.DoesNotContain("lc", attached[1]);
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void DetachLastDevice_ReleasesPortListener()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        try
        {
            var a = hub.AttachDevice(port, 1, "emu", new[] { Entry("E", 0) });
            var b = hub.AttachDevice(port, 1, "lc", new[] { Entry("L", 100) });
            Assert.True(a.Ok && b.Ok);
            Assert.True(hub.IsPortListening(port));

            hub.DetachDevice(port, 1, "emu");
            Assert.True(hub.IsPortListening(port)); // lc 仍在，监听保持

            hub.DetachDevice(port, 1, "lc");
            Assert.False(hub.IsPortListening(port));
            Assert.Null(hub.GetDataStore(port, 1));
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void EmptyAllowList_TcpWriteRead_StillWorks()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        try
        {
            hub.SetAllowList(MustCreate(true));
            Assert.True(hub.AttachDevice(port, 1, "dev", new[] { Entry("P1", 100) }).Ok);

            using var client = new TcpClient("127.0.0.1", port);
            var master = new NModbus.ModbusFactory().CreateMaster(client);
            master.WriteSingleRegister(1, 100, 42);
            Assert.Equal((ushort)42, master.ReadHoldingRegisters(1, 100, 1)[0]);
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void AllowLoopbackTrue_NonEmptyList_LocalTcpWriteReadWorks()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        try
        {
            hub.SetAllowList(MustCreate(true, "10.1.2.3"));
            Assert.True(hub.AttachDevice(port, 1, "dev", new[] { Entry("P1", 100) }).Ok);

            using var client = new TcpClient("127.0.0.1", port);
            var master = new NModbus.ModbusFactory().CreateMaster(client);
            master.WriteSingleRegister(1, 100, 77);
            Assert.Equal((ushort)77, master.ReadHoldingRegisters(1, 100, 1)[0]);
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void AllowLoopbackFalse_LocalTcpIsRejected()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        try
        {
            hub.SetAllowList(MustCreate(false, "10.1.2.3"));
            Assert.True(hub.AttachDevice(port, 1, "dev", new[] { Entry("P1", 100) }).Ok);

            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            Assert.True(WaitUntilDisconnected(client), "白名单拒绝后本机连接应被关闭");
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    private static ModbusIpAllowList MustCreate(bool allowLoopback, params string[] addresses)
    {
        Assert.True(ModbusIpAllowList.TryCreate(allowLoopback, addresses, out var list, out var errors),
            string.Join("; ", errors));
        return list;
    }

    private static bool WaitUntilDisconnected(TcpClient client, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var socket = client.Client;
                if (socket == null || !client.Connected)
                    return true;
                if (socket.Poll(1000, SelectMode.SelectRead) && socket.Available == 0)
                    return true;
            }
            catch
            {
                return true;
            }
            Thread.Sleep(20);
        }
        return false;
    }
}
