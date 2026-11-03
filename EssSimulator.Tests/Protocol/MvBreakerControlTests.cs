using System.Net;
using System.Net.Sockets;
using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi;
using EssSimulator.LocalControl;
using EssSimulator.Protocol.Modbus;
using NModbus;

namespace EssSimulator.Tests.Protocol;

[Collection("SimulatorHost")]
public class MvBreakerControlTests : SimulatorHostTestBase
{
    private sealed class MemPort : IMvPointPort
    {
        public Dictionary<string, object> Values { get; } = new();
        public object? Read(string name) => Values.TryGetValue(name, out var value) ? value : null;
        public void Write(string name, object value) => Values[name] = value;
    }

    private static double Command(IMvPointPort port) =>
        ModbusValueConverter.ToDouble(port.Read(LcMvMap.HvBreakerCommand)!);

    private static (EnergyStorageSystem Ess, List<MvBreakerEndpoint> Breakers, Dictionary<string, MemPort> Servers) Build()
    {
        var cfg = new SimulatorConfig
        {
            Devices = { new EssUnitConfig { Pcs = { new EssSimulator.Configuration.PcsDeviceConfig() } } }
        };
        var ess = new EnergyStorageSystem(
            cfg, new PcsPhysicalConfig(), new TransformerConfig(), new UnitTransformerConfig(),
            new LoadConfig(), new PccConfig(), new MeterConfig());
        var emu = PcsDataServer.BuildEmuMirror(cfg.Devices[0], new PcsPhysicalConfig());
        SimulatorHost.Instance.RegisterEss(ess);
        SimulatorHost.Instance.RegisterEmu(1, emu);
        ess.SetMainBreakerClosed(true);
        ess.SetUnitBreakerClosed(0, true);

        var breakers = new List<MvBreakerEndpoint>
        {
            new() { NodeId = "main", Label = "主断", Role = MvBreakerRole.Main },
            new() { NodeId = "unit", Label = "单元断", Role = MvBreakerRole.Unit, UnitId = 1 },
            new() { NodeId = "other", Label = "支路断", Role = MvBreakerRole.Other }
        };
        var servers = new Dictionary<string, MemPort>(StringComparer.OrdinalIgnoreCase)
        {
            ["simMv1"] = new MemPort(),
            ["simMv2"] = new MemPort(),
            ["simMv3"] = new MemPort()
        };
        return (ess, breakers, servers);
    }

    [Fact]
    public void UnitOpen_LeavesMainAndOtherClosed()
    {
        var (ess, breakers, servers) = Build();
        using (ess)
        {
            var runtime = new MvBreakerRuntime();
            runtime.RunCycle(breakers, name => servers[name]);
            Assert.Equal(0xAA, Command(servers["simMv2"]));

            servers["simMv2"].Write(LcMvMap.HvBreakerCommand, 0xEE);
            runtime.RunCycle(breakers, name => servers[name]);

            Assert.False(ess.IsUnitBreakerClosed(0));
            Assert.True(ess.IsMainBreakerClosed);
            Assert.Equal(0xEE, Command(servers["simMv2"]));
            Assert.Equal(0xAA, Command(servers["simMv1"]));
            Assert.Equal(0xAA, Command(servers["simMv3"]));
        }
    }

    [Fact]
    public void OtherBreaker_Toggle_OnlyChangesItsOwnFlag()
    {
        var (ess, breakers, servers) = Build();
        using (ess)
        {
            var runtime = new MvBreakerRuntime();
            runtime.RunCycle(breakers, name => servers[name]);
            bool main = ess.IsMainBreakerClosed;
            bool unit = ess.IsUnitBreakerClosed(0);

            MvBreakerProtocolState.Clear();
            servers["simMv3"].Write(LcMvMap.HvBreakerCommand, 0xEE);
            runtime.RunCycle(breakers, name => servers[name]);
            Assert.Equal(0xEE, Command(servers["simMv3"]));
            Assert.False(MvBreakerProtocolState.Copy()["other"]);
            Assert.Equal(main, ess.IsMainBreakerClosed);
            Assert.Equal(unit, ess.IsUnitBreakerClosed(0));
            Assert.Equal(0xAA, Command(servers["simMv1"]));

            servers["simMv3"].Write(LcMvMap.HvBreakerCommand, 0xAA);
            runtime.RunCycle(breakers, name => servers[name]);
            Assert.Equal(0xAA, Command(servers["simMv3"]));
            Assert.True(MvBreakerProtocolState.Copy()["other"]);
            Assert.Equal(main, ess.IsMainBreakerClosed);
            Assert.Equal(unit, ess.IsUnitBreakerClosed(0));
            MvBreakerProtocolState.Clear();
        }
    }

    [Fact]
    public void ExternalWrite_OpensOtherBreaker_AndKeepsRegister()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        string map = Path.Combine(FindRepoRoot(), "pointmaps", "models", "mv", "standard", "mv.csv");
        var server = new ModbusSimServer(map, port, "simMvProbe");
        Assert.True(server.Start(1));
        MvBreakerProtocolState.Clear();
        try
        {
            var runtime = new MvBreakerRuntime();
            var breakers = new List<MvBreakerEndpoint>
            {
                new() { NodeId = "pv", Label = "三相断路器", Role = MvBreakerRole.Other }
            };
            runtime.RunCycle(breakers, _ => new MvModbusPointPort(server));

            using var client = new TcpClient("127.0.0.1", port);
            var master = new ModbusFactory().CreateMaster(client);
            master.WriteSingleRegister(1, 33200, 238);
            runtime.RunCycle(breakers, _ => new MvModbusPointPort(server));

            Assert.Equal(new ushort[] { 238 }, master.ReadHoldingRegisters(1, 33200, 1));
            Assert.False(MvBreakerProtocolState.Copy()["pv"]);
        }
        finally
        {
            MvBreakerProtocolState.Clear();
            server.SetOnline(false);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EssSimulator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录");
    }

    [Fact]
    public void IllegalCommand_RollsRegisterBack_AndLeavesBreaker()
    {
        var (ess, breakers, servers) = Build();
        using (ess)
        {
            var runtime = new MvBreakerRuntime();
            runtime.RunCycle(breakers, name => servers[name]);
            Assert.True(ess.IsUnitBreakerClosed(0));

            servers["simMv2"].Write(LcMvMap.HvBreakerCommand, 0x11);
            runtime.RunCycle(breakers, name => servers[name]);

            Assert.Equal(0xAA, Command(servers["simMv2"]));
            Assert.True(ess.IsUnitBreakerClosed(0));
            Assert.True(ess.IsMainBreakerClosed);
        }
    }
}
