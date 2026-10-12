using System.Net;
using System.Net.Sockets;
using EssSimulator;
using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.DataExchange.Config;
using EssSimulator.EssDeviceSimModel.Svg;
using EssSimulator.Protocol.Modbus;
using NModbus;

namespace EssSimulator.Tests.Svg;

public class SvgPortPlanTests
{
    [Fact]
    public void DisabledSvg_DoesNotAddAnEndpoint()
    {
        var plan = ProtocolPortPlan.BuildDefault(new SimulatorConfig());
        Assert.Null(plan.Find("simSvg"));
    }

    [Fact]
    public void EnabledSvg_AddsOneSlaveOnPort2201()
    {
        var config = new SimulatorConfig { Devices = { new EssUnitConfig() } };
        var before = ProtocolPortPlan.BuildDefault(config);
        config.EnableSvg = true;
        var after = ProtocolPortPlan.BuildDefault(config);

        Assert.Equal(before.Entries.Count + 1, after.Entries.Count);
        var svg = Assert.Single(after.Entries.Where(e => e.Type == ProtocolDeviceType.Svg));
        Assert.Equal("simSvg", svg.Name);
        Assert.Equal("svg.csv", svg.PointMapFile);
        Assert.Equal(2201, svg.Port);
        Assert.Equal(1, svg.SlaveId);
    }
}

[Collection("SimulatorHost")]
public class SvgModbusTests : SimulatorHostTestBase
{
    [Fact]
    public async Task WritesUpdateSetpointsAndTelemetryReadsDeviceOutput()
    {
        var device = new SvgDevice("svg", new SvgConfig { RatedCapacityKvar = 10000, NominalLineVoltageV = 35000 });
        var protocol = new SvgProtocolData(device);
        SimulatorHost.Instance.Register("svg", protocol);

        var server = new ModbusSimServer(
            "svg.csv",
            FreePort(),
            "simSvg",
            dataExchangeOptions: new DataExchangeOptions { TelemetryIntervalMs = 20, ControlPollIntervalMs = 20 });
        Assert.True(server.Start(1));
        try
        {
            using var connection = new Connection(server);
            connection.Write("svg13", 1);
            connection.Write("svg14", 5000);
            await WaitUntil(() => device.RunCommand == 1 && Math.Abs(device.ReactiveSetpointKvar - 5000) < 1e-3,
                "开关机或无功设定没有写入 svg");

            device.ApplyMeasurement(35000, 50, TimeSpan.FromMilliseconds(100));
            await WaitUntil(() => connection.Read("svg1") == 1 && connection.Read("svg8") >= 0.99 * 5000,
                "遥测没有读到设备输出");
            Assert.Equal(5000, connection.Read("svg14"), 3);
            Assert.Equal(10000, connection.Read("svg11"), 3);
        }
        finally
        {
            server.Stop();
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitUntil(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }

        Assert.Fail(message);
    }

    private sealed class Connection : IDisposable
    {
        private readonly TcpClient _client;
        private readonly ModbusSimServer _server;
        private readonly IModbusMaster _master;

        public Connection(ModbusSimServer server)
        {
            _server = server;
            _client = new TcpClient("127.0.0.1", server.Port);
            _master = new ModbusFactory().CreateMaster(_client);
            _master.Transport.ReadTimeout = 1000;
            _master.Transport.WriteTimeout = 1000;
            _master.Transport.Retries = 0;
        }

        public double Read(string point)
        {
            var entry = _server.PointMap.RawMaps[0].Single(e => e.ParamName == point);
            var words = entry.FunctionCode == 4
                ? _master.ReadInputRegisters(_server.SlaveId, (ushort)entry.Address, (ushort)(entry.Size / 16))
                : _master.ReadHoldingRegisters(_server.SlaveId, (ushort)entry.Address, (ushort)(entry.Size / 16));
            var bytes = new byte[words.Length * 2];
            Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
            return Convert.ToDouble(ModbusPointCodec.Decode(bytes, entry));
        }

        public void Write(string point, double value)
        {
            var entry = _server.ControlMaps.Single(e => e.ParamName == point);
            var bytes = ModbusPointCodec.Encode(value, entry, applyScale: true);
            var words = Common.ConvertBytesToUShorts(bytes);
            if (words.Length == 1)
                _master.WriteSingleRegister(_server.SlaveId, (ushort)entry.Address, words[0]);
            else
                _master.WriteMultipleRegisters(_server.SlaveId, (ushort)entry.Address, words);
        }

        public void Dispose()
        {
            _master.Dispose();
            _client.Dispose();
        }
    }
}
