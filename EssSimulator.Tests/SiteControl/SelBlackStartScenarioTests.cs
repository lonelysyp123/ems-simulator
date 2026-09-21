using System.Net;
using System.Net.Sockets;
using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssSimModelApi.Bms;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.LocalControl;
using EssSimulator.Protocol.Modbus;
using EssSimulator.SiteControl;
using log4net;
using NModbus;
using PcsDeviceConfig = EssSimulator.Configuration.PcsDeviceConfig;

namespace EssSimulator.Tests.SiteControl;

public class SelBlackStartScenarioTests : SimulatorHostTestBase
{
    [Fact]
    public void LcStartsPcsInBatches_ThenSelRaisesVoltageWhileSharingReactiveLoad()
    {
        using var fixture = new BlackStartFixture();
        fixture.OpenBreakers();
        fixture.EnterBmsBlackStart();
        Assert.All(fixture.Stacks, stack =>
        {
            Assert.Equal((ushort)3, stack.BlackStartStatus);
            Assert.Equal((ushort)1, stack.BlackStartEnterSuccess);
            Assert.True(stack.IsPcsLinked);
        });

        fixture.SelMaster.WriteMultipleRegisters(1, 2, new ushort[] { 800, 5000 });
        Assert.All(fixture.Commands, pcs =>
        {
            Assert.Equal((ushort)552, pcs.IslandVoltageSetting);
            Assert.Equal(50f, pcs.IslandFrequencySetting);
            Assert.False(pcs.pcsOnOffSwitch);
        });
        fixture.EnablePcsBlackStart();
        Assert.All(fixture.Commands, pcs => Assert.True(pcs.BlackStartEnabled));
        fixture.CloseUnitBreakers();
        Assert.False(fixture.Ess.IsMainBreakerClosed);
        Assert.True(fixture.Ess.IsUnitBreakerClosed(0));
        Assert.True(fixture.Ess.IsUnitBreakerClosed(1));

        fixture.StartUnit(0);
        fixture.StepFor(8);
        var first = fixture.Ess._pcsList.Take(2).Select(p => p.GetCurrentState()).ToArray();
        Assert.All(first, state =>
        {
            Assert.Equal(OperationMode.Normal, state.Mode);
            Assert.InRange(state.AcVoltage, 500, 590);
            Assert.True(state.ReactivePower > 1, $"首批 PCS 应承担无功，实际 {state.ReactivePower:F2} kvar");
        });
        Assert.All(fixture.Ess._pcsList.Skip(2), pcs => Assert.Equal(OperationMode.Off, pcs.GetCurrentState().Mode));
        double firstBatchQ = first.Sum(s => s.ReactivePower);

        fixture.StartUnit(1);
        fixture.StepFor(15);
        var states = fixture.Ess._pcsList.Select(p => p.GetCurrentState()).ToArray();
        Assert.All(fixture.Ess._pcsList, pcs => Assert.Equal(BlackStartPhase.Synchronized, pcs.GetBlackStartPhase()));
        Assert.All(states, state =>
        {
            Assert.Equal(OperationMode.Normal, state.Mode);
            Assert.True(state.ReactivePower > 1, $"并机 PCS 应共同承担无功，实际 {state.ReactivePower:F2} kvar");
        });
        double hostQ = states.Take(2).Sum(s => s.ReactivePower);
        double joinQ = states.Skip(2).Sum(s => s.ReactivePower);
        Assert.True(hostQ < firstBatchQ, $"后机应分担首批负荷：原 {firstBatchQ:F2}，现 {hostQ:F2}");
        Assert.InRange(Math.Abs(hostQ - joinQ), 0, Math.Max(hostQ, joinQ) * 0.4);

        fixture.SelMaster.WriteSingleRegister(1, 4, 1);
        fixture.SelMaster.WriteSingleRegister(1, 5, 500);
        fixture.StepFor(1);
        Assert.All(fixture.Commands, pcs => Assert.Equal((ushort)552, pcs.IslandVoltageSetting));
        fixture.SelMaster.WriteSingleRegister(1, 2, 1000);
        ushort[] expected = { 580, 607, 635, 662, 690 };
        foreach (ushort voltage in expected)
        {
            fixture.StepFor(1);
            Assert.All(fixture.Commands, pcs => Assert.Equal(voltage, pcs.IslandVoltageSetting));
        }
        fixture.StepFor(5);
        Assert.All(fixture.Ess._pcsList, pcs =>
        {
            var state = pcs.GetCurrentState();
            Assert.Equal(OperationMode.Normal, state.Mode);
            Assert.Equal(690, state.IslandVoltageCommandV);
            Assert.InRange(state.AcVoltage, 650, 710);
            Assert.True(state.ReactivePower > 1);
        });
        Assert.False(fixture.Ess.IsMainBreakerClosed);
    }

