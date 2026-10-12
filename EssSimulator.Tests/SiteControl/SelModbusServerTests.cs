using System.Net;
using System.Net.Sockets;
using EssSimulator.Core;
using EssSimulator.Protocol.Modbus;
using EssSimulator.SiteControl;
using NModbus;
using PcsFixture = EssSimulator.Tests.SiteControl.SelPcsProtocolWriterTests.PcsFixture;

namespace EssSimulator.Tests.SiteControl;

public class SelModbusServerTests : SimulatorHostTestBase
{
    [Fact]
    public void TcpWrites_ConvertVoltageAndFrequencyWithoutStartingPcs()
    {
        using var fixture = new SelFixture();
        fixture.Master.WriteSingleRegister(1, 2, 500);
        fixture.Master.WriteSingleRegister(1, 3, 5025);
        Assert.Equal(new ushort[] { 500, 5025 }, fixture.Master.ReadHoldingRegisters(1, 2, 2));
        fixture.AssertCommands(345, 50.25f);
    }

    [Fact]
    public void HeartbeatAndReservedRegisters_DoNotBroadcast()
    {
        using var fixture = new SelFixture();
        fixture.Master.WriteMultipleRegisters(1, 0, new ushort[] { 1, 65535 });
        fixture.Master.WriteSingleRegister(1, 6, 123);
        Assert.Equal(new ushort[] { 0, 65535, 0, 5000, 0, 0, 123 },
            fixture.Master.ReadHoldingRegisters(1, 0, 7));
        fixture.AssertVoltage(0);
    }

    [Fact]
    public void TcpRamp_FollowsRealSecondsAndKeepsTargetReadback()
    {
        using var fixture = new SelFixture();
        fixture.Master.WriteMultipleRegisters(1, 2, new ushort[] { 200, 5000 });
        fixture.Master.WriteMultipleRegisters(1, 4, new ushort[] { 1, 500 });
        fixture.Master.WriteSingleRegister(1, 2, 1000);
        fixture.Clock.Elapsed = TimeSpan.FromMilliseconds(999);
        fixture.Server.RunCycle();
        fixture.AssertVoltage(138);
        ushort[] expected = { 248, 359, 469, 580, 690 };
        for (int second = 1; second <= 5; second++)
        {
            fixture.Clock.Elapsed = TimeSpan.FromSeconds(second);
            fixture.Server.RunCycle();
            fixture.Server.RunCycle();
            fixture.AssertCommands(expected[second - 1], 50);
            Assert.Equal((ushort)1000, fixture.Master.ReadHoldingRegisters(1, 2, 1)[0]);
        }
    }

    [Fact]
    public void InvalidBatch_IsRejectedWithoutPartialStateOrPcsChanges()
    {
        using var fixture = new SelFixture();
        fixture.Master.WriteMultipleRegisters(1, 2, new ushort[] { 200, 5000 });
        var before = fixture.Master.ReadHoldingRegisters(1, 0, 7);
        var error = Assert.Throws<SlaveException>(() => fixture.Master.WriteMultipleRegisters(1, 0,
            new ushort[] { 1, 123, 1000, 6501, 1, 500, 456 }));
        Assert.Equal((byte)3, error.SlaveExceptionCode);
        Assert.Equal(before, fixture.Master.ReadHoldingRegisters(1, 0, 7));
        fixture.Clock.Elapsed = TimeSpan.FromSeconds(5);
        fixture.Server.RunCycle();
        fixture.AssertCommands(138, 50);
    }

    [Theory]
    [InlineData(2, 1001)]
    [InlineData(3, 4499)]
    [InlineData(4, 2)]
    [InlineData(5, 10001)]
    [InlineData(0, 2)]
    public void InvalidSingleValue_ReturnsIllegalValue(ushort address, ushort value)
    {
        using var fixture = new SelFixture();
        var before = fixture.Master.ReadHoldingRegisters(1, 0, 7);
        var error = Assert.Throws<SlaveException>(() => fixture.Master.WriteSingleRegister(1, address, value));
        Assert.Equal((byte)3, error.SlaveExceptionCode);
        Assert.Equal(before, fixture.Master.ReadHoldingRegisters(1, 0, 7));
    }

