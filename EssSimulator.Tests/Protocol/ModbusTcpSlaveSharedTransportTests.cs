using System.Net.Sockets;
using EssSimulator.Protocol.Modbus;
using NModbus;

namespace EssSimulator.Tests;

/// <summary>
/// 共享传输层下的从站行为：写读往返、换端口热重绑（Rebind）、
/// 同端口同从站号合并点表后控制写按地址路由。
/// </summary>
public class ModbusTcpSlaveSharedTransportTests
{
    private static int NextPort() => Random.Shared.Next(42000, 58000);

    private static MapEntry Entry(string name, int addr, int fc = 6, int size = 16, string type = "u16") =>
        new() { ParamName = name, Address = addr, FunctionCode = fc, Size = size, Type = type };

    [Fact]
    public void Attach_WriteRead_RoundTrip_ThenRebindToNewPort()
    {
        var hub = new ModbusPortHub();
        int port1 = NextPort();
        int port2 = NextPort();
        try
        {
            var dto = new DeviceInfoDto { name = "testDev", port = port1, slaveId = 1 };
            var map = new[] { Entry("P1", 100) };
            var slave = new ModbusTCPSlave(dto, new List<MapEntry[]> { map }, 0, hub);

            slave.DeviceConnect();
            Assert.True(slave.GetCommunicatorState());
            Assert.True(hub.IsPortListening(port1));

            slave.Write(new Dictionary<string, object> { ["P1"] = (ushort)123 }, dto.slaveId);
            Assert.Equal((ushort)123, BitConverter.ToUInt16(slave.Read("P1")));

            // 换端口热重绑：旧监听释放，新端口读写正常（实例不重建）
            slave.DeviceDisconnect();
            Assert.False(hub.IsPortListening(port1));

            dto.port = port2;
            slave.DeviceConnect();
            Assert.True(slave.GetCommunicatorState());
            Assert.True(hub.IsPortListening(port2));

            slave.Write(new Dictionary<string, object> { ["P1"] = (ushort)456 }, dto.slaveId);
            Assert.Equal((ushort)456, BitConverter.ToUInt16(slave.Read("P1")));
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void RackSlaves_OccupyExpandedSlaveIds()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        try
        {
            var dto = new DeviceInfoDto { name = "bmsDev", port = port, slaveId = 1 };
            var bankMap = new[] { Entry("BANK", 10) };
            var rackMap = new[] { Entry("RACK", 20) };
            var slave = new ModbusTCPSlave(dto, new List<MapEntry[]> { bankMap, rackMap }, rackCount: 2, hub);

            slave.DeviceConnect();

            var attached = hub.GetAttachedDevices(port);
            Assert.Equal(3, attached.Count); // bank(1) + rack(2, 3)
            Assert.Contains("bmsDev", attached[1]);
            Assert.Contains("bmsDev#rack1", attached[2]);
            Assert.Contains("bmsDev#rack2", attached[3]);

            slave.DeviceDisconnect();
            Assert.False(hub.IsPortListening(port));
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void MergedSlave_ControlWrite_RoutedOnlyToOwningDevice()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        try
        {
            var dtoA = new DeviceInfoDto { name = "emu", port = port, slaveId = 1 };
            var dtoB = new DeviceInfoDto { name = "lc", port = port, slaveId = 1 };
            var slaveA = new ModbusTCPSlave(dtoA, new List<MapEntry[]> { new[] { Entry("A_CTRL", 100) } }, 0, hub);
            var slaveB = new ModbusTCPSlave(dtoB, new List<MapEntry[]> { new[] { Entry("B_CTRL", 200) } }, 0, hub);

            slaveA.DeviceConnect();
            slaveB.DeviceConnect();

            int aCount = 0, bCount = 0;
            slaveA.ExternalControlWrite += _ => aCount++;
            slaveB.ExternalControlWrite += _ => bCount++;

            // 经真实 TCP 主站写入（AfterWrite 仅在从站网络处理外部请求时触发）
            var factory = new ModbusFactory();
            using var client = new TcpClient("127.0.0.1", port);
            var master = factory.CreateMaster(client);

            // 写 A 的控制区：仅 A 收到通知
            master.WriteSingleRegister(1, 100, 5);
            SpinWaitNotify();
            Assert.Equal(1, aCount);
            Assert.Equal(0, bCount);

            // 写 B 的控制区：仅 B 收到通知
            master.WriteSingleRegister(1, 200, 7);
            SpinWaitNotify();
            Assert.Equal(1, aCount);
            Assert.Equal(1, bCount);

            // 写无归属地址：双方都不触发
            master.WriteSingleRegister(1, 500, 1);
            SpinWaitNotify();
            Assert.Equal(1, aCount);
            Assert.Equal(1, bCount);

            // A 断开后旧钩子失效，写 A 的控制区不再通知 A
            slaveA.DeviceDisconnect();
            master.WriteSingleRegister(1, 100, 9);
            SpinWaitNotify();
            Assert.Equal(1, aCount);
        }
        finally
        {
            hub.ShutdownAll();
        }
    }

    /// <summary>从站网络在后台线程处理请求，等待 AfterWrite 钩子回调完成。</summary>
    private static void SpinWaitNotify() => Thread.Sleep(200);
}
