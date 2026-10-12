using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Protocol;

public class ThirdPartyEmsModbusClientTests
{
    private static int NextPort() => Random.Shared.Next(42000, 58000);

    [Fact]
    public void WriteHolding_1010_RoundTripsEngineeringValue()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        using var client = new ThirdPartyEmsModbusClient();
        try
        {
            StartSlave(hub, port, ThirdPartyEmsPoints.TargetP, ThirdPartyEmsPoints.ActivePower);

            Assert.True(client.TryConnect("127.0.0.1", port, 1, out var connectError), connectError);
            Assert.True(client.TryWrite(ThirdPartyEmsPoints.TargetP, 500, out var writeError), writeError);
            Assert.True(client.TryRead(ThirdPartyEmsPoints.TargetP, out var readBack, out var readError), readError);
            Assert.Equal(500, readBack);
        }
        finally
        {
            client.Dispose();
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void ReadInput_125_AppliesScaleToEngineeringValue()
    {
        var hub = new ModbusPortHub();
        int port = NextPort();
        using var client = new ThirdPartyEmsModbusClient();
        try
        {
            var slave = StartSlave(hub, port, ThirdPartyEmsPoints.TargetP, ThirdPartyEmsPoints.ActivePower);
            Assert.True(slave.Write(new Dictionary<string, object> { ["sysyc125"] = 1234.0 }, applyScale: true));

            Assert.True(client.TryConnect("127.0.0.1", port, 1, out var connectError), connectError);
            Assert.True(client.TryRead(ThirdPartyEmsPoints.ActivePower, out var kw, out var readError), readError);
            Assert.Equal(1234, kw);
        }
        finally
        {
            client.Dispose();
            hub.ShutdownAll();
        }
    }

    [Fact]
    public void TryConnect_UnreachablePort_ReturnsErrorWithoutThrowing()
    {
        using var client = new ThirdPartyEmsModbusClient();
        bool ok = client.TryConnect("127.0.0.1", 59999, 1, out var error);
        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("59999", error);
        Assert.False(client.IsConnected);
    }

    private static ModbusTCPSlave StartSlave(ModbusPortHub hub, int port, params MapEntry[] points)
    {
        var dto = new DeviceInfoDto { name = "simLcTest", port = port, slaveId = 1 };
        var slave = new ModbusTCPSlave(dto, new List<MapEntry[]> { points }, 0, hub);
        slave.DeviceConnect();
        Assert.True(slave.GetCommunicatorState());
        return slave;
    }
}