    [Fact]
    public void InvalidAddressesAndUnsupportedFunctions_ReturnModbusExceptions()
    {
        using var fixture = new SelFixture();
        Assert.Equal((byte)2, Assert.Throws<SlaveException>(() =>
            fixture.Master.ReadHoldingRegisters(1, 0, 8)).SlaveExceptionCode);
        Assert.Equal((byte)2, Assert.Throws<SlaveException>(() =>
            fixture.Master.WriteSingleRegister(1, 7, 1)).SlaveExceptionCode);
        Assert.Equal((byte)2, Assert.Throws<SlaveException>(() =>
            fixture.Master.WriteMultipleRegisters(1, 6, new ushort[] { 1, 2 })).SlaveExceptionCode);
        Assert.Equal((byte)1, Assert.Throws<SlaveException>(() =>
            fixture.Master.ReadCoils(1, 0, 1)).SlaveExceptionCode);
        Assert.Equal((byte)1, Assert.Throws<SlaveException>(() =>
            fixture.Master.ReadWriteMultipleRegisters(1, 2, 1, 2, new ushort[] { 500 })).SlaveExceptionCode);
        fixture.AssertVoltage(0);
    }

    [Fact]
    public void BlockedControl_ReturnsBusyButHeartbeatStillWorks()
    {
        using var fixture = new SelFixture();
        ExternalControlGate.SetBlocked(true);
        using var client = new TcpClient("127.0.0.1", fixture.Server.Port);
        client.ReceiveTimeout = 3000;
        var stream = client.GetStream();
        stream.Write(new byte[] { 0, 1, 0, 0, 0, 6, 1, 6, 0, 2, 1, 244 });
        var response = new byte[9];
        stream.ReadExactly(response);
        Assert.Equal(new byte[] { 0, 1, 0, 0, 0, 3, 1, 0x86, 6 }, response);
        fixture.Master.WriteSingleRegister(1, 0, 1);
        Assert.Equal((ushort)0, fixture.Master.ReadHoldingRegisters(1, 0, 1)[0]);
        Assert.Equal((ushort)0, fixture.Master.ReadHoldingRegisters(1, 2, 1)[0]);
        fixture.AssertVoltage(0);
    }

    [Fact]
    public void LosingControlDuringRamp_StopsWithoutResumingLater()
    {
        using var fixture = new SelFixture();
        fixture.Master.WriteMultipleRegisters(1, 2, new ushort[] { 200, 5000 });
        fixture.Master.WriteMultipleRegisters(1, 4, new ushort[] { 1, 500 });
        fixture.Master.WriteSingleRegister(1, 2, 1000);
        fixture.Clock.Elapsed = TimeSpan.FromSeconds(1);
        fixture.Server.RunCycle();
        fixture.AssertVoltage(248);
        ExternalControlGate.SetBlocked(true);
        fixture.Clock.Elapsed = TimeSpan.FromSeconds(2);
        fixture.Server.RunCycle();
        ExternalControlGate.SetBlocked(false);
        fixture.Clock.Elapsed = TimeSpan.FromSeconds(10);
        fixture.Server.RunCycle();
        fixture.AssertVoltage(248);
    }

    [Fact]
    public void SharingSlaveWithOtherDevice_PreservesItsRegistersAndTransport()
    {
        using var fixture = new SelFixture(shared: true);
        fixture.Master.WriteSingleRegister(1, 100, 42);
        fixture.Master.WriteSingleRegister(1, 2, 500);
        Assert.Equal((ushort)42, fixture.Master.ReadHoldingRegisters(1, 100, 1)[0]);
        fixture.AssertVoltage(345);
        fixture.Server.Stop();
        fixture.Master.WriteSingleRegister(1, 100, 77);
        Assert.Equal((ushort)77, fixture.Master.ReadHoldingRegisters(1, 100, 1)[0]);
        Assert.True(fixture.Server.Start());
        Assert.Equal((ushort)77, fixture.Master.ReadHoldingRegisters(1, 100, 1)[0]);
        Assert.Equal((ushort)0, fixture.Master.ReadHoldingRegisters(1, 2, 1)[0]);
    }

