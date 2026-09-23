using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Pv;

namespace EssSimulator.Tests.Pv;

public class PvInverterProtocolDataTests
{
    [Fact]
    public void Telemetry_ConvertsEngineeringUnitsAndCurrentMagnitude()
    {
        var inv = PvInverterDevice.Create320kW("pv1.inv1");
        var state = inv.GetCurrentState();
        state.ActivePower = 100;
        state.ReactivePower = -75;
        state.AcVoltage = 690;
        state.AcCurrent = -105;
        state.Frequency = 49.8;

        Assert.Equal(100000, inv.Protocol.ActivePowerW);
        Assert.Equal(-75000, inv.Protocol.ReactivePowerVar);
        Assert.Equal(125000, inv.Protocol.ApparentPowerVa);
        Assert.Equal(690, inv.Protocol.LineVoltageV);
        Assert.Equal(105, inv.Protocol.PhaseCurrentAmplitudeA);
        Assert.Equal(49.8, inv.Protocol.FrequencyHz);
        Assert.Same(inv.MpptVoltageV, inv.Protocol.MpptVoltageV);
        Assert.Same(inv.MpptCurrentA, inv.Protocol.MpptCurrentA);
    }

    [Theory]
    [InlineData(100, 75, 0.8)]
    [InlineData(100, -75, 0.8)]
    [InlineData(0, 100, 0)]
    [InlineData(0, -100, 0)]
    [InlineData(0, 0, 0)]
    public void PowerFactor_FollowsPcsActivePowerSign(double p, double q, double expected)
    {
        var inv = PvInverterDevice.Create320kW("pv1.inv1");
        inv.GetCurrentState().ActivePower = p;
        inv.GetCurrentState().ReactivePower = q;
        Assert.Equal(expected, inv.Protocol.PowerFactor, 6);
    }

    [Theory]
    [InlineData(OperationMode.Off, false, 0, 0, 1)]
    [InlineData(OperationMode.Off, true, 0, 3, 1)]
    [InlineData(OperationMode.Normal, false, 0, 0, 1)]
    [InlineData(OperationMode.Standby, true, 0, 0, 2)]
    [InlineData(OperationMode.Normal, true, 0, 0, 2)]
    [InlineData(OperationMode.Normal, true, 10, 0, 2)]
    [InlineData(OperationMode.Normal, true, 10.01, 0, 5)]
    [InlineData(OperationMode.Normal, true, 100, 0, 5)]
    [InlineData(OperationMode.Normal, true, 100, 3, 6)]
    public void OperationStatus_UsesExistingPcsRules(OperationMode mode, bool run, double p, ushort fault, ushort expected)
    {
        var inv = PvInverterDevice.Create320kW("pv1.inv1");
        inv.SyncExternalRunCommand(run);
        var state = inv.GetCurrentState();
        state.Mode = mode;
        state.ActivePower = p;
        state.FaultType = fault;

        Assert.Equal(expected, inv.Protocol.OperationStatus);
        Assert.Equal((ushort)PcsDisplayLabels.ToOperationStatusCode(state, run), inv.Protocol.OperationStatus);
    }

    [Fact]
    public void ControlsAndReadback_ShareDeviceCommandState()
    {
        var inv = PvInverterDevice.Create320kW("pv1.inv1");
        inv.Protocol.ActivePowerSettingKw = 80;
        inv.Protocol.ReactivePowerSettingKvar = -25;
        inv.Protocol.RunCommand = 1;
        inv.UpdateGridState(690, 50, true);
        inv.Update(1000, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.Equal(80000, inv.Protocol.ActivePowerW, 6);
        Assert.Equal(-25000, inv.Protocol.ReactivePowerVar, 6);
        inv.Protocol.RunCommand = 0;
        Assert.Equal(0, inv.Protocol.RunCommand);
        Assert.Equal(1, inv.Protocol.OperationStatus);
        Assert.Equal(0, inv.Protocol.ActivePowerW);
        Assert.Equal(80000, inv.Protocol.ActivePowerSettingW);
        Assert.Equal(-25000, inv.Protocol.ReactivePowerSettingVar);

        inv.SetPowerCommand(50, 30);
        Assert.Equal(50, inv.Protocol.ActivePowerSettingKw);
        Assert.Equal(30, inv.Protocol.ReactivePowerSettingKvar);
        Assert.Equal(50000, inv.Protocol.ActivePowerSettingW);
        Assert.Equal(30000, inv.Protocol.ReactivePowerSettingVar);
        inv.Protocol.ActivePowerSettingKw = 1000;
        Assert.Equal(352000, inv.Protocol.ActivePowerSettingW);
        Assert.Equal(30000, inv.Protocol.ReactivePowerSettingVar);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(65535)]
    public void RunCommand_RejectsValuesOtherThanZeroAndOne(ushort value)
    {
        var inv = PvInverterDevice.Create320kW("pv1.inv1");
        Assert.Throws<ArgumentOutOfRangeException>(() => inv.Protocol.RunCommand = value);
        Assert.False(inv.IsExternalRunCommand);
    }

    [Theory]
    [InlineData(null, 200d)]
    [InlineData(100d, 100d)]
    [InlineData(0d, 0d)]
    public void RatedReactivePower_UsesConfigurationOrExplicitSimulationDefault(double? configured, double expected)
    {
        var unit = PvUnitDevice.FromRuntime("pv1", new PvUnitRuntimeConfig
        {
            InverterCount = 2,
            InverterRatedPowerKw = 200,
            InverterRatedReactivePowerKvar = configured
        });
        unit.Update(0, 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.All(unit.Inverters, inv =>
        {
            Assert.Equal(200, inv.Protocol.RatedActivePowerKw);
            Assert.Equal(expected, inv.Protocol.RatedReactivePowerKvar);
            Assert.Equal(1, inv.Protocol.RunCommand);
            Assert.Equal(200, inv.Protocol.ActivePowerSettingKw);
        });
        Assert.Equal(expected * 2, unit.Logger.NominalReactivePowerKvar);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RatedReactivePower_RejectsInvalidConfiguration(double configured)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PvInverterDevice("pv1.inv1", new PvInverterConfig
        {
            RatedReactivePowerKvar = configured
        }));
    }
}
