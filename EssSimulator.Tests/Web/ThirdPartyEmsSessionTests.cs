using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.Web.ThirdPartyEms;

namespace EssSimulator.Tests.Web;

public class ThirdPartyEmsSessionTests : SimulatorHostTestBase
{
    [Fact]
    public void GetDashboard_NoEmu_UnavailableWithChineseReason()
    {
        using var session = new ThirdPartyEmsSession(() => Array.Empty<ThirdPartyEmsTarget>());
        var dash = session.GetDashboard();
        Assert.False(dash.Available);
        Assert.Equal(ThirdPartyEmsSession.NoUnitReason, dash.Reason);
        Assert.Equal(nameof(ExternalControlOwner.None), dash.GateOwner);
        Assert.False(dash.Exclusive);
        Assert.False(session.TryConnect(null, out var error));
        Assert.Equal(ThirdPartyEmsSession.NoUnitReason, error);
        Assert.False(session.TrySetPower("emu1", 100, 0, out var powerError));
        Assert.Equal(ThirdPartyEmsSession.NoUnitReason, powerError);
    }

    [Fact]
    public void GetDashboard_StrategyOccupy_ExposesGateOwner_NotExclusive()
    {
        Assert.True(ExternalControlGate.TryOccupy(ExternalControlOwner.EmsStrategy, out _));
        using var session = CreateSession(1);
        var dash = session.GetDashboard();
        Assert.Equal(nameof(ExternalControlOwner.EmsStrategy), dash.GateOwner);
        Assert.False(dash.Exclusive);
        Assert.Equal(ExternalControlGate.StrategyBlockedMessage, dash.Reason);
        Assert.False(session.TryConnect("emu1", out var error));
        Assert.Equal(ExternalControlGate.StrategyBlockedMessage, error);
    }

    [Fact]
    public void SetPower_WritesEmuModel_AndOccupiesExclusive()
    {
        var (ess, emu) = BuildEssWithEmuMirror();
        using (ess)
        using (var session = CreateSession(1))
        {
            Assert.True(session.TryConnect("emu1", out var connectError), connectError);
            Assert.True(ExternalControlGate.IsBlocked);
            Assert.True(session.TrySetPower("emu1", 800, -50, out var writeError), writeError);

            Assert.Equal(1, emu.Emu.RemoteControlEnable);
            Assert.Equal(1, emu.Emu.RemoteControlMode);
            Assert.Equal(800, emu.Emu.TargetActivePower);
            Assert.Equal(-50, emu.Emu.TargetReactivePower);

            var unit = Assert.Single(session.GetDashboard().Units);
            Assert.Equal("emu1", unit.Name);
            Assert.True(unit.Connected);
            Assert.True(unit.Live);
            Assert.Equal(800, unit.TargetActivePowerKw);
            Assert.Equal(-50, unit.TargetReactivePowerKvar);
            Assert.True(session.GetDashboard().Exclusive);
            Assert.Equal(nameof(ExternalControlOwner.ThirdPartyEms), session.GetDashboard().GateOwner);
        }
    }

    [Fact]
    public void TwoUnits_SetPower_DoesNotCrossWrite()
    {
        var (ess, emu1, emu2) = BuildTwoUnitPlant();
        using (ess)
        using (var session = new ThirdPartyEmsSession(() => new[]
        {
            new ThirdPartyEmsTarget { Name = "emu1", UnitIndex = 1 },
            new ThirdPartyEmsTarget { Name = "emu2", UnitIndex = 2 }
        }))
        {
            Assert.True(session.TryConnect(null, out var connectError), connectError);
            Assert.True(session.TrySetPower("emu1", 100, 10, out var aErr), aErr);
            Assert.True(session.TrySetPower("emu2", 200, 20, out var bErr), bErr);

            Assert.Equal(100, emu1.Emu.TargetActivePower);
            Assert.Equal(200, emu2.Emu.TargetActivePower);

            emu1.Emu.OutputActivePower = 100;
            emu1.Emu.OutputReactivePower = 5;
            emu2.Emu.OutputActivePower = 250;
            emu2.Emu.OutputReactivePower = 15;
            var dash = session.GetDashboard();
            Assert.Equal(2, dash.Units.Count);
            Assert.Equal(2, dash.Station.ConnectedCount);
            Assert.Equal(350, dash.Station.ActivePowerKw);
            Assert.Equal(20, dash.Station.ReactivePowerKvar);
        }
    }

    [Fact]
    public void Disconnect_ReleasesExclusive_KeepsModelTelemetry()
    {
        var (ess, emu) = BuildEssWithEmuMirror();
        using (ess)
        using (var session = CreateSession(1))
        {
            emu.Emu.OutputActivePower = 50;
            Assert.True(session.TryConnect("emu1", out var connectError), connectError);
            Assert.True(session.TrySetPower("emu1", 50, 0, out var writeError), writeError);
            Assert.True(session.TryDisconnect("emu1", out var disconnectError), disconnectError);

            Assert.False(ExternalControlGate.IsBlocked);
            var unit = Assert.Single(session.GetDashboard().Units);
            Assert.False(unit.Connected);
            Assert.True(unit.Live);
            Assert.Equal(50, unit.ActivePowerKw);
            Assert.False(session.GetDashboard().Exclusive);
            Assert.Equal(nameof(ExternalControlOwner.None), session.GetDashboard().GateOwner);
        }
    }