    [Fact]
    public void FaultedBms_RejectsBlackStartAndSelCannotBypassPcsInterlock()
    {
        using var fixture = new BlackStartFixture();
        fixture.OpenBreakers();
        fixture.Stacks[0].Cluseter[0].Alarms.OvervoltageFault = true;
        fixture.EnterBmsBlackStart();
        Assert.Equal((ushort)4, fixture.Stacks[0].BlackStartStatus);
        Assert.Equal((ushort)0, fixture.Stacks[0].BlackStartEnterSuccess);
        Assert.False(fixture.Stacks[0].IsPcsLinked);
        Assert.All(fixture.Stacks.Skip(1), stack => Assert.Equal((ushort)3, stack.BlackStartStatus));
        fixture.SelMaster.WriteMultipleRegisters(1, 2, new ushort[] { 800, 5000 });
        fixture.EnablePcsBlackStart();
        fixture.CloseUnitBreakers();
        fixture.StartUnit(0);
        Assert.Equal(OperationMode.Off, fixture.Ess._pcsList[0].GetCurrentState().Mode);
        Assert.False(fixture.Ess._pcsList[0].GetCurrentState().BlackStartEnabled);
    }

    private sealed class ScenarioClock : TimeProvider
    {
        public TimeSpan Elapsed { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Elapsed.Ticks;
    }

    private sealed class BlackStartFixture : IDisposable
    {
        private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);
        private readonly List<ModbusSimServer> _pcsServers = new();
        private readonly List<ModbusSimServer> _bmsServers = new();
        private readonly List<LocalControlModbusServer> _lcs = new();
        private readonly List<EnergyManagementData> _emus = new();
        private readonly StandardLcRuntime _lcRuntime = new(LogManager.GetLogger(typeof(SelBlackStartScenarioTests)));
        private readonly BmsDataService _bmsData;
        private readonly DirectoryInfo _selectionRoot = Directory.CreateTempSubdirectory("ess-sel-scenario-");
        private readonly ScenarioClock _clock = new();
        private readonly SelModbusServer _sel;
        private readonly TcpClient _selClient;
        public EnergyStorageSystem Ess { get; }
        public IModbusMaster SelMaster { get; }
        public BatteryStack[] Stacks { get; }
        public IEnumerable<PcsData> Commands => _emus.SelectMany(e => e.PcsList);

