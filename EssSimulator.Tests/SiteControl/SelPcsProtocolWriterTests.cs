using System.Net;
using System.Net.Sockets;
using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi;
using EssSimulator.SiteControl;
using PcsDeviceConfig = EssSimulator.Configuration.PcsDeviceConfig;

namespace EssSimulator.Tests.SiteControl;

public class SelPcsProtocolWriterTests : SimulatorHostTestBase
{
    [Fact]
    public void Broadcast_UsesExistingProtocolScalingAndPreservesStoppedState()
    {
        using var fixture = new PcsFixture();
        var writer = new SelPcsProtocolWriter(fixture.Servers);
        writer.Write(new SelReferenceCommand(248.4, 50.25));
        foreach (var server in fixture.Servers)
        {
            using var client = new TcpClient("127.0.0.1", server.Port);
            using var master = new NModbus.ModbusFactory().CreateMaster(client);
            Assert.Equal(new ushort[] { 248, 5025 }, master.ReadHoldingRegisters(1, 40003, 2));
        }
        foreach (var pcs in fixture.Ess._pcsList)
        {
            Assert.Equal(OperationMode.Off, pcs.GetCurrentState().Mode);
            Assert.False(pcs.GetCurrentState().BlackStartEnabled);
        }
        foreach (var emu in fixture.Emus)
            Assert.All(emu.PcsList, pcs =>
            {
                Assert.Equal((ushort)248, pcs.IslandVoltageSetting);
                Assert.Equal(50.25f, pcs.IslandFrequencySetting);
                Assert.False(pcs.pcsOnOffSwitch);
                Assert.False(pcs.BlackStartEnabled);
            });
    }

    [Fact]
    public void VoltageTick_DoesNotReplayFrequencyOrRunCommands()
    {
        using var fixture = new PcsFixture();
        var writer = new SelPcsProtocolWriter(fixture.Servers);
        for (int i = 0; i < fixture.Servers.Count; i++)
            fixture.Servers[i].SetDataObjectByMesurePointName("yt4", (ushort)(5000 + i * 10));
        writer.Write(new SelReferenceCommand(345, null));
        var commands = fixture.Emus.SelectMany(emu => emu.PcsList).ToArray();
        for (int i = 0; i < fixture.Servers.Count; i++)
        {
            using var client = new TcpClient("127.0.0.1", fixture.Servers[i].Port);
            using var master = new NModbus.ModbusFactory().CreateMaster(client);
            Assert.Equal(new ushort[] { 345, (ushort)(5000 + i * 10) },
                master.ReadHoldingRegisters(1, 40003, 2));
            Assert.Equal((float)(50.0 + i * 0.1), commands[i].IslandFrequencySetting);
            Assert.False(commands[i].pcsOnOffSwitch);
            Assert.False(commands[i].BlackStartEnabled);
        }
        Assert.All(fixture.Ess._pcsList, pcs => Assert.Equal(OperationMode.Off, pcs.GetCurrentState().Mode));
    }

    [Fact]
    public void FrequencyWrite_PreservesIndividualVoltageSettings()
    {
        using var fixture = new PcsFixture();
        for (int i = 0; i < fixture.Servers.Count; i++)
            fixture.Servers[i].SetDataObjectByMesurePointName("yt3", (ushort)(100 + i));
        var writer = new SelPcsProtocolWriter(fixture.Servers);
        writer.Write(new SelReferenceCommand(null, 51));
        for (int i = 0; i < fixture.Servers.Count; i++)
            Assert.Equal(100 + i, Convert.ToDouble(fixture.Servers[i].GetDataObjectByMesurePointName("yt3")));
    }

    [Fact]
    public void ExistingControlOwnershipGate_IsNotBypassed()
    {
        using var fixture = new PcsFixture();
        var writer = new SelPcsProtocolWriter(fixture.Servers);
        ExternalControlGate.SetBlocked(true);
        Assert.False(writer.CanWrite);
        Assert.Throws<InvalidOperationException>(() => writer.Write(new SelReferenceCommand(690, 50)));
        Assert.All(fixture.Emus, emu => Assert.All(emu.PcsList, pcs => Assert.Equal((ushort)0, pcs.IslandVoltageSetting)));
    }

    internal sealed class PcsFixture : IDisposable
    {
        public EnergyStorageSystem Ess { get; }
        public List<ModbusSimServer> Servers { get; } = new();
        public List<EssSimulator.EssSimModelApi.EnergyManagementSystem.EnergyManagementData> Emus { get; } = new();

        public PcsFixture()
        {
            var cfg = new SimulatorConfig
            {
                Devices =
                {
                    new EssUnitConfig
                    {
                        Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig(), new PcsDeviceConfig() }
                    },
                    new EssUnitConfig { Pcs = { new PcsDeviceConfig() } }
                }
            };
            var physical = new PcsPhysicalConfig { AcVoltageNominal = 690 };
            Ess = new EnergyStorageSystem(cfg, physical, new TransformerConfig(),
                new UnitTransformerConfig(), new LoadConfig(), new PccConfig(), new MeterConfig());
            SimulatorHost.Instance.RegisterEss(Ess);
            int global = 0;
            for (int unit = 0; unit < cfg.Devices.Count; unit++)
            {
                var emu = PcsDataServer.BuildEmuMirror(cfg.Devices[unit], physical);
                Emus.Add(emu);
                SimulatorHost.Instance.RegisterEmu(unit + 1, emu);
                for (int channel = 0; channel < cfg.Devices[unit].PcsCount; channel++)
                {
                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    listener.Stop();
                    var server = new ModbusSimServer(
                        Path.Combine(AppContext.BaseDirectory, "pointmaps/models/emu/standard/emu.csv"),
                        port, $"simEmu{++global}", essUnits: cfg.Devices,
                        emuDeviceIdOverride: unit + 1, pcsIndex: channel);
                    Servers.Add(server);
                    SimulatorHost.Instance.Register(server.ServerName, server);
                    Assert.True(server.Start(1));
                }
            }
        }

        public void Dispose()
        {
            foreach (var server in Servers)
                server.Stop();
            Ess.Dispose();
        }
    }
}