    [Fact]
    public void Reconfigure_ChangesEndpointAndDoesNotReplayOldRamp()
    {
        using var fixture = new SelFixture();
        fixture.Master.WriteMultipleRegisters(1, 2, new ushort[] { 500, 5000 });
        fixture.Master.WriteMultipleRegisters(1, 4, new ushort[] { 1, 500 });
        fixture.Master.WriteSingleRegister(1, 2, 1000);
        Assert.Throws<InvalidOperationException>(() => fixture.Server.Reconfigure(NextPort(), 2));
        fixture.Server.Stop();
        fixture.Server.Reconfigure(NextPort(), 2);
        Assert.True(fixture.Server.Start());
        using var client = new TcpClient("127.0.0.1", fixture.Server.Port);
        using var master = new ModbusFactory().CreateMaster(client);
        fixture.Clock.Elapsed = TimeSpan.FromSeconds(10);
        fixture.Server.RunCycle();
        fixture.AssertVoltage(345);
        Assert.Equal(new ushort[] { 0, 0, 0, 5000, 0, 0, 0 }, master.ReadHoldingRegisters(2, 0, 7));
        master.WriteSingleRegister(2, 2, 800);
        fixture.AssertVoltage(552);
    }

    [Fact]
    public async Task ConcurrentClients_DoNotMixBatchRegistersOrCommands()
    {
        using var fixture = new SelFixture();
        async Task Write(ushort voltage, ushort frequency)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, fixture.Server.Port);
            using var master = new ModbusFactory().CreateMaster(client);
            for (int i = 0; i < 20; i++)
                await master.WriteMultipleRegistersAsync(1, 2, new[] { voltage, frequency, (ushort)0, (ushort)0 });
        }
        await Task.WhenAll(Write(500, 5000), Write(800, 5100));
        var registers = fixture.Master.ReadHoldingRegisters(1, 2, 2);
        Assert.True(registers.SequenceEqual(new ushort[] { 500, 5000 })
            || registers.SequenceEqual(new ushort[] { 800, 5100 }));
        fixture.AssertCommands((ushort)(690 * registers[0] / 1000), registers[1] / 100f);
    }

    [Fact]
    public void BroadcastFailure_KeepsLastAcceptedSetpointAndRetryStillDelivers()
    {
        using var fixture = new SelFixture(writerFactory: servers => new FlakyWriter(servers));
        var writer = (FlakyWriter)fixture.Writer;
        fixture.Master.WriteSingleRegister(1, 2, 300);
        fixture.AssertVoltage(207);

        writer.Fail = true;
        // 异常码 6 = SLAVE DEVICE BUSY，NModbus 主站会自动重试并再次拿到 6，
        // 这里用裸 TCP 只发一次核对响应字节（与 BlockedControl 用例同一手法）。
        using (var client = new TcpClient("127.0.0.1", fixture.Server.Port))
        {
            client.ReceiveTimeout = 3000;
            var stream = client.GetStream();
            stream.Write(new byte[] { 0, 1, 0, 0, 0, 6, 1, 6, 0, 2, 1, 244 });
            var response = new byte[9];
            stream.ReadExactly(response);
            Assert.Equal(new byte[] { 0, 1, 0, 0, 0, 3, 1, 0x86, 6 }, response);
        }

        Assert.Equal((ushort)300, fixture.Master.ReadHoldingRegisters(1, 2, 1)[0]);
        fixture.AssertVoltage(207);

        // 心跳写不得把被拒的目标镜像回寄存器
        fixture.Master.WriteSingleRegister(1, 0, 1);
        Assert.Equal((ushort)300, fixture.Master.ReadHoldingRegisters(1, 2, 1)[0]);
        fixture.AssertVoltage(207);

        writer.Fail = false;
        fixture.Master.WriteSingleRegister(1, 2, 500);
        fixture.AssertVoltage(345);
    }

    [Fact]
    public void BroadcastFailureDuringRamp_NextRampStartsFromLastSentVoltage()
    {
        using var fixture = new SelFixture(writerFactory: servers => new FlakyWriter(servers));
        var writer = (FlakyWriter)fixture.Writer;
        fixture.Master.WriteMultipleRegisters(1, 2, new ushort[] { 200, 5000 });
        fixture.Master.WriteMultipleRegisters(1, 4, new ushort[] { 1, 500 });
        fixture.Master.WriteSingleRegister(1, 2, 1000);
        fixture.Clock.Elapsed = TimeSpan.FromSeconds(1);
        fixture.Server.RunCycle();
        fixture.AssertVoltage(248);

        writer.Fail = true;
        fixture.Clock.Elapsed = TimeSpan.FromSeconds(2);
        fixture.Server.RunCycle();

        writer.Fail = false;
        fixture.Master.WriteSingleRegister(1, 5, 200);
        fixture.Master.WriteSingleRegister(1, 2, 900);
        fixture.Clock.Elapsed = TimeSpan.FromSeconds(3);
        fixture.Server.RunCycle();
        // 起点必须是已下发的 36%，不是失败那一拍的 52%
        fixture.AssertVoltage(435);
    }

    private static int NextPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class ManualClock : TimeProvider
    {
        public TimeSpan Elapsed { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Elapsed.Ticks;
    }

    private sealed class FlakyWriter : SelPcsProtocolWriter
    {
        public FlakyWriter(IEnumerable<ModbusSimServer> servers) : base(servers)
        {
        }

        public bool Fail { get; set; }

        public override void Write(SelReferenceCommand command)
        {
            if (Fail)
                throw new InvalidOperationException("注入的 PCS 广播失败");
            base.Write(command);
        }
    }

    private sealed class SelFixture : IDisposable
    {
        private readonly PcsFixture _pcs = new();
        private readonly ModbusPortHub _hub = new();
        private readonly TcpClient _client;
        public SelModbusServer Server { get; }
        public SelPcsProtocolWriter Writer { get; }
        public IModbusMaster Master { get; }
        public ManualClock Clock { get; } = new();

        public SelFixture(bool shared = false,
            Func<List<ModbusSimServer>, SelPcsProtocolWriter>? writerFactory = null)
        {
            int port = NextPort();
            if (shared)
                Assert.True(_hub.AttachDevice(port, 1, "peer", new[]
                {
                    new MapEntry { FunctionCode = 6, Address = 100, Size = 16, Type = "u16", ParamName = "peer" }
                }).Ok);
            Writer = (writerFactory ?? (servers => new SelPcsProtocolWriter(servers)))(_pcs.Servers);
            Server = new SelModbusServer(Writer, port, hub: _hub, clock: Clock);
            Assert.True(Server.Start());
            _client = new TcpClient("127.0.0.1", port);
            Master = new ModbusFactory().CreateMaster(_client);
            Master.Transport.ReadTimeout = 3000;
            Master.Transport.WriteTimeout = 3000;
            Master.Transport.Retries = 0;
        }

        public void AssertVoltage(ushort voltage)
        {
            foreach (var pcs in _pcs.Emus.SelectMany(e => e.PcsList))
            {
                Assert.Equal(voltage, pcs.IslandVoltageSetting);
                Assert.False(pcs.pcsOnOffSwitch);
                Assert.False(pcs.BlackStartEnabled);
            }
        }

        public void AssertCommands(ushort voltage, float frequency)
        {
            AssertVoltage(voltage);
            Assert.All(_pcs.Emus.SelectMany(e => e.PcsList), p => Assert.Equal(frequency, p.IslandFrequencySetting));
        }

        public void Dispose()
        {
            Master.Dispose();
            _client.Dispose();
            Server.Stop();
            _hub.ShutdownAll();
            _pcs.Dispose();
        }
    }
}
