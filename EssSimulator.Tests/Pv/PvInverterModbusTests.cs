using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.DataExchange;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Pv;
using EssSimulator.Protocol.Modbus;
using Microsoft.Extensions.Options;
using NModbus;

namespace EssSimulator.Tests.Pv;

public class PvInverterModbusTests : SimulatorHostTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_SeedsActualCommandsWithoutApplyingControls(bool run)
    {
        using var fixture = new PvFixture(2, 1);
        var inverter = fixture.Units[1].Inverters[0];
        inverter.SetPowerCommand(87.6, -12.3);
        inverter.SyncExternalRunCommand(run);
        var server = fixture.StartInverter(2, 0);
        using var connection = new Connection(server);

        Assert.Equal(run ? 1 : 0, connection.Read("yk0"));
        Assert.Equal(87.6, connection.Read("yt0"), 6);
        Assert.Equal(-12.3, connection.Read("yt1"), 6);
        await WaitUntil(() => connection.Read("yc21") == 87600 && connection.Read("yc22") == -12300,
            "启动后的只读设定遥测未同步");
        await Task.Delay(120);

        Assert.Equal(run, inverter.IsExternalRunCommand);
        Assert.Equal(87.6, inverter.ActivePowerSettingKw, 6);
        Assert.Equal(-12.3, inverter.ReactivePowerSettingKvar, 6);
        Assert.Empty(fixture.Capture.Events);
    }

    [Fact]
    public async Task TcpCommands_UpdateOnlySelectedInverterAndPublishElectricalTelemetry()
    {
        using var fixture = new PvFixture(2, 2);
        fixture.StartAllInverters();
        var target = fixture.Units[1].Inverters[0];
        using var connection = new Connection(fixture.Server("simPvInv3"));
        connection.Master.WriteMultipleRegisters(1, 5039, new ushort[] { 1234, unchecked((ushort)(short)-456) });
        connection.Master.WriteSingleRegister(1, 5006, 1);
        await WaitUntil(() => fixture.Capture.Events.Count == 3, "单机启停和 P/Q 命令未全部执行");

        Assert.Equal(123.4, target.ActivePowerSettingKw, 6);
        Assert.Equal(-45.6, target.ReactivePowerSettingKvar, 6);
        Assert.True(target.IsExternalRunCommand);
        foreach (var inverter in fixture.Units.SelectMany(u => u.Inverters).Where(i => i != target))
        {
            Assert.False(inverter.IsExternalRunCommand);
            Assert.Equal(320, inverter.ActivePowerSettingKw);
            Assert.Equal(0, inverter.ReactivePowerSettingKvar);
        }

        fixture.Step();
        await WaitUntil(() => connection.Read("yc14") == 123400 && connection.Read("yc15") == -45600,
            "有功/无功遥测未反映单机实际输出");
        Assert.Equal(123400, connection.Read("yc21"));
        Assert.Equal(-45600, connection.Read("yc22"));
        Assert.Equal(320, connection.Read("yc0"));
        Assert.Equal(320, connection.Read("yc20"));
        foreach (string point in new[] { "yc8", "yc9", "yc10" })
            Assert.Equal(690, connection.Read(point));
        foreach (string point in new[] { "yc11", "yc12", "yc13" })
            Assert.InRange(Math.Abs(connection.Read(point) - target.Protocol.PhaseCurrentAmplitudeA), 0, 0.051);
        Assert.InRange(Math.Abs(connection.Read("yc1") - target.Protocol.ApparentPowerVa), 0, 0.51);
        Assert.InRange(Math.Abs(connection.Read("yc16") - target.Protocol.PowerFactor), 0, 0.00051);
        Assert.Equal(50, connection.Read("yc17"));
        Assert.Equal(target.Protocol.OperationStatus, connection.Read("yc18"));
        string[] voltagePoints = { "yc2", "yc4", "yc6", "yc23", "yc25", "yc27" };
        string[] currentPoints = { "yc3", "yc5", "yc7", "yc24", "yc26", "yc28" };
        for (int i = 0; i < voltagePoints.Length; i++)
        {
            Assert.InRange(Math.Abs(connection.Read(voltagePoints[i]) - target.Protocol.MpptVoltageV[i]), 0, 0.051);
            Assert.InRange(Math.Abs(connection.Read(currentPoints[i]) - target.Protocol.MpptCurrentA[i]), 0, 0.051);
        }
        foreach (string point in new[] { "yc19", "yc29", "yc30" })
            Assert.Equal(0, connection.Read(point));

        connection.Master.WriteSingleRegister(1, 5006, 0);
        await WaitUntil(() => fixture.Capture.Events.Count == 4, "单机停机命令未执行");
        fixture.Step();
        await WaitUntil(() => connection.Read("yc14") == 0 && connection.Read("yc15") == 0, "停机后输出未归零");
        Assert.Equal(OperationMode.Off, target.GetCurrentState().Mode);
        Assert.Equal(123.4, target.ActivePowerSettingKw, 6);
        Assert.Equal(-45.6, target.ReactivePowerSettingKvar, 6);
    }

    [Fact]
    public async Task IndependentPqWrites_DoNotStartStoppedInverterOrOverwriteOtherAxis()
    {
        using var fixture = new PvFixture(1);
        var target = fixture.Units[0].Inverters[0];
        target.SetPowerCommand(80, -20);
        using var connection = new Connection(fixture.StartInverter(1, 0));

        connection.Master.WriteSingleRegister(1, 5039, 900);
        await WaitUntil(() => fixture.Capture.Events.Count == 1, "有功命令未执行");
        Assert.Equal(90, target.ActivePowerSettingKw);
        Assert.Equal(-20, target.ReactivePowerSettingKvar);
        connection.Master.WriteSingleRegister(1, 5040, 300);
        await WaitUntil(() => fixture.Capture.Events.Count == 2, "无功命令未执行");
        fixture.Step();

        Assert.Equal(90, target.ActivePowerSettingKw);
        Assert.Equal(30, target.ReactivePowerSettingKvar);
        Assert.False(target.IsExternalRunCommand);
        Assert.Equal(OperationMode.Off, target.GetCurrentState().Mode);
        Assert.Equal(0, connection.Read("yc14"));
        Assert.Equal(0, connection.Read("yc15"));
        await WaitUntil(() => connection.Read("yc21") == 90000 && connection.Read("yc22") == 30000,
            "停机时设定回读未更新");
    }

    [Fact]
    public async Task FeedbackAndRepeatedTcpValues_DoNotReplayControlOrResetRamp()
    {
        using var fixture = new PvFixture(2);
        var unit = fixture.Units[0];
        unit.SyncExternalRunCommand(true);
        unit.SetPowerCommand(160, -40);
        fixture.StartAllInverters();
        using var connection = new Connection(fixture.Server("simPvInv1"));
        using var sibling = new Connection(fixture.Server("simPvInv2"));

        unit.SetActivePowerCommand(200);
        unit.SetReactivePowerCommand(-60);
        await WaitUntil(() => connection.Read("yt0") == 100 && connection.Read("yt1") == -30
            && sibling.Read("yt0") == 100 && sibling.Read("yt1") == -30,
            "模型整组命令没有反馈到单机保持寄存器");
        for (int i = 1; i <= 5; i++)
        {
            connection.Master.WriteSingleRegister(1, 5006, 1);
            connection.Master.WriteMultipleRegisters(1, 5039, new ushort[] { 1000, unchecked((ushort)(short)-300) });
            unit.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));
            await Task.Delay(30);
            Assert.Equal(Math.Min(i * 10, 100), unit.Inverters[0].GetCurrentState().ActivePower, 6);
        }

        Assert.Empty(fixture.Capture.Events);
        Assert.Equal(100, unit.Inverters[0].ActivePowerSettingKw);
        Assert.Equal(-30, unit.Inverters[0].ReactivePowerSettingKvar);
    }

    [Fact]
    public async Task LoggerAndSingleInverterCommands_CanAlternateWithoutReplayingGroupSettings()
    {
        using var fixture = new PvFixture(2, 1);
        var unit = fixture.Units[0];
        unit.Logger.SubarrayOnOff = 1;
        unit.Logger.SubarrayActivePowerKw = 400;
        fixture.StartAllInverters();
        using var logger = new Connection(fixture.StartLogger(1));
        using var first = new Connection(fixture.Server("simPvInv1"));
        using var second = new Connection(fixture.Server("simPvInv2"));
        await Task.Delay(120);
        Assert.All(unit.Inverters, i => Assert.Equal(200, i.ActivePowerSettingKw));
        Assert.Empty(fixture.Capture.Events);

        first.Write("yt0", 80);
        await WaitUntil(() => unit.Inverters[0].ActivePowerSettingKw == 80, "单机有功命令未执行");
        fixture.Step();
        await Task.Delay(120);
        Assert.Equal(200, unit.Inverters[1].ActivePowerSettingKw);
        Assert.Equal(80, unit.Inverters[0].ActivePowerSettingKw);

        logger.Write("yt5", 600);
        await WaitUntil(() => first.Read("yt0") == 300 && second.Read("yt0") == 300,
            "Logger 整组有功未分发到全部单机");
        first.Write("yk0", 0);
        await WaitUntil(() => !unit.Inverters[0].IsExternalRunCommand, "单机停机未执行");
        fixture.Step();
        await Task.Delay(120);
        Assert.False(unit.Inverters[0].IsExternalRunCommand);
        Assert.True(unit.Inverters[1].IsExternalRunCommand);
        logger.Write("yt4", 0);
        await WaitUntil(() => unit.Inverters.All(i => !i.IsExternalRunCommand), "Logger 整组停机未执行");
        logger.Write("yt4", 1);
        await WaitUntil(() => unit.Inverters.All(i => i.IsExternalRunCommand), "Logger 整组开机未执行");
        Assert.False(fixture.Units[1].Inverters[0].IsExternalRunCommand);
        Assert.Equal(320, fixture.Units[1].Inverters[0].ActivePowerSettingKw);
    }

    [Fact]
    public async Task Reconnect_SeedsCommandsChangedWhileOfflineWithoutReplayingOldValues()
    {
        using var fixture = new PvFixture(1);
        var unit = fixture.Units[0];
        var server = fixture.StartInverter(1, 0);
        Assert.True(server.SetOnline(false));
        Assert.False(server.IsDataPathReady);
        unit.Inverters[0].SetPowerCommand(66.6, -7.7);
        unit.SyncExternalRunCommand(true);
        fixture.Step();
        Assert.True(server.SetOnline(true, maxRetries: 1));
        using var connection = new Connection(server);
        await WaitUntil(() => connection.Read("yc21") == 66600 && connection.Read("yc22") == -7700,
            "重连后遥测仍为旧值");
        Assert.Equal(1, connection.Read("yk0"));
        Assert.Equal(66.6, connection.Read("yt0"), 6);
        Assert.Equal(-7.7, connection.Read("yt1"), 6);
        await Task.Delay(120);
        Assert.Empty(fixture.Capture.Events);
    }

    [Fact]
    public async Task HostedService_RegistersAllInvertersAndRebuildsPortAndSlaveOverrides()
    {
        using var fixture = new PvFixture(1, 2);
        var originalDirectory = Directory.GetCurrentDirectory();
        var temporary = Directory.CreateTempSubdirectory("pv-modbus-host-");
        var manager = new ProtocolLayerManager();
        var previousAllowList = ModbusPortHub.Instance.ActiveAllowList;
        var previousListenInfo = SimServer.serverListenInfo.ToArray();
        var hosted = new ModbusHostedService(Options.Create(fixture.Config), Options.Create(PvFixture.Options), manager);
        try
        {
            Directory.CreateDirectory(Path.Combine(temporary.FullName, "configs", "topology"));
            File.WriteAllText(Path.Combine(temporary.FullName, ProtocolPortPlan.OverridesRelativePath), "{\"entries\":[]}");
            File.WriteAllText(Path.Combine(temporary.FullName, ModbusIpAllowList.RelativePath), "{\"allowLoopback\":true,\"addresses\":[\"127.0.0.1\"]}");
            File.WriteAllText(Path.Combine(temporary.FullName, DeviceModelRegistry.SelectionRelativePath),
                "{\"selections\":{\"pv\":\"standard\",\"emu\":\"standard\",\"em\":\"standard\"}}");
            Directory.SetCurrentDirectory(temporary.FullName);
            DeviceModelRegistry.InvalidateCache();
            var plan = ProtocolPortPlan.BuildDefault(fixture.Config);
            var reserved = plan.Entries.Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToArray();
            try
            {
                for (int i = 0; i < reserved.Length; i++)
                {
                    reserved[i].Start();
                    plan.Entries[i].Port = ((IPEndPoint)reserved[i].LocalEndpoint).Port;
                }
                ProtocolPortPlan.SaveOverrides(plan.Entries);
            }
            finally
            {
                foreach (var listener in reserved)
                    listener.Stop();
            }

            await hosted.StartAsync(CancellationToken.None);
            await WaitUntil(() => manager.GetSnapshot().Count == plan.Entries.Count && manager.GetSnapshot().All(s => s.Online),
                "HostedService 未启动全部协议服务");
            foreach (var endpoint in PvInverterProtocolLayout.Enumerate(fixture.Config))
            {
                var server = SimulatorHost.Instance.Get<ModbusSimServer>(endpoint.ServerName)!;
                Assert.NotNull(server);
                Assert.True(server.IsDataPathReady);
                Assert.Equal(plan.Find(endpoint.ServerName)!.Port, server.Port);
                Assert.Equal($"pv{endpoint.UnitId}.Inverters[{endpoint.InverterIndex0}].Protocol.RunCommand",
                    server.PointMap.ParamModelLookup["yk0"].Arg1);
                using var connection = new Connection(server);
                await WaitUntil(() => connection.Read("yc0") == 320, "HostedService 单机遥测未就绪");
            }
            for (int i = 1; i <= fixture.Config.PvUnitCount; i++)
            {
                Assert.True(SimulatorHost.Instance.Get<ModbusSimServer>($"simPv{i}")!.IsOnline);
                Assert.True(SimulatorHost.Instance.Get<ModbusSimServer>($"simPvMeter{i}")!.IsOnline);
            }
            var originalServer = SimulatorHost.Instance.Get<ModbusSimServer>("simPvInv2")!;
            var changed = plan.Find(originalServer.ServerName)!;
            changed.Port = FreePort();
            changed.SlaveId = 7;
            ProtocolPortPlan.SaveOverrides(plan.Entries);
            var rebuilt = manager.Rebuild();
            Assert.True(rebuilt.Ok, string.Join("; ", rebuilt.Devices.SelectMany(d => d.Errors)));
            Assert.Same(originalServer, SimulatorHost.Instance.Get<ModbusSimServer>("simPvInv2"));
            Assert.Equal(changed.Port, originalServer.Port);
            Assert.Equal((byte)7, originalServer.SlaveId);
            using (var connection = new Connection(originalServer))
            {
                await WaitUntil(() => connection.Read("yc0") == 320 && connection.Read("yc21") == 320000,
                    "重建后的新从站遥测未同步");
                Assert.Equal(320, connection.Read("yt0"));
                connection.Master.WriteSingleRegister(7, 5039, 777);
                await WaitUntil(() => fixture.Units[1].Inverters[0].ActivePowerSettingKw == 77.7,
                    "重建后的新端口/从站命令未执行");
                await WaitUntil(() => connection.Read("yc21") == 77700,
                    "重建后的新从站设定遥测未同步");
                fixture.Units[1].Inverters[0].ActivePowerSettingKw = 66.6;
                await WaitUntil(() => connection.Read("yt0") == 66.6,
                    "重建后的新从站控制反馈未同步");
                Assert.Equal(320, fixture.Units[0].Inverters[0].ActivePowerSettingKw);
                Assert.Equal(320, fixture.Units[1].Inverters[1].ActivePowerSettingKw);
            }
            await hosted.StopAsync(CancellationToken.None);
            Assert.All(manager.GetSnapshot(), s => Assert.False(s.Online));
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
            ModbusPortHub.Instance.SetAllowList(previousAllowList);
            SimServer.serverListenInfo.Clear();
            foreach (var pair in previousListenInfo)
                SimServer.serverListenInfo[pair.Key] = pair.Value;
            Directory.SetCurrentDirectory(originalDirectory);
            DeviceModelRegistry.InvalidateCache();
            temporary.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoggerHoldingTelemetry_KeepsFunctionCode3AndPercentCommand()
    {
        using var fixture = new PvFixture(2);
        var unit = fixture.Units[0];
        var server = fixture.StartLogger(1);
        fixture.Step();
        using var connection = new Connection(server);
        var map = server.PointMap.RawMaps[0];

        foreach (string point in new[] { "yc11", "yc12", "yc13", "yc23", "yc29" })
            Assert.Equal(3, map.Single(e => e.ParamName == point).FunctionCode);
        var percent = map.Single(e => e.ParamName == "yt6");
        Assert.Equal(6, percent.FunctionCode);
        Assert.Equal(8005, percent.Address);
        Assert.Equal(8005, map.Single(e => e.ParamName == "yc11").Address);

        await WaitUntil(() => connection.Read("yc11") == PvLogger.ForwardedDeviceCount
            && connection.Read("yc23") == unit.RatedPowerKw,
            "Logger 保持寄存器遥测未发布");
        Assert.Equal(0, connection.Read("yc12"));

        connection.Write("yt6", 40);
        await WaitUntil(() => unit.Logger.SubarrayActivePowerPercent == 40, "百分比设定未执行");
        int applied = fixture.Capture.Events.Count;
        await Task.Delay(200);
        fixture.Step();
        await Task.Delay(80);

        Assert.Equal(40, unit.Logger.SubarrayActivePowerPercent);
        Assert.Equal(applied, fixture.Capture.Events.Count);
        Assert.Equal(PvLogger.ForwardedDeviceCount, connection.Read("yc11"));
        Assert.Equal(0, connection.Read("yc12"));
        Assert.Equal(unit.RatedPowerKw, connection.Read("yc23"));
        Assert.Equal((ushort)PvLogger.ForwardedDeviceCount,
            connection.Master.ReadHoldingRegisters(server.SlaveId, 8005, 1)[0]);

        connection.Write("yt4", 0);
        await WaitUntil(() => unit.Logger.SubarrayOnOff == 0, "相邻保持寄存器设定未执行");
        Assert.Equal(0, connection.Read("yt4"));
        Assert.Equal(PvLogger.ForwardedDeviceCount, connection.Read("yc11"));
    }

    private static async Task WaitUntil(Func<bool> predicate, string message)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate() && timeout.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10);
        Assert.True(predicate(), message);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class Capture : IControlPointCapture
    {
        public ConcurrentQueue<(string Server, string Point, object Value)> Events { get; } = new();
        public void OnControlApplied(string serverName, PointBinding binding, object applied, object? previous) =>
            Events.Enqueue((serverName, binding.ParamName, applied));
    }

    private sealed class PvFixture : IDisposable
    {
        private readonly IControlPointCapture _previousCapture = ControlPointCapture.Current;
        public Capture Capture { get; } = new();
        public SimulatorConfig Config { get; }
        public List<PvUnitDevice> Units { get; } = new();
        private readonly List<ModbusSimServer> _servers = new();
        public static DataExchangeOptions Options => new()
        {
            TelemetryIntervalMs = 20,
            EmuTelemetryIntervalMs = 20,
            ControlPollIntervalMs = 20,
            ControlEventDriven = true
        };

        public PvFixture(params int[] counts)
        {
            Config = new SimulatorConfig
            {
                PvUnits = counts.Select(n => new PvUnitRuntimeConfig { InverterCount = n }).ToList()
            };
            for (int i = 0; i < counts.Length; i++)
            {
                var unit = new PvUnitDevice($"pv{i + 1}", new PvUnitConfig
                {
                    InverterCount = counts[i],
                    Inverter = new PvInverterConfig { RampSlope = 0.1 }
                });
                unit.UpdateGridState(690, 50, true);
                Units.Add(unit);
                SimulatorHost.Instance.Register(unit.DeviceId, unit);
            }
            ControlPointCapture.Current = Capture;
        }

        public void StartAllInverters()
        {
            foreach (var endpoint in PvInverterProtocolLayout.Enumerate(Config))
                StartInverter(endpoint.UnitId, endpoint.InverterIndex0);
        }

        public ModbusSimServer StartInverter(int unitId, int inverterIndex)
        {
            var endpoint = PvInverterProtocolLayout.Enumerate(Config)
                .Single(e => e.UnitId == unitId && e.InverterIndex0 == inverterIndex);
            return Start(new ModbusSimServer(
                Path.Combine(AppContext.BaseDirectory, "pointmaps/models/pv/standard/pv_inverter.csv"),
                FreePort(), endpoint.ServerName, dataExchangeOptions: Options,
                pvDeviceIdOverride: unitId, inverterIndex: inverterIndex));
        }

        public ModbusSimServer StartLogger(int unitId) => Start(new ModbusSimServer(
            Path.Combine(AppContext.BaseDirectory, "pointmaps/models/pv/standard/pv_logger.csv"),
            FreePort(), $"simPv{unitId}", dataExchangeOptions: Options));

        private ModbusSimServer Start(ModbusSimServer server)
        {
            _servers.Add(server);
            SimulatorHost.Instance.Register(server.ServerName, server);
            Assert.True(server.Start(1));
            return server;
        }

        public ModbusSimServer Server(string name) => _servers.Single(s => s.ServerName == name);

        public void Step()
        {
            foreach (var unit in Units)
                unit.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        public void Dispose()
        {
            foreach (var server in _servers)
                server.Stop();
            ControlPointCapture.Current = _previousCapture;
        }
    }

    private sealed class Connection : IDisposable
    {
        private readonly TcpClient _client;
        private readonly ModbusSimServer _server;
        public IModbusMaster Master { get; }

        public Connection(ModbusSimServer server)
        {
            _server = server;
            _client = new TcpClient("127.0.0.1", server.Port);
            Master = new ModbusFactory().CreateMaster(_client);
            Master.Transport.ReadTimeout = 1000;
            Master.Transport.WriteTimeout = 1000;
            Master.Transport.Retries = 0;
        }

        public double Read(string point)
        {
            var entry = _server.PointMap.RawMaps[0].Single(e => e.ParamName == point);
            var words = entry.FunctionCode == 4
                ? Master.ReadInputRegisters(_server.SlaveId, (ushort)entry.Address, (ushort)(entry.Size / 16))
                : Master.ReadHoldingRegisters(_server.SlaveId, (ushort)entry.Address, (ushort)(entry.Size / 16));
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
                Master.WriteSingleRegister(_server.SlaveId, (ushort)entry.Address, words[0]);
            else
                Master.WriteMultipleRegisters(_server.SlaveId, (ushort)entry.Address, words);
        }

        public void Dispose()
        {
            Master.Dispose();
            _client.Dispose();
        }
    }
}