        public BlackStartFixture()
        {
            var config = new SimulatorConfig
            {
                Devices =
                {
                    new EssUnitConfig { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } },
                    new EssUnitConfig { Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() } }
                }
            };
            var physical = new PcsPhysicalConfig
            {
                AcVoltageNominal = 690, FrequencyNominal = 50, MaxCurrent = 2000,
                BlackStartPrechargeDelayMs = 0, BlackStartVoltageRampVs = 400,
                BlackStartJoinShareRampSec = 5, InrushPeakMultiplier = 0.3, DvDtTripThresholdVPerSec = 10_000
            };
            Ess = new EnergyStorageSystem(config, physical, new TransformerConfig(),
                new UnitTransformerConfig { RatedPower = 6300, MagnetizingInrushEnabled = true },
                new LoadConfig(), new PccConfig(), new MeterConfig());
            SimulatorHost.Instance.RegisterEss(Ess);
            _bmsData = new BmsDataService(config);
            _bmsData.Project(Ess);
            Stacks = Enumerable.Range(1, 4).Select(i => SimulatorHost.Instance
                .Get<BatteryManagementSystemData>($"bms{i}")!.BatteryStacks[0]).ToArray();
            // 与生产 BmsLinkService 一致：先按 DTO 默认并网态建立物理直流链路，再扫描黑启动边沿
            BmsLinkEngine.ApplyStartupGridLinks(Ess);
            BmsLinkEngine.ApplyAllChannels();
            for (int unit = 0; unit < 2; unit++)
            {
                var emu = PcsDataServer.BuildEmuMirror(config.Devices[unit], physical);
                _emus.Add(emu);
                SimulatorHost.Instance.RegisterEmu(unit + 1, emu);
                for (int channel = 0; channel < 2; channel++)
                {
                    int id = unit * 2 + channel + 1;
                    var pcs = new ModbusSimServer(Map("emu", "emu.csv"), NextPort(), $"simEmu{id}",
                        essUnits: config.Devices, emuDeviceIdOverride: unit + 1, pcsIndex: channel);
                    _pcsServers.Add(pcs);
                    SimulatorHost.Instance.Register(pcs.ServerName, pcs);
                    Assert.True(pcs.Start(1));
                    var bms = new ModbusSimServer(Map("bms", "bms_bank.csv"), NextPort(), $"simBms{id}",
                        essUnits: config.Devices);
                    _bmsServers.Add(bms);
                    SimulatorHost.Instance.Register(bms.ServerName, bms);
                    Assert.True(bms.Start(1));
                }
                var lc = new LocalControlModbusServer(1, NextPort(), $"simLc{unit + 1}", unit + 1,
                    config.Devices, selectionRoot: _selectionRoot.FullName);
                _lcs.Add(lc);
                Assert.False(lc.UsesDataExchange);
                Assert.True(lc.Start(1));
            }
            CycleLc();
            _sel = new SelModbusServer(new SelPcsProtocolWriter(_pcsServers), NextPort(), clock: _clock);
            Assert.True(_sel.Start());
            _selClient = new TcpClient("127.0.0.1", _sel.Port);
            SelMaster = new ModbusFactory().CreateMaster(_selClient);
        }

        public void OpenBreakers()
        {
            Ess.SetMainBreakerClosed(false);
            foreach (var lc in _lcs)
                WritePoint(lc, LcMvMap.HvBreakerCommand, 0xEE);
            CycleLc();
            StepFor(0.5);
            Assert.False(Ess.IsMainBreakerClosed);
            Assert.False(Ess.IsUnitBreakerClosed(0));
            Assert.False(Ess.IsUnitBreakerClosed(1));
            Assert.InRange(Ess.PccLineVoltageV, 0, 1);
        }

        public void EnterBmsBlackStart()
        {
            foreach (var bms in _bmsServers)
            {
                WritePoint(bms, "yt1", 1);
                WritePoint(bms, "yt2", 200);
                WritePoint(bms, "yk1", 1);
            }
            WaitUntil("BMS 黑启动命令落地", () => Stacks.All(s => s.BlackStartStatus != 0));
        }

        /// <summary>Modbus 控制写由 DataExchange 在线程池上异步落地，断言前需等待状态变化。</summary>
        private static void WaitUntil(string what, Func<bool> condition, int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
                Thread.Sleep(10);
            Assert.True(condition(), $"{what}超时（{timeoutMs}ms）");
        }

        public void EnablePcsBlackStart()
        {
            foreach (var lc in _lcs)
                WritePoint(lc, LcSystemMap.BlackStartWrite, 1);
            CycleLc();
        }

        public void CloseUnitBreakers()
        {
            foreach (var lc in _lcs)
                WritePoint(lc, LcMvMap.HvBreakerCommand, 0xAA);
            CycleLc();
        }

        public void StartUnit(int unit)
        {
            for (int slot = 0; slot < 2; slot++)
                WritePoint(_lcs[unit], LcChannelMap.StartStop(1, slot), 1);
            CycleLc();
        }

        public void StepFor(double seconds)
        {
            for (int i = 0; i < (int)Math.Round(seconds / Step.TotalSeconds); i++)
            {
                _clock.Elapsed += Step;
                CycleLc();
                _sel.RunCycle();
                BmsLinkEngine.ApplyAllChannels();
                Ess.PlantEngine.Step(new DateTime(2026, 1, 1).Add(_clock.Elapsed), Step, Step);
                _bmsData.Project(Ess);
                for (int unit = 0; unit < _emus.Count; unit++)
                    PcsEmuSynchronizer.SyncUnit(Ess, _emus[unit], unit, unit * 2);
            }
        }

        private void CycleLc()
        {
            for (int i = 0; i < _lcs.Count; i++)
                _lcRuntime.RunCycle(SimulatorHost.Instance.Get<ModbusSimServer>, _lcs[i], i, 1, 2);
        }

        private static void WritePoint(IProtocolLayerServer server, string param, ushort raw)
        {
            var point = server.PointMap.ControlMaps.Single(p => p.ParamName == param);
            using var client = new TcpClient("127.0.0.1", server.Port);
            using var master = new ModbusFactory().CreateMaster(client);
            ushort address = (ushort)point.Address;
            if (point.FunctionCode is 1 or 5)
                master.WriteSingleCoil(server.SlaveId, address, raw != 0);
            else if (point.FunctionCode == 16)
                master.WriteMultipleRegisters(server.SlaveId, address, new[] { raw });
            else
            {
                Assert.Equal(6, point.FunctionCode);
                master.WriteSingleRegister(server.SlaveId, address, raw);
            }
        }

        private static string Map(string type, string name) =>
            Path.Combine(AppContext.BaseDirectory, "pointmaps", "models", type, "standard", name);

        private static int NextPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            SelMaster.Dispose();
            _selClient.Dispose();
            _sel.Stop();
            foreach (var lc in _lcs) lc.Stop();
            foreach (var server in _bmsServers.Concat(_pcsServers)) server.Stop();
            Ess.Dispose();
            _selectionRoot.Delete(true);
        }
    }
}