    [Fact]
    public void SetOperation_WritesSystemOperation()
    {
        var (ess, emu) = BuildEssWithEmuMirror();
        using (ess)
        using (var session = CreateSession(1))
        {
            Assert.True(session.TryConnect("emu1", out var connectError), connectError);
            Assert.True(session.TrySetOperation("emu1", ThirdPartyEmsSession.OpStart, out var opError), opError);
            Assert.Equal(ThirdPartyEmsSession.OpStart, emu.Emu.SystemOperation);
        }
    }

    [Fact]
    public void Snapshot_ReadsEmuTelemetryWhenLive()
    {
        var (ess, emu) = BuildEssWithEmuMirror();
        using (ess)
        using (var session = CreateSession(1))
        {
            emu.Emu.OutputActivePower = 321;
            emu.Emu.OutputReactivePower = 12;
            emu.Emu.AverageBatterySoc = 0.85f;
            emu.Emu.OperationStatus = 5;
            emu.Emu.FaultPcsCount = 0;
            emu.Emu.MaxChargePower = 1100;
            emu.Emu.MaxDischargePower = 1200;

            Assert.True(session.TryConnect("emu1", out var connectError), connectError);
            var unit = Assert.Single(session.GetDashboard().Units);
            Assert.True(unit.Live);
            Assert.Equal(321, unit.ActivePowerKw);
            Assert.Equal(12, unit.ReactivePowerKvar);
            Assert.Equal(0.85, unit.Soc!.Value, 5);
            Assert.Equal(5, unit.DetailedStatus);
            Assert.Equal(1100, unit.MaxChargePowerKw);
            Assert.Equal(1200, unit.MaxDischargePowerKw);
        }
    }

    [Fact]
    public void Exclusive_BlocksDeviceControlFacadeAndReleasesOnDisconnect()
    {
        var (ess, emu) = BuildEssWithEmuMirror();
        using (ess)
        using (var session = CreateSession(1))
        {
            Assert.True(session.TryConnect("emu1", out var connectError), connectError);
            Assert.False(DeviceControlFacade.TrySetPcsRun(1, true, out var blocked));
            Assert.Equal(ExternalControlGate.BlockedMessage, blocked);

            Assert.True(session.TryDisconnect(null, out var disconnectError), disconnectError);
            Assert.True(DeviceControlFacade.TrySetPcsRun(1, true, out var ok));
            Assert.True(emu.PcsList[0].pcsOnOffSwitch);
            Assert.NotEqual(ExternalControlGate.BlockedMessage, ok);
        }
    }

    [Fact]
    public void SetPower_WithoutOccupy_Fails()
    {
        var (ess, _) = BuildEssWithEmuMirror();
        using (ess)
        using (var session = CreateSession(1))
        {
            Assert.False(session.TrySetPower("emu1", 100, 0, out var error));
            Assert.Contains("占用", error);
        }
    }

    private static ThirdPartyEmsSession CreateSession(int unit) =>
        new(() => new[] { new ThirdPartyEmsTarget { Name = $"emu{unit}", UnitIndex = unit } });

    private static PcsPhysicalConfig CreatePcsPhy() => new() { AcVoltageNominal = 690 };

    private static (EnergyStorageSystem ess, EnergyManagementData emu) BuildEssWithEmuMirror()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig { Pcs = { new EssSimulator.Configuration.PcsDeviceConfig(), new EssSimulator.Configuration.PcsDeviceConfig() } }
            }
        };
        var pcsPhy = CreatePcsPhy();
        var ess = new EnergyStorageSystem(
            cfg, pcsPhy, new TransformerConfig(), new UnitTransformerConfig(),
            new LoadConfig(), new PccConfig(), new MeterConfig());
        var emu = PcsDataServer.BuildEmuMirror(cfg.Devices[0], pcsPhy);
        SimulatorHost.Instance.RegisterEss(ess);
        SimulatorHost.Instance.RegisterEmu(1, emu);
        return (ess, emu);
    }

    private static (EnergyStorageSystem ess, EnergyManagementData emu1, EnergyManagementData emu2) BuildTwoUnitPlant()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig { Pcs = { new EssSimulator.Configuration.PcsDeviceConfig() } },
                new EssUnitConfig { Pcs = { new EssSimulator.Configuration.PcsDeviceConfig() } }
            }
        };
        var pcsPhy = CreatePcsPhy();
        var ess = new EnergyStorageSystem(
            cfg, pcsPhy, new TransformerConfig(), new UnitTransformerConfig(),
            new LoadConfig(), new PccConfig(), new MeterConfig());
        var emu1 = PcsDataServer.BuildEmuMirror(cfg.Devices[0], pcsPhy);
        var emu2 = PcsDataServer.BuildEmuMirror(cfg.Devices[1], pcsPhy);
        SimulatorHost.Instance.RegisterEss(ess);
        SimulatorHost.Instance.RegisterEmu(1, emu1);
        SimulatorHost.Instance.RegisterEmu(2, emu2);
        return (ess, emu1, emu2);
    }
}
